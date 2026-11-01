#!/usr/bin/env python3
"""sim_play: shell client for the Sts2Sim play server (stdlib only).

Usage (base URL from --url or SIM_PLAY_URL, default http://127.0.0.1:8765):

  sim_play.py new [--seed S] [--character C] [--ascension A] [--content ported_only|all]
  sim_play.py new --combat ENCOUNTER --deck ID [ID ...] [--seed S] [--hp N] [--max-hp N]
  sim_play.py show <id>
  sim_play.py step <id> <token...>        e.g.  step r1 "play 0 -> 1"   or   step r1 play 0 1
  sim_play.py history <id>
  sim_play.py transcript <id> <file.jsonl>
  sim_play.py random --runs N [--seed S] [--max-steps M] [--character C]
  sim_play.py catalog [--kind characters|encounters|cards] [--grep TEXT]
  sim_play.py health

Exit codes: 0 ok, 1 random-policy found a crash/anomaly, 2 illegal token (HTTP 400),
3 simulator crash (HTTP 500), 4 server unreachable / other HTTP error.
"""

import argparse
import json
import os
import random
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

DEFAULT_URL = "http://127.0.0.1:8765"
EXIT_OK, EXIT_ANOMALY, EXIT_ILLEGAL, EXIT_CRASH, EXIT_UNREACHABLE = 0, 1, 2, 3, 4
TARGETED_VERBS = ("play", "potion")


# ---------------------------------------------------------------- helpers

def base_url(arg=None, env=None):
    env = os.environ if env is None else env
    url = (arg or env.get("SIM_PLAY_URL") or DEFAULT_URL).strip()
    if "://" not in url:
        url = "http://" + url
    return url.rstrip("/")


def parse_seed(text):
    """'123' -> 123, 'ABC123' -> 'ABC123', None/''/'null' -> None."""
    if text is None:
        return None
    s = str(text).strip()
    if s == "" or s.lower() in ("null", "none"):
        return None
    if re.fullmatch(r"-?\d+", s):
        return int(s)
    return s


def normalize_token(words):
    """Turn CLI words into one canonical token.

    Accepts shell-friendly spellings for targets, since an unquoted '->' is a shell
    redirect: 'play 0 1', 'play 0 @1', 'play 0 to 1', 'play 0 ->1', 'play 0->1'.
    """
    if isinstance(words, str):
        words = [words]
    s = " ".join(w for w in words if w is not None).strip()
    s = re.sub(r"\s*->\s*", " -> ", s)
    s = re.sub(r"\s+@\s*", " -> ", s)
    parts = s.split()
    if not parts:
        return ""
    parts[0] = parts[0].lower()
    if parts[0] in TARGETED_VERBS:
        parts = [("->" if p.lower() in ("to", "on", "at") else p) for p in parts]
        if len(parts) == 3 and parts[2] != "->":
            parts = [parts[0], parts[1], "->", parts[2]]
    return " ".join(parts)


def split_tokens(words):
    """'play 0 -> 1 ; end' -> ['play 0 -> 1', 'end'] (';' separates tokens)."""
    joined = " ".join(words)
    return [normalize_token(chunk) for chunk in joined.split(";") if chunk.strip()]


class ApiError(Exception):
    def __init__(self, status, body, message=None):
        self.status = status
        self.body = body if isinstance(body, dict) else {"error": str(body)}
        super().__init__(message or self.body.get("error") or f"HTTP {status}")


class Unreachable(Exception):
    pass


class Client:
    def __init__(self, url=None, timeout=60.0):
        self.url = base_url(url)
        self.timeout = timeout

    def request(self, method, path, body=None):
        data = None if body is None else json.dumps(body).encode("utf-8")
        req = urllib.request.Request(self.url + path, data=data, method=method)
        req.add_header("Accept", "application/json")
        if data is not None:
            req.add_header("Content-Type", "application/json")
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as resp:
                return _decode(resp.read())
        except urllib.error.HTTPError as e:
            raise ApiError(e.code, _decode(e.read())) from None
        except (urllib.error.URLError, ConnectionError, TimeoutError) as e:
            reason = getattr(e, "reason", e)
            raise Unreachable(f"server not reachable at {self.url} ({reason}); "
                              "start it with scripts/sim_server.sh") from None

    @staticmethod
    def gid(game_id):
        return urllib.parse.quote(str(game_id), safe="")

    def health(self):
        return self.request("GET", "/health")

    def catalog(self):
        return self.request("GET", "/catalog")

    def new_run(self, seed=None, character="IRONCLAD", ascension=0, content="ported_only"):
        return self.request("POST", "/runs", {"seed": seed, "character": character,
                                              "ascension": ascension, "content": content})

    def new_combat(self, deck, encounter, seed=None, ascension=0, hp=None, max_hp=None):
        return self.request("POST", "/combats", {"seed": seed, "deck": list(deck),
                                                 "encounter": encounter, "ascension": ascension,
                                                 "hp": hp, "max_hp": max_hp})

    def show(self, game_id):
        return self.request("GET", f"/games/{self.gid(game_id)}")

    def step(self, game_id, token):
        return self.request("POST", f"/games/{self.gid(game_id)}/step", {"token": token})

    def history(self, game_id):
        return self.request("GET", f"/games/{self.gid(game_id)}/history")

    def delete(self, game_id):
        return self.request("DELETE", f"/games/{self.gid(game_id)}")


def _decode(raw):
    if not raw:
        return {}
    try:
        return json.loads(raw.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError):
        return {"error": raw.decode("utf-8", "replace")[:2000]}


def render(obs):
    """Server text, plus any legal token the text does not already mention."""
    if not obs:
        return "(no observation)"
    text = obs.get("text") or _fallback_text(obs)
    missing = [a for a in obs.get("legal_actions") or [] if a.get("token") and a["token"] not in text]
    if missing:
        text = text.rstrip() + "\nlegal actions:\n" + "\n".join(
            f"  {a['token']:<18} {a.get('desc', '')}" for a in missing)
    return text


def _fallback_text(obs):
    p = obs.get("player") or {}
    head = (f"[{obs.get('game_id')}] {obs.get('kind')} phase={obs.get('phase')} act={obs.get('act')} "
            f"floor={obs.get('floor')} turn={obs.get('turn')} hp={p.get('hp')}/{p.get('max_hp')} "
            f"block={p.get('block')} energy={p.get('energy')} gold={p.get('gold')}")
    if obs.get("terminal"):
        head += f" TERMINAL result={obs.get('result')}"
    return head


def append_jsonl(path, record):
    d = os.path.dirname(os.path.abspath(path))
    os.makedirs(d, exist_ok=True)
    with open(path, "a", encoding="utf-8") as f:
        f.write(json.dumps(record, ensure_ascii=False) + "\n")


def legal_tokens(obs):
    return [a["token"] for a in (obs or {}).get("legal_actions") or [] if a.get("token")]


# ---------------------------------------------------------------- commands

def cmd_health(client, args):
    _print_json(client.health())
    return EXIT_OK


def cmd_catalog(client, args):
    cat = client.catalog()
    if args.json:
        _print_json(cat)
        return EXIT_OK
    kinds = [args.kind] if args.kind else ["characters", "encounters", "cards"]
    for k in kinds:
        ids = [i for i in cat.get(k, []) if not args.grep or args.grep.lower() in str(i).lower()]
        print(f"{k} ({len(ids)}):")
        print("  " + " ".join(str(i) for i in ids))
    return EXIT_OK


def cmd_new(client, args):
    seed = parse_seed(args.seed)
    if args.combat:
        deck = [d for chunk in (args.deck or []) for d in chunk.split(",") if d]
        if not deck:
            print("error: --combat needs --deck ID [ID ...]", file=sys.stderr)
            return EXIT_ILLEGAL
        res = client.new_combat(deck, args.combat, seed=seed if seed is not None else 1,
                                ascension=args.ascension, hp=args.hp, max_hp=args.max_hp)
    else:
        res = client.new_run(seed=seed, character=args.character, ascension=args.ascension,
                             content=args.content)
    if args.json:
        _print_json(res)
        return EXIT_OK
    print(f"game {res.get('id')}")
    print(render(res.get("observation")))
    return EXIT_OK


def cmd_show(client, args):
    res = client.show(args.id)
    if args.json:
        _print_json(res)
    else:
        print(render(res.get("observation")))
    return EXIT_OK


def cmd_step(client, args):
    tokens = split_tokens(args.token)
    if not tokens:
        print("error: empty token", file=sys.stderr)
        return EXIT_ILLEGAL
    log_path = args.log or os.environ.get("SIM_PLAY_TRANSCRIPT")
    res = None
    for token in tokens:
        before = None
        if log_path:
            before = client.show(args.id).get("observation")
        try:
            res = client.step(args.id, token)
        except ApiError as e:
            if log_path:
                append_jsonl(log_path, {"game_id": args.id, "ts": time.time(), "token": token,
                                        "observation": before, "ok": False, "status": e.status,
                                        "error": e.body.get("error")})
            return _report_step_error(e, token)
        if log_path:
            append_jsonl(log_path, {"game_id": args.id, "ts": time.time(), "token": token,
                                    "observation": before, "ok": True, "log": res.get("log"),
                                    "next_observation": res.get("observation")})
        if len(tokens) > 1 and not args.json:
            print(f">>> {token}")
            for line in res.get("log") or []:
                print(f"  {line}")
    if args.json:
        _print_json(res)
        return EXIT_OK
    if len(tokens) == 1:
        print("--- log ---")
        for line in res.get("log") or []:
            print(f"  {line}")
    print("--- state ---")
    print(render(res.get("observation")))
    return EXIT_OK


def _report_step_error(e, token):
    body = e.body
    if e.status == 400:
        print(f"illegal action {token!r}: {body.get('error')}", file=sys.stderr)
        legal = body.get("legal_actions") or []
        if legal:
            print("legal actions:", file=sys.stderr)
            for a in legal:
                if isinstance(a, dict):
                    print(f"  {a.get('token', ''):<18} {a.get('desc', '')}", file=sys.stderr)
                else:
                    print(f"  {a}", file=sys.stderr)
        return EXIT_ILLEGAL
    if e.status == 500:
        print(f"SIMULATOR CRASH on {token!r}: {body.get('error')}", file=sys.stderr)
        print("the game is marked crashed; repro appended to tmp/play-crashes.jsonl on the server",
              file=sys.stderr)
        return EXIT_CRASH
    print(f"HTTP {e.status}: {body.get('error')}", file=sys.stderr)
    return EXIT_UNREACHABLE


def cmd_history(client, args):
    _print_json(client.history(args.id))
    return EXIT_OK


def replay(client, game_id, out_path):
    """Re-create a game from its history and append one JSONL record per step."""
    hist = client.history(game_id)
    live = client.show(game_id).get("observation") or {}
    kind = live.get("kind") or ("combat" if "encounter" in (hist.get("config") or {}) else "run")
    body = dict(hist.get("config") or {})
    body["seed"] = hist.get("seed", body.get("seed"))
    path = "/combats" if kind == "combat" else "/runs"
    created = client.request("POST", path, body)
    rid, obs = created.get("id"), created.get("observation")
    append_jsonl(out_path, {"type": "header", "game_id": game_id, "replay_id": rid, "kind": kind,
                            "seed": body["seed"], "config": body, "steps": len(hist.get("tokens") or []),
                            "crashed": hist.get("crashed", False)})
    error = None
    try:
        for i, token in enumerate(hist.get("tokens") or []):
            rec = {"type": "step", "game_id": game_id, "step": i, "observation": obs, "action": token}
            try:
                res = client.step(rid, token)
            except ApiError as e:
                rec.update(ok=False, status=e.status, error=e.body.get("error"))
                append_jsonl(out_path, rec)
                error = f"step {i} {token!r}: HTTP {e.status} {e.body.get('error')}"
                break
            rec.update(ok=True, log=res.get("log"))
            append_jsonl(out_path, rec)
            obs = res.get("observation")
        else:
            append_jsonl(out_path, {"type": "final", "game_id": game_id, "observation": obs})
    finally:
        try:
            client.delete(rid)
        except (ApiError, Unreachable):
            pass
    matches = error is None and _comparable(obs) == _comparable(live)
    return {"steps": len(hist.get("tokens") or []), "error": error, "matches_live": matches}


def _comparable(obs):
    if not obs:
        return None
    o = {k: v for k, v in obs.items() if k not in ("game_id", "text")}
    return json.dumps(o, sort_keys=True)


def cmd_transcript(client, args):
    r = replay(client, args.id, args.file)
    print(f"wrote {r['steps']} step(s) of {args.id} to {args.file}")
    if r["error"]:
        print(f"replay stopped: {r['error']}")
        return EXIT_CRASH
    print("replay final observation matches live game: " + ("yes" if r["matches_live"] else "NO"))
    return EXIT_OK if r["matches_live"] else EXIT_ANOMALY


def play_random_run(client, rng, seed, character, max_steps, ascension=0, content="ported_only"):
    """One uniform-random run. Returns a result dict (never raises ApiError)."""
    created = client.new_run(seed=seed, character=character, ascension=ascension, content=content)
    gid, obs = created.get("id"), created.get("observation") or {}
    out = {"game_id": gid, "seed": seed, "character": character, "steps": 0,
           "outcome": None, "act": obs.get("act"), "floor": obs.get("floor"), "error": None}
    max_floor = obs.get("floor") or 0
    while True:
        if obs.get("terminal"):
            out["outcome"] = obs.get("result") or "terminal"
            break
        if out["steps"] >= max_steps:
            out["outcome"] = "max_steps"
            break
        tokens = legal_tokens(obs)
        if not tokens:
            out["outcome"], out["error"] = "stuck", f"no legal actions in phase {obs.get('phase')}"
            break
        token = rng.choice(tokens)
        try:
            res = client.step(gid, token)
        except ApiError as e:
            out["outcome"] = "crash" if e.status == 500 else "rejected"
            out["error"] = f"{token!r}: HTTP {e.status} {e.body.get('error')}"
            break
        out["steps"] += 1
        obs = res.get("observation") or {}
        max_floor = max(max_floor, obs.get("floor") or 0)
        out["act"], out["floor"] = obs.get("act"), obs.get("floor")
    out["max_floor"] = max_floor
    if out["outcome"] in ("crash", "rejected", "stuck"):
        try:
            out["repro"] = client.history(gid)
        except (ApiError, Unreachable) as e:
            out["repro"] = {"error": str(e)}
    else:
        try:
            client.delete(gid)
        except (ApiError, Unreachable):
            pass
    return out


def cmd_random(client, args):
    base = parse_seed(args.seed)
    rng = random.Random(args.policy_seed)
    results = []
    for i in range(args.runs):
        if base is None:
            seed = rng.randrange(1, 2**31)
        elif isinstance(base, int):
            seed = base + i
        else:
            seed = f"{base}{i}"
        r = play_random_run(client, rng, seed, args.character, args.max_steps,
                            ascension=args.ascension, content=args.content)
        results.append(r)
        line = (f"run {i + 1}/{args.runs} game={r['game_id']} seed={seed} outcome={r['outcome']} "
                f"act={r['act']} floor={r['floor']} max_floor={r['max_floor']} steps={r['steps']}")
        if r["error"]:
            line += f" error={r['error']}"
        print(line, flush=True)
        if "repro" in r:
            print("  repro: " + json.dumps(r["repro"], ensure_ascii=False), flush=True)
    bad = [r for r in results if r["outcome"] in ("crash", "rejected", "stuck")]
    counts = {}
    for r in results:
        counts[r["outcome"]] = counts.get(r["outcome"], 0) + 1
    print("summary: " + " ".join(f"{k}={v}" for k, v in sorted(counts.items()))
          + f" total_steps={sum(r['steps'] for r in results)}")
    return EXIT_ANOMALY if bad else EXIT_OK


def _print_json(obj):
    print(json.dumps(obj, indent=2, ensure_ascii=False))


# ---------------------------------------------------------------- CLI

def build_parser():
    common = argparse.ArgumentParser(add_help=False)
    common.add_argument("--url", default=argparse.SUPPRESS, help="server base URL (env SIM_PLAY_URL)")
    common.add_argument("--json", action="store_true", default=argparse.SUPPRESS,
                        help="print raw JSON responses")

    p = argparse.ArgumentParser(prog="sim_play.py", description=__doc__.split("\n")[0], parents=[common])
    sub = p.add_subparsers(dest="cmd", required=True)

    sub.add_parser("health", parents=[common], help="server health")

    c = sub.add_parser("catalog", parents=[common], help="list character/encounter/card ids")
    c.add_argument("--kind", choices=["characters", "encounters", "cards"])
    c.add_argument("--grep")

    n = sub.add_parser("new", parents=[common], help="start a run, or a standalone combat")
    n.add_argument("--seed")
    n.add_argument("--character", default="IRONCLAD")
    n.add_argument("--ascension", type=int, default=0)
    n.add_argument("--content", choices=["ported_only", "all"], default="ported_only")
    n.add_argument("--combat", metavar="ENCOUNTER")
    n.add_argument("--deck", nargs="+", metavar="ID", help="card ids (space or comma separated)")
    n.add_argument("--hp", type=int)
    n.add_argument("--max-hp", type=int, dest="max_hp")

    s = sub.add_parser("show", parents=[common], help="print the current observation")
    s.add_argument("id")

    st = sub.add_parser("step", parents=[common], help="apply one action token (';' chains several)")
    st.add_argument("id")
    st.add_argument("token", nargs="+")
    st.add_argument("--log", metavar="FILE.jsonl",
                    help="append observation/action records (env SIM_PLAY_TRANSCRIPT)")

    h = sub.add_parser("history", parents=[common], help="seed, config and token history")
    h.add_argument("id")

    t = sub.add_parser("transcript", parents=[common],
                       help="replay a game's history, appending observation/action pairs as JSONL")
    t.add_argument("id")
    t.add_argument("file")

    r = sub.add_parser("random", parents=[common], help="uniform-random legal policy playtest")
    r.add_argument("--runs", type=int, default=10)
    r.add_argument("--seed")
    r.add_argument("--max-steps", type=int, default=3000, dest="max_steps")
    r.add_argument("--character", default="IRONCLAD")
    r.add_argument("--ascension", type=int, default=0)
    r.add_argument("--content", choices=["ported_only", "all"], default="ported_only")
    r.add_argument("--policy-seed", type=int, default=0, dest="policy_seed",
                   help="RNG seed of the random policy itself")
    return p


COMMANDS = {"health": cmd_health, "catalog": cmd_catalog, "new": cmd_new, "show": cmd_show,
            "step": cmd_step, "history": cmd_history, "transcript": cmd_transcript,
            "random": cmd_random}


def main(argv=None):
    args = build_parser().parse_args(argv)
    args.json = getattr(args, "json", False)
    client = Client(getattr(args, "url", None))
    try:
        return COMMANDS[args.cmd](client, args)
    except Unreachable as e:
        print(f"error: {e}", file=sys.stderr)
        return EXIT_UNREACHABLE
    except ApiError as e:
        if args.cmd in ("step", "transcript"):
            return _report_step_error(e, " ".join(getattr(args, "token", []) or []))
        print(f"error: HTTP {e.status}: {e.body.get('error')}", file=sys.stderr)
        return EXIT_CRASH if e.status == 500 else EXIT_UNREACHABLE


if __name__ == "__main__":
    sys.exit(main())

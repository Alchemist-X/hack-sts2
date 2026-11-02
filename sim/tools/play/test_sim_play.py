"""Tests for sim_play.py against a tiny in-process fake of the play server contract.

Run: python3 -m unittest discover -s sim/tools/play -p 'test_*.py'
"""

import contextlib
import io
import json
import os
import random
import sys
import tempfile
import threading
import unittest
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlsplit

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sim_play  # noqa: E402

CRASH_SEED = 666

# Typed catalog of the fake server: kind -> items, mirroring GET /catalog?kind=K[&ids=A,B][&grep=X].
FAKE_RULES = {
    "cards": [{"id": "STRIKE_IRONCLAD", "name": "Strike", "text": "Deal 6 damage."},
              {"id": "BASH", "name": "Bash", "text": "Deal 8 damage.\nApply 2 Vulnerable."}],
    "relics": [{"id": "BURNING_BLOOD", "name": "Burning Blood", "text": "At the end of combat, heal 6 HP."}],
    "potions": [{"id": "BLOCK_POTION", "name": "Block Potion", "text": "Gain 12 Block."}],
    "powers": [{"id": "STRENGTH_POWER", "name": "Strength", "text": "Increases attack damage by N."}],
    "events": [{"id": "NEOW", "name": "Neow", "text": None}],
    "enemies": [{"id": "NIBBIT", "name": "Nibbit", "text": None,
                 "moves": [{"id": "BUTT", "title": "Butt"}, {"id": "HISS", "title": "Hiss"}]}],
    "keywords": [{"id": "EXHAUST", "name": "Exhaust", "text": "Removed until the end of combat."}],
}


def fake_catalog(kind, ids=None, grep=None):
    items = FAKE_RULES.get(kind)
    if items is None:
        return 400, {"ok": False, "error": f"unknown catalog kind '{kind}'"}
    wanted = {i.upper() for i in ids} if ids else None
    out = []
    for item in items:
        short = item["id"][:-6] if item["id"].endswith("_POWER") else item["id"]
        if wanted is not None and item["id"] not in wanted and short not in wanted:
            continue
        if grep and not any(grep.lower() in str(item.get(k) or "").lower() for k in ("id", "name", "text")):
            continue
        out.append(item)
    return 200, {"kind": kind, "loaded": True, "items": out}


class FakeGame:
    """A one-enemy combat: 'play 0 -> 0' deals 6, 'end' takes 4 damage. Seed 666 crashes on 'end'."""

    def __init__(self, gid, kind, seed, config):
        self.gid, self.kind, self.seed, self.config = gid, kind, seed, config
        self.tokens, self.crashed = [], False
        self.enemy_hp, self.hp, self.energy, self.turn = 12, 10, 3, 1

    def phase(self):
        if self.enemy_hp <= 0:
            return "Victory"
        if self.hp <= 0:
            return "Defeat"
        return "Combat"

    def legal(self):
        if self.phase() != "Combat":
            return []
        acts = []
        if self.energy >= 1:
            acts.append({"token": "play 0 -> 0", "desc": "play STRIKE_IRONCLAD (6 dmg) on enemy 0 NIBBIT"})
        acts.append({"token": "end", "desc": "end turn"})
        return acts

    def obs(self):
        ph = self.phase()
        terminal = ph in ("Victory", "Defeat")
        legal = self.legal()
        text = (f"[{self.gid}] {ph} turn {self.turn} hp {self.hp} energy {self.energy} "
                f"enemy NIBBIT {self.enemy_hp}\n" + "\n".join(a["token"] for a in legal[:1]))
        return {"game_id": self.gid, "kind": self.kind, "phase": ph, "terminal": terminal,
                "result": ("win" if ph == "Victory" else "loss") if terminal else None,
                "act": 1, "floor": 1 if self.kind == "run" else 0, "turn": self.turn,
                "player": {"hp": self.hp, "max_hp": 10, "energy": self.energy},
                "legal_actions": legal, "text": text}

    def step(self, token):
        if token not in [a["token"] for a in self.legal()]:
            return 400, {"ok": False, "error": f"illegal token {token!r}", "legal_actions": self.legal()}
        if token == "end" and self.seed == CRASH_SEED:
            self.crashed = True
            self.tokens.append(token)
            return 500, {"ok": False, "error": "InvalidOperationException: boom", "crashed": True}
        self.tokens.append(token)
        if token == "end":
            self.hp -= 4
            self.energy, self.turn = 3, self.turn + 1
            log = ["enemy attacks for 4"]
        else:
            self.enemy_hp -= 6
            self.energy -= 1
            log = ["STRIKE_IRONCLAD hits NIBBIT for 6"]
        return 200, {"ok": True, "observation": self.obs(), "log": log}


class FakeServer:
    def __init__(self):
        self.games, self.counter, self.requests = {}, 0, []
        fake = self

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *a):
                pass

            def _send(self, status, body):
                raw = json.dumps(body).encode()
                self.send_response(status)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(raw)))
                self.end_headers()
                self.wfile.write(raw)

            def _body(self):
                n = int(self.headers.get("Content-Length") or 0)
                return json.loads(self.rfile.read(n)) if n else {}

            def do_GET(self):
                fake.requests.append(("GET", self.path, None))
                url = urlsplit(self.path)
                query = parse_qs(url.query)
                parts = url.path.strip("/").split("/")
                if parts == ["catalog"] and "kind" in query:
                    ids = [i for v in query.get("ids", []) for i in v.split(",") if i]
                    return self._send(*fake_catalog(query["kind"][0], ids or None, (query.get("grep") or [None])[0]))
                if parts == ["health"]:
                    return self._send(200, {"ok": True, "version": "fake", "games": len(fake.games)})
                if parts == ["catalog"]:
                    return self._send(200, {"characters": ["IRONCLAD", "SILENT"],
                                            "encounters": ["NIBBITS_WEAK"], "cards": ["STRIKE_IRONCLAD"]})
                g = fake.games.get(parts[1]) if len(parts) >= 2 and parts[0] == "games" else None
                if g is None:
                    return self._send(404, {"ok": False, "error": "no such game"})
                if len(parts) == 2:
                    return self._send(200, {"observation": g.obs()})
                if parts[2] == "history":
                    return self._send(200, {"seed": g.seed, "config": g.config, "tokens": g.tokens,
                                            "steps": len(g.tokens), "crashed": g.crashed})
                return self._send(404, {"ok": False, "error": "not found"})

            def do_POST(self):
                body = self._body()
                fake.requests.append(("POST", self.path, body))
                parts = self.path.strip("/").split("/")
                if parts in (["runs"], ["combats"]):
                    fake.counter += 1
                    kind = "run" if parts == ["runs"] else "combat"
                    gid = f"{kind[0]}{fake.counter}"
                    seed = body.get("seed")
                    if seed is None:
                        seed = 1000 + fake.counter
                    g = fake.games[gid] = FakeGame(gid, kind, seed, body)
                    return self._send(200, {"id": gid, "observation": g.obs()})
                if len(parts) == 3 and parts[0] == "games" and parts[2] == "step":
                    g = fake.games.get(parts[1])
                    if g is None:
                        return self._send(404, {"ok": False, "error": "no such game"})
                    if g.crashed:
                        return self._send(409, {"ok": False, "error": "game crashed"})
                    return self._send(*g.step(body.get("token")))
                return self._send(404, {"ok": False, "error": "not found"})

            def do_DELETE(self):
                fake.requests.append(("DELETE", self.path, None))
                parts = self.path.strip("/").split("/")
                fake.games.pop(parts[1], None)
                return self._send(200, {"ok": True})

        self.httpd = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.url = f"http://127.0.0.1:{self.httpd.server_address[1]}"
        self.thread = threading.Thread(target=self.httpd.serve_forever, daemon=True)

    def __enter__(self):
        self.thread.start()
        return self

    def __exit__(self, *exc):
        self.httpd.shutdown()
        self.httpd.server_close()


def read_jsonl(path):
    with open(path, encoding="utf-8") as f:
        return [json.loads(line) for line in f]


def run_cli(argv):
    out, err = io.StringIO(), io.StringIO()
    with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
        code = sim_play.main(argv)
    return code, out.getvalue(), err.getvalue()


class TokenTests(unittest.TestCase):
    def test_canonical_tokens_pass_through(self):
        for t in ["end", "play 0 -> 1", "potion 2 -> 0", "pick 3", "travel 1", "card 0", "skip",
                  "buy 4", "rest 0", "event 1", "next", "proceed", "confirm"]:
            self.assertEqual(sim_play.normalize_token(t), t)

    def test_shell_friendly_target_spellings(self):
        for words in (["play", "0", "1"], ["play", "0", "@1"], ["play", "0", "@", "1"],
                      ["play", "0", "to", "1"], ["play", "0", "->1"], ["play 0->1"],
                      ["PLAY", "0", "->", "1"], ["  play   0  ->   1 "]):
            self.assertEqual(sim_play.normalize_token(words), "play 0 -> 1", words)
        self.assertEqual(sim_play.normalize_token(["potion", "1", "0"]), "potion 1 -> 0")

    def test_untargeted_and_other_verbs_untouched(self):
        self.assertEqual(sim_play.normalize_token(["play", "2"]), "play 2")
        self.assertEqual(sim_play.normalize_token(["discard", "1"]), "discard 1")
        self.assertEqual(sim_play.normalize_token(["take", "0"]), "take 0")

    def test_split_tokens(self):
        self.assertEqual(sim_play.split_tokens(["play", "0", "1;", "end"]), ["play 0 -> 1", "end"])
        self.assertEqual(sim_play.split_tokens(["end"]), ["end"])
        self.assertEqual(sim_play.split_tokens([";"]), [])

    def test_parse_seed(self):
        self.assertEqual(sim_play.parse_seed("123"), 123)
        self.assertEqual(sim_play.parse_seed("ABC123"), "ABC123")
        self.assertIsNone(sim_play.parse_seed(None))
        self.assertIsNone(sim_play.parse_seed("null"))


class UrlTests(unittest.TestCase):
    def test_base_url_precedence(self):
        self.assertEqual(sim_play.base_url(None, {}), sim_play.DEFAULT_URL)
        self.assertEqual(sim_play.base_url(None, {"SIM_PLAY_URL": "http://h:1/"}), "http://h:1")
        self.assertEqual(sim_play.base_url("localhost:9", {"SIM_PLAY_URL": "http://h:1"}),
                         "http://localhost:9")

    def test_game_id_is_quoted(self):
        self.assertEqual(sim_play.Client.gid("a/b c"), "a%2Fb%20c")

    def test_unreachable_exit_code(self):
        code, _, err = run_cli(["--url", "http://127.0.0.1:9", "show", "r1"])
        self.assertEqual(code, sim_play.EXIT_UNREACHABLE)
        self.assertIn("sim_server.sh", err)


class ServerTests(unittest.TestCase):
    def setUp(self):
        self.fake = FakeServer().__enter__()

    def tearDown(self):
        self.fake.__exit__(None, None, None)

    def cli(self, *argv):
        return run_cli(["--url", self.fake.url, *argv])

    def test_new_run_sends_contract_body(self):
        code, out, _ = self.cli("new", "--seed", "ABC123", "--character", "SILENT")
        self.assertEqual(code, 0)
        self.assertIn("game r1", out)
        _, path, body = self.fake.requests[-1]
        self.assertEqual(path, "/runs")
        self.assertEqual(body, {"seed": "ABC123", "character": "SILENT", "ascension": 0,
                                "content": "ported_only"})

    def test_new_combat_sends_deck(self):
        code, out, _ = self.cli("new", "--combat", "NIBBITS_WEAK", "--deck", "STRIKE_IRONCLAD,DEFEND_IRONCLAD",
                                "BASH", "--seed", "7", "--hp", "50")
        self.assertEqual(code, 0)
        self.assertIn("game c1", out)
        _, path, body = self.fake.requests[-1]
        self.assertEqual(path, "/combats")
        self.assertEqual(body["deck"], ["STRIKE_IRONCLAD", "DEFEND_IRONCLAD", "BASH"])
        self.assertEqual((body["seed"], body["encounter"], body["hp"], body["max_hp"]), (7, "NIBBITS_WEAK", 50, None))

    def test_show_appends_tokens_missing_from_text(self):
        self.cli("new")
        code, out, _ = self.cli("show", "r1")
        self.assertEqual(code, 0)
        self.assertIn("legal actions:", out)
        self.assertIn("end turn", out)

    def test_step_normalizes_and_prints_log(self):
        self.cli("new")
        code, out, _ = self.cli("step", "r1", "play", "0", "0")
        self.assertEqual(code, 0)
        self.assertIn("hits NIBBIT for 6", out)
        self.assertEqual(self.fake.requests[-1][2], {"token": "play 0 -> 0"})

    def test_step_illegal_is_400_with_legal_list(self):
        self.cli("new")
        code, _, err = self.cli("step", "r1", "play", "5", "0")
        self.assertEqual(code, sim_play.EXIT_ILLEGAL)
        self.assertIn("illegal", err)
        self.assertIn("play 0 -> 0", err)

    def test_step_crash_is_500(self):
        self.cli("new", "--seed", str(CRASH_SEED))
        code, _, err = self.cli("step", "r1", "end")
        self.assertEqual(code, sim_play.EXIT_CRASH)
        self.assertIn("SIMULATOR CRASH", err)
        self.assertIn("boom", err)

    def test_step_chain_stops_on_error(self):
        self.cli("new")
        code, out, _ = self.cli("step", "r1", "play 0 0; play 7; end")
        self.assertEqual(code, sim_play.EXIT_ILLEGAL)
        self.assertEqual(self.fake.games["r1"].tokens, ["play 0 -> 0"])

    def test_step_live_log(self):
        self.cli("new")
        with tempfile.TemporaryDirectory() as d:
            path = os.path.join(d, "t.jsonl")
            self.cli("step", "r1", "end", "--log", path)
            self.cli("step", "r1", "play", "9", "--log", path)
            recs = read_jsonl(path)
        self.assertEqual([r["ok"] for r in recs], [True, False])
        self.assertEqual(recs[0]["token"], "end")
        self.assertEqual(recs[0]["observation"]["turn"], 1)
        self.assertEqual(recs[0]["next_observation"]["turn"], 2)

    def test_history_and_transcript_replay(self):
        self.cli("new", "--seed", "42")
        self.cli("step", "r1", "play 0 -> 0")
        self.cli("step", "r1", "end")
        code, out, _ = self.cli("history", "r1")
        self.assertEqual(json.loads(out)["tokens"], ["play 0 -> 0", "end"])
        with tempfile.TemporaryDirectory() as d:
            path = os.path.join(d, "t.jsonl")
            code, out, _ = self.cli("transcript", "r1", path)
            recs = read_jsonl(path)
        self.assertEqual(code, 0, out)
        self.assertIn("matches live game: yes", out)
        self.assertEqual([r["type"] for r in recs], ["header", "step", "step", "final"])
        self.assertEqual(recs[0]["seed"], 42)
        self.assertEqual([r["action"] for r in recs[1:3]], ["play 0 -> 0", "end"])
        self.assertEqual(recs[1]["observation"]["turn"], 1)
        self.assertNotIn("r2", self.fake.games)

    def test_random_policy_finishes(self):
        code, out, _ = self.cli("random", "--runs", "3", "--seed", "1")
        self.assertEqual(code, 0, out)
        self.assertEqual(out.count("outcome="), 3)
        self.assertIn("summary:", out)
        self.assertNotIn("outcome=crash", out)
        self.assertEqual(self.fake.games, {})

    def test_random_policy_reports_crash_with_repro(self):
        code, out, _ = self.cli("random", "--runs", "2", "--seed", str(CRASH_SEED - 1))
        self.assertEqual(code, sim_play.EXIT_ANOMALY)
        self.assertIn("outcome=crash", out)
        repro = json.loads(out.split("repro: ", 1)[1].splitlines()[0])
        self.assertEqual(repro["seed"], CRASH_SEED)
        self.assertTrue(repro["crashed"])
        self.assertEqual(repro["tokens"][-1], "end")

    def test_random_max_steps(self):
        c = sim_play.Client(self.fake.url)
        r = sim_play.play_random_run(c, random.Random(0), 5, "IRONCLAD", max_steps=1)
        self.assertEqual((r["outcome"], r["steps"]), ("max_steps", 1))

    def test_catalog_grep(self):
        code, out, _ = self.cli("catalog", "--kind", "characters", "--grep", "sil")
        self.assertEqual(code, 0)
        self.assertIn("SILENT", out)
        self.assertNotIn("IRONCLAD", out)

    def test_catalog_rules_kind_prints_id_name_text(self):
        code, out, _ = self.cli("catalog", "--kind", "cards")
        self.assertEqual(code, 0)
        self.assertIn("cards (2):", out)
        self.assertIn("  STRIKE_IRONCLAD  Strike  Deal 6 damage.", out)
        self.assertIn("  BASH  Bash  Deal 8 damage. Apply 2 Vulnerable.", out, "text collapsed to one line")
        path = [r[1] for r in self.fake.requests if r[0] == "GET"][-1]
        self.assertTrue(path.startswith("/catalog?kind=cards"), path)

    def test_catalog_rules_kind_grep_and_ids(self):
        code, out, _ = self.cli("catalog", "--kind", "cards", "--grep", "vulnerable")
        self.assertEqual(code, 0)
        self.assertIn("BASH", out)
        self.assertNotIn("STRIKE_IRONCLAD", out)
        code, out, _ = self.cli("catalog", "--kind", "cards", "--ids", "STRIKE_IRONCLAD")
        self.assertIn("STRIKE_IRONCLAD", out)
        self.assertNotIn("BASH", out)

    def test_catalog_enemy_moves(self):
        code, out, _ = self.cli("catalog", "--kind", "enemies")
        self.assertEqual(code, 0)
        self.assertIn("NIBBIT  Nibbit  -  moves: BUTT (Butt), HISS (Hiss)", out)

    def test_describe_across_kinds(self):
        code, out, _ = self.cli("describe", "strength", "BURNING_BLOOD")
        self.assertEqual(code, 0)
        self.assertIn("[powers] STRENGTH_POWER  Strength  Increases attack damage by N.", out)
        self.assertIn("[relics] BURNING_BLOOD  Burning Blood", out)
        kinds = [parse_qs(urlsplit(r[1]).query)["kind"][0] for r in self.fake.requests if r[1].startswith("/catalog?")]
        self.assertEqual(set(kinds), set(sim_play.RULE_KINDS))

    def test_describe_nothing_matched(self):
        code, _, err = self.cli("describe", "NO_SUCH_THING")
        self.assertEqual(code, sim_play.EXIT_ANOMALY)
        self.assertIn("nothing matched", err)


if __name__ == "__main__":
    unittest.main()

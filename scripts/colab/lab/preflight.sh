#!/bin/bash
# preflight.sh --line NAME [options]   (backlog GUARD = process P3: CPU/GPU preflight before a treatment line)
#
# Three checks at the fork, one PASS/FAIL summary JSON for the ledger (--out, default ./preflight-NAME.json):
#   premise  for an --explore-kinds line: the policy's own mass on every targeted row at the fork must be below
#            --premise-max (default 1e-6), read from --premise-dump (a metrics/eval dump: .json object with a "pmass"
#            map or explore_pmass[_macro].<kind> keys, or .jsonl whose row update = fork+1, else the first row), or
#            from SMOKE_DIR/metrics.jsonl's first post-fork row once the smoke ran (it measures the fork policy).
#            n/a without --kinds. (Line G would have been refused here: ChooseRestOption mass 0.34 at C u10664.)
#   whatif   the forced-choice what-if (`sts2train eval --force`) paired against greedy at the fork. The commands
#            are PRINTED, never run here; pass the finished `sts2train compare --json` file with --whatif-json and
#            the check reads pairs[0] (force - greedy): PASS when delta >= 0, or when --whatif-justification says
#            why a conditional push is still wanted; FAIL otherwise; pending without the file.
#   smoke    a 10-update numerics smoke of the line's flags from the fork into a scratch run dir (--smoke-dir),
#            through the line driver with ENDS=fork+10 EVALS=0 SYNC=0. PRINTED; executed only with RUN=1. Judged
#            (whenever SMOKE_DIR/metrics.jsonl holds >= 10 post-fork rows) by line_guard.py --once --dry-run
#            --tags smoke: approx_kl median > 0.05, non-finite / exploding loss, sps < 0.7 x --control.
# Verdict: FAIL if any check failed; PASS if every check passed or is n/a; INCOMPLETE otherwise (something pending).
# Exit 0 PASS, 1 FAIL, 3 INCOMPLETE, 2 usage.
#
# Options:
#   --line NAME             line label (required)
#   --fork-ckpt PATH        the fork checkpoint (what-if evals and the smoke)
#   --fork-update N         the fork update (required for premise .jsonl row selection and the smoke)
#   --kinds K1,K2           the line's --explore-kinds (premise targets; default the --explore-kinds in $EXTRA)
#   --premise-dump FILE     pmass dump (see above)
#   --premise-max X         (default 1e-6)
#   --force SEL             what-if selectors (default --kinds)
#   --seedset S             what-if seed set (default silent-a10-dev-v1)
#   --macros M              eval macros (default $MACROS, else the checkpoint's)
#   --whatif-json FILE      finished `sts2train compare GREEDY FORCE --json FILE` result
#   --whatif-justification TEXT
#   --smoke-dir DIR         scratch run dir of the smoke (default /content/sts2/runs/smoke-NAME)
#   --bundle DIR            sts2train bundle for the driver (default /content/sts2/bundle)
#   --driver PATH           line driver (default scripts/colab/lab/w3m_line_v2.sh if present, else scripts/colab/w3m_line.sh)
#   --control DIR           control run dir for the smoke's sps rule
#   --out FILE              summary JSON (default ./preflight-NAME.json)
# Env passed to the smoke driver as the line runs it: MACROS ENT0 ENT1 EXTRA LAST OVERLAP. RUN=1 runs the smoke.
set -u
HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../../.." && pwd)"
usage() { sed -n '2,/^set -u$/p' "$0" | sed '$d' | sed 's/^# \{0,1\}//'; }

LINE=""; FORK_CKPT=""; FORK_U=""; KINDS=""; DUMP=""; PMAX=1e-6; FORCE=""; SEEDSET=silent-a10-dev-v1
EMACROS="${MACROS:-}"; WJSON=""; WJUST=""; SMOKE=""; BUNDLE=/content/sts2/bundle; DRIVER=""; CONTROL=""; OUT=""
while [ $# -gt 0 ]; do
  case "$1" in
    -h|--help) usage; exit 0 ;;
    --line) LINE=$2; shift ;;
    --fork-ckpt) FORK_CKPT=$2; shift ;;
    --fork-update) FORK_U=$2; shift ;;
    --kinds) KINDS=$2; shift ;;
    --premise-dump) DUMP=$2; shift ;;
    --premise-max) PMAX=$2; shift ;;
    --force) FORCE=$2; shift ;;
    --seedset) SEEDSET=$2; shift ;;
    --macros) EMACROS=$2; shift ;;
    --whatif-json) WJSON=$2; shift ;;
    --whatif-justification) WJUST=$2; shift ;;
    --smoke-dir) SMOKE=$2; shift ;;
    --bundle) BUNDLE=$2; shift ;;
    --driver) DRIVER=$2; shift ;;
    --control) CONTROL=$2; shift ;;
    --out) OUT=$2; shift ;;
    *) echo "preflight.sh: unknown argument $1 (see --help)" >&2; exit 2 ;;
  esac
  shift
done
[ -n "$LINE" ] || { echo "preflight.sh: --line is required (see --help)" >&2; exit 2; }
if [ -n "$FORK_U" ] && ! [[ "$FORK_U" =~ ^[0-9]+$ ]]; then echo "preflight.sh: --fork-update must be an integer" >&2; exit 2; fi
if [ -z "$KINDS" ] && [ -n "${EXTRA:-}" ]; then   # the --explore-kinds of the line's EXTRA flags
  KINDS=$(echo "$EXTRA" | sed -n 's/.*--explore-kinds[ =]\([^ ]*\).*/\1/p')
fi
FORCE=${FORCE:-$KINDS}
SMOKE=${SMOKE:-/content/sts2/runs/smoke-$LINE}
OUT=${OUT:-./preflight-$LINE.json}
if [ -z "$DRIVER" ]; then
  [ -f "$HERE/w3m_line_v2.sh" ] && DRIVER="$HERE/w3m_line_v2.sh" || DRIVER="$REPO/scripts/colab/w3m_line.sh"
fi
EV="python3 -m sts2train.cli"
q() { printf '%q' "$1"; }

# ------------------------------------------------------------------------------------------- printed commands
WI_DIR="$SMOKE.whatif"
MAC_ARG=""; [ -n "$EMACROS" ] && MAC_ARG=" --macros $(q "$EMACROS")"
WI_CMDS=""
if [ -n "$FORCE" ]; then
  WI_CMDS="$EV eval --out $(q "$WI_DIR/greedy") --checkpoint $(q "${FORK_CKPT:-FORK_CKPT}") --seedset $(q "$SEEDSET") --policy greedy --breaker off$MAC_ARG
$EV eval --out $(q "$WI_DIR/force") --checkpoint $(q "${FORK_CKPT:-FORK_CKPT}") --seedset $(q "$SEEDSET") --policy greedy --breaker off$MAC_ARG --force $(q "$FORCE")
$EV compare $(q "$WI_DIR/greedy") $(q "$WI_DIR/force") --labels greedy,force --json $(q "$WI_DIR/compare.json")"
fi
SMOKE_END=""; [ -n "$FORK_U" ] && SMOKE_END=$((FORK_U + 10))
SMOKE_ENV="TAG=$(q "smoke-$LINE") SYNC=0 EVALS=0 WAIT_RUN=$(q "$SMOKE") WAIT_MAX_S=0 FORK_U=${FORK_U:-FORK_U} ENDS=${SMOKE_END:-FORK_U+10} LAST=${LAST:-${SMOKE_END:-FORK_U+10}}"
for v in MACROS ENT0 ENT1 EXTRA; do
  [ -n "${!v:-}" ] && SMOKE_ENV="$SMOKE_ENV $v=$(q "${!v}")"
done
SMOKE_CMD="env $SMOKE_ENV bash $(q "$DRIVER") $(q "$BUNDLE") $(q "$SMOKE") a $(q "${FORK_CKPT:-FORK_CKPT}") ${OVERLAP:-off}"

echo "preflight $LINE: fork ${FORK_CKPT:-?} (u${FORK_U:-?}), kinds ${KINDS:-none}"
[ -n "$WI_CMDS" ] && { echo "what-if commands (run on a VM with the bundle, not here):"; echo "$WI_CMDS" | sed 's/^/  /'; }
echo "smoke command (runs only with RUN=1):"; echo "  $SMOKE_CMD"

SMOKE_RAN=0; SMOKE_RC=""
if [ "${RUN:-0}" = 1 ]; then
  if [ -z "$FORK_U" ] || [ -z "$FORK_CKPT" ]; then
    echo "RUN=1 needs --fork-update and --fork-ckpt" >&2; exit 2
  fi
  echo "RUN=1: running the smoke into $SMOKE"
  # word-split SMOKE_ENV on purpose: every value in it is %q-quoted
  eval "$SMOKE_CMD"; SMOKE_RC=$?; SMOKE_RAN=1
  echo "smoke driver exited rc $SMOKE_RC"
fi

GUARD_JSON=""; GUARD_RC=""
if [ -f "$SMOKE/metrics.jsonl" ]; then
  GUARD_JSON=$(mktemp "${TMPDIR:-/tmp}/preflight-guard.XXXXXX")
  GARGS=(--once --dry-run --quiet --tags smoke --json "$GUARD_JSON" --rules "$HERE/guard-rules-default.json")
  [ -n "$FORK_U" ] && GARGS+=(--fork-update "$FORK_U")
  [ -n "$CONTROL" ] && GARGS+=(--control "$CONTROL")
  python3 -I "$HERE/line_guard.py" "$SMOKE" "${GARGS[@]}"; GUARD_RC=$?
fi

# ------------------------------------------------------------------------------------------- the summary JSON
python3 -I - "$OUT" "$LINE" "$FORK_CKPT" "$FORK_U" "$KINDS" "$DUMP" "$PMAX" "$SMOKE" "$WJSON" "$WJUST" \
  "$WI_CMDS" "$SMOKE_CMD" "$SMOKE_RAN" "$SMOKE_RC" "$GUARD_JSON" "$GUARD_RC" <<'PY'
import json, math, sys
from datetime import datetime, timezone
from pathlib import Path
(out, line, fork_ckpt, fork_u, kinds, dump, pmax, smoke, wjson, wjust, wi_cmds, smoke_cmd, smoke_ran, smoke_rc,
 guard_json, guard_rc) = sys.argv[1:17]
fork_u = int(fork_u) if fork_u else None
pmax = float(pmax)
kinds = [k for k in kinds.split(",") if k]


def load(p):
    p = Path(p)
    if p.suffix == ".jsonl":
        rows = []
        for ln in p.read_text(encoding="utf-8").splitlines():
            try:
                r = json.loads(ln)
            except ValueError:
                continue
            if isinstance(r, dict):
                rows.append(r)
        if not rows:
            return None
        if fork_u is not None:
            for r in rows:
                if r.get("update") == fork_u + 1:
                    return r
        return rows[0]
    return json.loads(p.read_text(encoding="utf-8"))


def mass(row, kind):
    pm = row.get("pmass") if isinstance(row.get("pmass"), dict) else None
    if pm is not None and kind in pm:
        return kind, pm[kind]
    for pre in ("explore_pmass_macro.", "explore_pmass."):
        if pre + kind in row:
            return pre + kind, row[pre + kind]
    typ = kind.split(":")[0]   # e.g. BuyEntry:remove (flag grammar) vs BuyEntry:card_removal (metrics key)
    for pre in ("explore_pmass_macro.", "explore_pmass."):
        hits = sorted(k for k in row if k == pre + typ or k.startswith(pre + typ + ":"))
        if len(hits) == 1:
            return hits[0], row[hits[0]]
    return None, None


# premise
premise = {"status": "n/a", "threshold": pmax, "kinds": kinds}
if kinds:
    src = dump or (str(Path(smoke) / "metrics.jsonl") if (Path(smoke) / "metrics.jsonl").exists() else "")
    premise["source"] = src or None
    if not src:
        premise.update(status="pending", reason="no --premise-dump and no smoke metrics yet")
    else:
        try:
            row = load(src)
        except (OSError, ValueError) as e:
            row, premise["reason"] = None, f"unreadable dump: {e}"
        if not isinstance(row, dict):
            premise.update(status="fail", reason=premise.get("reason") or "dump holds no row")
        else:
            vals, bad, missing = {}, [], []
            for k in kinds:
                key, v = mass(row, k)
                if key is None or not isinstance(v, (int, float)):
                    missing.append(k)
                    continue
                vals[k] = {"key": key, "pmass": v}
                if not (math.isfinite(v) and v < pmax):
                    bad.append(f"{k} ({key}) = {v:.3g}")
            premise["values"], premise["row_update"] = vals, row.get("update")
            why = (["target rows already carry policy mass >= %g: %s" % (pmax, "; ".join(bad))] if bad else []) + \
                  (["no mass for " + ", ".join(missing) + " in the dump"] if missing else [])
            premise["missing"] = missing
            if why:
                premise.update(status="fail", reason="; ".join(why))
            else:
                premise["status"] = "pass"

# what-if
whatif = {"status": "n/a", "commands": wi_cmds.splitlines() if wi_cmds else []}
if wi_cmds:
    if not wjson:
        whatif.update(status="pending", reason="commands printed; pass the compare JSON with --whatif-json")
    else:
        try:
            pr = (json.loads(Path(wjson).read_text(encoding="utf-8")).get("pairs") or [])[0]
            d = float(pr["delta_mean"])
            whatif.update(delta=d, ci95=pr.get("t_ci95"), pair=f"{pr.get('b')} - {pr.get('a')}", source=wjson)
            if d >= 0:
                whatif["status"] = "pass"
            elif wjust:
                whatif.update(status="pass", justification=wjust, reason=f"delta {d:+.4f} < 0, justified")
            else:
                whatif.update(status="fail", reason=f"forced choice costs progress (delta {d:+.4f}) and no "
                                                    "--whatif-justification")
        except (OSError, ValueError, KeyError, IndexError, TypeError) as e:
            whatif.update(status="fail", reason=f"unreadable --whatif-json: {e}")

# smoke
smk = {"status": "pending", "command": smoke_cmd, "ran": smoke_ran == "1", "smoke_dir": smoke}
if smoke_rc:
    smk["driver_rc"] = int(smoke_rc)
g = None
if guard_json:
    try:
        g = json.loads(Path(guard_json).read_text(encoding="utf-8"))
    except (OSError, ValueError):
        g = None
if g is not None:
    res = g.get("results") or []
    fired = [r for r in res if r.get("status") == "triggered"]
    judged = [r for r in res if r.get("status") in ("ok", "triggered")]
    smk["guard"] = {"rc": int(guard_rc) if guard_rc else None, "last_update": g.get("last_update"),
                    "fired": [{k: r.get(k) for k in ("rule", "key", "update", "reason")} for r in fired],
                    "results": [{k: r.get(k) for k in ("rule", "key", "status", "reason")} for r in res]}
    n = (g.get("last_update") or 0) - (g.get("fork_update") or 0)
    if fired:
        smk.update(status="fail", reason="; ".join(f"{r['rule']}: {r['reason']}" for r in fired))
    elif n < 10:
        smk.update(status="pending", reason=f"smoke has {n} post-fork updates (needs 10)")
    elif not any(r.get("rule") == "kl_high" and r.get("status") == "ok" for r in judged):
        smk.update(status="pending", reason="kl rule not judged yet")
    else:
        smk["status"] = "pass"
elif smoke_rc and smoke_rc != "0":
    smk.update(status="fail", reason=f"smoke driver rc {smoke_rc} and no metrics")
else:
    smk["reason"] = "not run (RUN=1 runs it)"

checks = {"premise": premise, "whatif": whatif, "smoke": smk}
sts = [c["status"] for c in checks.values()]
verdict = "FAIL" if "fail" in sts else ("PASS" if all(s in ("pass", "n/a") for s in sts) else "INCOMPLETE")
doc = {"kind": "preflight", "line": line, "fork_ckpt": fork_ckpt or None, "fork_update": fork_u,
       "verdict": verdict, "checks": checks, "at": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")}
Path(out).parent.mkdir(parents=True, exist_ok=True)
Path(out).write_text(json.dumps(doc, indent=1) + "\n", encoding="utf-8")
print(f"PREFLIGHT {line}: {verdict} (premise {premise['status']}, whatif {whatif['status']}, smoke {smk['status']})"
      f" -> {out}")
for name, c in checks.items():
    if c.get("reason"):
        print(f"  {name}: {c['reason']}")
sys.exit({"PASS": 0, "FAIL": 1}.get(verdict, 3))
PY
rc=$?
[ -n "$GUARD_JSON" ] && rm -f "$GUARD_JSON"
exit $rc

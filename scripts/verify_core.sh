#!/usr/bin/env bash
# Level-1 verification loop for the game-agnostic recorder core:
#   1. dotnet test (xunit) on mod/tests — includes the FakeGameScenario sandbox,
#      which writes a scripted session into a temp dir via STS2REC_FAKE_SESSION_OUT.
#   2. Python cross-check: sts2rec validate + canonical must accept the
#      C#-written session, and the canonical step count must match the
#      scenario's ground truth.
set -euo pipefail

export DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec
export PATH="/opt/homebrew/opt/dotnet@9/bin:$HOME/.dotnet/tools:$PATH"

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TESTS_PROJ="$REPO_ROOT/mod/tests/Sts2Recorder.Core.Tests.csproj"
EXPECTED_STEPS=5  # FakeGameScenario.ExpectedCanonicalSteps

TMP_DIR="$(mktemp -d /tmp/sts2rec-verify.XXXXXX)"
trap 'rm -rf "$TMP_DIR"' EXIT
FAKE_OUT="$TMP_DIR/out"

fail() {
    echo ""
    echo "=================================================="
    echo "verify_core: FAIL — $1"
    echo "=================================================="
    exit 1
}

echo "== [1/3] dotnet test (xunit, game-agnostic core) =="
STS2REC_FAKE_SESSION_OUT="$FAKE_OUT" dotnet test "$TESTS_PROJ" \
    || fail "dotnet test reported failures"

SESSION_DIR="$(find "$FAKE_OUT/sessions" -mindepth 1 -maxdepth 1 -type d | head -n 1 || true)"
[ -n "$SESSION_DIR" ] || fail "FakeGameScenario did not produce a session under $FAKE_OUT/sessions"
echo ""
echo "sandbox session: $SESSION_DIR"

echo ""
echo "== [2/3] Python cross-check: sts2rec validate =="
cd "$REPO_ROOT/sts2rec"
uv run --with pytest python -m sts2rec.cli validate "$SESSION_DIR" \
    || fail "sts2rec validate rejected the C#-written session"

echo ""
echo "== [3/3] Python cross-check: sts2rec canonical =="
CANONICAL_JSON="$TMP_DIR/canonical.json"
uv run python -m sts2rec.cli canonical "$SESSION_DIR" -o "$CANONICAL_JSON" \
    || fail "sts2rec canonical failed on the C#-written session"

uv run python - "$CANONICAL_JSON" "$EXPECTED_STEPS" <<'PY' || fail "canonical trajectory mismatch"
import json
import sys

path, expected_steps = sys.argv[1], int(sys.argv[2])
with open(path, encoding="utf-8") as handle:
    trajectory = json.load(handle)

steps = trajectory["steps"]
assert len(steps) == expected_steps, (
    f"expected {expected_steps} canonical steps, got {len(steps)}"
)
assert len(trajectory["prelude_events"]) == 5, (
    f"expected 5 prelude events, got {len(trajectory['prelude_events'])}"
)
statuses = [step["info"].get("action_status") for step in steps]
assert statuses == ["cancelled", "executed", "executed", "committed", "committed"], statuses
assert steps[0]["action"] == {"type": "play_card", "params": {"card": "CARD.ZAP", "target": 0}}, (
    steps[0]["action"]
)
assert steps[-1]["terminal"] is True
assert trajectory["meta"]["result"] == {"win": True, "abandoned": False,
                                        "end_time": trajectory["meta"]["result"]["end_time"]}
assert trajectory["meta"]["part"] == 1
print(f"canonical OK: {len(steps)} steps, statuses={statuses}")
PY

echo ""
echo "=================================================="
echo "verify_core: PASS"
echo "  - dotnet test: green"
echo "  - sts2rec validate: accepted C#-written session"
echo "  - sts2rec canonical: $EXPECTED_STEPS steps as expected"
echo "=================================================="

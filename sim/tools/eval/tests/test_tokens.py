from sts2eval.tokens import any_line_token, first_line_token, illegal_message, normalize, resolve

LEGAL = ["play 0 -> 0", "play 1", "potion 2 -> 1", "end", "proceed", "take 0"]


def test_normalize_matches_server_rules():
    assert normalize("PLAY 2->0") == "play 2 -> 0"
    assert normalize("  play   2   ->   0 ") == "play 2 -> 0"
    assert normalize("End Turn") == "end"
    assert normalize("end_turn") == "end"
    assert normalize("play 0 1") == "play 0 -> 1"
    assert normalize("potion 2 1") == "potion 2 -> 1"
    assert normalize(None) == ""


def test_resolve_returns_listed_token_verbatim():
    assert resolve("PLAY 0->0", LEGAL) == "play 0 -> 0"
    assert resolve("play 0 0", LEGAL) == "play 0 -> 0"
    assert resolve("end turn", LEGAL) == "end"
    assert resolve("play 1", LEGAL) == "play 1"
    assert resolve("play 1 -> 0", LEGAL) is None
    assert resolve("play 9", LEGAL) is None
    assert resolve("", LEGAL) is None
    assert resolve(None, LEGAL) is None


def test_resolve_aliases():
    assert resolve("take", LEGAL) == "take 0"
    for exit_word in ("next", "leave", "continue", "proceed"):
        assert resolve(exit_word, LEGAL) == "proceed"
    assert resolve("leave", ["buy 0", "leave"]) == "leave"
    assert resolve("next", ["event 0", "event 1"]) is None


def test_first_line_token_variants():
    assert first_line_token("play 2 -> 0\nBash first.") == "play 2 -> 0"
    assert first_line_token("\n\n  end  \nreason") == "end"
    assert first_line_token("TOKEN: play 1\nwhy") == "play 1"
    assert first_line_token("token:play 1") == "play 1"
    assert first_line_token("`play 1`") == "play 1"
    assert first_line_token("**end**") == "end"
    assert first_line_token("```\nplay 0 -> 0\n```") == "play 0 -> 0"
    assert first_line_token("end.") == "end"
    assert first_line_token("") is None
    assert first_line_token(None) is None


def test_only_first_line_counts_for_first_line_token():
    # first_line_token keeps the contract (token on the first line); any_line_token is the fallback
    assert resolve(first_line_token("I think we should\nend"), LEGAL) is None


def test_any_line_token_takes_the_last_legal_line():
    reply = "Incoming 14 damage. Bash then Strike kills it.\n\nplay 1 -> 0\nBash first."
    assert any_line_token(reply, ["play 0", "play 1 -> 0", "end"]) == ("play 1 -> 0", 2)
    # prose around a token does not count; the bare 'end' line is the last resolving line
    assert any_line_token("end\nActually no: play 0", ["play 0", "end"]) == ("end", 0)
    assert any_line_token("`end`\n", ["play 0", "end"]) == ("end", 0)
    assert any_line_token("no token here", ["play 0", "end"]) is None
    assert any_line_token(None, ["end"]) is None


def test_illegal_message_uses_server_wording():
    m = illegal_message("Play 9", LEGAL)
    assert "'Play 9' is not a legal token here (normalised 'play 9')" in m
    assert "no token" in illegal_message(None, LEGAL)
    assert "no legal actions" in illegal_message("end", [])

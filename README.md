# hack-sts2

Passive full-trajectory recorder for **Slay the Spire 2** — the STS2 counterpart of
[hack-balatro](https://github.com/Alchemist-X/hack-balatro)'s real-client pipeline.

While you play the game normally, a mod records **every state and every action** to
local, versioned, append-only artifacts, for building an AI benchmark dataset.

> Research project. Data-cooperation agreement with the developer is in place;
> the game copy is legally owned. Recording is local-only — nothing is uploaded.

## How it works

- **`mod/`** — a C# mod (`Sts2Recorder.dll`) loaded by STS2's *official* first-party
  mod loader. Harmony postfix patches on the game's human-input funnels capture the
  exact action stream; the game's native `CombatHistory` provides fine-grained combat
  events (true draw order, damage, energy); a throttled snapshotter writes full
  hash-deduped state JSON at every decision point. At run end it archives the game's
  own `.run` summary and `latest.mcr` deterministic replay alongside our streams.
- **`sts2rec/`** — a Python CLI for the offline side: list/validate sessions, tail a
  live run, convert sessions into hack-balatro-style canonical trajectories.

State serialization is adapted from [STS2MCP](https://github.com/Gennadiyev/STS2MCP)
(MIT) with its action-injection and HTTP surface removed — this mod is
**observation-only, write-only-to-disk** (`affects_gameplay: false`).

Full architecture and format spec: [docs/design.md](docs/design.md).

## Session layout

```
<output_root>/sessions/<start_unix>-<seed>/
  manifest.json    # schema/game/mod versions, seed, character, result
  states.jsonl     # full state snapshots at stable decision points
  actions.jsonl    # exact human actions (Harmony-hooked, not diff-inferred)
  events.jsonl     # combat history: draws, damage, energy, … (privileged channel)
  native/          # the game's own .run summary + .mcr replay for cross-validation
```

Default `<output_root>`: `~/Library/Application Support/Sts2Recorder` (macOS).

## Install (macOS)

```bash
brew install dotnet@9
./scripts/install_mod.sh   # builds mod against your game install, copies into mods/
```

Then launch STS2 with "Load with Mods" and accept the mod-consent dialog once.
Play normally; sessions appear under the output root.

```bash
cd sts2rec && uv run sts2rec sessions      # list recorded runs
uv run sts2rec validate <session-dir>      # schema + consistency checks
uv run sts2rec canonical <session-dir>     # emit canonical trajectory JSON
```

## Version compatibility

STS2 is Early Access and moves fast. Every hook installs independently and degrades
gracefully: after a game update, a missing method disables that one hook (recorded in
`manifest.degraded_hooks`) while polling snapshots keep the session usable. Every
manifest stamps the exact game version/commit/build. See
[docs/design.md](docs/design.md#version-drift-policy-early-access-reality).

## License

MIT — see [LICENSE](LICENSE). Contains code adapted from STS2MCP (MIT, © 2026
Yikun Ji).

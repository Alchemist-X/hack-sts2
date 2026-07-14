# Research notes (2026-07-14)

Condensed findings that drove the design. Four parallel investigations: the
hack-balatro recorder architecture, the STS2MCP mod, the local game install, and
the community ecosystem.

## What hack-balatro taught us

Its real-client pipeline (Lovely injector → Steamodded → BalatroBot JSON-RPC on
localhost:12346) records human play by **polling at 5 Hz and diffing states**
(`scripts/experimental/observe_real_play.py`), because Balatro has no sanctioned
mod-side event hooks. Documented costs of that approach:

- `cards_played` dropped on fast inputs (<200 ms) even at 5 Hz;
- round-boundary counter resets produced phantom discard events;
- every derived action carries `reconstructed: true` / `action_approximate: true`.

Its lasting contribution we keep: the **canonical trajectory schema**
(`{meta, steps[{state_before, action, state_after, reward, terminal, info}]}`,
`env/canonical_trajectory.py` + `docs/canonical_trajectory_schema.md` in
hack-balatro) and the per-session directory layout with append-only `events.jsonl`
plus periodic full snapshots.

STS2 lets us do strictly better: official mod loader, in-process C#, Harmony
shipped with the game — actions can be **hooked exactly**, not inferred.

## STS2 facts (verified locally on this machine)

- Install: `~/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/`
  (Steam app 2868840). Engine: custom Godot fork "MegaDot v4.5.1.m.8.mono", .NET 9.
  Managed code: `SlayTheSpire2.app/Contents/Resources/data_sts2_macos_arm64/sts2.dll`
  (+ `GodotSharp.dll`, and `0Harmony.dll` 2.4.2 ships with the game).
- Installed build: v0.99.1, commit 7ac1f450, Steam buildid 22340209; an update is
  queued (AutoUpdateBehavior=1 ⇒ applies on next launch). Live stable in July 2026
  is v0.107.1 — version drift is a first-class design constraint.
- **Native run summaries already exist**: `<userdata>/steam/<steamid>/profileN/
  saves/history/<start_unix>.run` — plain pretty-printed JSON, RunHistory
  schema_version 8: seed (string, e.g. `K7E4UUKHNZ`), build_id, per-act
  `map_point_history` (card_choices with was_picked, hp/gold deltas, encounters,
  turns_taken), full final deck/relics/potions, win/abandoned. Floor-level only —
  no per-turn detail. 37 real samples on this machine.
- **Native replay**: `profileN/replays/latest.mcr` — binary deterministic command
  stream (header: game version + commit + ModelIdSerializationCache hash), used for
  multiplayer sync. Only the latest is kept, overwritten every run ⇒ must be
  archived at run end. Not yet decoded; treated as opaque ground-truth artifact.
- Saves are Steam-cloud-synced; recorder output must live outside the game's
  user-data dir (we use `~/Library/Application Support/Sts2Recorder`).
- The game uploads its own run metrics to Mega Crit (`NGameInfoUploader`), i.e.
  run-history telemetry exists first-party; our recording stays local regardless.

## STS2MCP (upstream v0.4.0, MIT, ~435 stars)

- Loading: official mod system — `[ModInitializer("Initialize")]` attribute from
  `MegaCrit.Sts2.Core.Modding`; DLL + `<ModId>.json` manifest in `mods/`
  (macOS: `SlayTheSpire2.app/Contents/MacOS/mods/`); consent dialog on first load;
  mods load at startup only.
- State access is direct public API, no reflection: `RunManager.Instance.
  DebugOnlyGetState()`, `CombatManager.Instance.DebugOnlyGetState()`,
  `LocalContext.GetMe(runState)`, UI singletons (`NMapScreen.Instance`, …).
- Its `StateBuilder` (~2.4k lines) serializes every screen to JSON. Two known side
  effects to strip for passive use: shop `merchUI.OpenInventory()` and treasure
  `chestButton.ForceClick()`.
- Purely pull-based (HTTP polling); no recording. Upstream issue #91 requests
  exactly a research-grade recorder (unclaimed). Issues #28/#49/#114 catalogue
  state-coverage gaps and update breakage — used as our checklist.
- **Compile check done here**: upstream v0.4.0 builds with 0 errors against the
  installed v0.99.1 assemblies (macOS, dotnet@9). API drift is manageable.
- Hook surface confirmed in `sts2.dll` (strings + decompile): human-action funnels
  (`ActionQueueSynchronizer.RequestEnqueue`, `PlayerCmd.EndTurn`,
  `NMapScreen.OnMapPointSelectedLocally`, reward/event/rest button handlers) and a
  native combat event log (`MegaCrit.Sts2.Core.Combat.History.Entries`:
  CardPlayStarted/Finished, CardDrawn, CardDiscarded, BlockGained, DamageReceived,
  EnergySpent, PotionUsed, …) — see `docs/hook-map.md` for the verified map.

## Ecosystem / prior art

- StS1: CommunicationMod + spirecomm defined the "full JSON state at every stable
  state" protocol used by a decade of research (Orak benchmark, Language-Driven
  Play). StS1's native `.run` files have the same floor-level limitation as STS2's.
- Trajectory-format precedent: NetHack NLE ttyrec3, MineRL/BASALT paired
  jsonl+video, balatrollm/balatrobench producer-analyzer split → per-run directory,
  append-only JSONL streams, explicit schema versioning, raw capture + offline
  derived views.
- Other STS2 AI projects: STS2-Agent (competing state exporter), sts2-llm,
  spirescope (run tracker). None does passive full-trajectory recording.
- Mega Crit is explicitly pro-modding (built-in loader since day 1, Steam Workshop
  official since v0.107.1, example-mods repo on their GitLab). StS1 bot/mod
  research went unopposed for a decade.

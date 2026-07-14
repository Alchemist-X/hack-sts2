# Hook Map (game v0.99.1, commit 7ac1f450)

> **NOTE (2026-07-14):** the game moved to **v0.107.1** and the mod (v0.2.0) has been updated —
> see the "**v0.107.1 drift addendum**" at the bottom. The body below is kept intact as the
> v0.99.1 baseline; where the addendum contradicts it, the addendum wins.

Definitive hook map for the Sts2Recorder mod. Every entry below was proposed by a
static-analysis pass over the decompiled tree and then **adversarially re-verified against the
decompiled source**. Only `confirmed` and `corrected` hooks are included; corrections from the
verification pass override the original proposals. Anything not fully proven is flagged
**UNVERIFIED** inline. The mod wraps every patch/subscription in its own try/catch regardless.

Decompiled source root (this machine):
`/private/tmp/claude-501/-Users-Aincrad-dev-proj/f08abf1e-4d55-4d44-a828-e4fef5f57aa5/scratchpad/sts2-decomp/`
Note: on-disk paths use lowercase `MegaCrit/sts2/...`; namespaces are `MegaCrit.Sts2.*`. macOS is
case-insensitive, but scripts on case-sensitive filesystems must use the lowercase spelling.

---

## Mod system facts (verified)

- **Load path**: `<game executable dir>/mods` — NOT `user://`. Scanned **recursively**
  (`ModManager.ReadModsInDirRecursive`, ModManager.cs:63-69). Second source: Steam Workshop
  subscriptions (app id 2868840). No other search path.
- **Manifest**: ANY `*.json` file anywhere in the mods tree is parsed as a manifest (no fixed
  filename). Required field: `"id"` (unique; duplicates refuse to load). Optional: `name`,
  `author`, `description`, `version`, `has_dll` (default false), `has_pck` (default false),
  `dependencies` (topo-sorted; missing deps only log), `affects_gameplay` (**default TRUE**).
- **`"affects_gameplay": false` is MANDATORY** for the recorder manifest. It excludes the mod from
  the multiplayer mod-mismatch check (`ModManager.GetGameplayRelevantModNameList`, filter at
  ModManager.cs:533-542; enforced in JoinFlow.cs:82-95). Omitting it blocks co-op joins with
  `ConnectionFailureReason.ModMismatch`. A pure recorder (no `ModHelper.AddModelToPool`) also
  leaves `ModelIdSerializationCache.Hash` unchanged, passing the second MP gate.
- **DLL naming**: `<id>.dll` beside the manifest, with `"has_dll": true`
  (ModManager.TryLoadMod:407). Loaded via `AssemblyLoadContext.GetLoadContext(game asm)
  .LoadFromAssemblyPath` — **the game's own ALC**, so mod code sees game types directly and
  Harmony patching works. `AppDomain.AssemblyResolve` shim redirects `sts2,...` and `0Harmony,...`
  loads to the game's assemblies: compile against them, do not ship them.
- **Harmony is bundled and first-class**: if the DLL has NO `[ModInitializer]` class, the loader
  auto-runs `new Harmony((author ?? "unknown") + "." + id).PatchAll(assembly)`
  (ModManager.cs:466-468). If a `[ModInitializer]` class EXISTS, PatchAll is **not** called — the
  initializer must call it itself.
- **`[ModInitializer("MethodName")]` contract**: class-level attribute; named method must be
  STATIC, parameterless (invoked `method.Invoke(null, null)`). Multiple initializer classes all
  fire.
- **Init timing**: initializers fire inside `OneTimeInitialization.ExecuteEssential`:
  after settings load, **before** `LocManager`, `ModelDb`, profile data, and long before
  `RunManager` has a run. Restrict the initializer to: `PatchAll`, subscribing static events
  (`RunManager.Instance.RunStarted`, `CombatManager.Instance.*`, `ModManager.OnModDetected/
  OnMetricsUpload`). Do not touch run/model/loc state — it will crash.
- **No runtime loading**: after `Initialize`, `_initialized=true`; mods only load at startup.
  Consent flow: settings keys `mods_enabled` (global one-time consent; granting it quits the
  game) and `mod_list[].is_enabled` (per-mod, default true). CLI `nomods` and TestMode block all
  loading.
- **Side effects of running modded** (document for users):
  1. Saves relocate to `modded/profile{N}` (`UserDataPathProvider.IsRunningModded`) — separate
     progress tree from vanilla.
  2. Sentry crash reporting disabled.
  3. Official metrics upload skipped, replaced by `ModManager.OnMetricsUpload`.
- **Recorder output must NOT live under `mods/`** — every `.json` there is parsed as a manifest
  (id-less ones log errors). Write to `user://` paths resolved at runtime via
  `SaveManager.Instance.GetProfileScopedPath(...)` / `UserDataPathProvider` — never hardcode,
  because the modded/ prefix changes the tree.

---

## Session lifecycle hooks

All RunManager members are on the eager static singleton `RunManager.Instance`
(RunManager.cs:61). Per-run objects (`ActionQueueSet`, `PlayerChoiceSynchronizer`,
`ChecksumTracker`, `CombatReplayWriter`, all synchronizers) are **recreated in
`InitializeShared` and disposed in `CleanUp`** — resubscribe on every `RunStarted`.

### Run start / resume

| Purpose | Target | Signature | Mechanism |
|---|---|---|---|
| New SP run (committed embark; final character/ascension/seed) | `RunManager.SetUpNewSinglePlayer` (RunManager.cs:204) | `public void SetUpNewSinglePlayer(RunState state, bool shouldSave, DateTimeOffset? dailyTime = null)` | harmony_postfix |
| New MP run | `RunManager.SetUpNewMultiPlayer` (:218) | `public void SetUpNewMultiPlayer(RunState state, StartRunLobby lobby, bool shouldSave, DateTimeOffset? dailyTime = null)` | harmony_postfix |
| Resume SP run from save | `RunManager.SetUpSavedSinglePlayer` (:231) | `public void SetUpSavedSinglePlayer(RunState state, SerializableRun save)` | harmony_postfix |
| Resume MP run | `RunManager.SetUpSavedMultiPlayer` (:244) | `public void SetUpSavedMultiPlayer(RunState state, LoadRunLobby lobby)` | harmony_postfix |
| Run launched (UI up, RunState final) — **the primary attach point** | `RunManager.RunStarted` (:192, fired in `Launch()` at :486) | `public event Action<RunState>? RunStarted;` | event_subscribe |

- `RunStarted` fires for **new, resumed, MP, and replay-playback** runs. Inside the handler,
  `NetService` is already valid — apply the multiplayer/replay guard there.
- **EXCLUDE** `SetUpReplay(RunState, CombatReplay)` (:257) — replay playback, not human play.
  Detect via `RunManager.Instance.NetService.Type == NetGameType.Replay`.
- `RunManager.State` is **private** (:173) — cache the `RunState` handed to `RunStarted`; do not
  use `DebugOnlyGetState()` (:1412, debug-named, version-fragile).
- **Seed**: `state.Rng` is `RunRngSet` — `state.Rng.StringSeed` (canonical 10-char base-34 string,
  alphabet `0123456789ABCDEFGHJKLMNPQRSTUVWXYZ`) and `state.Rng.Seed` (derived uint).
- **Session key**: `StartTime` (Unix seconds, fixed at first creation, threaded through every
  save/resume; also the `{StartTime}.run` history filename). Use `StartTime + seed` as the
  recorder's session key (StartTime alone can theoretically collide). Resume across restarts is
  detected as `SetUpSaved*` with an already-known StartTime (`save.StartTime`).
- Embark UI click `NCharacterSelectScreen.OnEmbarkPressed` (:448) re-enters itself once via the
  FTUE tutorial callback — do not hook it for run-start; use the SetUp* methods.

### Run end / abandon

| Purpose | Target | Signature | Mechanism |
|---|---|---|---|
| Run ends in-game (win/death/in-run abandon), returns final `SerializableRun` | `RunManager.OnEnded` (RunManager.cs:1242) | `public SerializableRun OnEnded(bool isVictory)` | harmony_postfix — **LATCH FIRST CALL** |
| Catch-all terminal event incl. main-menu abandon | `RunHistoryUtilities.CreateRunHistoryEntry` (RunHistoryUtilities.cs:16) | `public static void CreateRunHistoryEntry(SerializableRun run, bool victory, bool isAbandoned, PlatformType platformType)` | harmony_postfix |
| End-of-run summary while modded | `ModManager.OnMetricsUpload` (ModManager.cs:45) | `public static event MetricsUploadHook? OnMetricsUpload;` / `public delegate void MetricsUploadHook(SerializableRun run, bool isVictory, ulong localPlayerId);` | event_subscribe |
| Abandon intent (in-run, initiating machine) | `RunManager.Abandon` (:1131) | `public void Abandon()` | harmony_prefix |
| Session teardown — final flush window | `RunManager.CleanUp` (:1203) | `public void CleanUp(bool graceful = true)` | harmony_prefix (**prefix, not postfix** — the finally block nulls `State` and `LocalContext.NetId`) |

**Critical corrections (from verification):**

- **`OnEnded` fires TWICE on victory.** `WinRun` (:1017) calls `OnEnded(true)`, then
  `GuaranteeKillAllPlayers -> CreatureCmd.Kill` (CreatureCmd.cs:319) calls `OnEnded(isVictory:
  false)` again once all players are dead. The `_runHistoryWasUploaded` guard (:1253) only
  suppresses internal side effects, not the method body. **The postfix must latch on the first
  invocation per run and treat it as authoritative.** Disambiguate abandon-vs-death via
  `RunManager.IsAbandoned` inside the postfix.
- `OnEnded`'s history/metrics work only runs when `ShouldSave` is true; replay/test runs never
  reach it. `OnMetricsUpload` additionally requires `LoadedMods.Count > 0`, prefs `UploadData`
  true, non-editor/release-info present, and is skipped for abandoned runs — **never the sole
  recording mechanism**, just a convenient final summary.
- **`Abandon()` is NOT human-only** and NOT complete: (a) AutoSlayer's `AbandonRunAsync` reaches
  it (debug builds); (b) the Trial event's "Double Down" option routes through the same confirm
  popup; (c) **main-menu abandons bypass it entirely** — `NMainMenu.AbandonRun()` (NMainMenu.cs:549)
  and `NMultiplayerSubmenu.TryAbandonMultiplayerRun()` (NMultiplayerSubmenu.cs:165) call
  `CreateRunHistoryEntry` directly. **Hook `CreateRunHistoryEntry` for complete terminal
  coverage.** MP client-side abandon arrives via `IRunLobbyListener.RunAbandoned` (:1144).
- Inside `OnEnded`, `current_run.save` is deleted (SP/Host). To archive the final save, prefix
  `OnEnded` or use the postfix's returned `SerializableRun`.
- `CleanUp` is **not called on process kill** — flush opportunistically on save heartbeats (below).

### Save / replay file writes

| Purpose | Target | Signature | Mechanism |
|---|---|---|---|
| Mid-run save heartbeat (recorder checkpoint/flush trigger) | `SaveManager.Instance.Saved` (SaveManager.cs:114-124) | `public event Action? Saved;` — public forwarding event onto `RunSaveManager.Saved`; fires after the file write completes | event_subscribe (**no reflection needed** — correction over the proposal) |
| Same, patch form | `RunSaveManager.SaveRun` (RunSaveManager.cs:73) | `public async Task SaveRun(AbstractRoom? preFinishedRoom)` | harmony (async: a plain postfix runs at first await — prefer the `Saved` event) |
| `.run` history file hits disk | `RunHistorySaveManager.SaveHistory` (RunHistorySaveManager.cs:48) | `public void SaveHistory(RunHistory history)` | harmony_postfix |
| `latest.mcr` flushed — archive point | `CombatReplayWriter.WriteReplay` (Core/Multiplayer/Replay/CombatReplayWriter.cs:157) | `public void WriteReplay(string filePath, bool stopRecording)` | harmony_postfix |

- Save paths: `saves/current_run.save` (SP) / `current_run_mp.save` (MP host) under the profile
  dir; JSON `SerializableRun` with a `.backup` sibling. Fires on every map-point entry, post-combat
  pre-finish, mid-event checkpoints (EventRoom.cs:125), and initial save at run start. No-ops when
  `!ShouldSave` or NetService.Type is not Singleplayer/Host (MP **clients never save**).
- History file: `profile{N}/saves/history/{StartTime}.run`, JSON; `RunHistory.Seed` is the
  **string** seed. `SaveHistory` can also fire outside a live run end (menu abandon of a stale
  save). Cloud store may prune the history dir to 100 files / 5 MiB.
- **`WriteReplay` correction: NOT every combat.** It fires on the combat **win/normal-completion**
  path (`CombatManager.EndCombatInternal`, CombatManager.cs:631-632), on MP desync
  (`RunManager.StateDiverged`), and via the GetLogs console cmd. **Combat losses go through
  `ProcessPendingLoss`, which fires `CombatEnded` but never flushes latest.mcr.** Also no-ops when
  `CombatReplayWriter.IsEnabled` is false (TestMode). `latest.mcr` is per-combat and overwritten;
  path = `GetProfileScopedPath("replays/latest.mcr")`. Recording re-arms every room entry via
  `RecordInitialState(ToSave(null))` — each .mcr contains ONE combat from a full run snapshot.
  **Conclusion: record the live event stream yourself (below); archive .mcr only as a bonus.**

---

## Action capture hooks (human decision funnels)

Architecture fact (verified): the game has a clean local/remote split. Locally-initiated synced
actions all pass `ActionQueueSynchronizer.RequestEnqueue` (remote actions arrive via message
handlers and bypass it). Non-action decisions go through `Local`-suffixed synchronizer methods.
`LocalContext.NetId` / `LocalContext.IsMe(Player?)` (Core/Context/LocalContext.cs:63) is the
canonical local-player test.

**Provisional-until-executed rule (applies to all enqueued combat actions):** an enqueued
`PlayCardAction`/`UsePotionAction`/`EndPlayerTurnAction` can still be game-cancelled before
execution (card left hand, target died, end-turn `StartCancellingAllPlayerDrivenCombatActions`,
combat end). Finalize every record via the public events `GameAction.BeforeExecuted` /
`GameAction.BeforeCancelled` (GameAction.cs:39/41).

### Umbrella funnel

| | |
|---|---|
| Target | `ActionQueueSynchronizer.RequestEnqueue` (Core/GameActions/Multiplayer/ActionQueueSynchronizer.cs:104) |
| Signature | `public void RequestEnqueue(GameAction action)` |
| Mechanism | harmony_prefix |
| Fires on | ONLY the initiating (local) machine. Carries: `PlayCardAction`, `EndPlayerTurnAction`, `UndoEndPlayerTurnAction`, `UsePotionAction`, `DiscardPotionGameAction`, `VoteForMapCoordAction`, `PickRelicAction`, `VoteToMoveToNextActAction`. Also **non-human** sources: `ReadyToBeginEnemyTurnAction` (CombatManager.cs:740, automatic), `MoveToMapCoordAction` (host vote resolution), `ConsoleCmdGameAction` (DevConsole), STS2MCP injections, AutoSlay potion uses. |
| Cancel/undo | **RE-ENTRY**: actions deferred during the enemy turn are re-passed through RequestEnqueue at PlayPhase start (`SetCombatState`, :88) — dedupe by object identity. Finalize via `BeforeExecuted`/`BeforeCancelled`. |
| Gaps | `GenericHookGameAction` and player-choice resumption bypass it via `RequestEnqueueHookAction` / `RequestResumeActionAfterPlayerChoice`. |
| Confidence | confirmed |

Recommended spine: prefix `RequestEnqueue` (type-switch on the action), subscribe each action's
`BeforeExecuted`/`BeforeCancelled`, and use the per-decision hooks below only where
human-vs-programmatic context is needed.

### Per-decision hooks

| Decision | Target | Exact signature | Mechanism | Fires on | Cancel/undo notes | Conf. |
|---|---|---|---|---|---|---|
| Play card (committed human play, post-targeting) | `CardModel.TryManualPlay` (Core/Models/CardModel.cs:1379) | `public bool TryManualPlay(Creature? target)` | harmony_postfix, record only if `__result == true` | Human UI only (sole caller `NCardPlay.TryPlayCard`, NCardPlay.cs:144). NOT fired for remote co-op players, card-effect autoplays (`CardCmd.AutoPlay`), AutoSlay, or STS2MCP (which calls `RequestEnqueue(new PlayCardAction(...))` directly). | Targeting cancels diverge earlier (`CancelPlayCard`). Enqueue ≠ execution — finalize via GameAction events. `PlayCardAction` exposes `Player`, `CardModelId`, `NetCombatCard` (incl. `CombatCardIndex`), `TargetId`. | confirmed |
| End turn / un-end turn (co-op toggle) | `NEndTurnButton.CallReleaseLogic` (Core/Nodes/Combat/NEndTurnButton.cs:418) | `public void CallReleaseLogic()` | harmony_prefix | Human click/long-press/controller only. In prefix read `CombatManager.Instance.IsPlayerReadyToEndTurn(...)`: false → `EndPlayerTurnAction`; true → `UndoEndPlayerTurnAction` (co-op un-ready). Guarded by `CanTurnBeEnded` (no-op presses exist — mirror the guard). Also patch `SecretEndTurnLogicViaFtue` (:440, tutorial path). | Do NOT hook `PlayerCmd.EndTurn` as the human funnel (runs on every machine for every player; also AutoSlay/MCP). Irrevocable commit = `ReadyToBeginEnemyTurnAction` enqueue (CombatManager.cs:740). `EndPlayerTurnAction` is ignored if round changed. | confirmed |
| Un-end turn execution (the only true user undo) | `UndoEndPlayerTurnAction.ExecuteAction` (Core/GameActions/UndoEndPlayerTurnAction.cs) | `protected override Task ExecuteAction()` (ctor `public UndoEndPlayerTurnAction(Player player, int combatRound)`) | harmony_postfix (AccessTools; `_player` is private readonly — reflect or record all players) | Executes on every machine for the un-readying player. Never fires in SP. | Invalidates a prior end-turn record; record first-class, don't collapse. | confirmed |
| Use potion (committed, post-targeting) | `PotionModel.EnqueueManualUse` (Core/Models/PotionModel.cs:211) | `public void EnqueueManualUse(Creature? target)` | harmony_prefix | Local machine only: NPotionHolder.cs:342 (self-target — passes `Owner.Creature`, not null) and :409 (post-targeting), AutoSlay, STS2MCP. Remote uses arrive via net. Filter `LocalContext.IsMe(__instance.Owner)`. | Queued use cancellable (`AfterUsageCanceled`, invoked from UsePotionAction.cs:129) — finalize via GameAction events. Non-combat use goes through the same method. | confirmed |
| Discard potion | `NPotionPopup.OnDiscardButtonPressed` (Core/Nodes/Potions/NPotionPopup.cs:260) | `private void OnDiscardButtonPressed(NButton _)` | harmony_prefix | Human only (popup Discard button). MCP bypasses via `PotionCmd.Discard` (PotionCmd.cs:48) — that layer fires for local AND remote (from `DiscardPotionGameAction.ExecuteAction`), so filter IsMe if hooking there. | Popup close (right-click/cancel in `_Input` :276) is the back-out and never reaches this. | confirmed |
| Map node selection / co-op map vote | `NMapScreen.OnMapPointSelectedLocally` (Core/Nodes/Screens/Map/NMapScreen.cs:633) | `public void OnMapPointSelectedLocally(NMapPoint point)` | harmony_prefix | Local human click (+ MCP direct call; AutoSlay via ForceClick, debug builds only). Remote votes hit `MapSelectionSynchronizer.PlayerVotedForMapCoord` instead. Re-clicking own voted node just pings (co-op). | SP: vote = instant commit (1 voter). Co-op: provisional until ALL vote, host RNG picks; votes auto-cancelled on map regen. Committed destination: postfix `MoveToMapCoordAction.ExecuteAction` (every machine, one shared destination; runs at commit, before travel anim) or `NMapScreen.TravelToMapCoord(MapCoord)` (:703). | confirmed |
| Event / ancient event option | `NEventRoom.OptionButtonClicked` (Core/Nodes/Rooms/NEventRoom.cs:214) | `public void OptionButtonClicked(EventOption option, int index)` | harmony_prefix | Human only (NEventOptionButton press; MCP via ForceClick). Check `option.IsLocked` (early-return). `IsProceed` options run `option.Chosen()` directly and **bypass the synchronizer** — this hook is the only capture for proceed clicks. Real choices route to `EventSynchronizer.ChooseLocalOption(int)` (EventSynchronizer.cs:184). | Shared party events are VOTES (host RNG after all vote); per-player events commit immediately. Committed option for local+remote executes in private `EventSynchronizer.ChooseOptionForEvent(Player, int)` (:227, patchable) — filter IsMe. Bespoke UIs (CrystalSphere grid, FakeMerchant, ancient dialogue-advance hitbox) do NOT pass here; FakeMerchant goes through the MerchantEntry hook. | confirmed |
| Event choice → game's own history append | `EventSynchronizer.SaveEventOptionToHistory` (EventSynchronizer.cs:244; entry built :250, appended :262) | `private void SaveEventOptionToHistory(Player player, EventOption option)` | harmony_postfix | **Corrected**: fires in singleplayer (statically proven — EventSynchronizer constructed unconditionally at RunManager.cs:302) AND for **remote** players' choices in co-op — filter on the `player` arg. Options with `ShouldSaveChoiceToHistory == false` (EventOption.cs:149) never reach it. | Writes `EventOptionHistoryEntry { LocString Title; Dictionary<string,object>? Variables }`. Patch this private sync method, not the async dispatchers. | corrected |
| Rest site option | `RestSiteSynchronizer.ChooseLocalOption` (Core/Multiplayer/Game/RestSiteSynchronizer.cs:110) | `public Task<bool> ChooseLocalOption(int index)` | harmony_postfix — **await `__result`**, record only `true` | Local player only (sole caller NRestSiteButton.cs:195). Remote via OptionIndexChosenMessage. No voting. | `false` = sub-flow backed out (e.g. closed the smith upgrade screen). Net message is sent BEFORE the sub-flow resolves — message send ≠ commitment. Passive alternative: `public event Action<RestSiteOption, bool, ulong> AfterPlayerOptionChosen` (:54, fires local+remote with success flag; filter playerId == LocalContext.NetId). Concrete card picked for smith/mend arrives via `SyncLocalChoice`. | confirmed |
| Shop purchase (card/relic/potion) | `MerchantEntry.OnTryPurchaseWrapper` (Core/Entities/Merchant/MerchantEntry.cs:65) | `public async Task<bool> OnTryPurchaseWrapper(MerchantInventory? inventory, bool ignoreCost = false)` | harmony — async: prefix works on the stub; a postfix must chain on the returned `Task<bool>` | Local machine only (shops per-player): NMerchantCard.cs:144, NMerchantRelic.cs:124, NMerchantPotion.cs:108, AutoSlay (debug), MCP, and LordsParasol relic free purchases (`ignoreCost:true` — tag as non-human). | **MUST ALSO PATCH** the *hiding* (not overriding) overload `MerchantCardRemovalEntry.OnTryPurchaseWrapper(MerchantInventory?, bool, bool cancelable = true)` (MerchantCardRemovalEntry.cs:33) — card-removal service, cancellable (returns false if deck screen backed out). Await the Task<bool>: false = failed/cancelled. Item identity: cast `__instance` to `MerchantCardEntry` (`.CreationResult.Card`) / `MerchantRelicEntry` (`.Model`) / `MerchantPotionEntry`. "Leave shop" has no command — next decision is the map click. | confirmed |
| Rewards screen: claim a reward | `Reward.OnSelectWrapper` (Core/Rewards/Reward.cs:80) | `public async Task<bool> OnSelectWrapper()` | harmony — await the returned Task<bool>; non-virtual, one patch covers all subclasses | Local player only. Callers: NRewardButton.cs:184 (human/MCP), RewardsSet.cs:116 (auto-claim chains), Draft.cs:32 (run-start modifier auto-claim) — tag programmatic sources. | `false` = flow backed out (potion belt full, re-openable card screen closed). Explicit whole-screen skip: **`Reward.OnSkipped` is virtual and overridden** — patching the base does NOT intercept overrides; patch each: CardReward.cs:217, PotionReward.cs:99, SpecialCardReward.cs:93, RelicReward.cs:104, LinkedRewardSet.cs:58. | confirmed |
| Card reward pick/skip (final) | `RewardSynchronizer.SyncLocalObtainedCard` (Core/Multiplayer/Game/RewardSynchronizer.cs:74) | `public void SyncLocalObtainedCard(CardModel card)` — companions at :79-168: `SyncLocalSkippedCard(CardModel)`, `SyncLocalObtainedRelic/SkippedRelic(RelicModel)`, `SyncLocalObtainedPotion/SkippedPotion(PotionModel)`, `SyncLocalObtainedGold(int)`, `SyncLocalGoldLost(int)` | harmony_prefix | Local committed acquisitions only; fires strictly after the pick is final (card already in deck). Callers: CardReward/SpecialCardReward/RelicReward/GoldReward/PotionReward **and merchant purchases** (MerchantCardEntry.cs:139-140, MerchantRelicEntry.cs:71-72, **MerchantPotionEntry.cs:86-87**) — dedupe against the shop hook via `RunState.CurrentRoom` type. Never remote. | Game also writes `CardChoiceHistoryEntry(card, wasPicked)` for every offered card into `CurrentMapPointHistoryEntry` — a passive recorder can read that instead. | confirmed |
| ALL card/relic-selection overlay confirms (upgrade/transform/remove/enchant/duplicate deck screens, in-combat hand picks, choose-a-card/bundle/relic, Mend target) | `PlayerChoiceSynchronizer.SyncLocalChoice` (Core/GameActions/Multiplayer/PlayerChoiceSynchronizer.cs:63) | `public void SyncLocalChoice(Player player, uint choiceId, PlayerChoiceResult result)` | harmony_prefix — or better, the passive event below | Local committed choices only; all 12 call sites post-confirm (CardSelectCmd.cs 137/178/221/256/293/347/399/441/495/519, RelicSelectCmd.cs:44, MendRestSiteOption.cs:106). Remote via `WaitForRemoteChoice`. AutoSlay/test bots still commit through it. | Skippable flows STILL call it with empty/-1 result — decode `PlayerChoiceResult` (`AsMutableCards`/`AsIndex`/`AsIndexes`/`AsPlayerId`, `ChoiceType`) to distinguish pick vs skip. `choiceId` is monotonic **per player slot** (not global). Pair with `PlayerChoiceContext.LastInvolvedModel` to know which card/relic/event asked. **The single most valuable decision hook.** | confirmed |
| Treasure chest open | `NTreasureRoom.OnChestButtonReleased` (Core/Nodes/Rooms/NTreasureRoom.cs:192) | `private void OnChestButtonReleased(NButton _)` | harmony_prefix | Human chest click (Godot Released signal; patch intercepts). One-shot, button disabled after; no cancel. | Relic claim is separate: `TreasureRoomRelicSynchronizer.PickRelicLocally(int index)` (TreasureRoomRelicSynchronizer.cs:102) — local-only, enqueues `PickRelicAction`; co-op picks are votes awarded when all picked (no retraction). Skipping chest = just leaving via map. | confirmed |

STS2MCP cross-check (verified): MCP bypasses `TryManualPlay` and `NEndTurnButton` (uses
`RequestEnqueue`/`PlayerCmd.EndTurn` directly) — so the UI-level hooks isolate genuine human
input, while `RequestEnqueue`-level hooks also catch MCP/AutoSlay/DevConsole.
`AutoSlayer.IsActive` (public static, AutoSlayer.cs:53) tags bot trajectories; AutoSlay is gated
by `!IsReleaseGame() && CommandLineHelper.HasArg("autoslay")` — irrelevant in release builds.

---

## Event stream hooks (native, zero-Harmony layers)

**Yes — there is a native event surface good enough that most of the recorder can be pure C#
event subscription.** The game's own `CombatReplayWriter` and `CombatStateTracker` are
first-party proof of each layer.

### 1. The in-memory command log (the .mcr source — exact action stream)

Subscribe per run (recreated in `InitializeShared`; resubscribe on every `RunStarted`):

```csharp
// All reachable via public properties on RunManager.Instance:
RunManager.Instance.ActionQueueSet.ActionEnqueued        // public event Action<GameAction>?  (ActionQueueSet.cs:~63)
RunManager.Instance.ActionQueueSet.ActionResumed         // public event Action<uint>?
RunManager.Instance.PlayerChoiceSynchronizer.PlayerChoiceReceived
    // public event Action<Player, uint, NetPlayerChoiceResult>?  (PlayerChoiceSynchronizer.cs:36)
RunManager.Instance.ChecksumTracker.ChecksumGenerated
    // public event Action<NetChecksumData, string, NetFullCombatState>?  (ChecksumTracker.cs:58)
    // companion: public event Action<NetFullCombatState>? StateDiverged;  (:56)
```

- `ActionEnqueued` fires for **local + remote + programmatic** actions; `GameAction.OwnerId`
  identifies the actor (filter by `LocalContext.NetId` for local-only).
- **Caveat (verified)**: `ActionEnqueued` fires in `EnqueueWithoutSynchronizing` BEFORE the
  cancellation checks — immediately-cancelled actions still raise it. Watch
  `GameAction.BeforeExecuted`/`BeforeCancelled` to match the .mcr exactly.
- `PlayerChoiceReceived` fires for local (from inside `SyncLocalChoice`, before the net send) and
  remote (`OnReceivePlayerChoice`) and replay playback.
- **Checksums correction**: not "after each executed action" — `SendPostActionChecksum`
  (RunManager.cs:379) skips `EndPlayerTurnAction`/`ReadyToBeginEnemyTurnAction`, and checksums are
  ALSO generated at turn boundaries (CombatManager.cs:334/364/722/797/901) and room exits.
  **Fires in singleplayer too** (only the network send is Client-gated) — free integrity stream.
- Reproducibility contract: seed alone is NOT enough (RNG counters fast-forward from saves;
  choices are external). Correct contract = **run snapshot + GameAction stream + PlayerChoice
  stream + same build/commit/modelIdHash** — exactly what the .mcr stores.

### 2. CombatHistory — the combat event vocabulary for events.jsonl

`CombatManager.Instance.History` (public get-only, CombatManager.cs:99):

```csharp
public event Action? Changed;                      // fires once per entry append AND once on Clear()
public IEnumerable<CombatHistoryEntry> Entries;    // ordered List backing; diff by last-seen count
```

- Executor-agnostic: human, AutoSlay, MCP, and network-synced MP actions all funnel through the
  model/command layer that appends entries. Trustworthy by construction — 20+ game mechanics
  (EchoForm, Feral, Nostalgia, Iteration, Juggling, ...) compute their own effects from it.
- `Changed` has **no payload** and also fires on `Clear()` — `Clear` is called at combat teardown
  (CombatManager.cs:568, 627). Treat a count decrease as Clear; **persist entries BEFORE
  `CombatEnded`**.
- Entries hold **live mutable model references** (CardModel/CardPlay/Creature) — serialize
  snapshots in the `Changed` handler immediately (use the game's `card.ToSerializable()` →
  `SerializableCard`), never at combat end.

**Complete entry-type list (17, all in `Core/Combat/History/Entries/`, all inherit
`CombatHistoryEntry { Creature Actor; int RoundNumber; CombatSide CurrentSide; string
HumanReadableString }`):**

| Entry type | Payload |
|---|---|
| `CardPlayStartedEntry` | `CardPlay` |
| `CardPlayFinishedEntry` | `CardPlay`, `bool WasEthereal` |
| `CardDrawnEntry` | `CardModel Card`, `bool FromHandDraw` |
| `CardDiscardedEntry` | `Card` |
| `CardExhaustedEntry` | `Card` |
| `CardGeneratedEntry` | `Card`, `bool GeneratedByPlayer` |
| `CardAfflictedEntry` | `Card`, `AfflictionModel Affliction` |
| `CreatureAttackedEntry` | `IReadOnlyList<DamageResult> DamageResults` |
| `DamageReceivedEntry` | `DamageResult Result`, `Creature? Dealer`, `CardModel? CardSource` (Receiver = Actor) |
| `BlockGainedEntry` | `int Amount`, `ValueProp Props`, `CardPlay? CardPlay` |
| `EnergySpentEntry` | `int Amount` |
| `MonsterPerformedMoveEntry` | `MonsterModel Monster`, `MoveState Move`, `IEnumerable<Creature>? Targets` |
| `OrbChanneledEntry` | `OrbModel Orb` |
| `PotionUsedEntry` | `PotionModel Potion`, `Creature? Target` |
| `PowerReceivedEntry` | `PowerModel Power`, `decimal Amount`, `Creature? Applier` |
| `StarsModifiedEntry` | `int Amount` |
| `SummonedEntry` | `int Amount` |

### 3. CombatManager lifecycle events (episode segmentation)

`CombatManager.Instance` (static singleton, CombatManager.cs:56 — subscriptions survive across
runs). All at lines 141-159:

```csharp
public event Action<CombatState>? CombatSetUp;
public event Action<CombatRoom>?  CombatEnded;   // fires on win AND loss path (loss via ProcessPendingLoss — deferred timing)
public event Action<CombatRoom>?  CombatWon;
public event Action<CombatState>? CreaturesChanged;
public event Action<CombatState>? TurnStarted;
public event Action<CombatState>? TurnEnded;
public event Action<Player,bool>? PlayerEndedTurn;    // PROVISIONAL — can be un-ended in co-op
public event Action<Player>?      PlayerUnendedTurn;
public event Action<CombatState>? AboutToSwitchToEnemyTurn;
public event Action<CombatState>? PlayerActionsDisabledChanged;
```

Finalize end-turn records only at `TurnEnded`/`AboutToSwitchToEnemyTurn`, never at
`PlayerEndedTurn`. `CombatEnded` fires on the loss path too — which is exactly where
`WriteReplay` does NOT — so it is the loss-side flush trigger.

### 4. Fine-grained per-object events (exact pile membership / vitals)

Subscribe on `CombatSetUp`, unsubscribe on `CombatEnded`. Piles via
`player.PlayerCombatState.{Hand,DrawPile,DiscardPile,ExhaustPile,PlayPile}`:

- `CardPile` (Core/Entities/Cards/CardPile.cs:29-37): `ContentsChanged`, `CardAdded(CardModel)`,
  `CardRemoved(CardModel)`, `CardAddFinished`, `CardRemoveFinished`; `Cards =>
  IReadOnlyList<CardModel>`.
- `CardModel` (:837-855): `Played`, `Drawn`, `Upgraded`, `Forged`, `AfflictionChanged`,
  `EnchantmentChanged`, `EnergyCostChanged`, `ReplayCountChanged`, `StarCostChanged`
  (+ `KeywordsChanged`).
- `Creature` (Core/Entities/Creatures/Creature.cs:260-276): `BlockChanged(int,int)`,
  `CurrentHpChanged(int,int)`, `MaxHpChanged`, `PowerApplied`,
  `PowerIncreased(PowerModel,int,bool)`, `PowerDecreased`, `PowerRemoved`, `Died` (+ `Revived`).
- `PlayerCombatState` (:90-92): `EnergyChanged`, `StarsChanged`.

These fire on any mutation regardless of cause (human, hook effects, monster moves, net sync).

### 5. Hook.* dispatcher (Harmony postfix targets for semantic gameplay moments)

`MegaCrit.Sts2.Core.Hooks.Hook` is **NOT a mod API** — it is the internal dispatcher fanning
~145 public static methods out to game models via `CombatState.IterateHookListeners()`
(powers, monsters, non-melted relics, potion slots, deck cards). But all are public static with
rich args — safe, passive Harmony postfix targets. Verified signatures:

```csharp
public static async Task BeforeCardPlayed(CombatState combatState, CardPlay cardPlay);                       // Hook.cs:172
public static async Task AfterCardPlayed(CombatState combatState, PlayerChoiceContext choiceContext, CardPlay cardPlay);  // :181
public static async Task AfterCardDrawn(CombatState combatState, PlayerChoiceContext choiceContext, CardModel card, bool fromHandDraw); // :125
public static async Task AfterDamageReceived(PlayerChoiceContext choiceContext, IRunState runState, CombatState? combatState, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource); // :294
public static async Task AfterRewardTaken(IRunState runState, Player player, Reward reward);                 // :780
public static async Task AfterRoomEntered(IRunState runState, AbstractRoom room);                            // :798
public static async Task AfterMapGenerated(IRunState runState, ActMap map, int actIndex);                    // :461
public static void ModifyShuffleOrder(CombatState combatState, Player player, List<CardModel> cards, bool isInitialShuffle); // :1499
```

- **Async caveat**: a plain postfix on an async method runs at task-creation, not completion —
  fine for observing args (all a recorder needs).
- Exactly 7 dispatchers early-return when `LocalContext.NetId == null`: `AfterDeath` (:323),
  `AfterDiedToDoom` (:348), `BeforeFlush` (:387), `BeforePlayPhaseStart` (:658),
  `BeforeSideTurnStart` (:820), `BeforeTurnEnd` (:883), `AfterTurnEnd` (:921).
- **Do NOT use the no-Harmony alternative** (injecting a custom AbstractModel/relic into
  `player.Relics`): it enters gameplay-affecting `Modify*` pipelines and hook-count-sensitive
  mechanics. Postfixes are the passive choice.
- **Hidden info**: `ModifyShuffleOrder` receives the full ordered post-shuffle list, and
  `DrawPile.Cards` is the TRUE ordered draw pile. Tag or exclude these channels in events.jsonl
  if trajectories must be human-visible-information-only. CombatHistory itself is post-hoc only
  (no future leakage); skipped card-reward options are visible info (fine).

### 6. Run-level history (the game already records the run trajectory)

`RunState.MapPointHistory` (RunState.cs:94) /
`CurrentMapPointHistoryEntry => MapPointHistory.LastOrDefault()?.LastOrDefault()` (:96);
writer `public void AppendToMapPointHistory(MapPointType, RoomType, ModelId?)` (:325).

Per floor: `MapPointHistoryEntry { map_point_type, rooms:[{room_type, model_id, monster_ids,
turns_taken}], player_stats:[PlayerMapPointHistoryEntry { player_id, gold_gained/spent/lost/
stolen, current_gold, current_hp, max_hp, damage_taken, hp_healed, max_hp_lost/gained,
ancient_choice, cards_gained, card_choices (picked AND skipped), relic_choices, potion_choices,
potion_discarded, potion_used, cards_removed, relics_removed, cards_enchanted, cards_transformed,
upgraded_cards, downgraded_cards, event_choices, rest_site_choices, bought_relics,
bought_potions, bought_colorless, completed_quests }]}` — all `System.Text.Json`-annotated
(`JsonPropertyName`), i.e. a free jsonl schema. Persisted as `RunHistory` at run end.

No Changed event on these lists — **snapshot `CurrentMapPointHistoryEntry` on
`RunManager.RoomExited`**. Verified writers if immediacy is needed: CardReward.cs:199/205/221/232
(note: :199/:205 are inside async `OnSelect` and :232 inside async `Reroll` — a naive postfix runs
BEFORE the append; await `__result` or use the RoomExited snapshot; `OnSkipped` :217 is sync and
safe), AncientEventModel.cs:200, EventSynchronizer.cs:250-262, CardCmd.cs:336/437,
MerchantRoom.cs:74/81, SpecialCardReward.cs:97, MassiveScroll.cs:36, LeadPaperweight.cs:33.

### 7. Mod enumeration for metadata

`ModManager.LoadedMods` / `AllMods` (`IReadOnlyList<Mod>`, ModManager.cs:37-39) — poll once after
startup for trajectory metadata. `public static event Action<Mod>? OnModDetected` (:43) exists but
polling is simpler. Game version/commit: `ReleaseInfoManager.Instance.ReleaseInfo` (nullable;
`Commit`/`Version`/`Date`/`Branch` from `release_info.json` next to the executable; fallbacks
`"UNRELEASED"` / `GitHelper.ShortCommitId`).

---

## State snapshot triggers (when to call the passive StateBuilder)

Primary trigger — the game's own debounced dirty signal:

```csharp
CombatManager.Instance.StateTracker.CombatStateChanged   // public event Action<CombatState>?
```

Aggregates History.Changed + CreaturesChanged + TurnStarted/Ended + all per-card, per-creature,
per-pile, and energy/stars events, coalesced via a deferred frame task — **one callback per burst
of changes**. Verified caveats:

1. The deferred callback **silently no-ops when `_state` is null or `state.Creatures` is empty** —
   end-of-combat snapshots can be dropped. Pair with an unconditional snapshot on
   `CombatManager.CombatEnded`/`CombatWon`.
2. `NotifyCombatStateChanged` **throws `InvalidOperationException` if anything is subscribed while
   `TestMode.IsOn`** — guard the subscription with a TestMode check.

Full snapshot schedule:

| Trigger | Snapshot |
|---|---|
| `RunStarted(RunState)` | Run header: seed (`state.Rng.StringSeed`), StartTime, character(s), ascension, acts, modifiers, mod list, game version/commit, NetService.Type |
| `CombatSetUp(CombatState)` | Combat-start state: creatures, piles, relics, potions, energy; subscribe fine-grained events here |
| `StateTracker.CombatStateChanged(CombatState)` | Debounced full observable state (hand, piles, HP, block, powers, energy, stars, orbs; `RoundNumber`, `CurrentSide`, `HittableEnemies`, per-player `PlayerCombatState`) |
| `TurnStarted` / `TurnEnded` (`AboutToSwitchToEnemyTurn`) | Turn-boundary state; finalize end-turn records here |
| `CombatEnded` / `CombatWon` (`Action<CombatRoom>`) | Final combat state + flush the combat's events **before History.Clear()**; unsubscribe fine-grained events |
| `RunManager.RoomExited` | Harvest `RunState.CurrentMapPointHistoryEntry` (the just-finished floor's decisions/deltas) |
| `RunManager.RoomEntered` / `ActEntered` | Floor/act progression markers |
| `SaveManager.Instance.Saved` | Recorder flush heartbeat (fires at every map-point entry, post-combat, mid-event) — the crash-resilience mechanism, since `CleanUp` never fires on process kill |
| `OnEnded` postfix (latched) / `CreateRunHistoryEntry` postfix | Terminal snapshot: outcome (isVictory + `IsAbandoned`), final `SerializableRun` |
| `ChecksumTracker.ChecksumGenerated` | Optional: attach the game's own post-action checksum to the trajectory for integrity validation (fires in SP too) |

Proven-public polling reads (the AutoSlay access paths, CombatRoomHandler.cs:33-146):
`CombatManager.Instance.IsInProgress` / `.IsPlayPhase`, `PileType.Hand.GetPile(player).Cards`,
`card.CanPlay(out UnplayableReason reason, out AbstractModel? preventer)`,
`card.CombatState.HittableEnemies`. (Avoid AutoSlay's
`LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState())` — use the cached RunState.)
Action surface if the recorder ever drives: `CardCmd.AutoPlay(new BlockingPlayerChoiceContext(),
card, target)`, `PlayerCmd.EndTurn(player, canBackOut: false)`, `PotionCmd`, `CardSelectCmd`.

---

## Multiplayer guard (detect and disable)

Inside the `RunStarted(RunState)` handler (NetService is valid by then — assigned in
`InitializeShared` before `Launch`):

```csharp
var type = RunManager.Instance.NetService.Type;   // NetGameType: None, Singleplayer, Host, Client, Replay
bool skip = type.IsMultiplayer()                  // true iff Host(2) or Client(3)
         || type == NetGameType.Replay            // replay playback — MUST be checked separately
         || TestMode.IsOn;                        // test flows reuse all funnels
```

- `NetGameTypeExtensions.IsMultiplayer` (Core/Multiplayer/Game/NetGameTypeExtensions.cs):
  `public static bool IsMultiplayer(this NetGameType type)` — decompiled body is literally
  `(uint)(type - 2) <= 1u`. `Replay` returns **false** from it, hence the separate check.
- The game itself uses this exact check (`RunManager.InitializeRunLobby`, RunManager.cs:324-332);
  `RelicSelectCmd` shows the Replay-guard pattern.
- `RunManager.IsSinglePlayerOrFakeMultiplayer` (:145) exists but returns false when no run is in
  progress — only usable inside an active run.
- If recording IS desired in MP: filter to the local player everywhere via
  `LocalContext.IsMe(Player?)` / `LocalContext.NetId`; remember MP **clients** never write saves
  (no SaveRun heartbeat) and remote actions arrive only through `ActionQueueSet.ActionEnqueued`
  (with `OwnerId`) and `PlayerChoiceReceived` — never through the UI-level hooks.
- Optionally gate on `AutoSlayer.IsActive` (public static) to tag bot-generated trajectories
  (debug builds only).

---

## Known risks

1. **Pre-release code.** This is decompiled v0.99.1; MegaCrit may change the mod API and any
   internal before 1.0. Apply every Harmony patch and event subscription in its own try/catch
   (already the plan) and log failures instead of crashing.
2. **`OnEnded` double-fire on victory** (corrected above) — the single most likely
   correctness bug if unlatched. Latch first call; read `IsAbandoned` there.
3. **Async Harmony targets**: `Reward.OnSelectWrapper`, `MerchantEntry.OnTryPurchaseWrapper`,
   `RestSiteSynchronizer.ChooseLocalOption` (returns Task<bool>), `CardReward.OnSelect/Reroll`,
   `RunSaveManager.SaveRun`, all async `Hook.*` — a plain postfix runs at task-creation.
   Await/chain on `__result` where the result matters; for `Hook.*` arg observation this is fine.
4. **Virtual-method patching**: Harmony on a virtual base does NOT intercept overrides —
   `Reward.OnSkipped` must be patched per-subclass (5 overrides listed above).
5. **Method hiding**: `MerchantCardRemovalEntry.OnTryPurchaseWrapper` hides (not overrides) the
   base — patch both or miss card removals.
6. **Debug-named APIs — avoid**: `RunManager.DebugOnlyGetState()`, AutoSlay classes,
   `NMultiplayerTest` (.mcr playback is dev-only; the recorder must be self-sufficient).
   Removal candidates in future builds.
7. **UI-layer fragility**: private `N*` node handlers (`OnDiscardButtonPressed`,
   `OnChestButtonReleased`, `CallReleaseLogic`, `OnMapPointSelectedLocally`,
   `OptionButtonClicked`) are more likely to churn across game updates than core-model/
   synchronizer methods. The Godot `StringName` registrations pin some names, but treat all UI
   hooks as best-effort; the synchronizer/action-queue layer is the durable spine.
8. **Re-entry/dedupe traps**: `RequestEnqueue` re-fires for deferred play-phase actions (dedupe by
   object identity); `OnEmbarkPressed` re-enters once via FTUE; `SyncLocalObtained*` overlaps the
   merchant hook (dedupe by current room type); `ActionEnqueued` fires before cancellation checks.
9. **Live references in CombatHistory** — serialize snapshots immediately in the `Changed`
   handler or record post-mutation state.
10. **History.Clear() at teardown** (CombatManager.cs:568/627) — a naive "read last entry"
    subscriber mis-logs or crashes on the count reset; flush before `CombatEnded`.
11. **Hidden-information channels** (`DrawPile.Cards` order, `ModifyShuffleOrder`) must be tagged
    or excluded if trajectories feed human-information agents.
12. **Modded save relocation** — resolve all paths at runtime through
    `SaveManager`/`UserDataPathProvider`; warn users that vanilla and modded profiles are
    separate trees.
13. **No `CleanUp` on process kill** — crash resilience comes from flushing on
    `SaveManager.Instance.Saved` heartbeats, not session teardown.
14. **`StateTracker` throws under TestMode** and drops empty-state snapshots — guard and pair
    with `CombatEnded`.
15. **UNVERIFIED (runtime)**: everything here is static analysis of the decompiled tree; nothing
    has been executed in-game yet. Specific items worth a first-run smoke test:
    (a) `SaveEventOptionToHistory` firing in pure singleplayer (statically proven, untested);
    (b) whether newer builds wire `NCardPlayQueue.RemoveCardFromQueueForCancellation` to user
    input (v0.99.1 has no user-facing card-play undo — verify it stays that way);
    (c) async-postfix timing on `CardReward.OnSelect` appends.
16. **Event options with `ShouldSaveChoiceToHistory == false`** never reach the game's own
    history — the `NEventRoom.OptionButtonClicked` prefix is the only capture for those and for
    `IsProceed` clicks; bespoke event UIs (CrystalSphere cells, ancient dialogue-advance) are
    cosmetic-local and uncaptured by design.

---

## v0.107.1 drift addendum

The body above is preserved as the **v0.99.1** map (history). This section records verified
v0.99.1 → v0.107.1 drift and how the recorder (mod v0.2.0) responded. New-tree evidence root:
`/private/tmp/claude-501/-Users-Aincrad-dev-proj/f08abf1e-4d55-4d44-a828-e4fef5f57aa5/scratchpad/sts2-decomp-v0107/MegaCrit/sts2/`.
Dominant refactor family: **global → per-player** (turn phases, merchant inventories, reward
synchronization) and **class → interface** (`CombatState` → `ICombatState`). Engine MegaDot m.8 → m.12.
`KNOWN_GOOD_VERSIONS` in the mod is now `["v0.107.1"]`.

### Renames / signature changes

| Target | v0.99.1 | v0.107.1 | Recorder response |
|---|---|---|---|
| `RunManager.SetUpNewSinglePlayer` | `SetUpNewSinglePlayer(RunState, bool, DateTimeOffset?)` (:204) | **Renamed** `SetUpNewSingleplayer` (RunManager.cs:236) — whole `*Player` family re-cased | None needed — recorder attaches via `RunStarted`, not these. A patch by old name would silently fail to bind. |
| `RunManager.SetUpSavedSinglePlayer` | `void SetUpSavedSinglePlayer(RunState, SerializableRun)` (:231) | **Renamed + async** `async Task SetUpSavedSingleplayer` (:285); awaits `IncrementNumReloads` BEFORE per-run objects exist | Same — `RunStarted` remains the attach point (a plain postfix here would now run too early). |
| `RunManager.SetUpNewMultiPlayer` / `SetUpSavedMultiPlayer` | (:218 / :244) | `SetUpNewMultiplayer` (:264) / `async Task SetUpSavedMultiplayer` (:312) | None (MP not recorded). |
| `ModManager.LoadedMods` / `AllMods` | `IReadOnlyList<Mod>` (:37-39) | **REMOVED** → `GetLoadedMods()` (ModManager.cs:938) filtering new `Mod.state == ModLoadState.Loaded`; `Mod.wasLoaded` → `state` + `errors:List<LocString>` + `version:SemanticVersion` | `RunLifecycle.ListLoadedMods` now calls `ModManager.GetLoadedMods()`. |
| `EndPlayerTurnAction` / `UndoEndPlayerTurnAction` ctor+field | `(Player, int combatRound)`, private `_combatRound`; stale-check vs `CombatState.RoundNumber` | `(Player, int turnNumber)`, private `_turnNumber` (:29); stale-check vs `PlayerCombatState.TurnNumber` (per-player) | `ActionPipeline` FieldRefs renamed to `_turnNumber`; jsonl param key `combat_round` → `turn_number` (semantics changed: per-player counter). |
| `VoteForMapCoordAction` ctor | `(Player, RunLocation source, MapVote?)` | `(Player, MapLocation source, MapVote?)` (:39) — NEW struct `Core/Runs/MapLocation.cs` (`actIndex`, `coord`; same member names) | `ActionPipeline._voteSource` FieldRef retyped to `MapLocation`. Harmony ctor-targeting by arg types would break (we don't). |
| `PickRelicAction` ctor | `(Player, int relicIndex)` | `(Player, int? relicIndex)` (:28) — **null = explicit treasure-relic SKIP** (new `TreasureRoomRelicSynchronizer.SkipRelicLocally` :139 → `PickRelicLocally(null)` :151) | FieldRef retyped `int?`; `pick_relic` params gain `"skipped"`; the existing `PickRelicLocally` human-scope patch covers the new skip button for free. |
| `CombatManager.IsPlayPhase` | `public bool IsPlayPhase` (:89) | **REMOVED** → `player.PlayerCombatState?.Phase == PlayerTurnPhase.Play` (new enum `Core/Combat/PlayerTurnPhase.cs`: None/Start/AutoPrePlay/Play/AutoPostPlay/End; `PlayerCombatState.Phase` at :44) | `PassiveStateBuilder.Combat` ported; also emits new `player_turn_number` from `PlayerCombatState.TurnNumber`. |
| `Creature.CombatState` | `CombatState?` | `ICombatState?` (Creature.cs:124) — interface exposes everything read (Enemies, Players, RoundNumber, CurrentSide, PlayerCreatures, HittableEnemies, IsLiveCombat) | Source-compatible; no change beyond comments. Creature's 9 events unchanged. NEW `Creature.Pets` (:204). |
| `LocalContext.GetMe(CombatState?)` | (:36) | `GetMe(ICombatState?)` (:52); NEW `GetMe(IEnumerable<Creature>)` (:76) | Source-compatible. |
| `Hook.BeforeCardPlayed/AfterCardPlayed/AfterCardDrawn/AfterDamageReceived/ModifyShuffleOrder` | `CombatState` params | `ICombatState` params (Hook.cs :263/:278/:202/:417/:2004); names/order otherwise identical | Not currently patched; by-name patches would keep binding, but a postfix declaring `CombatState` args must switch to `ICombatState`. |
| `MerchantRoom.Inventory` | single `MerchantInventory?` | **REMOVED** → `List<MerchantInventory> Inventories` (:27) + `GetLocalInventory()` (:46, per-player). `FakeMerchant.Inventory` UNCHANGED. Entry props intact; `CardEntries` now concat of `CharacterCardEntries`+`ColorlessCardEntries` | `PassiveStateBuilder.Rooms` shop section uses `GetLocalInventory()` (guarded). |
| `CombatHistoryEntry.RoundNumber` / `.CurrentSide` | public | **PRIVATE** (:27/:36); ctor gained trailing `IEnumerable<Player> players` (per-player turn capture); `HappenedThisTurn(ICombatState?)`; NEW `HappenedLastPlayerTurn(Player)` (:99). `Actor`/`History`/`Description`/`HumanReadableString` still public | `EventTap` reads round/side via cached `AccessTools.PropertyGetter` reflection delegates (guarded; null → field omitted). |
| `CardGeneratedEntry.GeneratedByPlayer` | `bool` | **REMOVED** → `Player? Creator` (:11) | `EventTap` emits `generated_by_player` = `(Creator != null)` (old semantics) + new `creator_player` net id. Other 16 entry payloads verified unchanged; no entry types added/removed. |
| `PlayerChoiceResult.FromIndex` | `FromIndex(int)` (:241) | `FromIndex(int?)` (:246) — null encodes a skipped index-choice; NEW `AsIndexOrNull()` (:410); other members unchanged | `DecodeChoice` unaffected (uses `AsIndexes()`/`AsIndex()`; empty list already decodes as skip). |
| `RunSaveManager.SaveRun` | did serialize+write+`Saved` inline (:73) | delegates to NEW overload `SaveRun(SerializableRun, bool isMultiplayer)` (:93) which writes and fires `Saved` (:110); also called outside live runs (IncrementNumReloads on resume) | None — recorder uses the `SaveManager.Instance.Saved` event, which still fires after every write. Heartbeat may now also fire on resume bookkeeping (harmless extra flush). |
| manifest schema | `dependencies:List<string>`; no version enforcement | `dependencies:List<ModDependency{id,min_version}>` (string form auto-migrated w/ deprecation error); NEW `min_game_version` **ENFORCED** (unparseable or > game version → mod Failed, ModManager.cs:562-623); `version` parsed as SemanticVersion | `Sts2Recorder.json`: version bumped to `0.2.0`, added `"min_game_version": "0.107.1"`; `affects_gameplay:false` still honored (`GetGameplayRelevantModNameList` ModManager.cs:856-864, JoinFlow.cs:94-111 unchanged). |

### Behavioral drift (signatures unchanged)

| Target | Drift | Recorder response |
|---|---|---|
| `ChecksumTracker.ChecksumGenerated` | NEW gate `IsEnabled` (public setter, :60): `GenerateChecksum` returns default and fires NOTHING when disabled (:89-92). `RunManager.InitializeShared` enables ONLY for Host/Client/Replay (:391-399) — **"fires in SP too" is no longer true by default**. | `EventTap.OnRunStarted` sets `tracker.IsEnabled = true` per run (SP-only recording) to restore the v0.99.1-vanilla SP integrity stream. Call sites still execute unconditionally; observation-side only. |
| `CombatHistory.Add` | Only appends (and fires `Changed`) when `combatState.IsLiveCombat()` (CombatHistory.cs:123-130); entries from simulated/scratch states silently dropped. All 17 logger methods take `ICombatState`; `CardGenerated` logger now `(ICombatState, CardModel, Player? creator)`. | None — live human combat unaffected; `Clear()` still fires `Changed`; flush-before-`CombatEnded` guidance still valid (Clear at CombatManager.cs:918/:989). |
| `UsePotionAction` | Invalid potion/target now **self-Cancels** instead of throwing (new `IsValidTarget` gate; CancelAction :136) — `BeforeCancelled` fires in more situations. UI callback renamed `OnPotionUseCanceled` → `OnPotionUseOrDiscardCanceled` (NPotionContainer). | Already handled — ActionPipeline finalizes via `BeforeExecuted`/`BeforeCancelled` for every action. |
| `DiscardPotionGameAction` | NEW `CancelAction()` override (:74) — discards are now cancellable like uses (null-slot discard cancels instead of throwing). | Same — `BeforeCancelled` is already a normal terminal for both potion action types. |
| `NEndTurnButton.CallReleaseLogic` / `SecretEndTurnLogicViaFtue` | Moved (:577/:602); body reads `me.PlayerCombatState.TurnNumber` now. Guards (`CanTurnBeEnded`, `IsPlayerReadyToEndTurn` CombatManager.cs:791) intact; End-vs-Undo branch unchanged. | None — patched by name; still binds. |
| `GetGameplayRelevantModNameList` null condition | old: null when `LoadedMods.Count==0`; new: null when `!IsRunningModded()` (Loaded OR Failed) | None for the recorder; noted for MP mod-mismatch behavior. |
| `ActionQueueSet.ActionEnqueued` | NEW `ActionQueueChanged` event (:70); subscriber exceptions now try/caught and reported to **MegaCrit's Sentry** (:106-114) | Keep the recorder's own try/catch in every handler so failures never leak telemetry. |
| Saved-run schema | `SerializableRun` latest v14 → **v16** (V14ToV15 adds `game_mode`; V15ToV16 ModelId renames); `RunHistory` v8 → **v9** (same `SharedMigrationHelper.V100Renames`: CARD.PREPARE→CARD.PREPARED, ENCOUNTER.TOADPOLES_NORMAL→ENCOUNTER.SEAPUNK_NORMAL, MONSTER.DOOR→MONSTER.DEPRECATED_MONSTER). NEW SavedMap-per-act + MapDrawings + ExtraFields in saves — resume restores exact map topology (seed-based map reproduction assumptions revisit). RunHistory entries persist per-player Badges. | Archived `.run`/save artifacts carry the new fields (additive for the recorder). v0.99.1 trajectories referencing renamed ModelIds need the V100Renames mapping when compared against v0.107.1 data. |
| `PlayerMapPointHistoryEntry` | GAINED `stolen_loot` int (+ helpers); all old JSON fields retained | Additive — floor_summary jsonl gains one field automatically. |
| `RunState` | NEW first-class `GameMode` enum property (None/Standard/Daily/Custom; persisted as `game_mode`) | `RunLifecycle.ComputeGameMode` reads `state.GameMode` (falls back to the old modifiers/DailyTime mirror for `None`). |

### Reward capture: local → synchronizer refactor (REDESIGNED)

`Reward.OnSelectWrapper` (v0.99.1 Reward.cs:80) **no longer exists**. v0.107.1 splits it:

- `RewardsSetSynchronizer.SelectLocalReward(Reward)` (Core/Multiplayer/Game/RewardsSetSynchronizer.cs:194)
  — LOCAL player's claim only (throws for non-local); sends `RewardSelectedMessage`. UI callers:
  NRewardButton.cs:250; RewardsSet.cs:182 is **TestMode-only**.
- `Reward.SelectUnsynchronized()` (Reward.cs:120) — executes the claim on EVERY machine
  (local + remote via `HandleRewardSelectedMessage` :236) and for programmatic auto-claims
  (`Draft.cs:29` run-start modifier). NEW `Reward.SuccessfullySelected` (:51).
- `RewardsSetSynchronizer.SkipLocalRewardsSet()` (:221) — the ONE local skip decision
  (NRewardsScreen.cs:588); `SkipRewardsSet` (:344-352) then invokes `OnSkipped` for every
  unselected reward **on all machines**. `RewardsSet` gained `DisallowSkipping` (:53).
- `RewardsSetSynchronizer` is per-run state (`RunManager.RewardsSetSynchronizer`, RunManager.cs:143),
  recreated each run like the other synchronizers, and now feeds `CombatReplayWriter` (:414) —
  reward selection joined the synchronized action surface.
- **Caller-set inversion**: reward screens NO LONGER call `RewardSynchronizer.SyncLocalObtained*/
  Skipped*` — remaining callers are merchant purchases (MerchantCardEntry.cs:142, MerchantRelicEntry.cs:63,
  MerchantPotionEntry.cs:90) and CrystalSphereCurse.cs:23. `SyncLocalPaelsWingSacrifice` REMOVED
  (Pael's Wing now injects a card-reward alternative via `Hook.ModifyCardRewardAlternatives`, so
  its sacrifice arrives as a CardReward `SyncLocalChoice` alternative index).
- Card reward redesigned around `CardRewardAlternative` (Skip/Reroll/hook-injected, max 2; UI
  `NCardRewardSelectionScreen` + `NCardRewardAlternativeButton`): pick/skip/reroll is now a
  synchronized PlayerChoice committed via `SyncLocalChoice` with `FromIndex` (index < cards.Count
  = card; >= = alternative; null = none) — captured automatically by the existing
  `SyncLocalChoice` hook. Reroll is no longer a separate async capture path.

**Recorder v0.2.0 patch set** (also fixes the v0.99.1 review findings on double-records):

1. `SelectLocalReward` prefix — marks the reward instance human-initiated
   (ConditionalWeakTable; no record).
2. `SelectUnsynchronized` postfix — the single committed `reward_taken` record: chains on
   `Task<bool> __result`, filters `LocalContext.IsMe(reward.Player)` (fires for remote players
   too), tags `human`/`programmatic` from the mark (Draft auto-claims → `programmatic:true`).
3. `SkipLocalRewardsSet` prefix — one `rewards_skipped` record per human skip decision. The 5
   per-subclass `OnSkipped` patches were REMOVED (they now fire per-reward on all machines —
   would multi-count one decision; skipped items are visible in the preceding rewards-screen
   state snapshot).
4. `MoveToMapCoordAction.ExecuteAction` postfix REMOVED (review finding: the umbrella
   `RequestEnqueue` funnel already records `move_to_map_coord` for the same action instance;
   human attribution lives on `vote_for_map_coord`). One map click = `vote_for_map_coord`
   (human) + `move_to_map_coord` (commit), no third record.
5. `SyncLocal*` prefixes kept as the merchant/curse channel; the `IsShopContext` dedupe now
   suppresses what is effectively their main caller set (merchant purchases are recorded by the
   `OnTryPurchaseWrapper` hook).

### New surfaces noted (not yet wired)

- **Official hook-listener API**: `ModHelper.SubscribeForRunStateHooks(string id, RunHookSubscriptionDelegate)`
  / `SubscribeForCombatStateHooks` (ModHelper.cs:94-183) — recorder-supplied models would receive
  every `Hook.*` dispatch without Harmony, but returned models enter the `Modify*` pipelines, so
  they must be strictly no-op observers (same caveat as the v0.99.1 relic-injection warning).
- `GameAction.JustBeforeFinished` (GameAction.cs:69, fired before `AfterFinished`) — clean
  per-action finalization hook matching checksum timing.
- New in-combat pile card selection (`CardSelectCmd.FromCombatPile` :375, `NCombatPileCardSelectScreen`)
  commits through `SyncLocalChoice` — captured automatically.
- `Hook.AfterAutoPrePlayPhaseEntered` / `AfterAutoPostPlayPhaseEntered` + `PlayerCombatState.TurnNumber`
  — per-player turn segmentation for co-op trajectories.
- `ModManager.HasHarmonyPatches()` (:921) and `GetNonGameplayRelevantModNameList()` (:869) exist;
  check consumers before shipping patch-heavy builds.
- Meta-only new screens (no run-decision capture needed): Bestiary, daily-run leaderboards,
  Phobia-mode / MP-map-drawings settings. Random-character button is not new drift (exists in
  both trees; resolved at embark, run-start capture unaffected).

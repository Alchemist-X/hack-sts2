# Choice area spec (W1: CONTRACTS Part B §B1, §B12 A4 and A14)

2026-11-01 · round 3 · base `263810d` ("sim: W1 phase-0b amendment fixes after adversarial check"). Rule truth:
decompile v0.111.0, cited `<ns-dir>/<File>.cs:<line>` with `MegaCrit.Sts2.Core.` dropped. Rules follow the
`TestMode.IsOff` branch, timing follows `NonInteractiveMode` (`docs/sim-port/design-subsystems.md` §2).

Files (owner: choice): `sim/Sts2Sim.Core/Choice/*` (`ChoiceTypes.cs` unchanged and frozen; `ChoiceRuntime.cs`,
`CardSelectCmd.cs`, `ChoiceCanonicalTags.cs` completed; new `ChoiceSelector.cs`, `ChoiceTape.cs`,
`ChoiceUnwindException.cs`, `VakuuCardSelector.cs`), `sim/Sts2Sim.Core/Combat/Loop/CombatSim.cs`,
`sim/Sts2Sim.Core/Combat/Loop/LegalActionGenerator.cs`, `sim/Sts2Sim.Tests/ChoiceTests.cs`, this file. No other file
was edited.

## 1. Summary

| Part | State |
| --- | --- |
| `ChoiceRuntime` | `Ask` (selector top, else `Answerer`, else `ChoicePendingException`), answer validation, the four context kinds with their first-pause semantics, selector stack with stable ids and a clone protocol, guarded pops (§3) |
| `CardSelectCmd` | the six W1 methods (seven entry points) ported line by line from `Commands/CardSelectCmd.cs:3169-3796` (§4) |
| `CombatSim` | B1 tape engine: one `DeepClone` per decision step, `Step(action, answerer)` fast path, `Begin` in `Create`, pending tuple hash with the A14 presence byte, element-wise tape invariant (§5) |
| `LegalActionGenerator` | decision-point order plays → `UsePotion` → `DiscardPotion` → `EndTurn` with the A4 target resolution; prompt legality; stray-index validation for every kind (§6) |
| Tests | 31 `Test_Choice_*` in `ChoiceTests.cs`; 30 pass, 1 is `W1Pending` on the relics-potions `PotionActions.CanUse` stub (§11) |

## 2. Types and canonical tags

`Choice/ChoiceTypes.cs` (frozen by the scaffold) is unchanged: `ChoicePromptKind`, `ChoiceResultType`, `ChoiceContextKind`,
`ChoiceOption`, `ChoicePrompt`, `ChoiceAnswer`, `IChoiceAnswerer`, `ChoiceSource`. Additions, all in `Sts2Sim.Core.Choice`:

| Type | File | Purpose |
| --- | --- | --- |
| `IChoiceSelector : IChoiceAnswerer` (`SelectorId`) | `ChoiceSelector.cs` | a content selector with a stable canonical id (§3.4) |
| `IStatefulChoiceSelector : IChoiceSelector, ICanonical` (`DeepClone()`) | `ChoiceSelector.cs` | a selector with state: cloned per state, written to the canonical form |
| `SelectorScope : IDisposable` | `ChoiceSelector.cs` | the game's `StackedSelectorScope` (`Commands/CardSelectCmd.cs:36-61`) |
| `ChoiceTapeAnswerer : IChoiceAnswerer` | `ChoiceTape.cs` | the tape answerer with the debug invariant and an optional fallback (§5.6) |
| `ChoiceTape` (static) | `ChoiceTape.cs` | `SameShape` (invariant comparison), `Describe`, `Copy` (prompt, answer), `WritePending` (§5.5) |
| `ChoiceUnwindException` | `ChoiceUnwindException.cs` | unwinds an action whose turn-setup first-pause steps ended the combat (§3.3) |
| `VakuuCardSelector : IChoiceSelector` | `VakuuCardSelector.cs` | port of `Models.Relics/VakuuCardSelector.cs:10-24` (take the first `MaxSelect` options) |
| `ChoiceContextFrame(Kind, OnFirstPause, FirstPauseDone)` | `ChoiceRuntime.cs` | the phase-0b frame gains the per-frame first-pause flag |

Tags (choice range `0x5Axx`, `Choice/ChoiceCanonicalTags.cs`): `Runtime 0x5A01`, `Selector 0x5A02` (scaffold), `Pending 0x5A03`,
`PendingAction 0x5A04`, `Tape 0x5A05`, `Answer 0x5A06`, `Picks 0x5A07`. A tag scan of Core finds no duplicate value.

## 3. `ChoiceRuntime`

### 3.1 `Ask(prompt)`

Order: validate the prompt (§3.2) → if the selector stack is non-empty, its top answers (`Commands/CardSelectCmd.cs:3088-3098`:
`Selector` is the stack's `Peek`) → else the installed `Answerer` answers → with neither, `ChoicePendingException(prompt)`. The tape
answerer throws the same exception when its tape is exhausted (§5.6). `Ask` performs no pause: the pause is
`SignalPlayerChoiceBegun` (§3.3), which every `CardSelectCmd` method calls at the game's position before it asks; a direct `Ask` caller
signals first (`ChoiceTests.UnorderedDiscardCard` shows the pattern).

### 3.2 Prompt and answer validation

`ValidatePrompt` (public, used by `Ask`): non-null `SiteId` and `SourceId`, at least one option, `0 <= MinSelect <= MaxSelect <= n`, and
not complete at zero picks (`!RequireConfirm && MaxSelect == 0` must be resolved by the caller, §4.7). `ArgumentException` otherwise.

An `Answerer` answer is checked against the B1 legality before it is used: `Cancelled` only on a `Cancelable` prompt and with no pick;
picks in range and distinct; at most `MaxSelect`; and complete — either the automatic completion (`!RequireConfirm && picks == MaxSelect`)
or a legal Confirm (`(RequireConfirm || CanSkip) && MinSelect <= picks <= MaxSelect`). `InvalidOperationException` otherwise. The picks
are copied, and sorted when `Ordered` is false, so the fast path and the tape (which stores sorted answers, §5.3) apply the same answer.

A selector answer is checked only for range and distinctness, and a `Cancelled` or null answer is an empty selection. The game does not
check selector answers against the bounds (`TestSupport/TestCardSelector.cs` returns an empty selection when nothing is queued), and
selector results are used in the selector's order (`Commands/CardSelectCmd.cs:3401-3408`, `:3697-3700`).

`CompletesAutomatically(prompt, n)` and `CanConfirm(prompt, n)` are public statics shared with `LegalActionGenerator` and `CombatSim`.

### 3.3 Context stack and the pause

`PushContext(kind, onFirstPause = null)` appends a frame and returns a `ContextScope` (dispose pops; out-of-order pops throw, as in the
phase-0b body). A callback is accepted only for `TurnSetup` (`ArgumentException` otherwise); an unknown kind throws
`ArgumentOutOfRangeException`. The stack is transient: not cloned (a clone starts empty), not hashed. An empty stack behaves as `Inline`
(`CurrentContextKind`). `ContextScope.FirstPauseDone` reports whether the frame has paused; the loop can use it to skip the deferred steps.

`SignalPlayerChoiceBegun(cs, siteId)` is the sim's `PlayerChoiceContext.SignalPlayerChoiceBegun`. It looks only at the top frame:

| Top frame | Game context and its `SignalPlayerChoiceBegun` | Sim effect |
| --- | --- | --- |
| none / `Inline` | `GameActionPlayerChoiceContext` pauses the running action in place (`GameActions.Multiplayer/GameActionPlayerChoiceContext.cs:109-121`); `BlockingPlayerChoiceContext` is a no-op (`GameActions.Multiplayer/BlockingPlayerChoiceContext.cs:11-19`); the end-of-turn hook context is waited on at once (`Combat/CombatManager.cs:2388-2389`, `:2435`) | nothing |
| `TurnSetup` | `HookPlayerChoiceContext(player, CombatPlayPhaseOnly)` of `SetupPlayerTurn`, reused for `RunAutoPrePlayPhase` (`Combat/CombatManager.cs:5059-5061`, `:5204-5205`): the first signal generates the hook action and releases `WaitForPauseOrCompletionWithoutAssigningTask` (`GameActions.Multiplayer/HookPlayerChoiceContext.cs:219-239`, `:653-660`); later signals only pause the hook action (`:241-244`, `:273-274`) | at the first signal of the frame: mark the frame, run `onFirstPause` once with the stack truncated to the frames below this one (the turn-loop root), restore the frames, then, if the steps ended the combat (in progress before them, not after), throw `ChoiceUnwindException`; later signals of the frame do nothing |
| `PerListener` | one `HookPlayerChoiceContext` per listener in six dispatchers (`Hooks/Hook.cs:2364` AfterDeath, `:2467` AfterDiedToDoom, `:6170`/`:6225` AfterSideTurnEnd, `:7776`/`:7819` BeforeFlush, `:8398`/`:8441`/`:8484` BeforeSideTurnEnd, `:8607` BeforeSideTurnStart): the first signal defers that listener's remainder | inline, plus `FidelityFlag.DeferredListenerPromptInline` with the prompt's `SiteId` on `cs.Run.Fidelity`, once per frame (the game defers once per context, `HookPlayerChoiceContext.cs:219`) — D2 |
| `Throwing` | `ThrowingPlayerChoiceContext.SignalPlayerChoiceBegun` throws (`GameActions.Multiplayer/ThrowingPlayerChoiceContext.cs`) | `InvalidOperationException` naming the site |

D2 overrides `docs/sim-port/design-combat-kernel.md` §4.6, which proposed `NotSupportedException` for `PerListener`.

What the TurnSetup callback runs is the loop's (B1 table, B6): `Hook.AfterSideTurnStart(Player, participants)` (`:5080`), every player's
`OrbQueue.AfterTurnStart` (`:5118-5120`), the dead-player auto-ready check (`:5143-5160`) and `CheckWinCondition` (`:5226`) for a first
pause in `SetupPlayerTurn`; only `CheckWinCondition` for one in `RunAutoPrePlayPhase` (`:3863` awaits the setup task, body
`:3902-3914`). The runtime guarantees: exactly once per frame per execution, before the answer is applied (the callback runs at the
signal, which precedes the option count and `Ask`), with the context stack reset during the callback, and the unwind: when the deferred
win check ends the combat the game never runs the queued `CombatPlayPhaseOnly` hook action, so the rest of the action must not run
either; `ChoiceUnwindException` carries the action out to `CombatSim`, which accepts the terminal state (§5.8). The steps are not
re-run on the selector path, because an installed selector skips the signal (§4.1).

### 3.4 Selectors

`PushSelector(IChoiceAnswerer)` keeps the contract signature but requires `IChoiceSelector` with a non-empty `SelectorId`
(`ArgumentException` otherwise), and rejects a selector that is `ICanonical` without `IStatefulChoiceSelector` (it would be shared by
clones). `PopSelector()` pops the top (throws on an empty stack). `PopSelector(selector)` pops only when the top is that object and reports
it, and `PushSelectorScope(selector)` returns a `SelectorScope` that pops at most once and only when its selector is on top — the game's
`StackedSelectorScope.Dispose` (`Commands/CardSelectCmd.cs:50-60`) returned by `PushSelector` (`:3134-3139`). The game's only content
user pushes and disposes inside one hook call (`Models.Relics/WhisperingEarring.cs:86`, `:159-162`), so a scope never spans a decision.
`UseSelector` (`:3122-3132`, AutoSlay only, `AutoSlay/AutoSlayer.cs:1920`), `SuspendSelectorForTest` and the local selector stack
(`:3086`, `:3100-3110`; no rule caller) have no sim counterpart.

`VakuuCardSelector` answers `Take(options, maxSelect)` (`Models.Relics/VakuuCardSelector.cs:12-15`) with the prompt's clipped `MaxSelect`.
Its card-reward method (`:17-23`, first offered card) belongs to W2's `CardReward` prompt; the generic answer picks option 0, which is the
first offered card as long as the W2 prompt lists offered cards first (B1: "offered cards, then alternatives").

### 3.5 Clone and canonical form

`DeepClone` copies the selector list; a stateful selector is replaced by its `DeepClone()` and aliasing inside one state is kept (the
same object twice on the stack becomes one clone twice); stateless selectors are shared (immutable). `Answerer` and the context stack are
transient. `WriteCanonical`: tag `Runtime`, selector count, then per selector tag `Selector`, `SelectorId` (a stable string, never
`Type.FullName`), a presence byte and the stateful payload. With an empty stack the bytes are the scaffold's (tag + count 0), so P0
hashes and the EndToEnd pins are unchanged.

## 4. `CardSelectCmd` (`Commands/CardSelectCmd.cs`)

### 4.1 Common rules

Signatures are the scaffold's: `cs` first, `choiceContext` dropped, the game's `AbstractModel source` replaced by `ChoiceSource`
(scaffold M10, §8 item 1). Every method rejects `default(ChoiceSource)` (null `SiteId` or `SourceId`) with `ArgumentException` right after
its null checks, before any game rule, so a missing source fails deterministically whatever the combat state.

Dropped in every method, none of which reorders a rule effect: `PlayerChoiceSynchronizer.ReserveChoiceId` / `SyncLocalChoice` /
`WaitForRemoteChoice` (multiplayer sync and the recorder hook), `SignalPlayerChoiceEnded` (no sim effect for any context: the end signal
only requests the paused action's resumption and waits for it, `GameActions.Multiplayer/GameActionPlayerChoiceContext.cs:45-49`, the state machine of `:124-134`), `ShouldSelectLocalCard`
(`:3152-3159`, always true in single player outside replays) and its remote branch, `LocalSelector` (§3.4), `ReportSoftlock`
(`:3161-3166`, log and Sentry), `LogChoice` (`:3842-3846`), `NPlayerHand.CancelAllCardPlay` and screen construction (UI),
`SaveManager.MarkCardAsSeen` (`:3202-3217`, save file), `prefs.ShouldGlowGold` (`:3730-3739`, UI) and `PlayerChoiceOptions`
(`CancelPlayCardActions` cancels queued play actions, `:3685`, `:3761`; the sim has no action queue).

**`UndoEndTurnIfNecessary` (`:3848-3854`; called at `:3182`, `:3303`, `:3373`, `:3680`, `:3756`) is omitted, with this justification.** It
acts only when the player is in `CombatTurnState.PlayersReadyToEndTurn` and the current side is the player's, and then
`UndoReadyToEndTurn` (`Combat/CombatManager.cs:6859-6881`) removes the player from that set, sets the UI flag `PlayerActionsDisabled` and
raises the UI event `PlayerUnendedTurn`. In single player the end-turn signal has already fired by then (`SetReadyToEndTurn` sets
`EndTurnSignalSource` when all players are ready, `:6836-6855`, and the turn flow continues from it, `:6715-6746`), so undoing readiness does
not cancel the turn end, and `StartTurn` clears the set at the next player turn (`:5298`). The only rule readers of the set in
v0.111.0 are `PlayerCmd.EndTurn`'s guard (`Commands/PlayerCmd.cs:984-994`; after an undo a second call only re-raises the UI event and
`TrySetResult`s a completed source) and `WhisperingEarring`'s auto-play loop condition (`Models.Relics/WhisperingEarring.cs:114`);
`PlayerActionsDisabled` is read only by nodes (`Nodes.Potions/NPotionPopup.cs:548`, `Nodes.Combat/NPlayerHand.cs:841`, `:1134`). The sim
has no readiness state: its `EndTurn` decision is atomic, and no ported content calls `PlayerCmd.EndTurn` (writers: `VoidForm`,
`Models.Cards/VoidForm.cs:91`; the multiplayer death path, `Commands/CreatureCmd.cs:2427`). So the omission has no observable effect today
and needs no fidelity flag. Hand-off: whoever ports `VoidForm` or `WhisperingEarring` adds the readiness set to `CombatState` (state) and
the clear call goes in these five methods before the selector check (contract change request 3).

Pause signal (§3.3): only when no selector is installed (`if (Selector == null)` / `if (Selector != null)` at `:3184`, `:3306`, `:3375`,
`:3682`, `:3758`), so a selector suppresses the turn-setup steps, the per-listener flag and the throwing context, as in the game.
`FromHand`, `FromSimpleGrid`, `FromCombatPile` and `FromHandForUpgrade` signal before counting their options (`:3309`, `:3378`, `:3685`,
`:3761`), so empty and automatic selections pause too; `FromChooseACardScreen` counts first and returns before signalling when it has no
card (`:3177-3191`).

### 4.2 `FromChooseACardScreen(cs, cards, player, canSkip = false, source)` — `:3169-3233`

| Game | Sim |
| --- | --- |
| more than 3 cards → `ArgumentException("Only works with less than 3 cards", "cards")` (`:3173-3176`) | same exception and message |
| 0 cards → `ReportSoftlock`, `null` (`:3177-3181`) | `null`, before the signal |
| no `IsEnding` / `IsOverOrEnding` early-out (B12 A14) | none |
| `UndoEndTurnIfNecessary` (`:3182`) | omitted (§4.1) |
| selector: `FirstOrDefault(GetSelectedCards(cards, 0, 1))` (`:3184-3187`) | `Ask` with `MinSelect 0, MaxSelect 1`; first pick or `null` |
| UI: signal (`:3191`), `NChooseACardSelectionScreen.ShowScreen(cards, canSkip)`; a click completes with that card, skip with nothing (`Nodes.Screens.CardSelection/NChooseACardSelectionScreen.cs:377`, `:403-406`); `FirstOrDefault` (`:3218`) | signal, then `Ask` with `Kind ChooseCard`, `Pile null`, options in list (generation) order, `MinSelect = canSkip ? 0 : 1`, `MaxSelect 1`, `RequireConfirm false`, `CanSkip = canSkip`, `Cancelable false`, `ResultType Index`; Confirm at zero picks is the skip; one card without skip is still a decision (B1 forced move) |

### 4.3 `FromSimpleGrid(cs, cardsIn, player, prefs, source)` — `:3295-3350`

`IsEnding` → empty (`:3299-3302`); `UndoEndTurnIfNecessary` (`:3303`, omitted); `cards = cardsIn.ToList()` (`:3304`); signal without a
selector (`:3306-3310`); no card → new empty list (`:3312-3315`); `!RequireManualConfirmation && count <= MinSelect` → all cards, a copy
(`:3316-3319`); otherwise selector or screen (`:3320-3343`). Prompt: `Kind SimpleGrid`, `Pile null`, generation order, bounds from
`prefs` clipped (§4.7), `RequireConfirm = prefs.RequireManualConfirmation`, `Cancelable = prefs.Cancelable`, `ResultType Index`. The grid
screen does not clip `MinSelect` (`Nodes.Screens.CardSelection/NSimpleCardSelectScreen.cs:338`, auto-complete at `MaxSelect` `:348-351`).

### 4.4 `FromCombatPile(cs, pile, player, prefs[, filter], source)` — `:3353-3359`, `:3361-3439`

The four-argument overload passes the always-true filter (`:3353-3359`), so its options are a fresh list. Five-argument body: `IsEnding`
→ empty (`:3365-3368`); a non-combat pile → `InvalidOperationException("Cannot perform on a non combat pile")` (`:3369-3372`, after the
ending check: an ending combat returns empty even for a deck pile; `CardPile.IsCombatPile` = `PileTypeExtensions.IsCombatPile`,
`Entities.Cards/PileTypeExtensions.cs:25-32`); `UndoEndTurnIfNecessary` (`:3373`, omitted); signal (`:3375-3379`); options = `pile.Cards`
for a null filter, else `Where(filter).ToList()` (`:3381-3390`); none → empty (`:3393-3396`); `!RequireManualConfirmation && n <= MinSelect`
→ the options as they are, in pile order (`:3397-3400`; D12) — for a null filter that is the live pile list, as in the game (Tutor, the
only null-filter caller, takes `FirstOrDefault` before moving the card, `Models.Cards/Tutor.cs:69`, `:86`); otherwise the selector branch
sorts a draw pile by `OrderBy(Rarity).ThenBy(Id)` (`:3403-3405`) and asks.

Prompt: `Kind PileSelect`, `Pile = pile.Type`, options for the draw pile in that order and otherwise in pile order, bounds clipped,
`RequireConfirm = prefs.RequireManualConfirmation`, `Cancelable = prefs.Cancelable`, `ResultType CombatCard`. The sort
(`CardSelectCmd.SortByRarityThenId`, public) is LINQ's stable order: rarity by enum ordinal (`Entities.Cards/CardRarity.cs`, equal to the
sim enum), then `ModelId.CompareTo`, which compares `Category` then `Entry`, both `StringComparison.Ordinal` (`Models/ModelId.cs:66-74`);
every card's category is `CARD` (`Models/ModelDb.cs:465-483`), so the sim compares `card.Id` with `string.CompareOrdinal` and breaks ties
by pile position. The exposed order is the contract's (B1); the human screen shows rarity then localized title
(`Nodes.Screens.CardSelection/NCombatPileCardSelectScreen.cs:411-433`), which only changes positions on screen, not the result. The
combat-pile screen clips `MinSelect` and `MaxSelect` to the shown count (`:346`, `:359`), so clipping there is exact.

### 4.5 `FromHand(cs, player, prefs, filter, source)` and `FromHandForDiscard` — `:3668-3723`, `:3726-3742`

`IsOverOrEnding` → empty (`:3672-3675`); `CancelAllCardPlay` (`:3676-3679`, UI); `UndoEndTurnIfNecessary` (`:3680`, omitted); signal
(`:3682-3686`); options = hand cards passing `filter ?? true`, a new list in hand order (`:3687`); none → that empty list (`:3689-3692`);
`!RequireManualConfirmation && n <= MinSelect` → that list (`:3693-3696`); otherwise selector (`:3697-3700`) or the hand screen
(`:3701-3713`). Prompt: `Kind HandSelect`, `Pile Hand`, hand order, bounds clipped, `RequireConfirm`, `Cancelable` from `prefs`,
`ResultType CombatCard`. The hand screen's result is in click order (`Nodes.Combat/NPlayerHand.cs:1167-1177`), its confirm compares the
unclipped bounds (`:1270-1281`), and without manual confirmation it completes at `MaxSelect` (`:1262-1268`). `FromHandForDiscard` sets the
UI glow and calls `FromHand` (`:3726-3742`).

### 4.6 `FromHandForUpgrade(cs, player, source)` — `:3744-3795`

`IsOverOrEnding` → `null` (`:3748-3751`); UI cancel (`:3752-3755`); `UndoEndTurnIfNecessary` (`:3756`, omitted); signal (`:3758-3762`);
options = upgradable hand cards (`CardModel.IsUpgradable`, `Models/CardModel.cs:2149-2159`; the sim's `CardInstance.IsUpgradable`), hand
order (`:3763`); at most one → that card or `null` (`:3765-3768`); otherwise selector `GetSelectedCards(val, 1, 1)` (`:3769-3772`) or the
upgrade-mode hand screen with `CardSelectorPrefs(…, 1)` (`:3781`). Prompt: `Kind HandUpgrade`, `Pile Hand`, `MinSelect = MaxSelect = 1`,
`RequireConfirm false`, `CanSkip false`, `Cancelable false`, `ResultType CombatCard`.

### 4.7 Bounds, option order and flagged approximations

`MaxSelect' = clamp(prefs.MaxSelect, 0, n)`, `MinSelect' = min(clamp(prefs.MinSelect, 0, n), MaxSelect')` (B1). The automatic rules use
the unclipped `prefs.MinSelect`, as the game does. Option order (B1): hand order (HandSelect, HandUpgrade), draw pile by
(rarity, ordinal id), other piles in pile order, generation order (ChooseCard, SimpleGrid). Automatic results keep pile order (D12).
`Ordered` is true at every W1 site (D1; no site note proves an order-free result); the unordered rules of §6.3 exist for future sites and
are tested through a test-double prompt.

Two unreachable cases are approximated and flagged with `FidelityFlag.ApproximatedRule` (site strings are public constants on
`CardSelectCmd`), only on the UI path (a selector takes the game's selector path, which asks the selector with the unclipped bounds):

| Case | Game | Sim | Site string | Reachable in v0.111.0? |
| --- | --- | --- | --- | --- |
| hand or grid prompt with manual confirmation and `prefs.MinSelect > n` | confirm never enables on the hand (`NPlayerHand.cs:1273`) and grid (`NSimpleCardSelectScreen.cs:338`) screens | Confirm at `n` picks | `CardSelectCmd:MinSelectClippedToOptions@<SiteId>` | no: every manual-confirmation site has `MinSelect 0` (§7) |
| `MaxSelect 0` without manual confirmation and `n >= 1` | the screen opens and completes with nothing on the first click or confirm (`NCombatPileCardSelectScreen.cs:244-251`, `:331-339`, `:357-363`), recording an empty choice | empty result without a decision | `CardSelectCmd:ZeroMaxSelectCompletedWithoutDecision@<SiteId>` | no: Dredge and NeowsFury guard their count (`Models.Cards/Dredge.cs:111-112`, `Models.Cards/NeowsFury.cs:119-120`) |

## 5. `CombatSim` tape engine (B1 "Engine", B12 A14)

### 5.1 Decision step

At a decision point `Step(a)`: validate with `LegalActionGenerator.TryResolve` (an illegal action throws `InvalidOperationException` and
clones nothing), take `basis = State.DeepClone()` (S0), then run `a` with no answerer installed on the live state. Completed: the basis is
dropped and `State` is the same object as before. Prompt (`ChoicePendingException`): the pending tuple is `(S0 = basis, a, tape [],
prompts [], prompt, picks [])` and `State` is the live, partially executed state (the observation). Any other exception: `State` becomes
the basis (content of S0) and the exception propagates. This is B1's "keep S0 untouched, run on `work = S0.DeepClone()`" with the roles of
the two equal copies swapped: identical semantics and cost (one `DeepClone` per decision step), but `State` keeps its identity across
completed steps, which the P0 suite relies on (`LoopTests`, `CorpseSlugsSeapunkTests` hold `sim.State`, its player and monsters across
`Step`).

### 5.2 Prompt steps

`Pick(i)` that does not complete the prompt appends `i` to the picks: no re-run, no clone, `State` unchanged. A completing `Pick`
(`!RequireConfirm && picks == MaxSelect`), `Confirm` or `Cancel` builds the answer (`Cancel`: no picks, `Cancelled = true`), appends it and
the prompt to new tape lists, and re-runs `a` (or `Begin`) on `basis.DeepClone()` with a `ChoiceTapeAnswerer` installed: completed →
`State` = the re-run state, no prompt; next prompt → new pending tuple with the longer tape and empty picks. A failure leaves the
pending tuple and `State` as they were. One `DeepClone` per completed prompt.

### 5.3 Unordered answers

For `Ordered = false` the answer is sorted before it enters the tape (B1), so transposed picks produce the same tape, the same next prompt
hash and the same final state; `ChoiceRuntime` sorts fast-path answers the same way (§3.2). Picks of a prompt that is not yet complete
stay in pick order in `PendingPicks` and in the hash; through listed actions an unordered prompt is only ever picked in ascending order
(§6.3), so this distinguishes only states reached through unlisted picks.

### 5.4 Fast path `Step(a, answerer)`

At a decision point: validate, then run `a` on the live state with `answerer` installed — no clone, no `ChoicePendingException`; every
prompt is answered inline through `Ask` (selectors first). At a prompt: the prompt action is applied as in §5.2; a completing one re-runs on
the basis itself (each sim owns its basis, §5.7, so no clone is needed) with the tape answerer falling back to `answerer` after the tape.
On an exception the fast path leaves the state undefined (no backup by design).

### 5.5 `StateHash` and `WriteCanonical` at a prompt

`H(S0 ‖ action ‖ tape ‖ picks)`, written by `ChoiceTape.WritePending`: tag `Pending`, S0's canonical form, tag `PendingAction`, a
presence byte (A14: 0 for `Begin`, then nothing; 1 for a `GameAction`, then `Kind` as a byte, `CardIndex`, `TargetIndex` as int32), tag
`Tape`, answer count, per answer tag `Answer`, `Cancelled`, pick count and picks, then tag `Picks`, pick count and picks. A `Begin` tuple
therefore differs from every `GameAction` tuple with the same S0, tape and picks (`ActionKind` stays frozen). The first two bytes are
`0x5A03`, so a pending tuple never collides with a plain state (whose form starts with `0x5D01`). Without a prompt `StateHash` and
`WriteCanonical` are the state's, unchanged from P0.

### 5.6 Tape answerer and the debug invariant

`ChoiceTapeAnswerer(answers, prompts, fallback)` answers prompt `k` with tape entry `k` after checking, always (not only in debug builds,
it is a short loop), that prompt `k` has the recorded prompt's shape: `Kind`, `SiteId` (ordinal) and, element-wise, each option's
`InstanceId` and `ModelId` (`ChoiceTape.SameShape`; record equality on `ChoicePrompt` compares the option lists by reference, A14). A
mismatch throws `InvalidOperationException` ("choice tape divergence"). Past the tape it uses the fallback, else throws
`ChoicePendingException`. After a re-run `CombatSim` checks that every taped answer was consumed.

### 5.7 `Clone`, `Outcome`, `IsTerminal`

`Clone()` at a decision point is `FromState(State.DeepClone())` (Part A). At a prompt it deep-clones the observation and S0 and copies the
tape, the recorded prompts (new option arrays), the current prompt and the picks, so no mutable object is shared
(`DeterminismTests.GraphInspector` treats any shared non-empty array as shared state). At a prompt `IsTerminal` is false and `Outcome` is
`InProgress` (the outcome is decided when the pending action completes; a pending loss can coexist with a `FromChooseACardScreen` prompt,
which has no ending check).

### 5.8 `Create` and `Begin`

Every `Create` overload runs `CombatLoop.SetUpCombat` with the same arguments as before (the `RunRngSet` overloads clone the set once, D4),
takes `basis = DeepClone()` of the post-setup state, and runs `CombatLoop.StartCombat` on the set-up state (Part A §4.8: `CreateCombat =
SetUpCombat + StartCombat`). A turn-1 prompt (Toolbox in `BeforeHandDraw`, GamblingChip in `AfterPlayerTurnStart`) leaves a pending tuple
with the absent action (`Begin`); answers re-run `StartCombat` on clones of the post-setup state. Cost: one `DeepClone` per `Create`.

`ChoiceUnwindException` raised while the combat is no longer in progress (§3.3) is caught where the action runs, and the step completes in
that terminal state; raised with the combat still in progress it propagates as a failure.

### 5.9 Members

`PendingChoice`, `PendingPicks` (B1), plus `HasPendingChoice`, `PendingFromBegin`, `PendingAction` (null for `Begin`), `PendingTape`.

## 6. `LegalActionGenerator`

### 6.1 Decision point (no prompt)

Order (B1): plays (hand index, then target, unchanged P0 code), then `UsePotion` (slot ascending; per slot `NoIndex`, then monster index
ascending, then pets `-3, -4, …`), then `DiscardPotion` (slot ascending), then `EndTurn`.

Potion gates, for both kinds (B12 A4): the player is in `PlayerTurnPhase.Play` (`UsePotionAction` and `DiscardPotionGameAction` are
`CombatPlayPhaseOnly` in combat, `GameActions/UsePotionAction.cs:156-166`, `GameActions/DiscardPotionGameAction.cs:103-112`), the owner is
alive and `CanUseOrRemovePotions` holds (`Nodes.Potions/NPotionPopup.cs:285-289`, `:330-334`, `:560-564`). `DiscardPotion(slot)` then needs
only a potion in the slot. `UsePotion` also needs `Usage` `CombatOnly` or `AnyTime` (`:292-327`: `Automatic` disables use, `None` throws),
checked before delegating, so an `Automatic` potion never reaches the stub; for a potion that passes, legality is
`PotionActions.CanUse(cs, slot, resolvedTarget)` (relics-potions: the node rule `:285-345`, `PassesCustomUsabilityCheck`, `IsValidTarget`),
called only for non-null slots. P0 states hold no potion, so P0 flows never reach the stub.

Target resolution (`TryResolvePotionTarget`, public; B12 A4, scaffold §14 item 13, §8 item 13): `NoIndex` → the owner for a single-target
`TargetType`, else no creature (`GameActions/UsePotionAction.cs:80-95`: a target id or a multi-target type takes `GetCreature(id)`, a null
id giving null; otherwise the owner, `:94`); `t >= 0` → `State.Monsters[t]`; `-2` (`Allies[0]`, the player) → illegal, the player is
`NoIndex`; `t <= -3` → `Allies[-2 - t]` (pets). Single-target types: `Self`, `AnyEnemy`, `AnyPlayer`, `AnyAlly`, `TargetedNoCreature`
(`Entities.Cards/ActionTargetExtensions.cs:5-18`, ported as `IsSingleTarget`). The explicit `-2` would resolve to the same creature as
`NoIndex` for every single-target type and to an invalid target for every multi-target type (`Models/PotionModel.cs:618-653`), so making it
illegal keeps "one action per (slot, resolved target)" and keeps F4's "no legal unlisted action" true. In v0.111.0 the in-combat potion
types are 51 `AnyPlayer`, 9 `AnyEnemy`, 1 `Self` and 3 `AllEnemies` plus FoulPotion's in-combat `AllEnemies`
(`Models.Potions/FoulPotion.cs:199-208`), so per potion either `NoIndex` or the enemy indices are legal, never both. The node layer
resolves a missing target to the owner when the owner is a valid target (`Models/PotionModel.cs:609-612`), which agrees.

### 6.2 Validation of every kind (`TryResolve`, `IsLegal`; integration.md §8.3 seam 3)

Common: the combat is in progress and at the player side (P0). `EndTurn`: both indices `NoIndex` (P0). `PlayCard`: P0. `UsePotion`: slot in
range and filled, the gates of §6.1, the target encoding of §6.1 (no stray value is accepted: every `TargetIndex` either resolves or is
rejected), then `CanUse`. `DiscardPotion`: `TargetIndex == NoIndex`, slot in range and filled, the gates. `ChoosePick`, `ChooseConfirm`,
`ChooseCancel` without a prompt: illegal. Unknown kinds: illegal. The resolved target is returned for `PlayCard` and `UsePotion`
(`CombatLoop.Step` passes it on).

### 6.3 At a prompt (`FillPrompt`, `TryResolvePrompt`)

Only the three answer kinds are legal; plays, potions, `EndTurn` and unknown kinds are not (the selection UI is modal, B1). `Pick(i)`:
`TargetIndex == NoIndex`, `0 <= i < n`, `i` not picked, picks `< MaxSelect`. `Confirm`: both indices `NoIndex` and `CanConfirm`. `Cancel`:
both indices `NoIndex` and `Cancelable`. Listed order: picks ascending, then `Confirm`, then `Cancel`.

Ordered prompts list every unpicked option. Unordered prompts list only options after the last pick (B1) and, refining B1, only those
that leave enough later options to complete the selection (target `MaxSelect` without confirmation, `MinSelect` with it): the literal rule
would list, say, the last option first in a two-of-five prompt and dead-end there. Every subset still has exactly one listed path. A
state reached through an unlisted (legal) pick that leaves no listed action falls back to listing every unpicked option, so a pending
prompt always has a listed answer.

## 7. In-combat prompt sites (v0.111.0)

Generated from `sim/specs/inventory/kernel-gaps.json → prompt_sites` (the 61 in-combat rows; relic `AfterObtained` deck prompts are run
layer) and checked against the decompile: every cited line holds the `CardSelectCmd` call. `SiteId` is `<slug>:<hook>`
(`StringHelper.Slugify` of the class name, `Helpers/StringHelper.cs:68-73`); `SourceInstanceId` is the card's instance id for cards, the
monster's combat id for KnowledgeDemon and `ChoiceSource.NoInstance` for relics, potions and powers. `Ordered` is true at every site.
Bounds are the prefs (`∞` = 999999999 before clipping; `Cards`, `PutBack`, `Amount`, `num2`, `cardCount` are the site's values).

| Kind | SiteId | Call | Method | Prompt kind | Options | Bounds | Skip / filter | Sim context at the prompt |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Cards | `ABUNDANCE:OnPlay` | `Models.Cards/Abundance.cs:83` | FromChooseACardScreen | ChooseCard | generated | 1..1 |  | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `ACROBATICS:OnPlay` | `Models.Cards/Acrobatics.cs:115` | FromHandForDiscard | HandSelect | Hand | 1..1 |  | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `ARMAMENTS:OnPlay` | `Models.Cards/Armaments.cs:86` | FromHandForUpgrade | HandUpgrade | Hand | 1..1 |  | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `BEGONE:OnPlay` | `Models.Cards/Begone.cs:65` | FromHand | HandSelect | Hand | 1..1 |  | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `BRAND:OnPlay` | `Models.Cards/Brand.cs:172` | FromHand | HandSelect | Hand | 1..1 |  | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `BURNING_PACT:OnPlay` | `Models.Cards/BurningPact.cs:81` | FromHand | HandSelect | Hand | 1..1 |  | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `CHARGE:OnPlay` | `Models.Cards/Charge.cs:77` | FromCombatPile | PileSelect | Draw | Cards..Cards |  | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `CLEANSE:OnPlay` | `Models.Cards/Cleanse.cs:118` | FromCombatPile | PileSelect | Draw | 1..1 |  | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `COSMIC_INDIFFERENCE:OnPlay` | `Models.Cards/CosmicIndifference.cs:143` | FromCombatPile | PileSelect | Discard | 1..1 |  | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `DAGGER_THROW:OnPlay` | `Models.Cards/DaggerThrow.cs:153` | FromHandForDiscard | HandSelect | Hand | 1..1 |  | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `DECISIONS_DECISIONS:OnPlay` | `Models.Cards/DecisionsDecisions.cs:154` | FromHand | HandSelect | Hand | 1..1 | filter | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `DISCOVERY:OnPlay` | `Models.Cards/Discovery.cs:71` | FromChooseACardScreen | ChooseCard | generated | 0..1 | skip | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `DREDGE:OnPlay` | `Models.Cards/Dredge.cs:114` | FromCombatPile | PileSelect | Discard | num2..num2 |  | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `DUAL_WIELD:OnPlay` | `Models.Cards/DualWield.cs:70` | FromHand | HandSelect | Hand | 1..1 | filter | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `GLIMMER:OnPlay` | `Models.Cards/Glimmer.cs:115` | FromHand | HandSelect | Hand | PutBack..PutBack |  | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `GRAVEBLAST:OnPlay` | `Models.Cards/Graveblast.cs:125` | FromCombatPile | PileSelect | Discard | 1..1 |  | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `GUARDS:OnPlay` | `Models.Cards/Guards.cs:105` | FromHand | HandSelect | Hand | 0..∞ |  | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `HAND_TRICK:OnPlay` | `Models.Cards/HandTrick.cs:85` | FromHand | HandSelect | Hand | 1..1 | filter | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `HEADBUTT:OnPlay` | `Models.Cards/Headbutt.cs:125` | FromCombatPile | PileSelect | Discard | 1..1 |  | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `HEIRLOOM_HAMMER:OnPlay` | `Models.Cards/HeirloomHammer.cs:126` | FromHand | HandSelect | Hand | 1..1 | filter | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `HIDDEN_DAGGERS:OnPlay` | `Models.Cards/HiddenDaggers.cs:70` | FromHandForDiscard | HandSelect | Hand | Cards..Cards |  | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `HOLOGRAM:OnPlay` | `Models.Cards/Hologram.cs:120` | FromCombatPile | PileSelect | Discard | 1..1 |  | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `NEOWS_FURY:OnPlay` | `Models.Cards/NeowsFury.cs:122` | FromCombatPile | PileSelect | Discard | 0..num2 |  | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `NIGHTMARE:OnPlay` | `Models.Cards/Nightmare.cs:93` | FromHand | HandSelect | Hand | 1..1 |  | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `PHOTON_CUT:OnPlay` | `Models.Cards/PhotonCut.cs:119` | FromHand | HandSelect | Hand | PutBack..PutBack |  | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `PREPARED:OnPlay` | `Models.Cards/Prepared.cs:111` | FromHandForDiscard | HandSelect | Hand | cardCount..cardCount |  | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `PURITY:OnPlay` | `Models.Cards/Purity.cs:63` | FromHand | HandSelect | Hand | 0..Cards |  | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `QUASAR:OnPlay` | `Models.Cards/Quasar.cs:71` | FromChooseACardScreen | ChooseCard | generated | 0..1 | skip | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `SCAVENGE:OnPlay` | `Models.Cards/Scavenge.cs:73` | FromHand | HandSelect | Hand | 1..1 |  | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `SCULPTING_STRIKE:OnPlay` | `Models.Cards/SculptingStrike.cs:90` | FromHand | HandSelect | Hand | 1..1 | filter | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `SEANCE:OnPlay` | `Models.Cards/Seance.cs:77` | FromCombatPile | PileSelect | Draw | Cards..Cards |  | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `SECRET_TECHNIQUE:OnPlay` | `Models.Cards/SecretTechnique.cs:64` | FromCombatPile | PileSelect | Draw | 1..1 | filter | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `SECRET_WEAPON:OnPlay` | `Models.Cards/SecretWeapon.cs:64` | FromCombatPile | PileSelect | Draw | 1..1 | filter | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `SEEKER_STRIKE:OnPlay` | `Models.Cards/SeekerStrike.cs:141` | FromCombatPile | PileSelect | Draw | 1..1 | filter | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `SNAP:OnPlay` | `Models.Cards/Snap.cs:106` | FromHand | HandSelect | Hand | 1..1 | filter | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `SPLASH:OnPlay` | `Models.Cards/Splash.cs:102` | FromChooseACardScreen | ChooseCard | generated | 0..1 | skip | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `SURVIVOR:OnPlay` | `Models.Cards/Survivor.cs:120` | FromHandForDiscard | HandSelect | Hand | 1..1 |  | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `THINKING_AHEAD:OnPlay` | `Models.Cards/ThinkingAhead.cs:115` | FromHand | HandSelect | Hand | 1..1 |  | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `TRANSFIGURE:OnPlay` | `Models.Cards/Transfigure.cs:68` | FromHand | HandSelect | Hand | 1..1 |  | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `TRUE_GRIT:OnPlay` | `Models.Cards/TrueGrit.cs:124` | FromHand | HandSelect | Hand | 1..1 |  | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `TUTOR:OnPlay` | `Models.Cards/Tutor.cs:69` | FromCombatPile | PileSelect | Draw | 1..1 | null filter | Inline (PlayCardAction; caller's when auto-played) |
| Cards | `WISH:OnPlay` | `Models.Cards/Wish.cs:64` | FromCombatPile | PileSelect | Draw | 1..1 |  | Inline (PlayCardAction; caller's when auto-played) |
| Monsters | `KNOWLEDGE_DEMON:ChooseCurse` | `Models.Monsters/KnowledgeDemon.cs:124` | FromChooseACardScreen | ChooseCard | generated | 1..1 |  | Inline (Blocking); exposed with the enemy side current |
| Potions | `ASHWATER:OnUse` | `Models.Potions/Ashwater.cs:72` | FromHand | HandSelect | Hand | 0..∞ |  | Inline (UsePotionAction) |
| Potions | `ATTACK_POTION:OnUse` | `Models.Potions/AttackPotion.cs:72` | FromChooseACardScreen | ChooseCard | generated | 0..1 | skip | Inline (UsePotionAction) |
| Potions | `COLORLESS_POTION:OnUse` | `Models.Potions/ColorlessPotion.cs:73` | FromChooseACardScreen | ChooseCard | generated | 0..1 | skip | Inline (UsePotionAction) |
| Potions | `DROPLET_OF_PRECOGNITION:OnUse` | `Models.Potions/DropletOfPrecognition.cs:71` | FromCombatPile | PileSelect | Draw | 1..1 |  | Inline (UsePotionAction) |
| Potions | `GAMBLERS_BREW:OnUse` | `Models.Potions/GamblersBrew.cs:73` | FromHandForDiscard | HandSelect | Hand | 0..∞ |  | Inline (UsePotionAction) |
| Potions | `LIQUID_MEMORIES:OnUse` | `Models.Potions/LiquidMemories.cs:76` | FromCombatPile | PileSelect | Discard | 1..1 |  | Inline (UsePotionAction) |
| Potions | `POWER_POTION:OnUse` | `Models.Potions/PowerPotion.cs:72` | FromChooseACardScreen | ChooseCard | generated | 0..1 | skip | Inline (UsePotionAction) |
| Potions | `SKILL_POTION:OnUse` | `Models.Potions/SkillPotion.cs:72` | FromChooseACardScreen | ChooseCard | generated | 0..1 | skip | Inline (UsePotionAction) |
| Potions | `TOUCH_OF_INSANITY:OnUse` | `Models.Potions/TouchOfInsanity.cs:54` | FromHand | HandSelect | Hand | 1..1 | filter | Inline (UsePotionAction) |
| Powers | `ENTROPY_POWER:AfterPlayerTurnStart` | `Models.Powers/EntropyPower.cs:77` | FromHand | HandSelect | Hand | Amount..Amount |  | TurnSetup (loop scope) |
| Powers | `FOREGONE_CONCLUSION_POWER:BeforeHandDraw` | `Models.Powers/ForegoneConclusionPower.cs:142` | FromCombatPile | PileSelect | Draw | Amount..Amount |  | TurnSetup (loop scope) |
| Powers | `STRATAGEM_POWER:AfterShuffle` | `Models.Powers/StratagemPower.cs:74` | FromCombatPile | PileSelect | Draw | Amount..Amount |  | caller's (Inline in a play, TurnSetup in the turn-start draw) |
| Powers | `TOOLS_OF_THE_TRADE_POWER:AfterPlayerTurnStart` | `Models.Powers/ToolsOfTheTradePower.cs:74` | FromHandForDiscard | HandSelect | Hand | Amount..Amount |  | TurnSetup (loop scope) |
| Powers | `TYRANNY_POWER:AfterPlayerTurnStart` | `Models.Powers/TyrannyPower.cs:74` | FromHand | HandSelect | Hand | Amount..Amount |  | TurnSetup (loop scope) |
| Relics | `CHOICES_PARADOX:AfterPlayerTurnStart` | `Models.Relics/ChoicesParadox.cs:102` | FromSimpleGrid | SimpleGrid | generated | 1..1 |  | TurnSetup (loop scope) |
| Relics | `GAMBLING_CHIP:AfterPlayerTurnStart` | `Models.Relics/GamblingChip.cs:74` | FromHandForDiscard | HandSelect | Hand | 0..∞ |  | TurnSetup (loop scope) |
| Relics | `TOASTY_MITTENS:AfterPlayerTurnStart` | `Models.Relics/ToastyMittens.cs:81` | FromHand | HandSelect | Hand | 1..1 |  | TurnSetup (loop scope) |
| Relics | `TOOLBOX:BeforeHandDraw` | `Models.Relics/Toolbox.cs:80` | FromChooseACardScreen | ChooseCard | generated | 1..1 |  | TurnSetup (loop scope) |

Counts: 42 cards, 1 monster, 9 potions, 5 powers, 4 relics. Manual confirmation: Guards, Ashwater, GamblersBrew, GamblingChip
(`0..∞`), Purity (`0..Cards`), NeowsFury (`0..num2`); `Cancelable` is false at every site (`docs/sim-port/design-combat-kernel.md` §4.3).
B11 pilot cards with prompts (cards-kernel): Armaments, Acrobatics, Headbutt, Seance, Discovery, SeekerStrike; relics-potions pilots:
GamblingChip, AttackPotion, Ashwater. No site is reachable inside a per-listener dispatcher with v0.111.0 content except through
auto-play (Hellraiser draws inside `AfterSideTurnEnd` → SeekerStrike, the B2 chain), which records the D2 flag once the hooks owner
wraps the six dispatchers (B12 A20).

## 8. Decisions on `sim/specs/scaffold-w1.md` §14

1. **Source (item 1, M10).** Kept the explicit, caller-supplied `ChoiceSource`. An ambient "current model + method" would make every
   dispatcher, the play wrapper, the potion wrapper and monster moves maintain a model stack on the hot path, owned by three other areas,
   and would still need an explicit method name. The cost of the explicit form is one argument per call site (61 sites). `default` is
   rejected (§4.1); contract change request 1 asks to make `source` required on the three methods that default it.
2. **Selectors (item 12, M23).** Stable `SelectorId` (`IChoiceSelector`) in the canonical form; stateful selectors implement
   `IStatefulChoiceSelector` and are cloned by `ChoiceRuntime.DeepClone`; `PushSelector` rejects the two unsafe cases (§3.4).
3. **Potion targets (item 13).** `LegalActionGenerator` resolves `TargetIndex` (§6.1); `PotionActions.CanUse` / `UsePotion` receive the
   resolved `CreatureState?`; `-2` is never legal.

## 9. Round-2 findings (`verify-notes-choice.md`)

| Finding | Resolution |
| --- | --- |
| [high] choice W1 work missing at `ef7c7b9`; [medium/high] `Ask`, `PushContext`, `PopContext` and the `CardSelectCmd` entry points are stubs; [high] no `CardSelectCmd` rule ported | delivered: §3, §4; `PushContext` / `PopContext` were made working in phase 0b and are completed here with the first-pause flag; every rule cited |
| [medium/high] `CombatSim` has no tape engine, no `PendingChoice` / `PendingPicks` / fast path / `Begin` / prompt hash / invariant | delivered: §5 |
| [medium/high] `LegalActionGenerator` rejects the new kinds and lists no potion or prompt action | delivered: §6, with `CanUse` called only for non-null slots of usable potions |
| [low] `ChoiceTests.cs` and `choice.md` missing; site table and citations | delivered: §7 (61 sites, lines checked), §11 |
| [low] `default(ChoiceSource)` gives a null site and instance id 0 | rejected with `ArgumentException` in every method (§4.1); contract change request 1 |
| [low] `PopSelector` differs from the guarded `StackedSelectorScope` | `PopSelector(selector)` and `SelectorScope` port the guard (§3.4); the unguarded `PopSelector()` stays for the contract |
| [low] design note §4.1 says `UndoEndTurnIfNecessary` is skipped with a selector | it runs before the selector check at all five sites (`:3182`, `:3303`, `:3373`, `:3680`, `:3756`); omitted with the justification of §4.1 |
| [low] `FromCombatPile` throws for a non-combat pile after the `IsEnding` check | ported, order tested (§4.4) |
| [low] `FromChooseACardScreen` has no early-out; `FromHand` / `FromHandForUpgrade` use `IsOverOrEnding`, `FromSimpleGrid` / `FromCombatPile` use `IsEnding` | ported per method and tested in both directions (§4.2-§4.6) |
| [low] the draw-pile order is a stable `OrderBy(Rarity).ThenBy(ModelId)` with ordinal comparison | ported as a total order with the pile position as the last key; ordinal and stability tested (§4.4) |
| [low] `ChoiceRuntime.DeepClone` shares stateful selectors | stateful selectors are cloned (§3.5) |
| [low] selectors hashed by `Type.FullName` | stable `SelectorId` (§3.5) |
| [low] the frozen `ActionKind` has no `Begin` | presence byte (§5.5); contract change request 2 records the encoding |
| [low] record equality on `ChoicePrompt` / `ChoiceAnswer` compares lists by reference | the invariant and all tests compare element-wise (`ChoiceTape.SameShape`, §5.6) |
| [low] design §4.6 says `PerListener` throws `NotSupportedException` | D2 overrides it: inline plus the flag (§3.3) |

## 10. Fidelity flags recorded by this area

`DeferredListenerPromptInline` (D2, §3.3, site = the prompt's `SiteId`); `ApproximatedRule` with the two site strings of §4.7 (unreachable
in v0.111.0). No other knowing deviation: the dropped steps of §4.1 have no rule effect, and the option order and bounds clipping follow
the contract.

## 11. Tests (`sim/Sts2Sim.Tests/ChoiceTests.cs`)

Test doubles in the file: prompt cards (`HandDiscardCard`, `HandUpgradeCard`, `PileExhaustCard`, `GenerateChooseCard`, `GridCard`,
`UnorderedDiscardCard`, `TwoPromptCard`, `TurnSetupProbeCard`, `UnwindProbeCard`, `PerListenerProbeCard`, `ThrowingProbeCard`,
`SelectorProbeCard`, `RarityCard`), powers (`TurnStartDiscardPower` prompting in `AfterPlayerTurnStart`, `ChoiceProbePower` recording the
first-pause order), monsters (`ChoiceDummy`, `ChoiceDemon` prompting in its move with an inline context), potions (Automatic, Self, AnyEnemy,
AllEnemies), selectors (fixed, recording, stateful counting, an uncloneable one) and a scripted answerer.

| Test | Covers |
| --- | --- |
| `ExposedHandPromptCarriesSiteOptionsAndBounds` | prompt fields, hand order, observation, picks-only legality, result |
| `AutomaticSelectionsResolveWithoutAPrompt` | one/zero options, upgrade with one/zero upgradable, pile order of an automatic pile result (D12), `State` identity on a plain step |
| `ChooseACardScreenRulesAndForcedSingleOption` | forced single option, skip via Confirm, generation order, >3 throws, 0 returns before the signal, selector gets `MinSelect 0` |
| `DrawPileOptionsSortByRarityThenOrdinalIdStably` | rarity ordinal, ordinal id ('C' before 'a'), stable duplicates, mapping through the sorted order, discard pile order |
| `FromCombatPileRejectsNonCombatPilesAfterTheEndingCheck` | the exception, its position before the signal and after the ending check |
| `EarlyOutsUseIsEndingOrIsOverOrEndingPerMethod` | the per-method gate in an ending and in an over combat |
| `PauseSignalPrecedesCountingAndSelectorsSuppressIt` | signal before counting for the four methods, turn-setup steps on an automatic selection, selector suppression of steps, flag and throw |
| `ChoiceSourceIsRequired` | `default(ChoiceSource)` rejected by all seven entry points |
| `SelectionBoundsAreClippedAndApproximationsFlagged` | clipping, pick-order result, the two `ApproximatedRule` sites |
| `OrderedPicksGiveDistinctHashesAndResults` | non-completing pick without re-run, picks in the hash, distinct results |
| `UnorderedPicksAreSortedBeforeTheTape` | listing rule, legal unlisted pick, sorted answer, equal hashes at the next prompt and at the end |
| `ConfirmAndCancelLegality` | manual confirmation, malformed answers, Confirm only with `RequireConfirm || CanSkip`, Cancel |
| `PromptStatesRejectPlaysPotionsEndTurnAndStrayIndices` | modal prompt, stray indices, answers without a prompt |
| `PromptStateLegalActionClosure` | F4-style closure over 11 prompt shapes and 3 policies |
| `TapedAndFastPathsReachTheSameHash` | re-execution determinism, fast path, fast step from a prompt, answer validation |
| `CloneAtAPromptIsIndependent` | clone bytes and hash, independent observation and picks, siblings agree |
| `PendingHashIsTheTupleNotTheObservation` | `H(S0 ‖ action ‖ tape ‖ picks)` |
| `BeginAndGameActionTuplesHashApart` | presence byte, tape and picks and cancel flag and answer order in the hash |
| `CreateStopsAtATurnOnePromptThroughBegin` | `Begin`, S0 = post-setup state, determinism, mixed fast and taped answers |
| `TurnStartPromptReRunsEndTurnFromS0` | `EndTurn` re-run through the enemy turn into the next turn start |
| `EnemyTurnPromptIsExposedWithTheEnemySideCurrent` | a prompt during the enemy turn |
| `TurnSetupFirstPauseStepsRunOnceBeforeTheAnswer` | once, before the answer, stack reset during the steps, frame restored |
| `TurnSetupStepsThatEndTheCombatUnwindToTheTerminalState` | unwind, fast path agrees, direct throw |
| `PerListenerPromptRunsInlineAndRecordsTheFlag` | inline exposure, one flag per frame, selector suppression with Vakuu over the sorted draw pile |
| `ThrowingContextRejectsAPromptAndRestoresTheState` | `InvalidOperationException`, S0 restored |
| `SelectorStackAnswersFirstAndClonesStatefulSelectors` | selector order, push validation, guarded pops, scope, clone independence and aliasing of stateful selectors, selector state hashed |
| `ContextStackRules` | callback only for TurnSetup, top-only pause, LIFO, transient stack |
| `TapeInvariantComparesPromptsElementWise` | `SameShape` vs record equality, divergence, exhaustion, fallback |
| `DiscardPotionListingOrderAndGates` | discard order before `EndTurn`, Automatic potions never used, stray and range checks, the two gates |
| `PotionTargetIndexResolution` | the A4 resolution and the single-target table |
| `UsePotionListingDelegatesToPotionActions` | `W1Pending` on relics-potions: the full potion order and target legality through `CanUse` |

A mutation pass (scratch, not committed) applied 32 single-point faults to the area's code (each first-pause, flag, unwind, ordering,
sorting, hashing, clipping, early-out, gate, clone and validation rule); every one fails at least one `Test_Choice_*`.

## 12. Gates and measurements

| Gate | Result |
| --- | --- |
| `dotnet build sim/Sts2Sim.sln` | 0 warnings, 0 errors |
| `dotnet run --project sim/Sts2Sim.Tests` | 400 passed, 5 failed, 405 total; `PENDING total: 1` (`Test_Choice_UsePotionListingDelegatesToPotionActions`, relics-potions). Failures: the two known content gates (`B2Chain`, `PilotContentIsRegistered`) and `W1Gate_F1`, `F4`, `F5`, which now list `UsePotion` for the gate's `GateChargePotion` doubles and stop at the relics-potions stub `PotionActions.CanUse` (B1 requires the listing; the gate fills potion slots) |
| same suite with scratch bodies for `PotionActions.CanUse` / `UsePotion` / `DiscardPotion`, `PotionModel.IsValidTarget` (the decompile rules) and the two `CombatLoop.Step` potion cases (not committed) | 403 passed, 2 failed (the content gates); `PENDING total: 0`; F1 64, F4 48, F5 63 potion actions |
| as above plus scratch prompt cards registered under the six pilot ids | F1 196 prompt answers, F4 133 prompt states (closure holds), F5 230 prompt answers; all pass |
| Diff gate (`Sts2Sim.Diff -c Release`) | 4/4 bundles agree, 69/69 decisions |
| Bench (`Sts2Sim.Bench -c Release`, `NIBBITS_WEAK`, 3 s, base `263810d` from `git archive` interleaved, load average 18-35) | playouts base 96k / 135k / 284k vs this tree 37k / 77k / 135k decisions/s (0.39×, 0.57×, 0.48× per pair); `Clone()` and `StateHash()` paths are unchanged at decision points (differences are load noise); equal sinks show identical states. The step now pays B1's one `DeepClone` per decision; a scratch bench of the fast path `Step(a, answerer)` under the same load gave 79k-215k against 66k-122k for `Step(a)`. Gate ≥300 decisions/s/core holds by more than 100×; re-bench on a quiet machine after integration |

## 13. Open questions, contract change requests, hand-offs

Contract change requests:

1. Make `ChoiceSource source` required (drop `= default`) on `FromChooseACardScreen`, `FromSimpleGrid` and both `FromCombatPile` overloads,
   or document that `default` is rejected (the code rejects it now).
2. B1 "Engine": record the pending-tuple encoding of §5.5 (presence byte, then kind byte and two int32 indices; tape and picks after it) as
   the contract's, and that `State` keeps its identity across completed steps (§5.1).
3. When `VoidForm` or `WhisperingEarring` is ported: add the end-turn readiness set (`Combat/CombatTurnState.cs:52`) to `CombatState` and
   clear it in the five `CardSelectCmd` methods before the selector check (§4.1).
4. B1 legality at a prompt: the unordered completability refinement and the fallback of §6.3.
5. B1 `UsePotion` encoding: `TargetIndex -2` (`Allies[0]`) is never legal; the player is `NoIndex` (§6.1).
6. `PushSelector(IChoiceAnswerer)` requires `IChoiceSelector` (stable id) at run time; the signature could take `IChoiceSelector`.

Hand-offs (other owners; nothing here edits their files):

- loop: push `ChoiceContextKind.TurnSetup` with the B1 callback around `SetupPlayerTurn` and keep the same frame (or push a second one only
  when `ContextScope.FirstPauseDone` is false) for `RunAutoPrePlayPhase`; use `FirstPauseDone` to skip the deferred steps; push `Inline`
  around the end-of-turn hook context; add the `UsePotion` / `DiscardPotion` cases to `CombatLoop.Step` with the target from
  `LegalActionGenerator.TryResolve` and the win check after each (`GameActions/ActionExecutor.cs:130-139`); a `ChoiceUnwindException`
  leaving the combat over may also be caught in `RunTurnLoop`, `CombatSim` accepts either.
- hooks: wrap each listener call of the six per-listener dispatchers in `cs.Choice.PushContext(ChoiceContextKind.PerListener)` (B12 A20).
- everyone: code that captures exceptions in the game's order (`PotionModel.OnUseWrapper` captures the `OnUse` exception, runs
  `EndCardOrPotionEffect` and rethrows, `Models/PotionModel.cs:180-240`, B12 A3) must rethrow `ChoicePendingException` and
  `ChoiceUnwindException` unchanged (`throw;` or `ExceptionDispatchInfo`), never wrap them: the engine recognises them by type.
- pipeline (if the loop pushes `Throwing` around monster turns): `AttackCommand.Execute` with no context replaces it by a
  `BlockingPlayerChoiceContext` for its damage and `AfterAttack` (`Commands.Builders/AttackCommand.cs:423`, `:434`, `:1132`, `:1146`), so it
  pushes `Inline` there; a player listener that prompts in reaction to an enemy attack is then exposed during the enemy turn (B1).
- relics-potions: `PotionActions.CanUse` must reject `Automatic` and `None` usage, a dead owner, `!CanUseOrRemovePotions` and invalid
  targets, as the pending test expects; monster code that issues player-facing commands pushes `Throwing` (or the loop pushes it around
  monster turns) and KnowledgeDemon pushes `Inline` (its `BlockingPlayerChoiceContext`, `Models.Monsters/KnowledgeDemon.cs:124`).
- cards-kernel / relics-potions content: call the `CardSelectCmd` methods with `ChoiceSource.For(card, "OnPlay")` or
  `ChoiceSource.For(<id>, ChoiceSource.NoInstance, "<hook>")` as in §7.
- W2: reuse `ChoiceRuntime`, the tape engine and `ChoiceTape` for run prompts; `SignalPlayerChoiceBegun` takes a `CombatState` today
  (the per-listener flag needs `cs.Run`), so run-layer sites need a `RunState` overload.

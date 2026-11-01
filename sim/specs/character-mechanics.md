# Character mechanics: orbs, Osty and pets, stars, forge (CONTRACTS Part B §B9, the orb row of §B5, §B12 A15–A16)

W1 round 3, 2026-11-01, on top of `263810d` (phase 0b). Rule truth: decompile v0.111.0, cited as
`<ns-dir>/<File>.cs:<line>` with `MegaCrit.Sts2.Core.` dropped; every line number is a line of the raw decompiled
file (the async state machines included). Rules follow the `TestMode.IsOff` branch; VFX, SFX, animations, waits and
`choiceContext` plumbing are stripped as in Part A §1 (§9 lists each stripped point and why it cannot reorder
rules).

Files (Part B §B11 row "character-mechanics"):

| File | Contents |
| --- | --- |
| `Orbs/OrbModel.cs` | orb base: owner, values, turn triggers, `TriggerPassive`, `GetRandomOrb`, clone, canonical |
| `Orbs/OrbQueue.cs` | the queue: capacity, enqueue, insert, remove, turn triggers, combat reset |
| `Orbs/OrbCanonicalTags.cs` | `Orb 0x5B01`, `Queue 0x5B02`, `DarkOrbExtra 0x5B10`, `GlassOrbExtra 0x5B11` |
| `Orbs/Defs/{Lightning,Frost,Dark,Plasma,Glass}Orb.cs` | the five orbs (complete ports) |
| `Combat/Pipeline/OrbCmd.cs` | `AddSlots`, `RemoveSlots`, `Channel` (3 overloads), `EvokeNext`, `EvokeLast`, private `Evoke`, `Passive` |
| `Combat/Pipeline/StarsCmd.cs` | `GainStars`, `LoseStars`, `SetStars`, `SpendStars`, `PayExcessEnergyCostWithStars`, `ResetForNewCombat` (the only star writer) |
| `Combat/Pipeline/OstyCmd.cs` | `OstyCmd.Summon`, `SummonResult`, `PlayerPets` (the pet-list semantics of `PlayerCombatState` and `Player`) |
| `Combat/Pipeline/ForgeCmd.cs` | `ForgeCmd.Forge`, `GetSovereignBlades`, the `ISovereignBladeDef` seam |
| `Monsters/Defs/Osty.cs` | the Osty monster def (complete port) |
| `Sts2Sim.Tests/CharacterMechanicsTests.cs` | 60 tests `Test_CharacterMechanics_*` (§11) |

## 1. Orb base (`Orbs/OrbModel.cs` = `Models/OrbModel.cs`)

| Sim member | Game | Rule |
| --- | --- | --- |
| `Id` (abstract) | `ModelId.Entry` | slug of the class name (`Models/ModelDb.cs:485-488`): `LIGHTNING_ORB`, `FROST_ORB`, `DARK_ORB`, `PLASMA_ORB`, `GLASS_ORB` |
| `Owner`, `HasOwner`, `AttachTo(owner)` | `Owner` `:313-330` | a second, different owner throws (`:324-327`). The game's setter also accepts `null`, but its only writer in v0.111.0 is `orb.Owner = player` in `Commands/OrbCmd.cs:189`, so the sim has no detach |
| `HasBeenRemovedFromState` (private setter) | `:220-227` | written only by `RemoveInternal()` (`:517-520`), as in the game |
| `PassiveVal(cs)`, `EvokeVal(cs)` (abstract) | `:216-218` | scaffold M14: the game's properties read `Hook.ModifyOrbValue` through `Owner.Creature.CombatState` (`:505-508`), so the sim takes `cs` |
| `BeforeTurnEndOrbTrigger(cs)`, `AfterTurnStartOrbTrigger(cs)` | `:470-478` | default: nothing |
| `TriggerPassive(cs, target)` | `:480-493`, state machine `:31-193` | `count = Hook.ModifyOrbPassiveTriggerCount(cs, orb, 1, out modifyingModels)` (`:93`), then `Hook.AfterModifyingOrbPassiveTriggerCount(cs, orb, modifyingModels)` (`:94`, always called; the sim passes `null` for an empty list), then `Passive(cs, target)` `count` times (`:133-146`); the waits between passives (`:155-175`) are stripped |
| `Passive(cs, target)`, `Evoke(cs)` (virtual) | `:495-503` | defaults: nothing / no target |
| `ModifyOrbValue(cs, value)` | `:505-508` | `Hook.ModifyOrbValue(cs, this, value)` |
| `RemoveInternal()` | `:517-520` | sets `HasBeenRemovedFromState` |
| `GetRandomOrb(Rng rng)` | `:402-405`, `_validOrbs` `:197-204` | `OrbRegistry.Create(rng.NextItem(ValidOrbIds))`. `ValidOrbIds` is an immutable list in the game's order Lightning, Frost, Dark, Plasma, Glass (not `OrbRegistry.AllIds`, which is ordinal: Dark, Frost, Glass, Lightning, Plasma). `NextItem` draws one `NextInt(0, 5)` (`Random/Rng.cs:213-223`). The game returns the canonical model and both callers call `.ToMutable()` (`Models.Cards/Chaos.cs:89`, `Models.Powers/TrashToTreasurePower.cs:65`); the sim has no canonical/mutable split, so `GetRandomOrb` returns the fresh instance directly. Callers pass `cs.Rng.CombatOrbGeneration` (the `CombatOrbs` stream, `Runs/RunRngSet.cs:53`) |
| `CloneFor(newOwner)`, `CopyMutableReferenceState` | scaffold M12 | `MemberwiseClone` + owner rebound; the orb fields are decimals, so no reference state exists |
| `WriteCanonical` / `WriteExtraCanonical` | Part A §6 | tag `0x5B01`, id, owner combat id (or `-1`), removed flag, then the subclass fields (§2) |

Orbs are hook listeners (`ShouldReceiveCombatHooks => true`, `:334`): the combat list visits each player's orbs in
queue order after the potion slots (`Combat/CombatState.cs:894`), and an orb is live while
`!HasBeenRemovedFromState && Owner.IsActiveForHooks` (`Combat/CombatState.cs:1049`). The snapshot itself is the hooks
owner's (`ListenerSnapshot`); no v0.111.0 orb overrides a hook virtual, so orbs are inert listeners. UI-only members
are not ported: titles, descriptions, hover tips, icons, SFX (`:229-292`), `ActivatePassive`/`ActivateEvoke` and their
events (`:338-400`, `:456-468`, cleared in `AfterCloned` `:510-515`), `CreateSprite` (`:443-446`).

## 2. The five orbs (`Orbs/Defs/*.cs` = `Models.Orbs/*.cs`)

| Orb | Values | Turn trigger | Passive | Evoke |
| --- | --- | --- | --- | --- |
| Lightning | passive `ModifyOrbValue(3)` (`LightningOrb.cs:336`), evoke `ModifyOrbValue(8)` (`:338`) | before turn end: `TriggerPassive(null)` (`:341-352`, `:161`) | `ApplyLightningDamage(PassiveVal, target)` (`:355-367`, `:293-294`) | `ApplyLightningDamage(EvokeVal, null)` (`:370-375`, `:226`) |
| Frost | passive `ModifyOrbValue(2)` (`FrostOrb.cs:349`), evoke `ModifyOrbValue(5)` (`:351`) | before turn end: `TriggerPassive(null)` (`:354`, `:51`) | a target throws `InvalidOperationException("Frost orbs cannot target creatures.")` (`:257-260`); `GainBlock(owner, PassiveVal, Unpowered, null)` (`:263`); with `HibernatePower` on the owner each other player in `CombatState.Players` gains `PassiveVal` too (`:279-307`) | `GainBlock(owner, EvokeVal, Unpowered, null)` (`:388`); with Hibernate the other players gain `EvokeVal` (`:389-401`) and the result is every player creature (`:407`), else the owner (`:409`) |
| Dark | passive `ModifyOrbValue(6)` (`DarkOrb.cs:170`), evoke `_evokeVal` starting at 6 (`:164`, `:172`), no hook | before turn end: `TriggerPassive(null)` (`:175`, `:50`) | a target throws `"Dark orbs cannot target creatures."` (`:191-194`); `_evokeVal += PassiveVal` (`:196`) | `HittableEnemies`; none → no target (`:206-210`); else the first enemy with the lowest current HP (`Enumerable.MinBy`, which keeps the first minimum, `:212`) takes `EvokeVal` unpowered damage from the owner (`:214`); result `[weakest]` (`:215`) |
| Plasma | passive 1 (`PlasmaOrb.cs:220`), evoke 2 (`:222`), no hook | after turn start: `TriggerPassive(null)` (`:225`, `:46`) | a target throws `"Plasma orbs cannot target creatures."` (`:177-180`); `PlayerCmd.GainEnergy(PassiveVal, owner)` (`:182`) | `PlayerCmd.GainEnergy(EvokeVal, owner)` (`:259`); result `[owner]` (`:260`) |
| Glass | passive `ModifyOrbValue(_passiveVal)` with `_passiveVal` starting at 4 (`GlassOrb.cs:234`, `:240`), evoke `PassiveVal * 2` (`:242`) | before turn end: `TriggerPassive(null)` (`:245`, `:48`) | ignores the target; `targets = HittableEnemies.Where(IsHittable)` (`:195`), `passiveVal = PassiveVal` (`:196`); if `passiveVal > 0` (`:197`): `_passiveVal = max(0, _passiveVal - 1)` (`:201`) **then** `Damage(targets, passiveVal, Unpowered, owner)` (`:202`) | `enemies = HittableEnemies.Where(IsHittable)` (`:277`); `EvokeVal <= 0` → no target (`:278-281`); else `Damage(enemies, EvokeVal, Unpowered, owner)` (`:283`); result `enemies` (`:284`). `EvokeVal` is evaluated twice, as in the game |

`ApplyLightningDamage` (`LightningOrb.cs:378-408`, state machine `:41-122`): hittable opponents of the owner
(`GetOpponentsOf(owner).Where(IsHittable)`, `:382`); none → no target and **no draw** (`:383-386`); the target is the
given one, else `Owner.RunState.Rng.CombatTargets.NextItem(hittable)` (`:387`); then
`CreatureCmd.Damage(targets, value, ValueProp.Unpowered, owner)` (`:406`). `PassiveVal`/`EvokeVal` are argument
expressions, so `Hook.ModifyOrbValue` runs before the hittable scan and before the `CombatTargets` draw (`:226`,
`:294`). A given target still needs at least one hittable opponent to exist (the scan comes first); a dead given
target takes no damage (`CreatureCmd.Damage` skips dead targets).

`HittableEnemies` is `Enemies.Where(IsHittable)` (`Combat/CombatState.cs:622`), `IsHittable` is
`!IsDead && Hook.ShouldAllowHitting` (`Entities.Creatures/Creature.cs:640-653`); the sim uses
`CombatStatus.IsHittable` over `cs.Monsters` (`OrbModel.HittableEnemies`) and over `CombatStatus.GetOpponentsOf`
(`OrbModel.HittableOpponentsOf`). `CombatState.Players` is the player creatures in ally order
(`Combat/CombatState.cs:560-562`): `OrbModel.PlayersInCombat` = the `PlayerState`s of `cs.Allies`.
`HibernatePower` is read by id `HIBERNATE_POWER` (`owner.HasPower(id)`); no W1 owner ports it.

Per-instance state (Part A §6): Dark `_evokeVal` and Glass `_passiveVal` are decimals copied by `MemberwiseClone` and
written by `WriteExtraCanonical` under `DarkOrbExtra 0x5B10` / `GlassOrbExtra 0x5B11`.

## 3. Orb queue (`Orbs/OrbQueue.cs` = `Entities.Orbs/OrbQueue.cs`)

| Member | Game | Rule |
| --- | --- | --- |
| `MaxCapacity = 10` | `maxCapacity` `:386` | a constant only; the queue never applies it |
| `Orbs`, `Capacity` | `:392-401` | list order is game state |
| `Clear()` | `:408-412` | orbs cleared, capacity 0 |
| `AddCapacity(n)` | `:414-417` | `Capacity += n`, no cap (the cap lives in `OrbCmd.AddSlots`) |
| `RemoveCapacity(n)` | `:419-426` | `Capacity = max(0, Capacity - n)`; while `Count > Capacity` remove the last orb through `Remove` only (no `RemoveInternal`, no hook) |
| `TryEnqueue(orb)` | `:428-445`, state machine `:309-384` | `Capacity == 0` → `false`; `Count >= Capacity` → `InvalidOperationException("OrbQueue is full")`; else append, `true`. `SmallWait` (`:490-501`) is stripped |
| `Remove(orb)` | `:447-450` | `List.Remove` by reference (no model overrides `Equals`) |
| `Insert(idx, orb)` | `:452-460` | `idx >= Capacity` → `InvalidOperationException("idx cannot be greater than capacity")`; otherwise `List.Insert` (which throws `ArgumentOutOfRangeException` for `idx > Count` or `idx < 0`). No full check. No v0.111.0 caller |
| `BeforeTurnEnd(cs)` | `:462-474`, state machine `:117-210` | iterate a copy of the orbs (`ToList`, `:149`); before each orb, if the owner's creature has no combat (`:170`), **leave the loop** (`goto end_IL_000e`, `:182`), else `orb.BeforeTurnEndOrbTrigger` (`:172`) |
| `AfterTurnStart(cs)` | `:476-488`, state machine `:20-113` | the same over `AfterTurnStartOrbTrigger` (`:52`, `:73-85`) |
| `ResetForNewCombat(baseOrbSlotCount)` | `Entities.Players/PlayerCombatState.cs:274-276` | `Clear()` then `AddCapacity(baseOrbSlotCount)`: the queue a new `PlayerCombatState` builds (`Player.ResetCombatState`, `Entities.Players/Player.cs:1209-1212`, at combat setup `Combat/CombatManager.cs:6592`) |
| `CloneFor(owner)`, `WriteCanonical` | scaffold §10 | capacity, then the orbs |

The queue holds no owner reference (the `PlayerState` constructor, a state file, builds `new OrbQueue()`); the game's
"`_owner.Creature.CombatState != null`" is "a `PlayerState` in `cs.Allies` owns this queue", which is exact in single
player: a player creature leaves a combat only when the room exits (`Combat/CombatManager.cs:7058-7078`,
`Rooms/CombatRoom.cs:735-755`) or through the test-mode `MockCombatCleanupOrb` (`Models.Orbs.Mock`, not ported).
The empty-queue early return avoids the copy and changes nothing observable. The triggers' choice context is the
loop's (`Combat/CombatManager.cs:1198`, `:5118-5120`); no orb prompt exists in v0.111.0 (D10).

## 4. OrbCmd (`Combat/Pipeline/OrbCmd.cs` = `Commands/OrbCmd.cs`)

Signatures follow the game with `cs` first and `choiceContext` dropped: `AddSlots(cs, player, amount)` (`:647`),
`RemoveSlots(cs, player, amount)` (`:659`), `Channel<T>(cs, player)` (`:669-681`), `Channel(cs, orb, player)`
(`:683-696`), `EvokeNext(cs, player, dequeue = true)` (`:698-711`), `EvokeLast(cs, player, dequeue = true)`
(`:713-726`), private `Evoke(cs, player, orb, dequeue = true)` (`:728-742`), `Passive(cs, orb, target,
countAffectedByHooks = false)` (`:744-758`). `Channel(cs, string orbId, player)` is a sim addition = `Channel<T>` for
callers that reference an orb by id (Part B §B5: cross-kind references by id string); it creates through
`OrbRegistry.Create` (unknown id → `KeyNotFoundException`). `Channel<T>` creates `new T()`, the sim's
`ModelDb.Orb<T>().ToMutable()` (`:50`).

- **AddSlots** (`:647-657`): nothing when `IsOverOrEnding` (`:649`); `amount = Math.Min(10 - Capacity, amount)`
  (`:653`, `MaxSlots = 10`); `AddCapacity(amount)` (`:654`). Above 10 the amount turns negative and lowers the capacity
  without evicting orbs, as in the game.
- **RemoveSlots** (`:659-667`): only when not `IsOverOrEnding` (`:661`); `amount = Math.Min(Capacity, amount)`
  (`:663`); `RemoveCapacity(amount)` (`:664`).
- **Channel** (state machine `:84-259`): nothing when `IsOverOrEnding` (`:146`); if the character's base orb slot count
  is 0 and the capacity is 0, `AddSlots(player, 1)` (`:150-152`); `orb.Owner = player` (`:189`); if `Count >= Capacity`,
  `EvokeNext(player)` (`:190-192`); if `TryEnqueue(orb)` (`:204`, re-read from the player): `History.OrbChanneled`
  (`:216`), then `Hook.AfterOrbChanneled(cs, player, orb)` (`:219`). The owner is set even when the enqueue is
  declined; a full queue that the front evoke did not free throws "OrbQueue is full", as in the game.
  The character's `BaseOrbSlotCount` (`Models/CharacterModel.cs:71`, Defect 3 at `Models.Characters/Defect.cs:63`) is
  read as `player.BaseOrbSlotCount`: `Player.BaseOrbSlotCount` (`Entities.Players/Player.cs:373`) is written only by
  the constructor (`:693`) from `character.BaseOrbSlotCount` for a new run (`:714`) and from the save, which stored that
  value (`:721`, `:798`), so the two agree on every v0.111.0 path; a hand-built test player can make them differ.
- **EvokeNext / EvokeLast** (state machines `:462-538`, `:384-460`): if the queue is not empty, `Evoke` its first
  (`:503`) / last (`:425`) orb with `dequeue`. No gate of their own.
- **Evoke** (state machine `:261-382`): nothing when `IsOverOrEnding` (`:318`) or when the queue is empty (`:321`);
  `removed = dequeue && queue.Remove(orb)` (`:323-327`) **before** `orb.Evoke` (`:330`); then, only if the owner's
  creature still has a combat (`:353`; the sim: the player is in `cs.Allies`): `Hook.AfterOrbEvoked(cs, orb, targets)`
  (`:355`) and **after** it `RemoveInternal()` when the orb was removed (`:344-347`). Without a combat both are skipped
  and the dequeued orb keeps `HasBeenRemovedFromState == false`.
- **Passive** (state machine `:540-645`): nothing when `IsOverOrEnding` (`:593`); `countAffectedByHooks` →
  `orb.TriggerPassive` (`:596-607`), else `orb.Passive` once (`:608`), so `ModifyOrbPassiveTriggerCount` is consulted
  only with the flag (default false).

`choiceContext.PushModel/PopModel` (`:329`, `:352`, `:426`, `:440`, `:504`, `:518`, `:595`, `:620`) only tell the
choice context which model is acting; no orb opens a prompt (D10), so dropping them reorders nothing. Orb animations
and SFX (`:217-218`, `:327`, `:655`, `:665`) are stripped.

## 5. Stars (`Combat/Pipeline/StarsCmd.cs`)

`StarsCmd` is the only writer of `PlayerState.Stars` (B12 A16; the field is a plain property of a state file, so the
command records the history entry itself). The game's `Stars` setter records `History.StarsModified(new − old)`
whenever the value changes (`Entities.Players/PlayerCombatState.cs:138-154`, append `:150`); `GainStars(amount)` and
`LoseStars(amount)` reject a negative amount with `ArgumentException("Must not be negative.")` and clamp at 0 with an
`(int)` truncation of the decimal result (`:355-373`) — ported in the state file as `PlayerState.GainStars/LoseStars`,
which `StarsCmd` wraps with the before/after comparison.

| Member | Game | Rule |
| --- | --- | --- |
| `GainStars(cs, player, amount)` | `Commands/PlayerCmd.cs:843-855`, state machine `:355-424` | only if `!IsEnding` **and** `Hook.ShouldGainStars(cs, amount, player)` (`:390`; the hook is not consulted while ending): `PlayerCombatState.GainStars(amount)` (`:392`, entry = the integer change), then `Hook.AfterStarsGained(cs, (int)amount, player)` (`:393`). For a non-negative amount `(int)(s + a) − s == (int)a`, so the entry and the hook argument agree; the hook runs even when the truncated change is 0 |
| `LoseStars(cs, player, amount)` | `:857-865` | nothing when `IsEnding` (`:859`); `PlayerCombatState.LoseStars(amount)` (`:863`): the entry is the **clamped** integer change (3 − 5 → −3; 3 − 1.5 → 1, change −2) |
| `SetStars(cs, player, amount)` | `:867-879`, state machine `:693-793` | nothing when `IsEnding` (`:742`); `stars < amount` → `GainStars(amount − stars)` (`:744-747`, so `ShouldGainStars` applies); `stars > amount` → `LoseStars(stars − amount)` (`:759`) |
| `SpendStars(cs, player, amount)` | `Models/CardModel.cs:3435-3446`, state machine `:1399-1466` | `amount > 0` (`:1433`) → `PlayerCombatState.LoseStars(amount)` (`:1435`; **no** `IsEnding` gate, unlike `PlayerCmd.LoseStars`) then `Hook.AfterStarsSpent(cs, amount, player)` (`:1436`). `LastStarsSpent = amount` (`:1432`) is a card field written before the gate, so the caller keeps it (scaffold §16.6 item 12). B12 A16 cites `:1425-1428`; in this decompile the lines are `:1432-1436` |
| `PayExcessEnergyCostWithStars(cs, player, ref energyCost, ref starCost)` | `Entities.Players/PlayerCombatState.cs:338-342`, `Models/CardModel.cs:3411-3415` | if `energyCost > player.Energy` and `Hook.ShouldPayExcessEnergyCostWithStars(cs, player)` (`Hooks/Hook.cs:11533`): `starCost += (energyCost − Energy) × 2` (`StarsPerMissingEnergy`), `energyCost = Energy`. The shared block of `HasEnoughResourcesFor` and `SpendResources` (the second also checks `card.CombatState != null`, always true in combat) |
| `ResetForNewCombat(player)` | `PlayerCombatState` constructor (`_stars` default, `:26`, `:257-277`) | `Stars = 0` without an entry: a new combat state starts at 0 |

Star costs themselves are cards-kernel and loop code that already follows the game: `Hook.ModifyStarCost`
(`Hooks/Hook.cs:11071-11092`, negative costs pass through unchanged) inside `GetStarCostWithModifiers`
(`Models/CardModel.cs:3204-3216`: an X star cost is the owner's current stars), `HasEnoughResourcesFor`
(`Entities.Players/PlayerCombatState.cs:334-353`), `SpendResources` (`Models/CardModel.cs:3404-3419`).

All area dispatchers iterate `IterateCombatHookListeners` (`Hooks/Hook.cs:8792-8810`), which yields nothing once the
combat is over or ending (outside setup): `AfterStarsSpent` during the ending window therefore reaches no listener
although the spend itself happens (state machine `Hooks/Hook.cs:6560`).

## 6. Osty and pets (`Monsters/Defs/Osty.cs`, `Combat/Pipeline/OstyCmd.cs`)

**Osty** (`Models.Monsters/Osty.cs`): id `OSTY`; `MinInitialHp = MaxInitialHp = 1` (`:27`, `:29`); move state machine
of one `MoveState("NOTHING_MOVE")` with an empty perform, no intent, `FollowUpState` = itself, initial state = itself
(`:37-42`); `CheckMissingWithAnim(owner)` = `owner.IsOstyMissing` (`:74-78`, the shake is UI) is
`Osty.CheckMissingWithAnim(cs, owner)`. Stripped: offsets, scale range, anim and SFX constants (`:15-25`),
`DeathSfx`/`HasDeathSfx`/`IsHealthBarVisible` (`:31-35`), `GenerateAnimator` (`:44-72`).

**`OstyCmd.Summon(cs, summoner, amount, source) → SummonResult(MonsterState? Creature, decimal Amount)`**
(`Commands/OstyCmd.cs:336-394`; `SummonResult` = `Entities.Creatures/SummonResult.cs`). There is no
`IsOverOrEnding` gate; only the SFX checks `IsInProgress` (`:346-349`):

1. `amount = Hook.ModifySummonAmount(cs, summoner, amount, source)` (`:341`); `amount == 0` → `SummonResult(Osty, 0)`
   (`:342-345`).
2. `osty` = the first ally whose monster is Osty and whose pet owner is the summoner (`:350`, read among `cs.Allies`,
   by id `OSTY`).
3. Osty alive (`summoner.IsOstyAlive`, from the pet list) → `CreatureCmd.GainMaxHp(summoner.Osty, amount)` (`:351-354`).
4. Else `isReviving = osty != null` (`:357`). Reviving: a living `osty` throws `InvalidOperationException("We
   shouldn't make it here if Osty is still alive!")` (`:360-363`, reached when an alive Osty is not in the pet list),
   else `AddPetInternal(osty)` (`:364`). Creating: `PlayerCmd.AddPet<Osty>(summoner)` (`:368`; the loop's
   `PlayerCmd.AddPet(cs, owner, MonsterRegistry.Get("OSTY"))`), then `PowerCmd.Apply<DieForYouPower>(osty, 1, null,
   null)` (`:377`) ported by id: `IsEnding` → skip, `!CanReceivePowers(osty)` → skip, `PowerRegistry.Create
   ("DIE_FOR_YOU_POWER")`, then `FindExistingInstanceForStacking` → `PowerCmd.Apply` or `PowerCmd.ModifyAmount`, the
   body of `PowerCmd.Apply<T>` (`Commands/PowerCmd.cs:1215-1239`). Then `CreatureCmd.SetMaxHp(osty, amount)` (`:380`),
   `CreatureCmd.Heal(osty, amount)` (`:381`; the `playAnim = isReviving` argument is UI only,
   `Commands/CreatureCmd.cs:2111-2160`), and `Hook.AfterOstyRevived(cs, osty)` when reviving (`:382-385`).
5. `History.Summoned(cs, (int)amount, summoner)` (`:391`), `Hook.AfterSummon(cs, summoner, amount)` (`:392`),
   `SummonResult(summoner.Osty, amount)` (`:393`).

`source` stays `AbstractModel?` (the scaffold's dispatcher type); `ModifySummonAmount` has no v0.111.0 overrider
(`hooks.json`), and the only rule reading a forge source is `HammerTimePower` (`source is HammerTimePower`,
`Models.Powers/HammerTimePower.cs:75`), so a card source maps to `null` or its listener adapter without a rule change.
The Osty node tween/revive animation and `TrackBlockStatus` (`:369-378`, `:387-390`) are stripped.

**`PlayerPets`** — the pet-list semantics of `PlayerCombatState` / `Player` on `PlayerState.PetIds` (ids in insertion
order) and `MonsterState.PetOwnerId`:

| Sim | Game | Rule |
| --- | --- | --- |
| `GetPets(cs, player, buffer)` | `PlayerCombatState.Pets` (`:39`) | the listed pets in list order; an id resolves among `cs.Allies` first, then `cs.GetCreatureById` (state A19 adds allies there too) |
| `GetPet(cs, player, monsterId)` | `GetPet<T>` (`:389-392`) | the first listed pet with that id |
| `GetOsty`, `IsOstyAlive`, `IsOstyMissing` | `Player.Osty`, `IsOstyAlive`, `IsOstyMissing` (`Entities.Players/Player.cs:247-251`) | `GetPet("OSTY")`; alive = listed and `IsAlive`; missing = not alive |
| `AddPetInternal(player, pet)` | `:375-387` | already listed → nothing; else set the owner unless it already is this player (a pet with another owner throws, the `PetOwner` setter `Entities.Creatures/Creature.cs:520-535`), then append. The game's `Died += OnPetDied` subscription has no field in the sim: `NotifyPetDied` stands for it |
| `OnPetDied(cs, player, pet)` | `:433-445` | not listed → `InvalidOperationException("Player does not have pet …")` (the game prints the localized `LogName`; the sim prints the id); listed and `Hook.ShouldCreatureBeRemovedFromCombatAfterDeath(cs, pet)` → unlisted. `DieForYouPower` returns false for its owner (`Models.Powers/DieForYouPower.cs:37-44`) and stays after death (`:46-49`), so a dead Osty stays listed (and in `_allies`, B12 A15) — the revive branch relies on it |
| `NotifyPetDied(cs, creature)` | `creature.InvokeDiedEvent()` (`Commands/CreatureCmd.cs:2770`, `Entities.Creatures/Creature.cs:1100-1103`) | for every player ally that lists the creature, `OnPetDied`; other `Died` subscribers are UI or monster-owned (`SlumberingBeetle`, `SoulNexus`) |
| `ResetForNewCombat(player)` | `PlayerCombatState.AfterCombatEnd` `_pets.Clear()` (`:296`), new state per combat | `PetIds.Clear()` |

## 7. Forge (`Combat/Pipeline/ForgeCmd.cs` = `Commands/ForgeCmd.cs`)

`ForgeCmd.Forge(cs, amount, player, source) → IReadOnlyList<CardInstance>` (`:152-172`, state machine `:28-146`):

1. `IsOverOrEnding` → no blade (`:157-160`).
2. `blades = GetSovereignBlades(player, includeExhausted: false)` (`:161`).
3. None → `cs.CreateCard(CardRegistry.Get("SOVEREIGN_BLADE"), player)` (`:164`, `Combat/CombatState.cs:671-677`),
   `CardPileCmd.AddGeneratedCardToCombat(cs, blade, PileType.Hand, player)` (`:166`), appended to `blades` (`:167`).
   `CreatedThroughForge = true` (`:165`) is not written: the flag is read only by `SovereignBlade.AfterCardChangedPiles`
   to choose a VFX (`Models.Cards/SovereignBlade.cs:342-357`) and reset by `AfterCloned` (`:324-328`).
4. `IncreaseSovereignBladeDamage` (`:174-195`): every blade of `GetSovereignBlades(player, includeExhausted: true)`
   (`:178`) gets `AddDamage(amount)` (`:185`); `AfterForged()` (`:186`) only raises the `Forged` event, whose one
   subscriber is the state tracker (`Models/CardModel.cs:3281-3288`, `Combat/CombatStateTracker.cs:197`), and the VFX
   (`:187`, `:194`, `:218-284`) are stripped.
5. `Hook.AfterForge(cs, amount, player, source)` (`:170`); the result is `blades` (`:171`).

`GetSovereignBlades(player, includeExhausted)` (`:197-216`): the cards of `AllPiles` in order Hand, Draw, Discard,
Exhaust, Play (`Entities.Players/PlayerCombatState.cs:105-115`) that are not dupes (`!c.IsDupe`, `:201`), outside the
exhaust pile unless `includeExhausted` (`:203-210`), and are Sovereign Blades (`OfType`, by id `SOVEREIGN_BLADE`).
Consequences kept: a dupe never counts and is never forged; an exhausted blade does not count for "none owned" but is
forged; with only an exhausted blade, a new blade enters the hand and both are forged.

`AddDamage` is `Damage.BaseValue += amount; CurrentDamage = Damage.BaseValue` (`Models.Cards/SovereignBlade.cs:359-364`),
and `CurrentDamage` is SovereignBlade per-instance state that `AfterDowngraded` restores (`:330-335`). The blade is a
cards-kernel pilot reached by id, so ForgeCmd calls the seam `ISovereignBladeDef.AddDamage(card, amount)` on the
blade's def; a def without the seam gets `card.Vars.Damage.BaseValue += amount` and
`FidelityFlag.ApproximatedRule` at site `SOVEREIGN_BLADE:AddDamage` (the `<model id>:<method>` site convention of Part B
§B1; its `CurrentDamage` would not follow), once per forged blade.

## 8. Interfaces other owners call

| Caller (owner) | Call | Game site |
| --- | --- | --- |
| loop, combat setup (`Player.ResetCombatState`) | `player.OrbQueue.ResetForNewCombat(player.BaseOrbSlotCount)`, `StarsCmd.ResetForNewCombat(player)`, `PlayerPets.ResetForNewCombat(player)` | `Combat/CombatManager.cs:6592`, `Entities.Players/PlayerCombatState.cs:257-277` |
| loop, `DoTurnEnd` | `player.OrbQueue.BeforeTurnEnd(cs)` first | `Combat/CombatManager.cs:1198` |
| loop, `StartTurn` (a `TurnSetup` first-pause step, B1) | each player's `OrbQueue.AfterTurnStart(cs)` | `Combat/CombatManager.cs:5118-5120` |
| loop, `SpendResources` | `card.LastStarsSpent = amount; StarsCmd.SpendStars(cs, player, amount)` (optionally `StarsCmd.PayExcessEnergyCostWithStars` for the shared block) | `Models/CardModel.cs:3404-3446` |
| loop, `PlayerCmd.AddPet` | `PlayerPets.AddPetInternal(owner, pet)`, then `CreatureCmd.Add(cs, pet)` | `Commands/PlayerCmd.cs:123-124` |
| pipeline, `KillWithoutCheckingWinCondition` | `PlayerPets.NotifyPetDied(cs, creature)` where the game invokes `Died`; for a dying player `player.OrbQueue.Clear()`, then `Kill(PlayerPets.GetOsty(cs, player))` only if `PlayerPets.IsOstyAlive` | `Commands/CreatureCmd.cs:2770`, `:2701-2717` |
| cards-kernel, `SOVEREIGN_BLADE` def | implement `ISovereignBladeDef.AddDamage` as `SovereignBlade.AddDamage` | `Models.Cards/SovereignBlade.cs:359-364` |
| hooks, combat listener list | orbs after potions, in queue order; live iff `!HasBeenRemovedFromState && Owner.IsActiveForHooks` | `Combat/CombatState.cs:894`, `:1049` |
| content (W3) | `OrbCmd.Channel(cs, "<ORB_ID>", player)`, `OrbModel.GetRandomOrb(cs.Rng.CombatOrbGeneration)`, `StarsCmd.*`, `OstyCmd.Summon`, `ForgeCmd.Forge` | |

## 9. Stripped points

| Site | Stripped | Why no rule moves |
| --- | --- | --- |
| `OrbModel.TriggerPassive` | `CustomScaledWait` / `Wait` after each passive (`Models/OrbModel.cs:155-175`) | timing only |
| `OrbQueue.TryEnqueue` | `SmallWait` (`Entities.Orbs/OrbQueue.cs:352-360`, `:490-501`) | timing only |
| `OrbCmd` | `PushModel`/`PopModel`, orb anims, channel SFX (§4) | no orb prompt (D10); UI |
| orbs | `ActivatePassive`/`ActivateEvoke`, VFX, SFX, `UpdateVisuals` (§2) | UI events |
| `OstyCmd.Summon` | summon SFX, Osty node tween, revive anim, `TrackBlockStatus`, `OstyScaleToSize` | UI |
| `CreatureCmd.Heal(osty, amount, playAnim)` | `playAnim` | selects VFX only |
| `ForgeCmd` | `CreatedThroughForge`, `AfterForged` (`Forged` event), `PlayCombatRoomForgeVfx`, `PreviewSovereignBlade` | VFX and state tracker only (§7) |
| `PlayerCombatState.Stars` setter | `StarsChanged` event (`:151`) | UI |

## 10. Fidelity flags

`FidelityFlag.ApproximatedRule` at `SOVEREIGN_BLADE:AddDamage` (§7) is the only flag this area records; it fires only for a
`SOVEREIGN_BLADE` def that does not implement `ISovereignBladeDef`. No other deviation from the decompile is known.

## 11. Tests (`Sts2Sim.Tests/CharacterMechanicsTests.cs`)

60 tests. 33 run fully in this worktree; 27 cross another owner's stub and are wrapped in `W1Pending.Run` (they print a
`PENDING` line here and run for real once the stub lands):

| Depends on | Tests |
| --- | --- |
| hooks (`ModifyOrbValue`, `ModifyOrbPassiveTriggerCount`, `AfterOrbChanneled`, `AfterOrbEvoked`, `ShouldGainStars`, `AfterStarsSpent`, `ModifySummonAmount`, `AfterForge`) | orb base values and `ModifyOrbValue` reach; Lightning, Frost, Dark passive, Glass; `TriggerPassive`; counted `OrbCmd.Passive`; `Channel` (zero-slot character, full queue, by id/type); `EvokeNext`/`EvokeLast`; evoke order; star gain, `SetStars` gain half, spend; Summon with 0; forge counting; forge fallback; Dark/Glass hash |
| state (`CombatState.AddAlly`) | pet lookups |
| hooks + state | Summon on a living Osty, revive, revive after the pet list dropped it, a living Osty outside the list; clone/hash after every command |
| hooks + loop (`PlayerCmd.AddPet`) + pipeline (`CreatureCmd.Add`) + state | Summon creating Osty |

The creation branches look up pilot content by id: while `DIE_FOR_YOU_POWER` (pipeline) or `SOVEREIGN_BLADE`
(cards-kernel) is unregistered, the test asserts the `KeyNotFoundException` names that exact id and that nothing was
forged before the lookup; once registered it asserts the full branch (pet side, owner, allies not enemies, 1-HP
creation then `SetMaxHp`/`Heal`, `DieForYouPower` 1; blade in hand, owner, canonical damage + amount, the exhausted
blade forged too).

Verification of the pending tests (scratch copies of this tree, not committed): (a) the stubs replaced by minimal
ports — the 12 area dispatchers over the guarded listener snapshot as in `Hooks/Hook.cs` (state machines `:2875`,
`:4205`, `:4589`, `:4685`, `:4775`, `:6468`, `:6560`, `:6654`; `:10840`, `:10865`, `:11094`, `:11512`),
`CombatState.AddAlly`/`RemoveAlly` on the ally list, `CombatStatus.IsAttached` counting allies (A19), and a
`PlayerCmd.AddPet` that creates a 1-HP player-side pet, lists it and adds it to the allies: 60/60 pass, 0 PENDING, the
creation branches take the `KeyNotFoundException` path; (b) the same plus registered stand-ins for
`DIE_FOR_YOU_POWER` (`ShouldCreatureBeRemovedFromCombatAfterDeath` false for its owner, kept after death) and
`SOVEREIGN_BLADE` (damage 10, implementing the seam): 60/60, 0 PENDING, full creation branches; whole suite 432 passed,
2 failed (the two known content gates, §12); (c) copy (b) with the loop calling `OrbQueue.BeforeTurnEnd` at the start of
`DoTurnEnd` and `OrbQueue.AfterTurnStart` after the player side's `AfterSideTurnStart` (§8) and a probe: every registered
encounter × 6 seeds (A0/A10) from `CombatSim.Create` with 3 slots and Lightning, Dark, Glass channeled, random legal
play, a `GetRandomOrb(CombatOrbGeneration)` channel into the full queue at a fifth of the decision points (each evokes
the front orb), and at a third of them `Hash(Clone(x)) == Hash(x)`, a stepped clone leaving the source unchanged and a
lockstep twin with an equal hash: 426 steps, 149 clone and lockstep checks, 86 channels, all five orbs drawn, no
failure; the whole suite (433 with the probe) and the diff gate (4/4, 69/69) unchanged.

## 12. Gates

| Gate | Result |
| --- | --- |
| `dotnet build sim/Sts2Sim.sln` | 0 warnings, 0 errors |
| `dotnet run --project sim/Sts2Sim.Tests` | 432 passed, 2 failed, 434 total (374 before + 60 new); the failures are the known content gates `W1GateTests.Test_W1Gate_B2Chain_StepsAndRecordsDeferredListenerPromptInline` and `Test_W1Gate_PilotContentIsRegistered`, whose missing list no longer names orbs or `OSTY` (21 items left, other owners' pilots); `PENDING total: 27`, all from this file |
| Diff gate (`Sts2Sim.Diff -c Release`, P0 bundles) | bundles 4 (agree 4), decisions 69/69, outcome 4/4, soft diffs 0 |
| Bench (`Sts2Sim.Bench -c Release`, `NIBBITS_WEAK`) | see §14 |

## 13. Round-2 findings

| Finding (verify-notes-character-mechanics.md) | Resolution |
| --- | --- |
| [high] W1 deliverables absent (twice) | Delivered: `Orbs/*` (base, queue, five defs, tags), `OrbCmd`, `OstyCmd` (+ `PlayerPets`, `SummonResult`), `ForgeCmd`, `StarsCmd`, `Monsters/Defs/Osty.cs`, 60 tests, this spec. `OrbRegistry` holds the five orbs and `MonsterRegistry` holds `OSTY` |
| [medium] `OrbQueue` members and `TriggerPassive` throw | Implemented exactly as the finding lists (§1, §3); tests: capacity 0, full queue, no cap, `Insert` against capacity (and `List.Insert` bounds), eviction from the back without `RemoveInternal`, `Remove`/`Clear`, snapshot iteration, owner outside the combat, empty queue, `TriggerPassive` count and after-hook |
| [medium] Capacity never initialised from `BaseOrbSlotCount` | `OrbQueue.ResetForNewCombat(baseOrbSlotCount)` (with `StarsCmd.ResetForNewCombat`, `PlayerPets.ResetForNewCombat`) is the port of the per-combat `PlayerCombatState` construction; the loop calls it at combat setup (§8, contract change request). Tested (3 slots after reset). The character-vs-player slot count is equal on every v0.111.0 path (§4) |
| [low] Player death should clear orbs and kill only Osty | Pipeline call site; `OrbQueue.Clear` and `PlayerPets.IsOstyAlive`/`GetOsty`/`NotifyPetDied` exist for it (§8); B12 A15 already narrows B8 |
| [low] Turn flow never runs the orb triggers | Loop call sites (§8); the triggers are implemented and tested, including Plasma's turn-start `TriggerPassive` through `OrbQueue.AfterTurnStart` |
| [low] Summaries omit gates and filters (a)–(g) | All ported and cited: forge `:153-216` with the dupe and exhaust filters (§7); no cap in the queue, `Math.Min` in `AddSlots`, clamp in `RemoveSlots` (§3–§4); `IsEnding` gates, `(int)amount`, clamped delta (§5); `countAffectedByHooks`, turn triggers through `TriggerPassive` (§2, §4); evoke order (§4); no gate in `Summon` (§6); values evaluated before Lightning's draw, Plasma and Dark evoke without the hook (§2). Each has a test |
| [low] Test doubles cannot reach the creation branches; `AddPet` / `CreatureCmd.Add` missing | Phase 0b added the stubs (A15s). The creation branches are tested through `W1Pending` and the exact-id lookup (§11) and were run in full against stand-ins |
| [low] `HasBeenRemovedFromState` has a public setter | Private setter; `RemoveInternal` is the only writer |
| [low] No area spec | This file |
| [medium] Stubs on paths the game runs every turn | Implemented; empty queues return early; the P0 suite and the diff gate are unchanged (4/4, 69/69). Rebuttal of one wording: when the owner's creature has left the combat the game **leaves the loop** (`goto end_IL_000e`, `Entities.Orbs/OrbQueue.cs:85`, `:182`), it does not skip one orb; ported as the exit |
| [medium] Star spending skips `AfterStarsSpent` and `StarsModified` | `StarsCmd.SpendStars` records the entry and dispatches the hook (§5); routing `ActionExecutor.SpendStars` through it is the loop's change (B12 A16, §8). This area cannot flag inside a loop file |
| [medium] Members the Osty and star facades need do not exist | Phase 0b: `PlayerCmd.AddPet`, `CreatureCmd.Add`, `CombatState.AddAlly` stubs; star commands live in `StarsCmd` because `PlayerCmd` is not partial. `DieForYouPower` is applied by id (§6) |
| [low] Pets stored by id cannot be found | `PlayerPets` resolves ids through `cs.Allies`, then `GetCreatureById` (§6); the lookup test is pending on `AddAlly` |
| [low] Clone/hash coverage; Dark and Glass fields not hashed | Dark `_evokeVal` and Glass `_passiveVal` are written under `0x5B10`/`0x5B11`; tests: clone isolation and hash equality for capacity, orb list, removed flag, pet ids, stars and their entry, an orb's evoke; the accumulated values change the hash; equality after every command |
| [low] `GetRandomOrb` must use the game order | `ValidOrbIds` in the game order; the test pins it, shows the registry order differs, checks 120 draws against `NextInt(0, 5)`, one counter step per draw and only the `CombatOrbs` stream advancing |
| [low] Command paths depend on hooks and history stubs | History appends are phase 0b bodies; the dispatcher paths are `W1Pending` tests verified in scratch (§11) |
| [low] `OrbModel` / `OrbQueue` mapping differences unrecorded | Recorded: private setter (§1), no detach (§1), queue owner by ally scan (§3), character slot count (§4), the `AddSlots` cap (§4), forge dupe exclusion and `CreatedThroughForge` (§7) |

## 14. Bench

`NIBBITS_WEAK`, Release, base tree `263810d` (`git archive`) against this tree. The machine was saturated by other
agents for the whole session (load average 4 to 64 on 12 cores), so absolute figures are far below the recorded P0
numbers and only same-condition ratios mean anything:

| Session | base `263810d` | this tree | ratio |
| --- | --- | --- | --- |
| interleaved, 2 usable rounds (load 4–16) | 460 979 / 533 901 decisions/s; `Clone()` 4.19 / 2.86 µs; `StateHash()` 1.55 / 1.27 µs | 545 523 / 459 268; 2.95 / 4.17 µs; 1.37 / 2.45 µs | noise in both directions |
| side by side (both processes at once, same load), 5 rounds (load 40–64), medians | 75 296 decisions/s; 16.38 µs; 7.59 µs | 75 740; 17.09 µs; 8.24 µs | 1.01× throughput, 1.04× clone, 1.08× hash (per-round spread larger than the gap) |

No difference beyond noise is expected or seen: a P0 combat never reaches the new code, and its canonical form is
unchanged (empty queue, no pets, no extras). The W1 thresholds (≥ 300 decisions/s/core, `Clone()` ≤ 1 ms) hold by
orders of magnitude even under this load; the integrator re-benches the merged tree on a quiet machine.

## 15. Open questions and contract change requests

1. **Combat-setup reset (loop).** Call `OrbQueue.ResetForNewCombat(BaseOrbSlotCount)`, `StarsCmd.ResetForNewCombat`,
   `PlayerPets.ResetForNewCombat` where the game builds a new `PlayerCombatState` (`Combat/CombatManager.cs:6592`);
   without it a 3-slot (Defect) player starts with capacity 0 and never channels into a base slot.
2. **Star spend (loop).** `ActionExecutor.SpendStars` → `card.LastStarsSpent = amount; StarsCmd.SpendStars(cs, player,
   amount)` (B12 A16); optionally share `StarsCmd.PayExcessEnergyCostWithStars` with `SpendResources` and
   `CardInstance.HasEnoughResources`.
3. **Pet bookkeeping (loop, pipeline).** `PlayerCmd.AddPet` uses `PlayerPets.AddPetInternal`; `CreatureCmd.Kill` calls
   `PlayerPets.NotifyPetDied` at the `Died` point and, for a dying player, `OrbQueue.Clear` then kills a living Osty
   (§8). One implementation of the list semantics avoids two diverging copies.
4. **Sovereign Blade seam (cards-kernel).** The `SOVEREIGN_BLADE` def implements `ISovereignBladeDef.AddDamage` (both
   lines of `SovereignBlade.AddDamage`); until then every forge of a real blade records the §10 flag.
5. **Orb listeners (hooks).** Orbs join the combat listener list after potions with the §8 liveness rule; the area
   dispatchers must stay guarded (`IterateCombatHookListeners`), which the ending-window assertions of the spend test
   rely on.
6. **`HibernatePower`** is consulted by id and has no W1 owner; Frost's multi-player branch is exact but untestable in
   single player beyond "the owner is skipped".

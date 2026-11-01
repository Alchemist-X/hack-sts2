# events-3456-2 (W3 wave C) — index

Common drops: VFX/SFX/portrait/hover tips/loc formatting, `ThatDoesDamage` UI markers, multiplayer branches (single player only). Deck prompts go through `CardSelectCmd.FromDeck*` (run overloads, kernel edit); out-of-combat HP through `CreatureCmd.Damage/Heal/GainMaxHp(RunState, …)` (kernel edit; out-of-combat death does not run `CreatureCmd.Kill`, the player is left dead). Card rewards are populated before `OfferCustom`.

- SYMBIOTE — Models.Events/Symbiote.cs:24 — deck transform via new `CardCmd.TransformToRandom(RunState, …)`; map-history transform entry and `FloorAddedToDeck` not recorded.
- THE_FUTURE_OF_POTIONS — Models.Events/TheFutureOfPotions.cs:21 — `AfterGenerated` upgrade applied right after `Populate` (a later reroll would not re-upgrade); potion per option carried as slot-order index in `StringArg`.
- THE_LEGENDS_WERE_TRUE — Models.Events/TheLegendsWereTrue.cs:24 — NAB_THE_MAP needs SPOILS_MAP (not ported: throws NotImplementedException).
- TRIAL — Models.Events/Trial.cs:31 — EntrantNumber (Rng.Chaotic, display) dropped; DOUBLE_DOWN abandon-run popup modelled as a no-op (cancel path) with a fidelity record.
- WATERLOGGED_SCRIPTORIUM — Models.Events/WaterloggedScriptorium.cs:25 — none.
- WELLSPRING — Models.Events/Wellspring.cs:21 — BATHE needs GUILTY (not ported: throws NotImplementedException).
- WHISPERING_HOLLOW — Models.Events/WhisperingHollow.cs:25 — none.
- WOOD_CARVINGS — Models.Events/WoodCarvings.cs:26 — transforms via new `CardCmd.TransformTo(RunState, …)`.
- ZEN_WEAVER — Models.Events/ZenWeaver.cs:21 — none.
- DENSE_VEGETATION — Models.Events/DenseVegetation.cs:29 — Heal display var (HealRestSiteOption.GetHealAmount) not computed; REST uses `PlayerCmd.MimicRestSiteHeal` (now implemented).
- PUNCH_OFF — Models.Events/PunchOff.cs:30 — punch animation loop / cancellation token dropped; combat-layout creature creation (RunRng.Niche draw) is the framework's (not done by EventRoom yet).
- THE_LANTERN_KEY — Models.Events/TheLanternKey.cs:16 — FIGHT reward needs LANTERN_KEY (not ported: throws NotImplementedException); combat-layout note as PUNCH_OFF.
- WAR_HISTORIAN_REPY — Models.Events/WarHistorianRepy.cs:19 — `PlayerCmd.CompleteQuest` is a no-op kernel stub; reachable only through ModifyNextEvent (IsAllowed false).
- COLOSSAL_FLOWER — Models.Events/ColossalFlower.cs:21 — arrays from IL: `_prizeCosts {35,75,135}`, `_prizeDamage {5,6,7}`.
- BATTLEWORN_DUMMY — Models.Events/BattlewornDummy.cs:25 — SKIPPED: its encounters are [SimExclude] and `BattlewornDummyEventEncounter.RanOutOfTime` is a W3 gap.
- CRYSTAL_SPHERE — Models.Events/CrystalSphere.cs:20 — SKIPPED: CrystalSphereMinigame (Events.Custom.CrystalSphereEvent, ~1.7k lines) needs grid-click run actions.
- FAKE_MERCHANT — Models.Events/FakeMerchant.cs:26 — SKIPPED: custom merchant layout (NFakeMerchant purchase/throw flow) has no run-layer rules or actions.
- TINKER_TIME — Models.Events/TinkerTime.cs:20 — SKIPPED: MAD_SCIENCE per-instance state (TinkerTimeType/Rider) not ported; rider arrays at IL I_0094F520 etc. not yet read.

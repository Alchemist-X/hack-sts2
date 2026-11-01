# W3 relics 2+4+6 (wave A) index

All files: `sim/Sts2Sim.Core/Relics/Defs/<Class>.cs`. Dropped everywhere: `Flash()`, hover tips, `ThrowingPlayerChoiceContext`/`choiceContext`, VFX/SFX/waits. Cross-owner powers/cards/potions referenced by id (`PowerRegistry.Create`, `cs.CreateCard(id)`, `PotionCmd.TryToProcure(rs, id)`); `DexterityPower`/`StrengthPower` by type (exist in base). `PowerVar<T>` ported as `DynamicVar("<T name>", v)`.

- AKABEKO — Models.Relics/Akabeko.cs:19 — VIGOR_POWER by id.
- DIAMOND_DIADEM — Models.Relics/DiamondDiadem.cs:20 — BLUR_POWER by id.
- FAKE_SNECKO_EYE — Models.Relics/FakeSneckoEye.cs:17 — TestMode energy-cost override (`SetTestEnergyCostOverride`, `:258-276`) dropped; CONFUSED_POWER by id.
- FUNERARY_MASK — Models.Relics/FuneraryMask.cs:19 — `CardCmd.PreviewCardPileAdd` dropped.
- HELICAL_DART — Models.Relics/HelicalDart.cs:19 — HELICAL_DART_POWER by id; Shiv check via `HasTag` (canonical tags).
- SELF_FORMING_CLAY — Models.Relics/SelfFormingClay.cs:19 — SELF_FORMING_CLAY_POWER by id.
- SNECKO_EYE — Models.Relics/SneckoEye.cs:19 — TestMode override dropped as FAKE_SNECKO_EYE.
- SNECKO_SKULL — Models.Relics/SneckoSkull.cs:11 — `power is PoisonPower` → `power.Id == "POISON_POWER"`; `AfterModifyingPowerAmountGiven` is Flash only (empty override kept).
- TWISTED_FUNNEL — Models.Relics/TwistedFunnel.cs:24 — smoke VFX + `Cmd.CustomScaledWait` dropped (before the hittable snapshot, no reorder); POISON_POWER by id.
- UNDYING_SIGIL — Models.Relics/UndyingSigil.cs:12 — DOOM_POWER amount read by id.
- BELT_BUCKLE — Models.Relics/BeltBuckle.cs:20 — `CombatManager.IsInProgress` → `rs.Combat?.IsInProgress`.
- BIG_HAT — Models.Relics/BigHat.cs:19 — Ethereal filter on canonical keywords.
- CROSSBOW — Models.Relics/Crossbow.cs:17 — every player turn (no turn-1 gate, as the decompile).
- DELICATE_FROND — Models.Relics/DelicateFrond.cs:13 — loop until no open slot or a procure fails.
- HISTORY_COURSE — Models.Relics/HistoryCourse.cs:17 — gap: the sim history stores `CardInstanceId`, not the card; the card is found in the owner's combat piles, else throws `NotImplementedException("W3 gap: ...")`. Dupe = `cs.CloneCard` + `ExhaustOnNextPlay=false` + `IsDupe` + remove Exhaust (`Models/CardModel.cs:3571-3608`); `DupeOf` branch unreachable (source filtered `!IsDupe`). Card type/IsDupe read from the history snapshot taken at play time.
- MYSTIC_LIGHTER — Models.Relics/MysticLighter.cs:10 — none.
- ORANGE_DOUGH — Models.Relics/OrangeDough.cs:19 — none.
- PAELS_EYE — Models.Relics/PaelsEye.cs:21 — `CombatManager.IsPartOfPlayerTurn` (`Combat/CombatManager.cs:7520-7541`) inlined as a private helper; WhisperingEarring check by id; actor compare via `Stamp.ActorId`.
- PETRIFIED_TOAD — Models.Relics/PetrifiedToad.cs:15 — POTION_SHAPED_ROCK by id.
- REPTILE_TRINKET — Models.Relics/ReptileTrinket.cs:18 — REPTILE_TRINKET_POWER by id.
- RIPPLE_BASIN — Models.Relics/RippleBasin.cs:22 — attack check reads history `CardType`.
- VEXING_PUZZLEBOX — Models.Relics/VexingPuzzlebox.cs:16 — none.
- CHOICES_PARADOX — Models.Relics/ChoicesParadox.cs:24 — empty-generation `Log.Error`/Sentry dropped (early return kept).
- GAMBLING_CHIP — Models.Relics/GamblingChip.cs:16 — W1 pilot, not re-ported.
- TOASTY_MITTENS — Models.Relics/ToastyMittens.cs:20 — FromHand source model → `ChoiceSource` site `TOASTY_MITTENS:AfterPlayerTurnStart`.
- TOOLBOX — Models.Relics/Toolbox.cs:20 — none.

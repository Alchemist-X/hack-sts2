# cards-4-1 (W3 wave A) — port index

All files under `sim/Sts2Sim.Core/Cards/Defs/`. Dropped everywhere: TriggerAnim, VFX/SFX (WithHitFx, hit VFX nodes, Stomp's spike-splash loop), hover tips, glow flags (ShouldGlowGold/Red), ExtraRunAssetPaths.

- BANSHEES_CRY — Models.Cards/BansheesCry.cs:20 — card listener AfterCardEnteredCombat (IsClone guard) + AfterCardPlayed; Ethereal check reads the local keyword set (game `Keywords`).
- BOLAS — Models.Cards/Bolas.cs:24 — listener BeforeHandDraw; history CardPlayFinished.HappenedLastPlayerTurn matched by instance id (game compares card reference).
- DEATHS_DOOR — Models.Cards/DeathsDoor.cs:21 — Doom check = PowerReceivedEntry this turn with PowerId DOOM_POWER and ApplierId == owner.
- DEATH_MARCH — Models.Cards/DeathMarch.cs:21 — CalculatedDamageVar via DamageCmd.Attack(cs, Func) calling Calculate(cs, card, target).
- ECHO_FORM — Models.Cards/EchoForm.cs:15 — applies ECHO_FORM_POWER by id (powers-3457).
- EVIL_EYE — Models.Cards/EvilEye.cs:20 — none.
- FERAL — Models.Cards/Feral.cs:15 — var name "FeralPower" (PowerVar<FeralPower> name) as plain DynamicVar; FERAL_POWER by id.
- FINISHER — Models.Cards/Finisher.cs:22 — hit count from CalculatedVar "CalculatedHits"; CardPlay.Card.Type read from HistoryCardPlay.CardType (captured at play).
- FTL — Models.Cards/Ftl.cs:19 — none.
- GOLD_AXE — Models.Cards/GoldAxe.cs:20 — none.
- HELIX_DRILL — Models.Cards/HelixDrill.cs:22 — `card.Pile == null` → GetCombatPile(cs) == null; cost via EnergyCost.GetWithModifiers(cs, card, All).
- ITERATION — Models.Cards/Iteration.cs:15 — ITERATION_POWER by id.
- JUGGLING — Models.Cards/Juggling.cs:13 — JUGGLING_POWER by id.
- LETHALITY — Models.Cards/Lethality.cs:15 — LETHALITY_POWER by id.
- LUNAR_BLAST — Models.Cards/LunarBlast.cs:20 — none.
- MAKE_IT_SO — Models.Cards/MakeItSo.cs:19 — listener AfterCardPlayedLate; null pile treated as not-in-hand (game would NRE).
- MEMENTO_MORI — Models.Cards/MementoMori.cs:21 — none.
- MURDER — Models.Cards/Murder.cs:21 — none.
- NORMALITY — Models.Cards/Normality.cs:13 — listener ShouldPlay; empty OnPlay (Unplayable, game has none).
- NOSTALGIA — Models.Cards/Nostalgia.cs:13 — NOSTALGIA_POWER by id.
- PALE_BLUE_DOT — Models.Cards/PaleBlueDot.cs:15 — PALE_BLUE_DOT_POWER by id.
- PHANTOM_BLADES — Models.Cards/PhantomBlades.cs:16 — PHANTOM_BLADES_POWER by id; empty CanonicalTags override is the default.
- PINPOINT — Models.Cards/Pinpoint.cs:19 — listener AfterCardEnteredCombat (IsClone guard) + AfterCardPlayed.
- PULL_FROM_BELOW — Models.Cards/PullFromBelow.cs:22 — none.
- SPITE — Models.Cards/Spite.cs:21 — none.
- STOMP — Models.Cards/Stomp.cs:26 — listener AfterCardEnteredCombat (IsClone guard) + BeforeCardPlayed.
- SUPERMASSIVE — Models.Cards/Supermassive.cs:21 — none.
- TEAR_ASUNDER — Models.Cards/TearAsunder.cs:21 — unused RepeatVar(1) kept as in the game.
- THRUMMING_HATCHET — Models.Cards/ThrummingHatchet.cs:20 — shares Bolas' BeforeHandDraw helper.
- UNMOVABLE — Models.Cards/Unmovable.cs:16 — UNMOVABLE_POWER by id.

Kernel edit: `CardInstance.IsClone` (settable, copied by MemberwiseClone; not yet written to the canonical form, no sim path sets it until `CardModel.CreateClone` is ported).

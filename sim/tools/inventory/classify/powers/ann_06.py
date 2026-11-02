from annot_lib import A, P

A("InfestedPower", 3, ["Phrog Parasite: on owner death (not prevented) spawns 4 Wrigglers (StartStunned) in slots PhrogParasiteElite.GetWrigglerSlotName(0..3) on the owner's side",
                       "keeps combat from ending (ShouldStopCombatFromEnding always true while present)"],
  req=["CreatureCmd.Add(monster, combatState, side, slotName) (mid-combat summon)", "Wriggler monster def (StartStunned)", "MonsterDef coupling: PhrogParasiteElite slot names"],
  notes="RevealWrigglersAfterDeathAnim and the music trigger are UI.",
  vd=("cosmetic", "v0.107.1 waited for the death animation before spawning; v0.111.0 spawns hidden and reveals later (UI); spawn order unchanged"),
  batch="summon")

A("InfiniteBladesPower", 2, ["before the owner's hand draw: create Amount Shivs in hand (Shiv.CreateInHand)"],
  req=["Shiv card def + Shiv.CreateInHand helper (fixed-card creation)", "Hook.BeforeHandDraw (present)"],
  batch="cardops")

A("IntangiblePower", 1, ["owner's HP loss per instance capped at 1 while combat is in progress (ModifyHpLostAfterOsty)", "each damage instance to the owner capped at 1 (ModifyDamageCap)",
                         "Decrement at the end of each enemy turn"],
  vd=("cosmetic", "ModifyDamageCap gained a CardPlay parameter; body identical"),
  batch="core")

A("InterceptPower", 1, ["owner takes x(covered creatures + 1) from powered attacks (it soaks covered teammates' hits)", "removed at the end of the enemy turn"],
  req=["per-instance data: covered creature-id list (AddCoveredCreature)"],
  notes="Multiplayer card family (Intercept/Covered); StringVar text is UI.",
  vd=("cosmetic", "ModifyDamageMultiplicative gained a CardPlay parameter; body identical"),
  batch="core")

A("IterationPower", 3, ["the first Status card the owner draws each turn (counted from combat history, the current draw included): draw Amount"],
  req=["CombatHistory: CardDrawnEntry by owner, Status, HappenedThisTurn (entry recorded before AfterCardDrawn)"],
  vd=("cosmetic", "closure capture renamed; same logic"),
  batch="history")

A("JuggernautPower", 1, ["whenever the owner gains > 0 block: deal Amount (Unpowered) to a random hittable enemy"],
  rng=["RunRng.CombatTargets (NextItem over HittableEnemies)"],
  batch="core")

A("JugglingPower", 3, ["the owner's 3rd Attack play each turn (counter seeded from combat history on apply): before it resolves, add Amount clones of it to hand",
                       "counter resets at owner side turn end"],
  req=["CombatHistory: CardPlayStartedEntry (Attack, owner's player, this turn)", "CardModel.CreateClone", "CardPileCmd.AddGeneratedCardToCombat (Hand)",
       "HookParam.SideTurnParticipants"],
  vd=("rule-change", "v0.107.1 counted and cloned at AfterCardPlayed; v0.111.0 at BeforeCardPlayed (clones made before the 3rd attack resolves)"),
  batch="history")

A("KnockdownPower", 2, ["instanced debuff: powered attacks on the owner from anyone other than the applier deal x Amount", "removed at owner side turn end"],
  req=["HookParam.SideTurnParticipants"],
  notes="Same shape as FlankingPower; in single player only non-applier dealers (e.g. Osty) are multiplied.",
  vd=("cosmetic", "ModifyDamageMultiplicative gained a CardPlay parameter; body identical"),
  batch="core")

A("LeadershipPower", 1, ["other creatures on the owner's side deal +Amount with powered attacks"],
  notes="Unreferenced in v0.111.0 and v0.107.1. dealer.Side is read without a null check (a dealer-less powered attack would throw).",
  vd=("cosmetic", "ModifyDamageAdditive gained a CardPlay parameter; body identical"),
  oq=["LeadershipPower has no referrer in either decompile; confirm it is unreachable before porting."],
  batch="unreferenced")

A("LethalityPower", 3, ["the owner's first Attack each turn (combat history, replays excluded) deals x(1 + Amount/100)"],
  req=["CombatHistory: CardPlayStartedEntry (Attack, owner's player, this turn)", "CardInstance current pile type + CurrentPlayIndex"],
  vd=("cosmetic", "predicate uses CardPlay.Player instead of Card.Owner.Creature; equivalent in single player"),
  batch="history")

A("LightningRodPower", 3, ["after the owner's player energy reset: channel a Lightning orb, Decrement"],
  req=["OrbCmd.Channel<LightningOrb>", "OrbQueue"],
  batch="orbs")

A("LoopPower", 3, ["owner's player turn start (orbs non-empty): trigger the first orb's passive Amount times"],
  req=["OrbCmd.Passive", "OrbQueue"],
  vd=("cosmetic", "loop counter hoisted; same logic"),
  batch="orbs")

A("MachineLearningPower", 1, ["owner's player draws +Amount at hand draw"],
  batch="core")

A("MagicBombPower", 2, ["instanced debuff: at owner side turn end, if the applier is alive, owner takes Amount (Unpowered) and the power is removed", "removed if the applier dies"],
  req=["HookParam.SideTurnParticipants"],
  notes="Unreferenced in v0.111.0 (MagiKnight's MAGIC_BOMB move is a plain attack).",
  oq=["MagicBombPower has no referrer in either decompile; confirm it is unreachable before porting."],
  batch="unreferenced")

A("ManglePower", 2, ["temporary Strength loss (TemporaryStrengthPower, IsPositive false; origin card Mangle)"],
  req=["TemporaryStrengthPower base", "ITemporaryPower"],
  batch="temp", deps=["TemporaryStrengthPower"])

A("MasterPlannerPower", 2, ["each Skill the owner plays gains the Sly keyword"],
  req=["CardCmd.ApplyKeyword", "CardKeyword.Sly behaviour in discard"],
  batch="cardops")

A("MayhemPower", 2, ["when the owner's auto pre-play phase starts: auto-play the top Amount cards of the draw pile"],
  req=["Hook.AfterAutoPrePlayPhaseEntered", "CardPileCmd.AutoPlayFromDrawPile", "auto pre-play phase in the turn loop"],
  rng=["RunRng.CombatTargets (indirect: each auto-played AnyEnemy/AnyAlly card picks a random target in CardCmd.AutoPlay, Commands/CardCmd.cs:411/431)"],
  batch="cardops")

A("MindRotPower", 1, ["owner's player draws Amount fewer at hand draw (floor 0)"],
  batch="core")

A("MinionPower", 1, ["marks the owner as a secondary enemy (OwnerIsSecondaryEnemy): its death does not count for combat end and is not fatal; survives owner death"],
  req=["PowerModel.OwnerIsSecondaryEnemy (present)", "ShouldOwnerDeathTriggerFatal / ShouldPowerBeRemovedAfterOwnerDeath (present)"],
  batch="core")

A("MockCloneCardsOnPlayPower", 1, ["mock: every owner card play adds a clone of the card to hand"],
  req=["CardModel.CreateClone", "CardPileCmd.AddGeneratedCardToCombat"],
  notes="Test-only mock; not ported.",
  vd=("cosmetic", "v0.111.0 adds IsMock => true"),
  batch="mock")

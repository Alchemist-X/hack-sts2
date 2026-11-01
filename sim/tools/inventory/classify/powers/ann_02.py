from annot_lib import A, P

A("CorruptionPower", 2, ["owner's Skills cost 0 (TryModifyEnergyCostInCombatLate)", "owner's played Skills go to the Exhaust pile (ModifyCardPlayResultLocation)"],
  req=["Hook.ModifyCardPlayResultLocation", "Hook.AfterModifyingCardPlayResultLocation", "CardLocation (pileType, position) result struct"],
  notes="AfterModifyingCardPlayResultLocation only flashes (UI) but the dispatcher must exist (unknown scope in hooks.json: called from card-play code, not Hook.cs).",
  vd=("cosmetic", "hook API refactor: v0.107.1 ModifyCardPlayResultPileTypeAndPosition returned (PileType, CardPilePosition); v0.111.0 uses CardLocation; same effect"),
  batch="cardops")

A("CountdownPower", 2, ["owner side turn start: apply Amount Doom to a random hittable enemy"],
  req=["HookParam.SideTurnParticipants"],
  rng=["RunRng.CombatTargets (NextItem over HittableEnemies)"],
  batch="doom", deps=["DoomPower"])

A("CoveredPower", 1, ["instanced cover from a teammate's Intercept: owner takes no powered attack damage (x0)", "on apply: give the applier InterceptPower (1) if missing and register the owner with it",
                      "removed when the applier dies or at the end of the enemy turn"],
  notes="Multiplayer card (Intercept targets another player); the PlatformUtil player-name lookup is UI.",
  vd=("cosmetic", "ModifyDamageMultiplicative gained a CardPlay parameter; await-or-reuse polarity inverted; same logic"),
  batch="core", deps=["InterceptPower"])

A("CrabRagePower", 2, ["when another creature on the owner's side dies: gain 6 Strength, gain 99 block (BlockVar, Unpowered), remove itself"],
  req=["CreatureCmd.GainBlock(BlockVar overload)"],
  notes="AfterDeath does not test wasRemovalPrevented.",
  batch="monster:Crusher", deps=["StrengthPower"])

A("CreativeAiPower", 3, ["before the owner's hand draw: Amount times add one random Power card from the character pool to hand"],
  req=["CardFactory.GetDistinctForCombat", "CardPoolModel.GetUnlockedCards(UnlockState, CardMultiplayerConstraint)", "CardPileCmd.AddGeneratedCardToCombat (Hand)"],
  rng=["RunRng.CombatCardGeneration (one GetDistinctForCombat(…,1,…) per card)"],
  vd=("cosmetic", "loop counter hoisted; same logic"),
  batch="cardgen")

A("CrimsonMantlePower", 1, ["owner's player turn start: owner takes SelfDamage (Unblockable|Unpowered, starts 0, +1 per IncrementSelfDamage), then gains Amount block"],
  req=["mutable per-instance DynamicVar SelfDamage (IncrementSelfDamage called by the CrimsonMantle card)"],
  batch="core")

A("CrueltyPower", 1, ["raises the Vulnerable multiplier of the owner's powered attacks by Amount/100 (ModifyVulnerableMultiplier, called by VulnerablePower)"],
  req=["IVulnerableMultiplierModifier seam (present in sim MultiplierModifiers.cs)"],
  batch="core", deps=["VulnerablePower"])

A("CrushUnderPower", 2, ["temporary Strength loss (TemporaryStrengthPower, IsPositive false; origin card CrushUnder)"],
  req=["TemporaryStrengthPower base", "ITemporaryPower"],
  batch="temp", deps=["TemporaryStrengthPower"])

A("CuriousPower", 1, ["owner's Power cards with cost > 0 cost Amount less (floor 0)"],
  batch="core")

A("CurlUpPower", 1, ["Louse Progenitor: the first card whose powered attack damages the owner is remembered; after that card play the owner gains Amount block, sets Curled, removes itself"],
  req=["per-instance data: played card reference (card instance id)", "MonsterDef coupling: LouseProgenitor.Curled"],
  notes="ShouldScaleInMultiplayer is multiplayer-only.",
  batch="monster:LouseProgenitor")

A("DampenPower", 2, ["on apply: downgrade every upgraded card the player owns in combat, remembering each card's upgrade level",
                     "removed when all registered casters (AddCaster) died; on removal re-upgrades each remembered card that many times"],
  req=["CardCmd.Downgrade", "CardCmd.Upgrade", "PlayerCombatState.AllCards", "per-instance data: caster creature-id set, card-instance-id -> level map",
       "MonsterDef coupling: MagiKnight calls AddCaster"],
  notes="StackType None.",
  vd=("cosmetic", "local renamed; same logic"),
  batch="monster:MagiKnight")

A("DanseMacabrePower", 1, ["before each owner card play whose resolved energy cost >= 2: gain Amount block"],
  req=["CardEnergyCost.GetResolved(cs, card) (present; includes Hook.ModifyEnergyCostInCombat)"],
  batch="core")

A("DarkEmbracePower", 2, ["each non-Ethereal owner card exhausted: draw Amount", "Ethereal exhausts are counted and drawn (Amount x count) at owner side turn end"],
  req=["HookParam.SideTurnParticipants", "AfterCardExhausted causedByEthereal flag (present)"],
  batch="core")

A("DarkShacklesPower", 2, ["temporary Strength loss (TemporaryStrengthPower, IsPositive false; origin card DarkShackles)"],
  req=["TemporaryStrengthPower base", "ITemporaryPower"],
  batch="temp", deps=["TemporaryStrengthPower"])

A("DebilitatePower", 2, ["doubles the extra of Vulnerable on the owner (m -> m + (m-1)) and of Weak on the owner's attacks (m -> m - (1-m)) via the multiplier seams", "Decrement at owner side turn end"],
  req=["HookParam.SideTurnParticipants", "IVulnerableMultiplierModifier / IWeakMultiplierModifier seams (present in sim)"],
  batch="core", deps=["VulnerablePower", "WeakPower"])

A("DemesnePower", 1, ["owner's player draws +Amount at hand draw and has +Amount max energy"],
  batch="core")

A("DemisePower", 2, ["owner side turn end: owner takes Amount damage (Unblockable|Unpowered, no dealer)"],
  req=["HookParam.SideTurnParticipants"],
  batch="core")

A("DemonFormPower", 2, ["owner side turn start: gain Amount Strength"],
  req=["HookParam.SideTurnParticipants"],
  notes="AfterApplied/AfterRemoved only drive a VFX node.",
  vd=("cosmetic", "v0.111.0 adds VFX-only AfterApplied/AfterRemoved; same rule"),
  batch="core", deps=["StrengthPower"])

A("DevourLifePower", 3, ["whenever the owner plays a Soul card: summon Osty for Amount (OstyCmd.Summon, source this power)"],
  req=["Osty / pets", "OstyCmd.Summon", "Soul card def"],
  batch="osty")

A("DexterityPower", 1, ["block +Amount on powered card/monster-move block gained by the owner (negative allowed)"],
  notes="Already ported (sim Powers/Defs/DexterityPower.cs).",
  batch="core")

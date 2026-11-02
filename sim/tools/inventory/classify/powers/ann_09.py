from annot_lib import A, P

A("PhantomBladesPower", 3, ["owner's Shiv-tagged cards get Retain (all on apply, new ones on entering combat)",
                            "the owner's first Shiv each turn (no Shiv CardPlayFinished this turn in combat history) deals +Amount"],
  req=["CardCmd.ApplyKeyword (Retain)", "CombatHistory: CardPlayFinishedEntry (Shiv tag, owner's player, this turn)", "PlayerCombatState.AllCards",
       "Hook.AfterCardEnteredCombat (present)"],
  vd=("cosmetic", "predicate uses CardPlay.Player (equivalent in single player)"),
  batch="history")

A("PiercingWailPower", 2, ["temporary Strength loss (TemporaryStrengthPower, IsPositive false; origin card PiercingWail)"],
  req=["TemporaryStrengthPower base", "ITemporaryPower"],
  batch="temp", deps=["TemporaryStrengthPower"])

A("PillarOfCreationPower", 1, ["whenever the owner's player generates a card for combat: gain Amount block (Unpowered)"],
  req=["Hook.AfterCardGeneratedForCombat (present; must be dispatched from every card-creation site)"],
  batch="core")

A("PlatingPower", 2, ["owner gains Amount block at its side turn end (early)", "monster owners also gain Amount block before the first player turn (round 1)",
                      "at owner side turn start (not the first turn/round): players Decrement; enemies lose Decrement (= player count) via ModifyAmount"],
  req=["HookParam.SideTurnParticipants", "CombatState.RoundNumber", "PlayerCombatState.TurnNumber", "PowerCmd.ModifyAmount (present)"],
  notes="GetScaledAmountForMultiplayer/ShouldScaleInMultiplayer are multiplayer-only; Decrement var = RunState.Players.Count = 1 in single player.",
  batch="core")

A("PlowPower", 1, ["Ceremonial Beast: when unblocked damage leaves the owner at or below Amount HP: remove all temporary-Strength powers and Strength, stun (CeremonialBeast.SetStunned + Stun into BeastCry state; others plain Stun), remove itself"],
  req=["owner power enumeration by base type (present; GetPowerInstances<TemporaryStrengthPower>)", "CreatureCmd.Stun (both overloads, present)", "MonsterDef coupling: CeremonialBeast.SetStunned / StunnedMove / BeastCryState"],
  notes="ShouldScaleInMultiplayer is multiplayer-only.",
  vd=("cosmetic", "conditions inverted by the state machine; same logic"),
  batch="monster:CeremonialBeast", deps=["StrengthPower", "TemporaryStrengthPower"])

A("PoisonPower", 2, ["owner side turn start: Trigger(): repeat TriggerCount = min(Amount, 1 + Accelerant on alive opponents) times: lose Amount HP (Unblockable|Unpowered, no dealer), then Decrement if alive",
                     "public Trigger() reused by the Outbreak card"],
  req=["HookParam.SideTurnParticipants"],
  notes="CalculateTotalDamageNextTurn is a health-bar preview (UI). The loop does not break when the owner dies: later iterations still call CreatureCmd.Damage on the dead owner (the kernel's dead-target handling decides the outcome) and skip the Decrement.",
  vd=("cosmetic", "trigger loop extracted into public Trigger() (used by Outbreak); same rule"),
  batch="core", deps=["AccelerantPower"])

A("PossessSpeedPower", 1, ["The Forgotten: remembers Dexterity it removes from players (negative applications it applied); returns all of it when it dies"],
  req=["per-instance data: creature-id -> stolen amount map"],
  vd=("cosmetic", "condition polarity inverted; same logic"),
  batch="monster:TheForgotten", deps=["DexterityPower"])

A("PossessStrengthPower", 1, ["The Lost: remembers Strength it removes from players; returns all of it when it dies"],
  req=["per-instance data: creature-id -> stolen amount map"],
  vd=("cosmetic", "condition polarity inverted; same logic"),
  batch="monster:TheLost", deps=["StrengthPower"])

A("PrepTimePower", 2, ["owner side turn start: gain Amount Vigor"],
  req=["HookParam.SideTurnParticipants"],
  batch="core", deps=["VigorPower"])

A("PyrePower", 1, ["owner's player +Amount max energy"],
  batch="core")

A("RadiancePower", 1, ["after the owner's player energy reset: gain 1 energy (Energy var), Decrement"],
  batch="core")

A("RagePower", 2, ["each owner Attack played: gain Amount block (Unpowered)", "removed at owner side turn end"],
  req=["HookParam.SideTurnParticipants"],
  batch="core")

A("RampartPower", 2, ["Living Shield: at each player side turn start (not during an extra turn) every Turret Operator gains Amount block"],
  req=["CombatState.PlayersTakingExtraTurn (extra-turn flag list)", "MonsterDef coupling: TurretOperator type test"],
  notes="ShouldScaleInMultiplayer is multiplayer-only.",
  vd=("cosmetic", "condition polarity inverted; same logic"),
  batch="monster:LivingShield")

A("RavenousPower", 1, ["Corpse Slug: when another creature on its side dies (not prevented) while it lives: IsRavenous, Stun into a move that clears IsRavenous, then gain Amount Strength"],
  notes="Already ported (sim Powers/Defs/RavenousPower.cs).",
  batch="monster:CorpseSlug", deps=["StrengthPower"])

A("ReaperFormPower", 2, ["powered attack damage dealt by the owner or its pets applies (total damage x Amount) Doom to the target"],
  req=["CreatureState.PetOwner (Osty attacks count)"],
  notes="AfterApplied/AfterRemoved only drive a VFX node.",
  vd=("cosmetic", "v0.111.0 adds VFX-only AfterApplied/AfterRemoved; same rule"),
  batch="doom", deps=["DoomPower"])

A("ReattachPower", 2, ["Decimillipede segment: on death (unless all other segments are dead) marks reviving, forces DeadState move, untargetable, stays in combat",
                       "DoReattach (called by the monster) heals Amount if another segment lives", "owner death is fatal only when all other segments are dead"],
  req=["MonsterModel.SetMoveImmediate", "dead-but-present monster revive flow", "ShouldOwnerDeathTriggerFatal (present)", "MonsterDef coupling: DecimillipedeSegment.DeadState / DoReattach caller"],
  notes="Fade-out of all segments and interactability toggles are UI. ShouldScaleInMultiplayer is multiplayer-only.",
  vd=("cosmetic", "condition polarity inverted; same logic"),
  batch="monster:Decimillipede")

A("ReboundPower", 2, ["the owner's next card going to discard goes to the top of the draw pile instead; Decrement per use", "removed at owner side turn end"],
  req=["Hook.ModifyCardPlayResultLocation", "Hook.AfterModifyingCardPlayResultLocation", "CardLocation result struct", "HookParam.SideTurnParticipants"],
  vd=("cosmetic", "CardLocation hook refactor; same effect"),
  batch="cardops")

A("ReflectPower", 2, ["when powered attack damage on the owner is blocked: deal the blocked amount back to the dealer (Unpowered)", "Decrement at owner side turn start"],
  req=["HookParam.SideTurnParticipants", "DamageResult.BlockedDamage (present)"],
  batch="core")

A("RegenPower", 2, ["owner side turn end (early, before other end-of-turn effects): heal Amount if alive, then Decrement"],
  req=["HookParam.SideTurnParticipants"],
  notes="ShouldScaleInMultiplayer is multiplayer-only.",
  vd=("rule-change", "v0.107.1 healed at AfterSideTurnEnd; v0.111.0 heals at BeforeSideTurnEndEarly"),
  batch="core")

A("ReptileTrinketPower", 2, ["temporary Strength (TemporaryStrengthPower subclass; origin relic ReptileTrinket)"],
  req=["TemporaryStrengthPower base", "ITemporaryPower"],
  batch="temp", deps=["TemporaryStrengthPower"])

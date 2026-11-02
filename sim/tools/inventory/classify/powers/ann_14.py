from annot_lib import A, P

A("VulnerablePower", 3, ["owner takes x1.5 (DamageIncrease) from powered attacks; the multiplier is modified in order by the dealer player's PaperPhrog relic, the dealer's (or its pet owner's) Cruelty, then the target's Debilitate",
                         "duration ticks down at the end of each enemy turn"],
  req=["Relic query: dealer.Player.GetRelic<PaperPhrog>().ModifyVulnerableMultiplier (relic runtime)", "CreatureState.PetOwner (Cruelty fallback for pet dealers)",
       "IVulnerableMultiplierModifier seam (present)"],
  notes="Already ported (sim Powers/Defs/VulnerablePower.cs) without the PaperPhrog step and without the pet-owner Cruelty fallback.",
  vd=("rule-change", "v0.111.0 adds the pet-owner fallback for CrueltyPower (Osty attacks use the owner's Cruelty); ModifyDamageMultiplicative also gained a CardPlay parameter"),
  batch="core", deps=["CrueltyPower", "DebilitatePower"])

A("WasteAwayPower", 1, ["owner's player -Amount max energy"],
  batch="core")

A("WeakPower", 3, ["owner's powered attacks deal x0.75 (DamageDecrease); modified by the target player's PaperKrane relic, then by the dealer's Debilitate", "duration ticks down at the end of each enemy turn"],
  req=["Relic query: target.Player.GetRelic<PaperKrane>().ModifyWeakMultiplier (relic runtime)", "IWeakMultiplierModifier seam (present)"],
  notes="Already ported (sim Powers/Defs/WeakPower.cs) without the PaperKrane step. Debilitate is called with the dealer as its target argument.",
  vd=("cosmetic", "ModifyDamageMultiplicative gained a CardPlay parameter; body identical"),
  batch="core", deps=["DebilitatePower"])

A("WellLaidPlansPower", 1, ["the owner's player keeps its whole hand at end of turn (ShouldFlush false) for the rest of combat"],
  vd=("rule-change", "v0.107.1 (Counter) prompted at BeforeFlushLate to choose up to Amount cards to retain (CardSelectCmd.FromHand, single-turn retain); v0.111.0 (Single) retains the whole hand via ShouldFlush, no prompt"),
  batch="core")

A("WitheringPresencePower", 2, ["Aeonglass: instanced countdown on its Target player; every 6 cards that player plays add a Wither to their hand, then reset"],
  req=["CardPileCmd.AddToCombatAndPreview<Wither>", "Wither card def", "PowerModel.Target (present as TargetId)", "mutable per-instance DynamicVar CardsLeft"],
  notes="Aeonglass.MatchWitherToUpgradeCount is only called while building hover tips; the Wither actually added is the canonical one.",
  oq=["WitheringPresencePower: confirm the Wither added by AddToCombatAndPreview<Wither> is not upgraded to match Aeonglass elsewhere (only the hover-tip path calls MatchWitherToUpgradeCount)."],
  batch="monster:Aeonglass")

A("WraithFormPower", 2, ["owner side turn start: lose Amount Dexterity"],
  req=["HookParam.SideTurnParticipants"],
  batch="core", deps=["DexterityPower"])

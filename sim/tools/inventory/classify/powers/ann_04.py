from annot_lib import A, P

A("FlameBarrierPower", 1, ["when the owner takes powered attack damage from a dealer: deal Amount damage back (Unpowered, dealer owner)", "removed at the end of the opposing side's turn"],
  batch="core")

A("FlankingPower", 2, ["instanced debuff: powered attacks on the owner from anyone other than the applier deal x Amount", "removed at owner side turn end"],
  req=["HookParam.SideTurnParticipants"],
  notes="Multiplayer-oriented (other players), but in single player Osty's attacks (dealer = Osty, not the applier) are multiplied. Applier name StringVar is UI.",
  vd=("cosmetic", "ModifyDamageMultiplicative gained a CardPlay parameter; body identical"),
  batch="core")

A("FlexPotionPower", 2, ["temporary Strength (TemporaryStrengthPower subclass; origin potion FlexPotion)"],
  req=["TemporaryStrengthPower base", "ITemporaryPower"],
  batch="temp", deps=["TemporaryStrengthPower"])

A("FlutterPower", 1, ["Thieving Hopper: takes x(DamageDecrease 50 / 100) from powered attacks",
                      "each unblocked powered hit: Decrement; at 0 Stun with a no-op move whose next state is the last logged state's GetNextState (MonsterAi rng), IsHovering = false"],
  req=["CreatureCmd.Stun(creature, stunMove, nextMoveId)", "move StateLog + AiStateDef.GetNextState(cs, owner, RunRng.MonsterAi) (present)",
       "MonsterDef coupling: ThievingHopper.IsHovering"],
  rng=["RunRng.MonsterAi (GetNextState of the last logged move state when Flutter runs out)"],
  notes="ShouldScaleInMultiplayer is multiplayer-only.",
  vd=("cosmetic", "ModifyDamageMultiplicative gained a CardPlay parameter; body identical"),
  batch="monster:ThievingHopper")

A("FocusedStrikePower", 3, ["temporary Focus (TemporaryFocusPower subclass; origin card FocusedStrike)"],
  req=["TemporaryFocusPower base", "ITemporaryPower", "FocusPower (orbs)"],
  batch="orbs", deps=["TemporaryFocusPower"])

A("FocusPower", 3, ["orb passive/evoke values of the owner's orbs +Amount (floor 0), negative allowed (ModifyOrbValue)"],
  req=["OrbQueue / OrbModel", "Hook.ModifyOrbValue"],
  batch="orbs")

A("ForbiddenGrimoirePower", 3, ["at combat end: add Amount card-removal rewards to the room's extra rewards"],
  req=["Hook.AfterCombatEnd", "CombatRoom.AddExtraReward + CardRemovalReward (run layer rewards)"],
  batch="run")

A("ForegoneConclusionPower", 3, ["before the owner's hand draw: shuffle if needed, choose exactly Amount cards from the draw pile (clipped), put them into hand, remove itself"],
  req=["CardSelectCmd.FromCombatPile (choice API, draw pile sorted by rarity then id)", "CardPileCmd.ShuffleIfNecessary (present)", "CardPileCmd.Add (draw -> hand, full-hand handling)",
       "turn-setup choice deferral (SetupPlayerTurn context)"],
  prompts=[P("PileSelect", "CardSelectCmd.FromCombatPile", "Draw", "Amount", "Amount", "BeforeHandDraw",
             extra={"prompt": "SelectionScreenPrompt", "followup": "CardPileCmd.Add(selected, Hand), then PowerCmd.Remove(self)"})],
  rng=["RunRng.Shuffle (indirect: ShuffleIfNecessary reshuffles discard into draw when the draw pile is empty)"],
  batch="prompt")

A("FrailPower", 1, ["owner's powered card/monster-move block x0.75", "duration ticks down at the end of each enemy turn"],
  notes="Already ported (sim Powers/Defs/FrailPower.cs).",
  batch="core")

A("FreeAttackPower", 1, ["owner's Attacks in hand or play pile cost 0 (Late cost hook)", "Decrement before each such Attack is played"],
  req=["CardInstance current pile type (Hand/Play)"],
  batch="core")

A("FreePowerPower", 1, ["owner's Power cards in hand or play pile cost 0 (Late cost hook)", "Decrement before each such Power is played"],
  req=["CardInstance current pile type (Hand/Play)"],
  batch="core")

A("FreeSkillPower", 1, ["owner's Skills in hand or play pile cost 0 (Late cost hook)", "Decrement before each such Skill is played"],
  req=["CardInstance current pile type (Hand/Play)"],
  batch="core")

A("FriendshipPower", 1, ["owner's player +Amount max energy (ModifyMaxEnergy)"],
  batch="core")

A("FurnacePower", 3, ["owner side turn start: Forge Amount (Sovereign Blade)"],
  req=["ForgeCmd.Forge", "SovereignBlade card def", "HookParam.SideTurnParticipants"],
  vd=("cosmetic", "hover-tip only identifier added; same logic"),
  batch="forge")

A("GalvanicPower", 3, ["Globe Head: before combat start afflicts every Power card of every player with Galvanized (Amount); Power cards entering combat later get it too",
                       "whenever a Galvanized card is played, its owner takes Amount damage (Unpowered|Move, no dealer)"],
  req=["Afflictions (Galvanized: CardCmd.Afflict<T>)", "Hook.BeforeCombatStart (present)", "Hook.AfterCardEnteredCombat (present)", "PlayerCombatState.AllCards",
       "CombatState.Allies"],
  vd=("cosmetic", "nested foreach became enumerator state machines; same logic"),
  batch="afflict")

A("GenesisPower", 3, ["after the owner's player energy reset: gain Amount stars"],
  req=["Stars subsystem", "PlayerCmd.GainStars"],
  batch="stars")

A("GigantificationPower", 1, ["the owner's next powered Attack-card AttackCommand deals x3; Decrement after that command (AfterAttack)"],
  req=["per-instance data: the AttackCommand being modified (transient, null between actions)", "Hook.BeforeAttack / AfterAttack (present)"],
  notes="While no command is remembered, any powered damage from an owner card is also x3 (cardSource matches or commandToModify is null).",
  vd=("cosmetic", "ModifyDamageMultiplicative gained a CardPlay parameter; body identical"),
  batch="core")

A("GravityPower", 2, ["after each owner card play (snapshot at BeforeCardPlayed): deal Amount to all hittable enemies (Unpowered)", "removed at owner side turn end"],
  req=["HookParam.SideTurnParticipants"],
  notes="Unreferenced: no card/monster/relic/potion/event applies it in v0.111.0 (nor in v0.107.1); port last.",
  vd=("cosmetic", "out-var renamed; same logic"),
  oq=["GravityPower has no referrer in either decompile; confirm it is unreachable content before spending port effort."],
  batch="unreferenced")

A("GuardedPower", 1, ["instanced: owner takes x0.5 (DamageDecrease) from powered attacks", "removed when the applier dies"],
  notes="Given by TankPower to other players only, so inert in single player.",
  vd=("cosmetic", "the 0.5 multiplier moved from a literal into a DamageDecrease DynamicVar"),
  batch="core")

A("HailstormPower", 3, ["owner side turn end: if the owner has at least 1 Frost orb, deal Amount to all hittable enemies (Unpowered)"],
  req=["OrbQueue (FrostOrb count)", "HookParam.SideTurnParticipants"],
  vd=("cosmetic", "local renamed; same logic"),
  batch="orbs")

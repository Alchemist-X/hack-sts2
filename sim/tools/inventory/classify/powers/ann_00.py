from annot_lib import A, P

A("AccelerantPower", 1, ["marker: each Accelerant on an alive opponent adds one Poison trigger (read by PoisonPower.TriggerCount)"],
  notes="No hooks; ported together with PoisonPower, which reads its amount on the poisoned creature's opponents.",
  batch="core")

A("AccuracyPower", 1, ["damage +Amount (additive) for owner's powered attacks from cards tagged Shiv"],
  req=["CardInstance.HasTag(CardTag.Shiv)"],
  vd=("cosmetic", "ModifyDamageAdditive gained a CardPlay parameter; body identical"),
  batch="core")

A("AdaptablePower", 1, ["revive gate: on owner death (not prevented) marks reviving and calls TestSubject.TriggerDeadState",
                        "untargetable while reviving (ShouldAllowHitting false)",
                        "keeps combat from ending (ShouldStopCombatFromEnding always true)",
                        "owner not removed from combat after death; power survives owner death"],
  req=["MonsterDef coupling: TestSubject.TriggerDeadState / DoRevive",
       "dead-but-present monster (revive) flow (present: ShouldCreatureBeRemovedFromCombatAfterDeath + ShouldStopCombatFromEnding gates)",
       "per-instance data (InitInternalData -> cloned field isReviving)"],
  notes="TestSubject boss mechanic; ShouldStopCombatFromEnding returns true unconditionally while the power exists, so combat end depends on TestSubject removing it.",
  batch="monster:TestSubject")

A("AfterimagePower", 1, ["on each owner card play: snapshot Amount at BeforeCardPlayed, gain that much block (Unpowered, fast) at AfterCardPlayed"],
  req=["per-instance data: card-instance-id -> amount map (InitInternalData)"],
  notes="Snapshot keyed by card makes a card that started before the power was applied not trigger, and replays add one entry per play.",
  vd=("cosmetic", "out-var renamed by the state machine; same logic"),
  batch="core")

A("AggressionPower", 2, ["at owner side turn start: move up to Amount random Attack cards from discard to hand (UnstableShuffle on CombatCardSelection), upgrading each if upgradable"],
  req=["HookParam.SideTurnParticipants", "CardPileCmd.Add (discard -> hand, full-hand handling)", "CardCmd.Upgrade (in-combat upgrade)",
       "RngCollectionExtensions.UnstableShuffle"],
  rng=["RunRng.CombatCardSelection (UnstableShuffle of the discard-pile Attack list, then Take(Amount))"],
  notes="Order per card: Add to hand, then Upgrade if IsUpgradable. Uses BeforeSideTurnStart (before the turn's hand draw).",
  vd=("cosmetic", "foreach became an enumerator state machine; same logic"),
  batch="cardops")

A("AmbergrisPower", 1, ["extra turn: ShouldTakeExtraTurn while Amount > 0 for the owner's player; Decrement after each extra turn"],
  req=["extra-turn loop (present: CombatLoop ShouldTakeExtraTurn/AfterTakingExtraTurn)"],
  notes="Invisible buff (IsVisibleInternal false) applied by the Ambergris potion; keep the override so Artifact-style visibility checks match.",
  vd=("new", "not in v0.107.1"),
  batch="core")

A("AnticipatePower", 2, ["temporary Dexterity (TemporaryDexterityPower subclass; origin card Anticipate)"],
  req=["TemporaryDexterityPower base", "ITemporaryPower (OriginModel = ModelDb.Card<Anticipate>)"],
  notes="All logic inherited from TemporaryDexterityPower (AfterPowerAmountChanged, AfterSideTurnEnd).",
  batch="temp", deps=["TemporaryDexterityPower"])

A("ArsenalPower", 1, ["whenever the owner's player generates a card for combat: gain Amount Strength"],
  req=["Hook.AfterCardGeneratedForCombat (present; must be dispatched from every card-creation site)"],
  batch="core", deps=["StrengthPower"])

A("ArtifactPower", 2, ["negates an incoming debuff application on the owner (TryModifyPowerAmountReceived -> 0) when the canonical power is Debuff for that amount and visible",
                       "Decrement after each negation (AfterModifyingPowerAmountReceived)"],
  req=["PowerModel.IsVisible / IsVisibleInternal on every power (Artifact skips invisible debuffs)",
       "PowerModel.GetScaledAmountForMultiplayer + ShouldScaleInMultiplayer (single player: identity)"],
  notes="GetTypeForAmount means a negative application of a buff that allows negatives (e.g. -Strength) counts as a debuff and is blocked.",
  batch="core")

A("AsleepPower", 2, ["Lagavulin Matriarch sleep: on unblocked damage to owner removes Plating, sets IsAwake, Stun into WakeUpMove (next SLASH_MOVE), removes itself",
                     "owner side turn end (very early): if Amount <= 1 removes Plating",
                     "owner side turn end: Decrement; at 0 runs LagavulinMatriarch.WakeUpMove directly"],
  req=["HookParam.SideTurnParticipants", "CreatureCmd.Stun(creature, stunMove, nextMoveId)",
       "MonsterDef coupling: LagavulinMatriarch.IsAwake / WakeUpMove"],
  notes="Order in AfterDamageReceived: Remove(Plating) -> IsAwake=true -> Stun -> Remove(self).",
  batch="monster:LagavulinMatriarch", deps=["PlatingPower"])

A("AutomationPower", 1, ["instanced counter: every card the owner's player draws decrements cardsLeft (10); at 0 gain Amount energy and reset to 10"],
  req=["PowerInstanceType.Instanced stacking (present)", "per-instance data cardsLeft (InitInternalData)"],
  batch="core")

A("BackAttackLeftPower", 1, ["marker for Surrounded side-facing (read by SurroundedPower)"],
  batch="monster:Crusher", deps=[])

A("BackAttackRightPower", 1, ["marker for Surrounded side-facing (read by SurroundedPower)"],
  batch="monster:Rocket", deps=[])

A("BarricadePower", 1, ["owner's block is not cleared at turn start (ShouldClearBlock false)"],
  notes="AfterApplied only fills a loc StringVar with the applier monster's title (UI).",
  batch="core")

A("BattlewornDummyTimeLimitPower", 2, ["owner side turn end: Decrement while Amount > 1; at 1 sets the event encounter's RanOutOfTime flag and the owner escapes"],
  req=["HookParam.SideTurnParticipants", "CreatureCmd.Escape", "EncounterDef state: BattlewornDummyEventEncounter.RanOutOfTime (event outcome)"],
  notes="Event combat (Battleworn Dummy). Escape ends the combat without a kill; the event reads RanOutOfTime afterwards.",
  batch="monster:BattleFriend")

A("BeaconOfHopePower", 1, ["when the owner gains >= 1 block on its own side's turn: give 50% of it as block to each other alive player (re-entrancy guarded)"],
  notes="Single player: GetTeammatesOf(owner) filtered to other players is empty, so the power is inert.",
  vd=("cosmetic", "early-return conditions inverted by the state machine; same logic"),
  batch="core")

A("BiasedCognitionPower", 2, ["owner side turn start: apply -Amount Focus to the owner"],
  req=["HookParam.SideTurnParticipants"],
  batch="orbs", deps=["FocusPower"])

A("BlackHolePower", 3, ["damage all hittable enemies by Amount (Unpowered, dealer owner) when the owner's card play spent stars (last in series)",
                        "same damage whenever the owner's player gains > 0 stars"],
  req=["Stars subsystem", "Hook.AfterStarsGained", "CardPlay.Resources.StarsSpent", "CardPlay.IsLastInSeries"],
  batch="stars")

A("BlockNextTurnPower", 1, ["after the owner's block is cleared: gain Amount block (Unpowered) and remove itself"],
  notes="Relies on AfterBlockCleared firing at the owner's turn-start clear.",
  batch="core")

A("BlurPower", 2, ["owner's block is not cleared (ShouldClearBlock false); Decrement at owner side turn start"],
  req=["HookParam.SideTurnParticipants"],
  notes="AfterPreventingBlockClear only flashes (UI).",
  batch="core")

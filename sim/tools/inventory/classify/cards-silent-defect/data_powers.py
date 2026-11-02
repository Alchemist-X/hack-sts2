P = {}


def p(name, tier, needs, *, ported=False, batch=None, delta="same", mp=False):
    P[name] = {"tier": tier, "needs": list(needs), "ported": ported, "batch": batch, "delta_v107": delta,
               "multiplayer_only_use": mp, "file": "Models.Powers/%s.cs" % name}


PART = "side-turn participants"

p("StrengthPower", 0, ["Hook.ModifyDamageAdditive"], ported=True)
p("DexterityPower", 0, ["Hook.ModifyBlockAdditive"], ported=True)
p("ThornsPower", 0, ["BeforeDamageReceived"], ported=True)
p("VulnerablePower", 0, ["ModifyDamageMultiplicative", PART], ported=True)
p("WeakPower", 0, ["ModifyDamageMultiplicative", PART], ported=True)

p("PoisonPower", 1, ["AfterSideTurnStart (" + PART + ")", "public Trigger(): TriggerCount = min(Amount, 1 + sum AccelerantPower of alive opponents)",
                     "CreatureCmd.Damage(Unblockable|Unpowered, ThrowingPlayerChoiceContext)", "PowerCmd.Decrement while alive"],
  batch=2, delta="cosmetic (tick body moved into a public Trigger(), used by Outbreak)")
p("AccelerantPower", 1, ["no hooks; read by PoisonPower.TriggerCount"], batch=2)
p("NoxiousFumesPower", 1, ["AfterSideTurnStart (" + PART + ")", "PowerCmd.Apply list overload"], batch=2)
p("EnvenomPower", 1, ["AfterDamageGiven (powered attack, UnblockedDamage > 0)"], batch=2)
p("CorrosiveWavePower", 1, ["AfterCardDrawn", "AfterSideTurnEnd remove (" + PART + ")"], batch=2, delta="cosmetic")
p("AnticipatePower", 1, ["TemporaryDexterityPower base"], batch=2,
  delta="cosmetic for this slice (TemporaryDexterityPower lost IgnoreNextInstance, called only by Misery in v0.107.1; FromPotion is a hover tip)")
p("PiercingWailPower", 1, ["TemporaryStrengthPower base (negative)"], batch=2,
  delta="cosmetic for this slice (TemporaryStrengthPower lost IgnoreNextInstance, called only by Misery in v0.107.1)")
p("FadePower", 1, ["TemporaryDexterityPower base"], batch=99, delta="new", mp=True)
p("AfterimagePower", 1, ["BeforeCardPlayed/AfterCardPlayed with per-card amount map", "CreatureCmd.GainBlock(fast)"], batch=2)
p("BlockNextTurnPower", 1, ["AfterBlockCleared"], batch=2)
p("BlurPower", 1, ["ShouldClearBlock", "AfterPreventingBlockClear", "AfterSideTurnStart decrement (" + PART + ")"], batch=2)
p("BufferPower", 1, ["ModifyHpLostAfterOstyLate", "AfterModifyingHpLostAfterOsty decrement"], batch=2)
p("BurstPower", 1, ["ModifyCardPlayCount", "AfterModifyingCardPlayCount", "AfterSideTurnEnd (" + PART + ")"], batch=2)
p("SignalBoostPower", 1, ["ModifyCardPlayCount (Power cards)", "AfterModifyingCardPlayCount"], batch=2)
p("DrawCardsNextTurnPower", 1, ["ModifyHandDraw (AmountOnTurnStart)", "AfterSideTurnStart remove"], batch=2)
p("EnergyNextTurnPower", 1, ["AfterEnergyReset"], batch=2)
p("FreeSkillPower", 1, ["TryModifyEnergyCostInCombatLate (Skills in Hand/Play)", "BeforeCardPlayed decrement"], batch=2)
p("FreePowerPower", 1, ["TryModifyEnergyCostInCombatLate (Powers in Hand/Play)", "BeforeCardPlayed decrement"], batch=2)
p("MachineLearningPower", 1, ["ModifyHandDraw"], batch=2)
p("NoDrawPower", 1, ["ShouldDraw (non-hand draws)", "AfterSideTurnEnd remove"], batch=2)
p("ShadowmeldPower", 1, ["ModifyBlockMultiplicative x 2^Amount", "AfterSideTurnEnd remove"], batch=2, delta="cosmetic")
p("ShadowStepPower", 1, ["AfterSideTurnStart: apply DoubleDamagePower, remove"], batch=3)
p("DoubleDamagePower", 1, ["ModifyDamageMultiplicative x2 (owner or pets, powered card attacks)", "AfterSideTurnEnd decrement"], batch=3)
p("SerpentFormPower", 1, ["BeforeCardPlayed/AfterCardPlayed", "random hittable enemy via RunRng.CombatTargets", "CreatureCmd.Damage"],
  batch=2, delta="cosmetic (VFX property added)")
p("SpeedsterPower", 1, ["AfterCardDrawn (not hand draw, own side's turn)", "CreatureCmd.Damage to all hittable"], batch=2)
p("StranglePower", 1, ["InstancedPerApplier", "BeforeCardPlayed/AfterCardPlayed", "AfterSideTurnEnd remove"], batch=2)
p("SubroutinePower", 1, ["BeforeCardPlayed/AfterCardPlayed (Power cards)", "PlayerCmd.GainEnergy"], batch=2)
p("TrackingPower", 1, ["ModifyDamageMultiplicative 1 + Amount/100 vs Weak targets (owner or pets, card attacks)"], batch=2,
  delta="rule-change (multiplier formula; card now applies 50)")
p("WellLaidPlansPower", 1, ["ShouldFlush"], batch=2, delta="rule-change (was a BeforeFlushLate FromHand retain prompt)")
p("WraithFormPower", 1, ["AfterSideTurnStart: -Amount DexterityPower"], batch=2)
p("IntangiblePower", 1, ["ModifyHpLostAfterOsty", "ModifyDamageCap", "AfterSideTurnEnd decrement (enemy side)"], batch=2,
  delta="cosmetic (decimal.MaxValue printed as a literal; ModifyDamageCap gained CardPlay)")
p("ArtifactPower", 1, ["TryModifyPowerAmountReceived", "AfterModifyingPowerAmountReceived decrement"], batch=2)
p("SmokestackPower", 1, ["AfterCardGeneratedForCombat (Status, creator == owner)", "CreatureCmd.Damage to all hittable"], batch=2)
p("TheHuntPower", 1, ["no hooks, no readers (display marker)"], batch=8)
p("AccuracyPower", 1, ["ModifyDamageAdditive (Shiv-tagged card source)"], batch=3)
p("FanOfKnivesPower", 1, ["no hooks; read by Shiv.TargetType and Shiv.OnPlay"], batch=3)
p("InfiniteBladesPower", 2, ["BeforeHandDraw", "Shiv.CreateInHand (AddGeneratedCardsToCombat)"], batch=3)
p("MasterPlannerPower", 2, ["AfterCardPlayed", "CardCmd.ApplyKeyword(Sly)"], batch=3)
p("FeralPower", 2, ["ModifyCardPlayResultLocation + AfterModifyingCardPlayResultLocation (missing in sim)", "AfterSideTurnStart reset"],
  batch=3, delta="rule-change (API rename and new !card.IsDupe condition)")
p("PhantomBladesPower", 3, ["AfterCardEnteredCombat/AfterApplied: CardCmd.ApplyKeyword(Retain) on Shivs",
                            "ModifyDamageAdditive for the first Shiv play this turn (CombatHistory)"], batch=4,
  delta="cosmetic (history filter API)")
p("EchoFormPower", 3, ["ModifyCardPlayCount while CardPlaysStarted(IsFirstInSeries, this turn) < Amount", "CombatHistory"], batch=4,
  delta="cosmetic (VFX-only AfterApplied/AfterRemoved/AfterCardPlayed/BeforeSideTurnStart overrides added)")
p("IterationPower", 3, ["AfterCardDrawn Status: draw if <= 1 Status drawn this turn (CombatHistory.CardDrawnEntry)"], batch=4)
p("NightmarePower", 3, ["InstanceType Instanced", "SetSelectedCard: CardModel.CreateClone + CardCmd.ClearAffliction",
                        "BeforeHandDraw: AddGeneratedCardToCombat(Hand) x Amount, remove"], batch=5)
p("ToolsOfTheTradePower", 3, ["ModifyHandDraw", "AfterPlayerTurnStart: CardSelectCmd.FromHandForDiscard prompt (turn-setup deferral)"], batch=5)
p("CreativeAiPower", 3, ["BeforeHandDraw: CardFactory.GetDistinctForCombat over own pool Powers (UnlockState)", "RunRng.CombatCardGeneration"],
  batch=8, delta="cosmetic")
p("FocusPower", 3, ["Hook.ModifyOrbValue (value + Amount, floor 0)", "AllowNegative", "orb subsystem"], batch=6)
p("FocusedStrikePower", 3, ["TemporaryFocusPower base"], batch=6,
  delta="cosmetic for this slice (TemporaryFocusPower lost IgnoreNextInstance)")
p("HotfixPower", 3, ["TemporaryFocusPower base"], batch=6, delta="cosmetic for this slice (TemporaryFocusPower lost IgnoreNextInstance)")
p("SynchronizePower", 3, ["TemporaryFocusPower base"], batch=6, delta="cosmetic for this slice (TemporaryFocusPower lost IgnoreNextInstance)")
p("HyperbeamFocusDownPower", 3, ["TemporaryFocusPower base, IsPositive=false"], batch=6, delta="new")
p("BiasedCognitionPower", 3, ["AfterSideTurnStart: -Amount FocusPower"], batch=6)
p("ConsumingShadowPower", 3, ["AfterSideTurnEnd: OrbCmd.EvokeLast when the queue is not empty"], batch=7)
p("CoolantPower", 3, ["AfterSideTurnStart: block = Amount x distinct orb ids"], batch=7, delta="cosmetic")
p("HailstormPower", 3, ["BeforeSideTurnEnd: >= 1 FrostOrb -> damage Amount to all hittable"], batch=7)
p("LightningRodPower", 3, ["AfterEnergyReset: OrbCmd.Channel<LightningOrb>, decrement"], batch=7)
p("LoopPower", 3, ["AfterPlayerTurnStart: OrbCmd.Passive(front orb) x Amount"], batch=7)
p("SpinnerPower", 3, ["AfterEnergyReset: OrbCmd.Channel<GlassOrb> x Amount"], batch=7)
p("StormPower", 3, ["BeforeCardPlayed/AfterCardPlayed (Power cards): OrbCmd.Channel<LightningOrb> x Amount"], batch=7)
p("ThunderPower", 3, ["Hook.AfterOrbEvoked (missing in sim): damage living targets of a Lightning evoke"], batch=7)
p("TrashToTreasurePower", 3, ["AfterCardGeneratedForCombat (Status): OrbModel.GetRandomOrb (RunRng.CombatOrbGeneration) + Channel"], batch=7)
p("HibernatePower", 3, ["AfterPlayerTurnStart decrement", "read by FrostOrb.Evoke (teammate block)"], batch=99, delta="new", mp=True)
p("ConcoctPower", 1, ["AfterDamageGiven", "AfterSideTurnEnd"], batch=99, delta="new", mp=True)
p("FlankingPower", 1, ["ModifyDamageMultiplicative", "AfterSideTurnEnd"], batch=99, delta="cosmetic", mp=True)
p("SneakyPower", 1, ["AfterCardPlayed (ally Attacks): GainBlock"], batch=99, mp=True)
p("OneForAllPower", 1, ["ModifyDamageAdditive"], batch=99, delta="new", mp=True)
p("ImitationLearningPower", 2, ["BeforeCardPlayed/AfterCardPlayed", "CardCmd.AutoPlay"], batch=99, delta="new", mp=True)

CARD_DEPS = {
    "Shiv": {"tier": 2, "pool": "TOKEN", "needs": ["Shiv.CreateInHand -> CardPileCmd.AddGeneratedCardsToCombat",
                                                     "TargetType getter reads FanOfKnivesPower", "CardTag.Shiv", "Exhaust"]},
    "Dazed": {"tier": 1, "pool": "STATUS", "needs": ["Ethereal", "Unplayable"]},
    "Wound": {"tier": 1, "pool": "STATUS", "needs": ["Unplayable"]},
    "Burn": {"tier": 1, "pool": "STATUS", "needs": ["OnTurnEndInHand: 2 damage to owner (CardDef.OnTurnEndInHand exists)"]},
    "Void": {"tier": 2, "pool": "STATUS", "needs": ["Unplayable", "Ethereal", "card listener AfterCardDrawn: lose 1 energy"]},
    "Slimed": {"tier": 0, "pool": "STATUS", "needs": ["ported"]},
    "Fuel": {"tier": 1, "pool": "TOKEN", "needs": ["0-cost Exhaust: gain 1 energy, draw 1 (+1)"]},
}

ORBS = {
    "LightningOrb": "Passive 3 / Evoke 8 (ModifyOrbValue); passive at BeforeTurnEndOrbTrigger via TriggerPassive; damage a random hittable opponent "
                    "(RunRng.CombatTargets) or the given target, Unpowered",
    "FrostOrb": "Passive 2 / Evoke 5 block (ModifyOrbValue); Evoke also shields teammates under HibernatePower",
    "DarkOrb": "Passive adds focused 6 to its own evoke value (starts 6, not focused); Evoke hits the lowest-HP hittable enemy (MinBy, first on ties)",
    "PlasmaOrb": "Passive 1 energy at AfterTurnStartOrbTrigger; Evoke 2 energy; values not focused",
    "GlassOrb": "Passive focused value (starts 4) to all hittable, then base -1 (floor 0); Evoke 2x passive value to all hittable",
}


POWER_RNG = {
    "LightningRodPower": [("CombatTargets", "LightningRodPower channels Lightning; a full queue evokes the front orb (Lightning evoke = random target)")],
    "StormPower": [("CombatTargets", "StormPower channels Lightning; a full queue evokes the front orb")],
    "SpinnerPower": [("CombatTargets", "SpinnerPower channels Glass; a full queue evokes the front orb")],
    "TrashToTreasurePower": [("CombatTargets", "TrashToTreasurePower channels; a full queue evokes the front orb")],
    "ConsumingShadowPower": [("CombatTargets", "ConsumingShadowPower EvokeLast of a LightningOrb picks a random target")],
    "LoopPower": [("CombatTargets", "LoopPower triggers the front orb passive with no target (LightningOrb = random target)")],
    "IterationPower": [("Shuffle", "IterationPower draws (reshuffle when the draw pile is empty)")],
    "ToolsOfTheTradePower": [("CombatTargets", "ToolsOfTheTradePower discard can auto-play a Sly AnyEnemy card at a random enemy")],
    "InfiniteBladesPower": [],
}

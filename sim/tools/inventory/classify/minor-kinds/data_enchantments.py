from lib import M

K = "enchantments"
CORE = "kernel:enchantments"

M(K, "Adroit", "game", [
    "OnPlay (after the card's OnPlay): CreatureCmd.GainBlock(owner, Block, cardPlay) with BlockVar(0, Move)",
    "RecalculateValues: Block.BaseValue = Amount (the enchant amount)",
], reqs=[CORE, "CreatureCmd.GainBlock(BlockVar)"], hooks=["OnPlay", "RecalculateValues"], tier=2, batch=2,
  notes="applied by the relic Kifuda (deck prompt, run layer)")
M(K, "Clone", "game", [
    "marker only: no members",
    "CloneRestSiteOption adds a copy of every deck card enchanted with Clone (RunState.CloneCard, Entities.RestSite/CloneRestSiteOption.cs:64-87)",
], reqs=[CORE], tier=1, batch=1, req_notes=["the clone itself needs run:rest_site and run:deck (CloneRestSiteOption, not a model)"],
  notes="the effect lives in CloneRestSiteOption (added by the relic PaelsGrowth); port with the rest-site option")
M(K, "Corrupted", "game", [
    "CanEnchantCardType: Attack only",
    "EnchantDamageMultiplicative: x1.5 on powered attacks",
    "OnPlay: CreatureCmd.Damage(owner, 2, Unblockable|Unpowered|Move, card, cardPlay)",
], reqs=[CORE], hooks=["CanEnchantCardType", "EnchantDamageMultiplicative", "OnPlay"], tier=1, batch=1,
  notes="applied by the event Symbiote (APPROACH)")
M(K, "DeprecatedEnchantment", "deprecated", ["empty placeholder (save compatibility)"], tier=1, batch=None)
M(K, "Glam", "game", [
    "EnchantPlayCount: +Times (1) until used this combat",
    "AfterCardPlayed (this card): UsedThisCombat = true, Status = Disabled",
], reqs=[CORE, "kernel:card_listeners", "kernel:per_instance_state"], hooks=["EnchantPlayCount", "AfterCardPlayed"],
  tier=2, batch=1, notes="per-combat flag _usedThisCombat (mutable field); applied by the relics Glitter and SilkenTress")
M(K, "Goopy", "game", [
    "CanEnchant: base checks and CardTag.Defend",
    "OnEnchant: AddKeyword(Exhaust)",
    "AfterCardPlayed (this card): Amount += 1, and DeckVersion.Enchantment.Amount += 1 (persists to the deck card)",
    "EnchantBlockAdditive: Amount - 1",
], reqs=[CORE, "kernel:card_listeners", "kernel:run_effects"], hooks=["CanEnchant", "OnEnchant", "AfterCardPlayed", "EnchantBlockAdditive"],
  tier=3, batch=2, notes="the DeckVersion write is a run-state effect from combat; applied by the relic PaelsClaw and the event Pael")
M(K, "Imbued", "game", [
    "CanEnchantCardType: Skill only; ShouldStartAtBottomOfDrawPile = true",
    "AfterAutoPrePlayPhaseEntered: on the owner's turn 1 (PlayerCombatState.TurnNumber <= 1) CardCmd.AutoPlay(card, null)",
], reqs=[CORE, "kernel:card_listeners", "CardCmd.AutoPlay", "AfterAutoPrePlayPhaseEntered", "kernel:auto_phases"],
  hooks=["CanEnchantCardType", "ShouldStartAtBottomOfDrawPile", "AfterAutoPrePlayPhaseEntered"], tier=2, batch=2,
  rng=["RunRngType.CombatTargets: CardCmd.AutoPlay picks a random enemy for an AnyEnemy card with a null target"],
  notes="applied by the relic ElectricShrymp; ThievingHopper skips Imbued cards when stealing")
M(K, "Inky", "game", [
    "OnPlay: PowerCmd.Apply<WeakPower>(Weak 1) to the play target, or to every hittable enemy when the card targets AllEnemies",
], reqs=[CORE], hooks=["OnPlay"], deps={"powers": ["WeakPower"]}, tier=1, batch=1,
  notes="applied in combat by the card BladeOfInk (CardCmd.Enchant)",
  vd=("rule-change", "v0.107.1 also added +1 damage to powered attacks (DamageVar 1, EnchantDamageAdditive); v0.111.0 only applies Weak 1"))
M(K, "Instinct", "game", [
    "CanEnchantCardType: Attack only", "EnchantDamageMultiplicative: x2 on powered attacks",
], reqs=[CORE], hooks=["CanEnchantCardType", "EnchantDamageMultiplicative"], tier=1, batch=1,
  notes="applied by the relic TriBoomerang and the event Tanx")
M(K, "MockFreeEnchantment", "test_only", ["OnEnchant: EnergyCost.UpgradeBy(-current cost) (cost 0)"], hooks=["OnEnchant"],
  tier=4, batch=None, vd=("refactor_only", "IsMock => true added (test mock)"))
M(K, "Momentum", "game", [
    "CanEnchantCardType: Attack only",
    "OnPlay: ExtraDamage += Amount (mutable field)",
    "EnchantDamageAdditive: +ExtraDamage on powered attacks",
], reqs=[CORE, "kernel:per_instance_state"], hooks=["CanEnchantCardType", "OnPlay", "EnchantDamageAdditive"], tier=2, batch=1,
  notes="the growth applies from the next play on (OnPlay runs after the card); applied by the relic PunchDagger")
M(K, "Nimble", "game", [
    "CanEnchant: base checks and card.GainsBlock", "EnchantBlockAdditive: +Amount",
], reqs=[CORE], hooks=["CanEnchant", "EnchantBlockAdditive"], tier=1, batch=1,
  notes="applied by the relic FresnelLens and the event SelfHelpBook")
M(K, "PerfectFit", "game", [
    "ModifyShuffleOrder: on a non-initial shuffle of the owner's draw pile, move this card to index 0 (top)",
], reqs=[CORE, "kernel:card_listeners"], hooks=["ModifyShuffleOrder"], tier=2, batch=2,
  notes="ModifyShuffleOrder exists in the sim for powers; needs the enchantment as a listener; applied by the event FieldOfManSizedHoles")
M(K, "RoyallyApproved", "game", [
    "CanEnchantCardType: Attack or Skill", "OnEnchant: AddKeyword(Innate), AddKeyword(Retain)",
], reqs=[CORE], hooks=["CanEnchantCardType", "OnEnchant"], tier=1, batch=1, notes="applied by the relic RoyalStamp")
M(K, "Sharp", "game", [
    "CanEnchantCardType: Attack only", "EnchantDamageAdditive: +Amount on powered attacks",
], reqs=[CORE], hooks=["CanEnchantCardType", "EnchantDamageAdditive"], tier=1, batch=1,
  notes="applied by the relic GnarledHammer and the event SelfHelpBook")
M(K, "Slither", "game", [
    "CanEnchant: base checks, not Unplayable, not X cost",
    "AfterCardDrawn (this card, now in hand): EnergyCost.SetThisCombat(RunRng.CombatEnergyCosts.NextInt(4)) (cost 0-3 for the rest of combat)",
], reqs=[CORE, "kernel:card_listeners", "AfterCardDrawn", "test:test_mode"], hooks=["CanEnchant", "AfterCardDrawn"], tier=2, batch=2,
  rng=["RunRngType.CombatEnergyCosts: NextInt(4) each time the card is drawn"],
  notes="TestEnergyCostOverride is a TestMode seam (dropped); applied by the event WoodCarvings (SNAKE)")
M(K, "SlumberingEssence", "unreferenced", [
    "BeforeFlush (owner): while the card is in hand, EnergyCost.AddUntilPlayed(-1)",
], reqs=[CORE, "kernel:card_listeners", "kernel:card_model_ops"], hooks=["BeforeFlush"], tier=2, batch=None,
  notes="registered but applied by no model in v0.111.0")
M(K, "SoulsPower", "game", [
    "CanEnchant: base checks and the card has Exhaust (local keyword sources)", "OnEnchant: CardCmd.RemoveKeyword(Exhaust)",
], reqs=[CORE, "kernel:card_model_ops", "CardCmd.RemoveKeyword"], hooks=["CanEnchant", "OnEnchant"], tier=2, batch=1,
  notes="applied by the event GraveOfTheForgotten")
M(K, "Sown", "game", [
    "OnPlay: if Status is Normal, Status = Disabled and PlayerCmd.GainEnergy(Amount)",
], reqs=[CORE], hooks=["OnPlay"], tier=1, batch=1,
  notes="one use per combat copy (Status lives on the combat card's enchantment); applied by the event SapphireSeed")
M(K, "Spiral", "game", [
    "CanEnchant: base checks, Basic rarity, Strike or Defend tag", "EnchantPlayCount: +Times (1)",
], reqs=[CORE], hooks=["CanEnchant", "EnchantPlayCount"], tier=1, batch=1, notes="applied by the event SpiralingWhirlpool")
M(K, "Steady", "game", ["OnEnchant: AddKeyword(Retain)"], reqs=[CORE], hooks=["OnEnchant"], tier=1, batch=1,
  notes="applied by the event WaterloggedScriptorium")
M(K, "Swift", "game", [
    "OnPlay: if Status is Normal, Status = Disabled and CardPileCmd.Draw(Amount)",
], reqs=[CORE], hooks=["OnPlay"], tier=1, batch=1,
  rng=["RunRngType.Shuffle (conditional: the draw reshuffles the discard pile when the draw pile is empty)"],
  notes="applied by the relics WingCharm, BeautifulBracelet and the events SelfHelpBook, Nonupeipe")
M(K, "TezcatarasEmber", "game", [
    "OnEnchant: EnergyCost.UpgradeBy(-current cost) (cost 0), AddKeyword(Eternal)",
    "EnchantDamageAdditive: +3 (DamageVar) on powered attacks",
], reqs=[CORE, "kernel:card_model_ops"], hooks=["OnEnchant", "EnchantDamageAdditive"], tier=2, batch=1,
  notes="applied by the relic NutritiousSoup")
M(K, "Vigorous", "game", [
    "CanEnchantCardType: Attack only",
    "EnchantDamageAdditive: +Amount on powered attacks while Status is Normal",
    "AfterCardPlayed (this card): Status = Disabled",
], reqs=[CORE, "kernel:card_listeners"], hooks=["CanEnchantCardType", "EnchantDamageAdditive", "AfterCardPlayed"], tier=2, batch=1,
  notes="first play only per combat copy; applied by the event StoneOfAllTime")

from lib import M

K = "afflictions"
CORE = "kernel:afflictions"

M(K, "Bound", "game", [
    "marker: extra card text only",
    "effect in ChainsOfBindingPower: afflicts drawn cards up to Amount per turn (CardAfflictedEntry count this turn) and gates their play (BeforeCardPlayed / ShouldPlay for Bound cards)",
], reqs=[CORE], co_port={"powers": ["ChainsOfBindingPower"]}, tier=1, batch=1,
  notes="applied by ChainsOfBindingPower (Queen boss); port together with that power")
M(K, "Entangled", "game", [
    "marker", "effect in TangledPower: afflicts the owner's Attacks; Entangled cards cannot be played while the power holds",
], reqs=[CORE], co_port={"powers": ["TangledPower"]}, tier=1, batch=1, notes="applied by TangledPower (VineShambler)")
M(K, "Galvanized", "game", [
    "marker; IsStackable",
    "effect in GalvanicPower: afflicts the owner's Power cards with Amount; playing a Galvanized card deals Amount Unpowered damage to its owner",
], reqs=[CORE], co_port={"powers": ["GalvanicPower"]}, tier=1, batch=1, notes="applied by GalvanicPower (GlobeHead)")
M(K, "Hexed", "game", [
    "AfterCardEnteredCombat (this card): CardCmd.ClearAffliction when the owner no longer has HexPower",
    "effect in HexPower (TryModifyKeywordsInCombat adds keywords to Hexed cards)",
], reqs=[CORE, "kernel:card_listeners", "CardCmd.ClearAffliction", "AfterCardEnteredCombat"], hooks=["AfterCardEnteredCombat"],
  co_port={"powers": ["HexPower"]}, tier=2, batch=1, notes="applied by HexPower (KnightsElite)")
M(K, "MockNoUnplayableAffliction", "test_only", ["CanAfflictUnplayableCards = false"], tier=4, batch=None, vd=("refactor_only", "IsMock => true added (test mock)"))
M(K, "MockSelfDamageAffliction", "test_only", ["OnPlay: CreatureCmd.Damage(owner, Amount, Unpowered|Move); IsStackable"],
  hooks=["OnPlay"], tier=4, batch=None, vd=("refactor_only", "IsMock => true added (test mock)"))
M(K, "MockUselessAffliction", "test_only", ["no effect"], tier=4, batch=None, vd=("refactor_only", "IsMock => true added (test mock)"))
M(K, "Ringing", "game", [
    "marker", "effect in RingingPower: afflicts the owner's cards entering combat and restricts plays of Ringing cards",
], reqs=[CORE], co_port={"powers": ["RingingPower"]}, tier=1, batch=1, notes="applied by RingingPower (CeremonialBeast boss)")
M(K, "Smog", "game", [
    "marker", "effect in SmoggyPower: afflicts Skills once a Skill was played this turn (combat history) and restricts their play",
], reqs=[CORE], co_port={"powers": ["SmoggyPower"]}, tier=1, batch=1, notes="applied by SmoggyPower (LivingFog)")
M(K, "Tainted", "game", [
    "marker; IsStackable; CanAfflictCardType: Skill only",
    "effect in VitalSparkPower: playing a Tainted card applies TaintedPower(Amount) to its owner",
], reqs=[CORE], hooks=["CanAfflictCardType"], co_port={"powers": ["VitalSparkPower"]}, tier=1, batch=1,
  notes="applied by VitalSparkPower (InfestedPrisms elite)")

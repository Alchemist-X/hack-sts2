"""Manual triage of the v0.107.1 -> v0.111.0 deltas where the classifiers and the diff gate disagree.

Read by build.py. Key: "<kind>:<Class>". Value: (triage, basis, note, evidence) where triage is changed |
refactor_only | unknown, basis is manual | il, and evidence maps each tree to "<path>:<line>" citations read
by hand (v0.111.0 paths are relative to <decompile root>/MegaCrit.Sts2.Core.<namespace>/, v0.107.1 paths
to artifacts/knowledge/decompiled-v0.107.1/MegaCrit/sts2/Core/). "IL:<type>.<method>" names the method whose
ldtoken RVA data was read from sts2.dll (docs/sim-port/w0-critique.md section 7; CONTRACTS D9).

MANUAL covers the 21 files the diff gate flags as rule-diverging while the classifiers call them `same`
(w0-critique section 5.4, C20) plus the v0.111.0 LdMemberToken sites whose IL value settles the delta.
build.py checks every citation against both trees and stops on a citation that does not resolve.
"""

V111, V107 = "v0.111.0", "v0.107.1"

MANUAL = {
    # cards (6 of the 21)
    "cards:DualWield": ("refactor_only", "manual",
                        "diff-gate false positive: both versions clone the selection (CreateClone) once per copy",
                        {V111: ["Models.Cards/DualWield.cs:101"], V107: ["Models/Cards/DualWield.cs:35"]}),
    "cards:FiendFire": ("refactor_only", "manual", "the new literal 0.1 is a VFX scale step (scale += 0.1f), present in both",
                        {V111: ["Models.Cards/FiendFire.cs:42"], V107: ["Models/Cards/FiendFire.cs:52"]}),
    "cards:HeirloomHammer": ("refactor_only", "manual",
                             "both versions clone the selection (CreateClone); the call moved into the state machine",
                             {V111: ["Models.Cards/HeirloomHammer.cs:112"], V107: ["Models/Cards/HeirloomHammer.cs:38"]}),
    "cards:Omnislice": ("refactor_only", "manual",
                        "same damage, overkill splash and AttackContext.AddHit; CreatureCmd.Damage gained the cardPlay argument",
                        {V111: ["Models.Cards/Omnislice.cs:119", "Models.Cards/Omnislice.cs:144"],
                         V107: ["Models/Cards/Omnislice.cs:28", "Models/Cards/Omnislice.cs:39"]}),
    "cards:SweepingBeam": ("refactor_only", "manual",
                           "same calls in the same await order (attack, then draw); AsyncStateMachine is the BeforeDamage lambda attribute",
                           {V111: ["Models.Cards/SweepingBeam.cs:175", "Models.Cards/SweepingBeam.cs:186"],
                            V107: ["Models/Cards/SweepingBeam.cs:32", "Models/Cards/SweepingBeam.cs:45"]}),
    "cards:Whirlwind": ("refactor_only", "manual", "same X-cost multi-hit attack; _002Ector is a display-class constructor",
                        {V111: ["Models.Cards/Whirlwind.cs:60", "Models.Cards/Whirlwind.cs:70"],
                         V107: ["Models/Cards/Whirlwind.cs:33", "Models/Cards/Whirlwind.cs:42"]}),
    # encounters (1)
    "encounters:TwoTailedRatsNormal": ("refactor_only", "manual",
                                       "same starter-move roll and slot assignment; the literals moved into the ValueTuple rendering",
                                       {V111: ["Models.Encounters/TwoTailedRatsNormal.cs:42", "Models.Encounters/TwoTailedRatsNormal.cs:47"],
                                        V107: ["Models/Encounters/TwoTailedRatsNormal.cs:33", "Models/Encounters/TwoTailedRatsNormal.cs:38"]}),
    # events (7)
    "events:ColossalFlower": ("refactor_only", "il",
                              "the two v0.111.0 int[3] arrays read from IL are 35, 75, 135 and 5, 6, 7: equal to v0.107.1",
                              {V111: ["Models.Events/ColossalFlower.cs:491", "Models.Events/ColossalFlower.cs:494", "IL:ColossalFlower..cctor"],
                               V107: ["Models/Events/ColossalFlower.cs:20", "Models/Events/ColossalFlower.cs:22"]}),
    "events:DenseVegetation": ("refactor_only", "manual", "0.25 / 0.6 are a UI position (Vector2 constructor) present in both",
                               {V111: ["Models.Events/DenseVegetation.cs:223"], V107: ["Models/Events/DenseVegetation.cs:73"]}),
    "events:EndlessConveyor": ("refactor_only", "manual",
                               "PrintMembers belongs to the compiler-generated record struct Dish (not rendered by the v0.107.1 decompile)",
                               {V111: ["Models.Events/EndlessConveyor.cs:30", "Models.Events/EndlessConveyor.cs:71"],
                                V107: ["Models/Events/EndlessConveyor.cs:27"]}),
    "events:Orobas": ("changed", "manual",
                      "relic option pools changed: v0.111.0 {ElectricShrymp, GlassEye} and {AlchemicalCoffer, Driftwood, RadiantPearl, "
                      "SandCastle}, v0.107.1 {ElectricShrymp, GlassEye, SandCastle} and {AlchemicalCoffer, Driftwood, RadiantPearl}: "
                      "NextItem list sizes and the relic odds differ (missed by the set-based event fingerprint, G20)",
                      {V111: ["Models.Events/Orobas.cs:32", "Models.Events/Orobas.cs:38", "Models.Events/Orobas.cs:41"],
                       V107: ["Models/Events/Orobas.cs:30", "Models/Events/Orobas.cs:32", "Models/Events/Orobas.cs:37"]}),
    "events:TabletOfTruth": ("refactor_only", "manual", "the new literal 3 is a state-machine case label; the decipher logic is unchanged",
                             {V111: ["Models.Events/TabletOfTruth.cs:67", "Models.Events/TabletOfTruth.cs:206"],
                              V107: ["Models/Events/TabletOfTruth.cs:63"]}),
    "events:Vakuu": ("refactor_only", "manual", "GetValueOrDefault is called as a static extension method; same ancient visit count",
                     {V111: ["Models.Events/Vakuu.cs:54"], V107: ["Models/Events/Vakuu.cs:54"]}),
    "events:WelcomeToWongos": ("refactor_only", "manual", "the 2000-point badge constant is inlined in v0.111.0; same arithmetic",
                               {V111: ["Models.Events/WelcomeToWongos.cs:420", "Models.Events/WelcomeToWongos.cs:538"],
                                V107: ["Models/Events/WelcomeToWongos.cs:20", "Models/Events/WelcomeToWongos.cs:114"]}),
    # monsters (7)
    "monsters:BowlbugSilk": ("refactor_only", "manual",
                             "ThrashDamage is GetValueIfAscension(DeadlyEnemies, 5, 4) in both versions (the gate dropped the 4 from the v0.111.0 rendering)",
                             {V111: ["Models.Monsters/BowlbugSilk.cs:235"], V107: ["Models/Monsters/BowlbugSilk.cs:32"]}),
    "monsters:BygoneEffigy": ("refactor_only", "manual", "300 / 1500 are VFX positions of the attack-distance node",
                              {V111: ["Models.Monsters/BygoneEffigy.cs:266"], V107: ["Models/Monsters/BygoneEffigy.cs:99"]}),
    "monsters:KinFollower": ("refactor_only", "manual",
                             "400 is the VFX offset of the AttackDistanceControl node (v0.107.1 derived it from creature positions)",
                             {V111: ["Models.Monsters/KinFollower.cs:183"], V107: ["Models/Monsters/KinFollower.cs:152"]}),
    "monsters:MagiKnight": ("refactor_only", "manual", "GetCurrentBody was a UI scale read of the attack-distance VFX",
                            {V111: ["Models.Monsters/MagiKnight.cs:219"], V107: ["Models/Monsters/MagiKnight.cs:131"]}),
    "monsters:Nibbit": ("refactor_only", "manual", "animator NextState -> AddNextState (UI)",
                        {V111: ["Models.Monsters/Nibbit.cs:395"], V107: ["Models/Monsters/Nibbit.cs:124"]}),
    "monsters:SlumberingBeetle": ("refactor_only", "manual", "750 is a VFX position of the attack-distance node",
                                  {V111: ["Models.Monsters/SlumberingBeetle.cs:236"], V107: ["Models/Monsters/SlumberingBeetle.cs:139"]}),
    "monsters:TestSubject": ("refactor_only", "manual",
                             "same Respawns < 2 / >= 2 branches; the extra 2 literals are animator conditions and state labels",
                             {V111: ["Models.Monsters/TestSubject.cs:1266", "Models.Monsters/TestSubject.cs:1267"],
                              V107: ["Models/Monsters/TestSubject.cs:210", "Models/Monsters/TestSubject.cs:211"]}),
    # LdMemberToken sites settled by the IL read (w0-critique section 7)
    "cards:Folly": ("refactor_only", "il",
                    "v0.111.0 CanonicalKeywords read from IL: Unplayable, Eternal, Innate, Ethereal = v0.107.1; every readable member identical",
                    {V111: ["Models.Cards/Folly.cs:19", "IL:Folly.get_CanonicalKeywords"],
                     V107: ["Models/Cards/Folly.cs:12"]}),
    "relics:GlassEye": ("refactor_only", "il",
                        "v0.111.0 CardRarity[5] read from IL: Common, Common, Uncommon, Uncommon, Rare = v0.107.1",
                        {V111: ["Models.Relics/GlassEye.cs:58", "IL:GlassEye.<AfterObtained>d__2.MoveNext"],
                         V107: ["Models/Relics/GlassEye.cs:18"]}),
    "monsters:KnowledgeDemon": ("refactor_only", "il",
                                "_disintegrationDamageValues read from IL: 6, 7, 8 = v0.107.1; CurseOfKnowledge renamed CurseOfKnowledgeMove",
                                {V111: ["Models.Monsters/KnowledgeDemon.cs:773", "Models.Monsters/KnowledgeDemon.cs:174", "IL:KnowledgeDemon..cctor"],
                                 V107: ["Models/Monsters/KnowledgeDemon.cs:45", "Models/Monsters/KnowledgeDemon.cs:130"]}),
}

# Content files with a unique (non-bulk) edit between the trees that neither the classifiers (status `same`) nor the
# diff gate flagged: found by scripts/version_delta.py (docs/version-delta-demo-2026-11-01.md, "Cross-check against
# the W0 version-delta list"). build.py adds these keys to the union with sources `version_delta`. Read by hand in
# both trees (2026-11-02); 27 refactor_only, 1 changed.
_NULLSAFE = "CombatState -> CombatState?: null-safety only (combat state is never null while a card is played)"
_CREATURE_CS = "base.Creature.CombatState -> base.CombatState: the same object, no behaviour change"
_CRAB_BG = ("the KaiserCrab background node became nullable (Background?.X): UI only, same moves, damage and powers")
_SPIT_VFX = "the spit-target VFX position is computed from the creature node instead of VfxSpawnPosition: VFX only"
_BOT_AWAIT = ("AfterAddedToRoom now awaits base.AfterAddedToRoom(), which returns Task.CompletedTask "
              "(Models/MonsterModel.cs:604-607): same order; the rest is the TestMode-gated fall animation")
MANUAL_VD = {
    "cards:Deathbringer": ("refactor_only", "manual", _NULLSAFE,
                           {V111: ["Models.Cards/Deathbringer.cs:98", "Models.Cards/Deathbringer.cs:109"],
                            V107: ["Models/Cards/Deathbringer.cs:34", "Models/Cards/Deathbringer.cs:35"]}),
    "cards:EvilEye": ("refactor_only", "manual",
                      "exhausted-this-turn test reads e.Actor == Owner.Creature instead of e.Card.Owner == Owner; "
                      "CardExhaustedEntry's Actor is card.Owner.Creature (Combat.History.Entries/CardExhaustedEntry.cs:20), "
                      "so the predicate is the same",
                      {V111: ["Models.Cards/EvilEye.cs:138"], V107: ["Models/Cards/EvilEye.cs:25"]}),
    "cards:HighFive": ("refactor_only", "manual", "FromOsty gained the cardPlay argument (bulk B4); " + _NULLSAFE,
                       {V111: ["Models.Cards/HighFive.cs:78"], V107: ["Models/Cards/HighFive.cs:39"]}),
    "cards:MeteorShower": ("refactor_only", "manual", "FromCard(this, cardPlay) (bulk B1); " + _NULLSAFE,
                           {V111: ["Models.Cards/MeteorShower.cs:74"], V107: ["Models/Cards/MeteorShower.cs:37"]}),
    "cards:Thrash": ("refactor_only", "manual",
                     "FromCard(this, cardPlay) (bulk B1); Hook.ModifyDamage gained a null cardPlay argument",
                     {V111: ["Models.Cards/Thrash.cs:72", "Models.Cards/Thrash.cs:113"],
                      V107: ["Models/Cards/Thrash.cs:45", "Models/Cards/Thrash.cs:70"]}),
    "cards:Thunderclap": ("refactor_only", "manual", "FromCard(this, cardPlay) (bulk B1); " + _NULLSAFE,
                          {V111: ["Models.Cards/Thunderclap.cs:70"], V107: ["Models/Cards/Thunderclap.cs:30"]}),
    "enchantments:Corrupted": ("refactor_only", "manual", "CreatureCmd.Damage gained the cardPlay argument",
                               {V111: ["Models.Enchantments/Corrupted.cs:49"], V107: ["Models/Enchantments/Corrupted.cs:31"]}),
    "enchantments:Sown": ("refactor_only", "manual",
                          "Status = Disabled moved before GainEnergy: differs only if the energy gain re-entered this "
                          "enchantment's OnPlay, which no single-player effect does",
                          {V111: ["Models.Enchantments/Sown.cs:50"], V107: ["Models/Enchantments/Sown.cs:18"]}),
    "enchantments:Swift": ("refactor_only", "manual",
                           "Status = Disabled moved before the draw: differs only if the draw re-entered this "
                           "enchantment's OnPlay (the card is being played, not in the draw pile)",
                           {V111: ["Models.Enchantments/Swift.cs:53"], V107: ["Models/Enchantments/Swift.cs:20"]}),
    "encounters:DeprecatedEncounter": ("refactor_only", "manual",
                                       "IsDebugEncounter override removed; the encounter has no monsters and no act "
                                       "lists it",
                                       {V111: ["Models.Encounters/DeprecatedEncounter.cs:7"],
                                        V107: ["Models/Encounters/DeprecatedEncounter.cs:14"]}),
    "monsters:Crusher": ("refactor_only", "manual", _CRAB_BG,
                         {V111: ["Models.Monsters/Crusher.cs:736"], V107: ["Models/Monsters/Crusher.cs:63"]}),
    "monsters:CubexConstruct": ("refactor_only", "manual",
                                "GenerateBestiaryMoveList override (bestiary UI)",
                                {V111: ["Models.Monsters/CubexConstruct.cs:766"],
                                 V107: ["Models/Monsters/CubexConstruct.cs:198"]}),
    "monsters:DecimillipedeSegment": ("refactor_only", "manual", _CREATURE_CS,
                                      {V111: ["Models.Monsters/DecimillipedeSegment.cs:233"],
                                       V107: ["Models/Monsters/DecimillipedeSegment.cs:228"]}),
    "monsters:Fabricator": ("refactor_only", "manual", _CREATURE_CS,
                            {V111: ["Models.Monsters/Fabricator.cs:545"], V107: ["Models/Monsters/Fabricator.cs:51"]}),
    "monsters:FakeMerchantMonster": ("refactor_only", "manual", "OnlyPlayAnimOnce() on the 8-hit attack: animation only",
                                     {V111: ["Models.Monsters/FakeMerchantMonster.cs:276"],
                                      V107: ["Models/Monsters/FakeMerchantMonster.cs:88"]}),
    "monsters:GremlinMerc": ("refactor_only", "manual", _CREATURE_CS,
                             {V111: ["Models.Monsters/GremlinMerc.cs:99"], V107: ["Models/Monsters/GremlinMerc.cs:50"]}),
    "monsters:Guardbot": ("refactor_only", "manual", _BOT_AWAIT + "; " + _CREATURE_CS,
                          {V111: ["Models.Monsters/Guardbot.cs:53", "Models.Monsters/Guardbot.cs:220"],
                           V107: ["Models/Monsters/Guardbot.cs:27", "Models/Monsters/Guardbot.cs:29"]}),
    "monsters:LeafSlimeM": ("refactor_only", "manual", _SPIT_VFX,
                            {V111: ["Models.Monsters/LeafSlimeM.cs:144"], V107: ["Models/Monsters/LeafSlimeM.cs:54"]}),
    "monsters:LivingShield": ("refactor_only", "manual", _CREATURE_CS,
                              {V111: ["Models.Monsters/LivingShield.cs:344"], V107: ["Models/Monsters/LivingShield.cs:73"]}),
    "monsters:Rocket": ("refactor_only", "manual", _CRAB_BG,
                        {V111: ["Models.Monsters/Rocket.cs:633"], V107: ["Models/Monsters/Rocket.cs:43"]}),
    "monsters:SlitheringStrangler": ("refactor_only", "manual", "animator trigger target state: animation only",
                                     {V111: ["Models.Monsters/SlitheringStrangler.cs:372"],
                                      V107: ["Models/Monsters/SlitheringStrangler.cs:98"]}),
    "monsters:Stabbot": ("refactor_only", "manual", _BOT_AWAIT,
                         {V111: ["Models.Monsters/Stabbot.cs:54", "Models.Monsters/Stabbot.cs:197"],
                          V107: ["Models/Monsters/Stabbot.cs:29", "Models/Monsters/Stabbot.cs:31"]}),
    "monsters:TwigSlimeM": ("refactor_only", "manual", _SPIT_VFX,
                            {V111: ["Models.Monsters/TwigSlimeM.cs:145"], V107: ["Models/Monsters/TwigSlimeM.cs:59"]}),
    "potion_pools:MockPotionPool": ("refactor_only", "manual", "IsMock => true on a test pool",
                                    {V111: ["Models.PotionPools/MockPotionPool.cs:8"],
                                     V107: ["Models/PotionPools/MockPotionPool.cs:9"]}),
    "relics:CharonsAshes": ("refactor_only", "manual", "CreatureCmd.Damage lost its trailing null argument (signature change)",
                            {V111: ["Models.Relics/CharonsAshes.cs:59"], V107: ["Models/Relics/CharonsAshes.cs:23"]}),
    "relics:GhostSeed": ("refactor_only", "manual",
                         "new card.Owner != Owner early return: every card has the relic's owner in single player",
                         {V111: ["Models.Relics/GhostSeed.cs:26"], V107: ["Models/Relics/GhostSeed.cs:20"]}),
    "relics:LostWisp": ("refactor_only", "manual", "CreatureCmd.Damage lost its trailing null argument (signature change)",
                        {V111: ["Models.Relics/LostWisp.cs:60"], V107: ["Models/Relics/LostWisp.cs:24"]}),
    "relics:MusicBox": ("changed", "manual",
                        "CardBeingPlayed is now also cleared at the owner's turn start and at combat end; in v0.107.1 "
                        "only AfterCardPlayed cleared it, so an attack whose play did not reach AfterCardPlayed (combat "
                        "ended mid-play) left it set and BeforeCardPlayed returned early in every later combat",
                        {V111: ["Models.Relics/MusicBox.cs:172", "Models.Relics/MusicBox.cs:179"],
                         V107: ["Models/Relics/MusicBox.cs:53", "Models/Relics/MusicBox.cs:82"]}),
}

# Classifier status -> triage. The classifiers judge single-player rules (events.classified.json
# version_delta_definition): cosmetic = source changed without a single-player rule effect.
STATUS_TRIAGE = {
    "rule-change": "changed",
    "new": "changed",
    "cosmetic": "refactor_only",
    "same": None,            # only in the union when the diff gate flags it: needs a MANUAL record
    "decompile-gap": None,   # needs an IL record in MANUAL, else unknown
    "unknown": "unknown",
}

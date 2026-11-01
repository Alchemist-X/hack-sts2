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

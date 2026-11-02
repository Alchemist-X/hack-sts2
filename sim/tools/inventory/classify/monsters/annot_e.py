E = {}


def add(name, comp, rng=None, **kw):
    kw["composition"] = comp
    if rng:
        kw["rng"] = rng
    E[name] = kw


add("AeonglassBoss", ["Aeonglass"])
add("AxebotsNormal", ["Axebot @front"],
    notes="One Axebot with StockPower 2; it respawns twice into the same slot (Axebot with StockAmount 1, then 0), so three bodies in total.")
add("BattlewornDummyEventEncounter", [], notes="Abstract base: RanOutOfTime custom state (SaveCustomState/LoadCustomState).")
add("BattlewornDummyEventV1Encounter", ["BattleFriendV1"], notes="ShouldGiveRewards false; event BattlewornDummy option 1 (Models.Events/BattlewornDummy.cs:183), resumes the event after combat.")
add("BattlewornDummyEventV2Encounter", ["BattleFriendV2"], notes="ShouldGiveRewards false; BattlewornDummy option 2 (Models.Events/BattlewornDummy.cs:189).")
add("BattlewornDummyEventV3Encounter", ["BattleFriendV3"], notes="ShouldGiveRewards false; BattlewornDummy option 3 (Models.Events/BattlewornDummy.cs:195).")
add("BowlbugsNormal", ["BowlbugRock @first", "worker #1 @middle", "worker #2 @last"],
    rng=["NextItem x2: each worker drawn from {BowlbugEgg, BowlbugSilk, BowlbugNectar} (Dictionary insertion order) minus types already taken (each at most once)"],
    notes="Slot names are given to the monsters but the encounter has no Slots override (empty), so SortEnemiesBySlotName keeps spawn order.")
add("BowlbugsWeak", ["BowlbugRock @odd", "random bug @even"], rng=["NextItem: one of {BowlbugEgg, BowlbugNectar}"],
    notes="No Slots override; slot names are labels only.")
add("BygoneEffigyElite", ["BygoneEffigy"])
add("ByrdonisElite", ["Byrdonis"])
add("CeremonialBeastBoss", ["CeremonialBeast"])
add("ChompersNormal", ["Chomper", "Chomper (ScreamFirst)"])
add("ConstructMenagerieNormal", ["PunchConstruct", "CubexConstruct", "CubexConstruct"])
add("CorpseSlugsNormal", ["CorpseSlug", "CorpseSlug", "CorpseSlug"],
    rng=["CorpseSlug.EnsureCorpseSlugsStartWithDifferentMoves(monsters, Rng): one NextInt(3), then StarterMoveIdx = (n + i) % 3"], ported_note="weak variant ported")
add("CorpseSlugsWeak", ["CorpseSlug", "CorpseSlug"],
    rng=["CorpseSlug.EnsureCorpseSlugsStartWithDifferentMoves(monsters, Rng): one NextInt(3), then StarterMoveIdx = (n + i) % 3"])
add("CubexConstructNormal", ["CubexConstruct"])
add("CultistsNormal", ["CalcifiedCultist", "DampCultist"])
add("DecimillipedeElite", ["DecimillipedeSegmentFront @segment1", "DecimillipedeSegmentMiddle @segment2", "DecimillipedeSegmentBack @segment3"],
    rng=["NextInt(3) = front StarterMoveIdx; middle (n+1)%3, back (n+2)%3"])
add("DenseVegetationEventEncounter", ["Wriggler @wriggler1", "Wriggler @wriggler2", "Wriggler @wriggler3", "Wriggler @wriggler4"],
    notes="StartStunned = false for all four; started by event DenseVegetation (Models.Events/DenseVegetation.cs:367), event not resumed after combat.")
add("DeprecatedEncounter", [], notes="Empty placeholder used when a save references a removed boss (Models/ActModel.cs:327); never generated.")
add("DevotedSculptorWeak", ["DevotedSculptor"])
add("EntomancerElite", ["Entomancer"])
add("ExoskeletonsNormal", ["Exoskeleton @first", "Exoskeleton @second", "Exoskeleton @third", "Exoskeleton @fourth"],
    notes="Each Exoskeleton's first move is chosen by its slot name.")
add("ExoskeletonsWeak", ["Exoskeleton @first", "Exoskeleton @second", "Exoskeleton @third"])
add("FabricatorNormal", ["Fabricator @fabricator"], notes="Slots bot1, bot2, fabricator, bot3, bot4: bots summoned later fill GetNextSlot (first free slot in that order), which also fixes turn order (SortEnemiesBySlotName).")
add("FakeMerchantEventEncounter", ["FakeMerchantMonster @merchant"], notes="Gold reward fixed at 300 (MinGoldReward = MaxGoldReward = 300); started by event FakeMerchant (Models.Events/FakeMerchant.cs:101).")
add("FlyconidNormal", ["random medium slime", "Flyconid"], rng=["NextItem: one of {LeafSlimeM, TwigSlimeM}"])
add("FogmogNormal", ["Fogmog @fogmog"], notes="EyeWithTeeth is summoned into slot 'illusion' (index 0), so it acts before Fogmog.")
add("FossilStalkerNormal", ["FossilStalker"])
add("FrogKnightNormal", ["FrogKnight"])
add("FuzzyWurmCrawlerWeak", ["FuzzyWurmCrawler"])
add("GlobeHeadNormal", ["GlobeHead"])
add("GremlinMercNormal", ["GremlinMerc @merc"],
    notes="Slots: merc, sneaky, fat (set by GremlinMerc's SurprisePower on death). CalculateGoldProportion: 1 unless the FatGremlin escaped; then 0 (gold was stolen) or 1 otherwise — reduces the combat gold reward.",
    kern=["EncounterModel.CalculateGoldProportion override (combat gold reward, run layer)", "encounter custom flag GoldWasStolen (MarkGoldStolen)"])
add("HauntedShipNormal", ["HauntedShip"])
add("HunterKillerNormal", ["HunterKiller"])
add("InfestedPrismsElite", ["InfestedPrism"])
add("InkletsNormal", ["Inklet", "Inklet (MiddleInklet)", "Inklet"])
add("KaiserCrabBoss", ["Crusher @crusher", "Rocket @rocket"])
add("KnightsElite", ["FlailKnight @first", "SpectralKnight @second", "MagiKnight @third"], notes="No Slots override; slot names are labels only.")
add("KnowledgeDemonBoss", ["KnowledgeDemon"])
add("LagavulinMatriarchBoss", ["LagavulinMatriarch"])
add("LivingFogNormal", ["LivingFog @livingFog"], notes="Slots bomb1..bomb5 then livingFog: GasBombs fill the first free bomb slot and act before LivingFog.")
add("LouseProgenitorNormal", ["LouseProgenitor"])
add("MawlerNormal", ["Mawler"])
add("MechaKnightElite", ["MechaKnight"])
add("MysteriousKnightEventEncounter", ["MysteriousKnight"], notes="Started by event TheLanternKey (Models.Events/TheLanternKey.cs:126) with extra rewards; CanonicalEncounter of TheLanternKey (:83). Combat-layout event (LayoutType Combat): EventCombatSynchronizer.InitializeForEvent generates the monsters and creates the creatures at event entry, right after BeginEvent (Rooms/EventRoom.cs:161-183; Multiplayer.Game/EventCombatSynchronizer.cs:83-128), so the encounter Rng draws and one RunRng.Niche HP draw per creature happen even if the player never fights; the combat room reuses that CombatState (ShouldCreateCombat false, :175-183).")
add("MytesNormal", ["Myte @first", "Myte @second"])
add("NibbitsNormal", ["Nibbit (IsFront) @front", "Nibbit @back"])
add("NibbitsWeak", ["Nibbit (IsAlone)"])
add("OvergrowthCrawlers", ["ShrinkerBeetle", "FuzzyWurmCrawler"])
add("OvicopterNormal", ["Ovicopter @ovicopter"], notes="Slots egg1..egg5 then ovicopter: eggs go into the last free slot (Slots.LastOrDefault), sorted before the Ovicopter.")
add("OwlMagistrateNormal", ["OwlMagistrate"])
add("PhantasmalGardenersElite", ["PhantasmalGardener @first", "PhantasmalGardener @second", "PhantasmalGardener @third", "PhantasmalGardener @fourth"])
add("PhrogParasiteElite", ["PhrogParasite @phrog"], notes="Slots phrog, wriggler1..wriggler4; Wrigglers spawn on the parasite's death via GetWrigglerSlotName(i) = 'wriggler' + (i+1).")
add("PunchConstructNormal", ["PunchConstruct"])
add("PunchOffEventEncounter", ["PunchConstruct (StartsWithFastPunch, StartingHpReduction)", "PunchConstruct (StartingHpReduction)"],
    rng=["NextInt(2, 10) StartingHpReduction for the first construct", "NextInt(2, 10) for the second"],
    notes="Started by event PunchOff (Models.Events/PunchOff.cs:533); CanonicalEncounter of PunchOff (:448). Combat-layout event (LayoutType Combat): EventCombatSynchronizer.InitializeForEvent generates the monsters and creates the creatures at event entry, right after BeginEvent (Rooms/EventRoom.cs:161-183; Multiplayer.Game/EventCombatSynchronizer.cs:83-128), so the encounter Rng draws and one RunRng.Niche HP draw per creature happen even if the player never fights; the combat room reuses that CombatState (ShouldCreateCombat false, :175-183).")
add("QueenBoss", ["TorchHeadAmalgam @amalgam", "Queen @queen"])
add("RubyRaidersNormal", ["raider #1", "raider #2", "raider #3"],
    rng=["NextItem x3 without replacement over {AxeRubyRaider, AssassinRubyRaider, BruteRubyRaider, CrossbowRubyRaider, TrackerRubyRaider} (Dictionary insertion order, each at most once)"])
add("ScrollsOfBitingNormal", ["ScrollOfBiting x4"], rng=["NextInt(3) = first StarterMoveIdx; second (n+1)%3, third (n+2)%3; fourth fixed 2"])
add("ScrollsOfBitingWeak", ["ScrollOfBiting x3"], rng=["NextInt(3) = first StarterMoveIdx; second (n+1)%3, third (n+2)%3"])
add("SeapunkNormal", ["CalcifiedCultist", "Seapunk"])
add("SeapunkWeak", ["Seapunk"])
add("SewerClamNormal", ["SewerClam"])
add("ShrinkerBeetleWeak", ["ShrinkerBeetle"])
add("SkulkingColonyElite", ["SkulkingColony"])
add("SlimedBerserkerNormal", ["SlimedBerserker"])
add("SlimesNormal", ["TwigSlimeM", "LeafSlimeM", "small slime A", "small slime B"],
    rng=["NextBool: true -> (LeafSlimeS, TwigSlimeS), false -> (TwigSlimeS, LeafSlimeS)"])
add("SlimesWeak", ["small slime #1", "medium slime", "small slime #2"],
    rng=["NextItem small slime #1 from {LeafSlimeS, TwigSlimeS}", "NextItem small slime #2 from the remaining one", "NextItem medium slime from {LeafSlimeM, TwigSlimeM}"],
    notes="Draw order: small, small, medium; list order: small #1, medium, small #2.")
add("SlitheringStranglerNormal", ["secondary group", "SlitheringStrangler"],
    rng=["NextItem over SecondaryEnemyType {SnappingJaxfruit, MediumSlime, SmallSlimes}", "MediumSlime: NextItem {LeafSlimeM, TwigSlimeM}", "SmallSlimes: NextItem {LeafSlimeS, TwigSlimeS} twice (with replacement)"])
add("SludgeSpinnerWeak", ["SludgeSpinner"])
add("SlumberingBeetleNormal", ["BowlbugRock @first", "BowlbugSilk @second", "SlumberingBeetle @third"], notes="No Slots override; slot names are labels only.")
add("SnappingJaxfruitNormal", ["SnappingJaxfruit", "Flyconid"])
add("SoulFyshBoss", ["SoulFysh"])
add("SoulNexusElite", ["SoulNexus"])
add("SpinyToadNormal", ["SpinyToad"])
add("TerrorEelElite", ["TerrorEel"])
add("TestSubjectBoss", ["TestSubject"])
add("TheArchitectEventEncounter", ["Architect"], notes="CanonicalEncounter of event TheArchitect (Models.Events/TheArchitect.cs:785), entered after the last act (Runs/RunManager.cs:872). Display only: the event never calls EnterCombatWithoutExitingEvent, so no combat loop runs; the layout CombatState is still built at event entry (Multiplayer.Game/EventCombatSynchronizer.cs:83-128), costing one RunRng.Niche draw for the Architect.")
add("TheInsatiableBoss", ["TheInsatiable"])
add("TheKinBoss", ["KinFollower (StartsWithDance) @slot1", "KinFollower @slot2", "KinPriest @leaderSlot"])
add("TheLostAndForgottenNormal", ["TheLost", "TheForgotten"])
add("TheObscuraNormal", ["TheObscura @obscura"], notes="Parafright is summoned into slot 'illusion' (index 0).")
add("ThievingHopperWeak", ["ThievingHopper"])
add("ToadpolesWeak", ["Toadpole (IsFront)", "Toadpole (IsFront = false)"])
add("TunnelerNormal", ["Chomper (ScreamFirst)", "Tunneler"], notes="Registered but listed in no act (v0.111.0 and v0.107.1): unreachable in a standard run.")
add("TunnelerWeak", ["Tunneler"])
add("TurretOperatorWeak", ["LivingShield", "TurretOperator"])
add("TwoTailedRatsNormal", ["TwoTailedRat @third", "TwoTailedRat @fourth", "TwoTailedRat @fifth"],
    rng=["NextInt(3) = first StarterMoveIndex; second (n+1)%3, third (n+2)%3"],
    notes="Slots first..fifth; reinforcements take the last free slot (first, then second).")
add("VantomBoss", ["Vantom"])
add("VineShamblerNormal", ["VineShambler"])
add("WaterfallGiantBoss", ["WaterfallGiant"])
for _m, _c in (("MockArtifactEncounter", ["MockArtifactMonster"]), ("MockAttackAndSummonEncounter", ["MockAttackAndSummonMinionMonster"]),
               ("MockBossEncounter", ["BigDummy"]), ("MockEliteEncounter", ["BigDummy"]), ("MockMonsterEncounter", ["BigDummy"]),
               ("MockNoRewardsEncounter", ["BigDummy"]), ("MockPlatingEncounter", ["MockPlatingMonster (PlatingAmount from the encounter, default 1)"]),
               ("MockTwoMonsterEncounter", ["BigDummy", "BigDummy"])):
    add(_m, _c, notes="Mock encounter (IsMock); the game's own test seam.")

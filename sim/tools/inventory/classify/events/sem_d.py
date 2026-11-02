from semlib import O, P, R, CB, V

DATA = {}

ANCIENT_KR = ["AncientFramework (arrival heal, RelicOption -> RelicCmd.Obtain + Done, pre-finished save point C)",
              "RelicCmd.Obtain (+ each relic's AfterObtained, relics-kind content)"]

DATA["Neow"] = dict(
    category="start_ancient",
    appearance=dict(pool="act_ancient", allowed_act_indices=[0], is_allowed=None, unlock_gate="NeowEpoch",
                    entry="act-1 ancient (Overgrowth or Underdocks AllAncients): RoomSet.Ancient = UpFront.NextItem("
                          "unlocked ancients), Neow removed unless NeowEpoch is revealed (Models.Acts/Overgrowth.cs:100-108, "
                          "Models.Acts/Underdocks.cs:93-101); without Neow the act-1 ancient list is empty"),
    layout="Ancient",
    options=[
        O("INITIAL", "<curse relic>", "obtain the drawn downside relic (CursedPearl, DowsingRod, HeftyTablet, "
          "LargeCapsule, LeafyPoultice, NeowsBones, NeowsSacrifice, PrecariousShears, SilkenTress, SilverCrucible)",
          cond="EventRng.NextItem over CurseOptions with IsAllowedAtNeow", cite="m:CurseOptions"),
        O("INITIAL", "<positive relic> x2", "obtain the relic", cond="first 2 of UnstableShuffle(positives): the 14 "
          "PositiveOptions minus the curse's exclusions (CursedPearl->GoldenPearl, HeftyTablet->ArcaneScroll, "
          "LeafyPoultice->NewLeaf, PrecariousShears->PreciseScissors, NeowsSacrifice->PhialHolster+LostCoffer), plus "
          "NextBool LavaRock|SmallCapsule (skipped when the curse is LargeCapsule), NextBool "
          "NutritiousOyster|StoneHumidifier, NextBool NeowsTalisman|Pomander, minus !IsAllowedAtNeow",
          cite="m:GenerateInitialOptions"),
    ],
    outcomes=dict(hp=["arrival: HP set to 0 then healed by MaxHp (x0.8 at A2)"],
                  relics=["one of three Neow relics (effects in the relics kind)"]),
    prompts=[],
    rng_streams=[
        R("EventRng", "GenerateInitialOptions", "NextItem(curses), [NextBool unless LargeCapsule], NextBool, NextBool, "
          "UnstableShuffle(positives) (count-1 NextInt draws)", "m:GenerateInitialOptions"),
    ],
    mechanics=["relic choice (1 downside + 2 positive)", "arrival heal"],
    kernel_requirements=ANCIENT_KR + ["RelicModel.IsAllowedAtNeow (Kaleidoscope: all character card pools unlocked; "
                                      "ScrollBoxes: >= 4 Common and >= 2 Uncommon unlocked character cards; "
                                      "MassiveScroll multiplayer only; WingedBoots/SilverCrucible single player only)",
                                      "RunState.Modifiers (empty in Standard; modifier Neow chain excluded)"],
    content_deps=dict(relics=["ArcaneScroll", "BoomingConch", "FishingRod", "GoldenPearl", "Kaleidoscope",
                              "LeadPaperweight", "LostCoffer", "MassiveScroll", "NeowsTorment", "NewLeaf",
                              "PhialHolster", "PreciseScissors", "ScrollBoxes", "WingedBoots", "LavaRock",
                              "NeowsTalisman", "NutritiousOyster", "Pomander", "SmallCapsule", "StoneHumidifier",
                              "CursedPearl", "DowsingRod", "HeftyTablet", "LargeCapsule", "LeafyPoultice", "NeowsBones",
                              "NeowsSacrifice", "PrecariousShears", "SilkenTress", "SilverCrucible"]),
    ascension="A2 WearyTraveler: arrival heal x0.8 (Models/AncientEventModel.cs:84-87)",
    unlock_progress=["NeowEpoch (Neow present)", "Kaleidoscope and ScrollBoxes availability depend on UnlockState"],
    version=V("rule-change", "CurseOptions grew from 8 to 10 (DowsingRod, NeowsSacrifice added) and NeowsSacrifice now "
              "excludes PhialHolster and LostCoffer from the positives", mech="fingerprint-differs"),
    tier=2, tier_reason="ancient framework + IsAllowedAtNeow unlock queries; nested prompts belong to the relics",
    port_batch=1,
    notes=["Seed: RunSeed + 0 + XXH64(\"NEOW\") in single player; options depend only on seed, character and "
           "unlock profile (no deck query), so they are a cheap W2 oracle against recorded runs.",
           "AncientEventModel.RelicOption<T>(page, customDonePage) drops customDonePage (Models/AncientEventModel.cs:"
           "353-356), so Neow's POSITIVE/CURSED done pages are never used (loc only).",
           "MassiveScroll is never offered in single player (IsAllowed = Players > 1, Models.Relics/MassiveScroll.cs:158-161).",
           "Modifier runs (daily/custom) replace the relic offer with ModifierModel.GenerateNeowOption chains "
           "(Models.Events/Neow.cs:365-387); out of scope for Standard."],
    open_questions=[],
)

DATA["Orobas"] = dict(
    category="act_ancient",
    appearance=dict(pool="act_ancient", allowed_act_indices=[1], is_allowed=None, unlock_gate="OrobasEpoch",
                    entry="act-2 ancient (Hive AllAncients; removed unless OrobasEpoch, Models.Acts/Hive.cs:98-106); "
                          "competes with Pael, Tezcatara and the shared Darv subset"),
    layout="Ancient",
    options=[
        O("INITIAL", "slot 1", "NextItem({ElectricShrymp, GlassEye, X}) where X = PrismaticGem when NextFloat() < "
          "0.3333333f else SeaGlass(CharacterId = random other unlocked character, own if none)",
          cite="m:GenerateInitialOptions"),
        O("INITIAL", "slot 2", "NextItem({AlchemicalCoffer, Driftwood, RadiantPearl, SandCastle})", cite="m:OptionPool2"),
        O("INITIAL", "slot 3", "NextItem({TouchOfOrobas if SetupForPlayer, ArchaicTooth if SetupForPlayer}); locked "
          "OPTION_POOL_3_LOCKED when neither qualifies", cite="m:OptionPool3"),
    ],
    outcomes=dict(relics=["one of three Orobas relics"], hp=["arrival heal"]),
    prompts=[],
    rng_streams=[R("EventRng", "GenerateInitialOptions", "NextItem(other unlocked characters), NextFloat, "
                   "NextItem(slot 1), NextItem(slot 2), NextItem(slot 3)", "m:GenerateInitialOptions")],
    mechanics=["relic choice (3 slots)", "arrival heal"],
    kernel_requirements=ANCIENT_KR + ["UnlockState.Characters", "SeaGlass.CharacterId (relic instance state)",
                                      "TouchOfOrobas.SetupForPlayer / ArchaicTooth.SetupForPlayer (relic logic at "
                                      "option time)"],
    content_deps=dict(relics=["ElectricShrymp", "GlassEye", "PrismaticGem", "SeaGlass", "AlchemicalCoffer", "Driftwood",
                              "RadiantPearl", "SandCastle", "TouchOfOrobas", "ArchaicTooth"]),
    ascension="A2 WearyTraveler: arrival heal x0.8",
    unlock_progress=["OrobasEpoch", "SeaGlass character drawn from unlocked characters"],
    version=V("same", "fingerprint equal"),
    tier=2, tier_reason="ancient framework + relic SetupForPlayer queries; nested prompts belong to the relics",
    port_batch=1,
    notes=["OptionPool3 is a property rebuilt on every access, so SetupForPlayer runs on fresh mutable copies."],
    open_questions=[],
)

DATA["Pael"] = dict(
    category="act_ancient",
    appearance=dict(pool="act_ancient", allowed_act_indices=[1], is_allowed=None, unlock_gate=None,
                    entry="act-2 ancient (Hive AllAncients)"),
    layout="Ancient",
    options=[
        O("INITIAL", "slot 1", "NextItem({PaelsFlesh, PaelsHorn, PaelsTears})", cite="m:OptionPool1"),
        O("INITIAL", "slot 2", "NextItem(L) where L = [PaelsWing, PaelsClaw (>= 3 Goopy-enchantable deck cards), "
          "PaelsTooth (>= 5 removable)] then L.AddRange(L) (every entry doubled), then + PaelsGrowth",
          cite="re:AddRange"),
        O("INITIAL", "slot 3", "NextItem({PaelsEye, PaelsBlood, PaelsLegion if !HasEventPet})", cite="m:OptionPool3"),
    ],
    outcomes=dict(relics=["one of three Pael relics"], hp=["arrival heal"]),
    prompts=[],
    rng_streams=[R("EventRng", "GenerateInitialOptions", "NextItem x3 (slot 1, 2, 3)", "m:GenerateInitialOptions")],
    mechanics=["relic choice (3 slots, weighted slot 2)", "arrival heal"],
    kernel_requirements=ANCIENT_KR + ["EnchantmentModel Goopy.CanEnchant", "CardModel.IsRemovable",
                                      "Player.HasEventPet", "List<T>.AddRange(self) doubling semantics"],
    content_deps=dict(relics=["PaelsFlesh", "PaelsHorn", "PaelsTears", "PaelsWing", "PaelsClaw", "PaelsTooth",
                              "PaelsGrowth", "PaelsEye", "PaelsBlood", "PaelsLegion"], enchantments=["Goopy"]),
    ascension="A2 WearyTraveler: arrival heal x0.8",
    unlock_progress=[],
    version=V("same", "fingerprint equal (the self-AddRange is in both versions)"),
    tier=2, tier_reason="ancient framework + deck queries",
    port_batch=1,
    notes=["Trap: slot 2 weights are Wing 2, Claw 2, Tooth 2, Growth 1 because the list is appended to itself."],
    open_questions=[],
)

DATA["Tezcatara"] = dict(
    category="act_ancient",
    appearance=dict(pool="act_ancient", allowed_act_indices=[1], is_allowed=None, unlock_gate=None,
                    entry="act-2 ancient (Hive AllAncients)"),
    layout="Ancient",
    options=[
        O("INITIAL", "slot 1", "NextItem({VeryHotCocoa, YummyCookie, NutritiousSoup if the deck has a Basic "
          "Strike-tagged card})", cite="m:GenerateInitialOptions"),
        O("INITIAL", "slot 2", "NextItem({BiiigHug, Storybook, ToastyMittens})", cite="m:OptionPool2"),
        O("INITIAL", "slot 3", "NextItem({GoldenCompass, PumpkinCandle, ToyBox, SealOfGold})", cite="m:OptionPool3"),
    ],
    outcomes=dict(relics=["one of three Tezcatara relics"], hp=["arrival heal"]),
    prompts=[],
    rng_streams=[R("EventRng", "GenerateInitialOptions", "NextItem x3", "m:GenerateInitialOptions")],
    mechanics=["relic choice (3 slots)", "arrival heal"],
    kernel_requirements=ANCIENT_KR + ["CardModel.Tags (Strike) + Rarity Basic"],
    content_deps=dict(relics=["VeryHotCocoa", "YummyCookie", "NutritiousSoup", "BiiigHug", "Storybook",
                              "ToastyMittens", "GoldenCompass", "PumpkinCandle", "ToyBox", "SealOfGold"]),
    ascension="A2 WearyTraveler: arrival heal x0.8",
    unlock_progress=[],
    version=V("same", "fingerprint equal"),
    tier=2, tier_reason="ancient framework",
    port_batch=1,
    notes=[],
    open_questions=[],
)

DATA["Nonupeipe"] = dict(
    category="act_ancient",
    appearance=dict(pool="act_ancient", allowed_act_indices=[2], is_allowed=None, unlock_gate=None,
                    entry="act-3 ancient (Glory AllAncients)"),
    layout="Ancient",
    options=[
        O("INITIAL", "3 of the pool", "pool = {BlessedAntler, BrilliantScarf, DelicateFrond, DiamondDiadem, FurCoat, "
          "Glitter, JewelryBox, LoomingFruit, SignetRing} + BeautifulBracelet when >= 4 Swift-enchantable deck cards; "
          "UnstableShuffle, take 3", cite="m:GenerateInitialOptions"),
    ],
    outcomes=dict(relics=["one of three Nonupeipe relics"], hp=["arrival heal"]),
    prompts=[],
    rng_streams=[R("EventRng", "GenerateInitialOptions", "UnstableShuffle of 9 or 10 (8 or 9 NextInt draws)",
                   "re:UnstableShuffle")],
    mechanics=["relic choice (3 of pool)", "arrival heal"],
    kernel_requirements=ANCIENT_KR + ["EnchantmentModel Swift.CanEnchant"],
    content_deps=dict(relics=["BlessedAntler", "BrilliantScarf", "DelicateFrond", "DiamondDiadem", "FurCoat", "Glitter",
                              "JewelryBox", "LoomingFruit", "SignetRing", "BeautifulBracelet"], enchantments=["Swift"]),
    ascension="A2 WearyTraveler: arrival heal x0.8",
    unlock_progress=[],
    version=V("same", "v0.107.1 used the fully qualified Swift name"),
    tier=2, tier_reason="ancient framework",
    port_batch=1,
    notes=[],
    open_questions=[],
)

DATA["Tanx"] = dict(
    category="act_ancient",
    appearance=dict(pool="act_ancient", allowed_act_indices=[2], is_allowed=None, unlock_gate=None,
                    entry="act-3 ancient (Glory AllAncients)"),
    layout="Ancient",
    options=[
        O("INITIAL", "3 of the pool", "pool = {Claws, Crossbow, IronClub, MeatCleaver, Sai, SpikedGauntlets, "
          "TanxsWhistle, ThrowingAxe, WarHammer} + TriBoomerang when >= 3 Instinct-enchantable deck cards; "
          "UnstableShuffle, take 3", cite="m:GenerateInitialOptions"),
    ],
    outcomes=dict(relics=["one of three Tanx relics"], hp=["arrival heal"]),
    prompts=[],
    rng_streams=[R("EventRng", "GenerateInitialOptions", "UnstableShuffle of 9 or 10", "re:UnstableShuffle")],
    mechanics=["relic choice (3 of pool)", "arrival heal"],
    kernel_requirements=ANCIENT_KR + ["EnchantmentModel Instinct.CanEnchant"],
    content_deps=dict(relics=["Claws", "Crossbow", "IronClub", "MeatCleaver", "Sai", "SpikedGauntlets", "TanxsWhistle",
                              "ThrowingAxe", "WarHammer", "TriBoomerang"], enchantments=["Instinct"]),
    ascension="A2 WearyTraveler: arrival heal x0.8",
    unlock_progress=[],
    version=V("same", "fingerprint equal"),
    tier=2, tier_reason="ancient framework",
    port_batch=1,
    notes=[],
    open_questions=[],
)

DATA["Vakuu"] = dict(
    category="act_ancient",
    appearance=dict(pool="act_ancient", allowed_act_indices=[2], is_allowed=None, unlock_gate=None,
                    entry="act-3 ancient (Glory AllAncients)"),
    layout="Ancient",
    options=[
        O("INITIAL", "slot 1", "UnstableShuffle({BloodSoakedRose, WhisperingEarring, Fiddle})[0]",
          cite="m:GenerateInitialOptions"),
        O("INITIAL", "slot 2", "UnstableShuffle({PreservedFog, SereTalon, DistinguishedCape})[0]", cite="m:Pool2",
          notes="SereTalon option carries ThatDecreasesMaxHp(9) (UI warning)"),
        O("INITIAL", "slot 3", "UnstableShuffle({ChoicesParadox, MusicBox, LordsParasol, JeweledMask})[0]",
          cite="m:Pool3"),
    ],
    outcomes=dict(relics=["one of three Vakuu relics"], hp=["arrival heal"]),
    prompts=[],
    rng_streams=[R("EventRng", "GenerateInitialOptions", "three UnstableShuffles: 2 + 2 + 3 NextInt draws",
                   "m:GenerateInitialOptions")],
    mechanics=["relic choice (3 slots)", "arrival heal"],
    kernel_requirements=ANCIENT_KR,
    content_deps=dict(relics=["BloodSoakedRose", "WhisperingEarring", "Fiddle", "PreservedFog", "SereTalon",
                              "DistinguishedCape", "ChoicesParadox", "MusicBox", "LordsParasol", "JeweledMask"]),
    ascension="A2 WearyTraveler: arrival heal x0.8",
    unlock_progress=["Visits var reads profile AncientStats (dialogue/loc only, Models.Events/Vakuu.cs:49-56)"],
    version=V("same", "fingerprint equal"),
    tier=2, tier_reason="ancient framework",
    port_batch=1,
    notes=[],
    open_questions=[],
)

DATA["Darv"] = dict(
    category="shared_ancient",
    appearance=dict(pool="shared_ancient", allowed_act_indices=[1, 2], is_allowed=None, unlock_gate="DarvEpoch",
                    entry="ModelDb.AllSharedAncients: UnlockState.SharedAncients (DarvEpoch) is shuffled on UpFront and "
                          "split across acts 2..n by UpFront.NextInt(count+1) (Runs/RunManager.cs:4055-4068); the act's "
                          "ancient is then UpFront.NextItem(unlocked act ancients + subset)"),
    layout="Ancient",
    options=[
        O("INITIAL", "2 or 3 drawn sets (+ DustyTome)", "for each valid set in order draw NextItem(set.relics) -> "
          "RelicOption; UnstableShuffle; NextBool: take 2 + DustyTome (SetupForPlayer) else take 3",
          cond="sets: Astrolabe, BlackStar, CallingBell, EmptyCage, PandorasBox (no ClearsPlayerDeck modifier), "
               "RunicPyramid, SneckoEye, Ectoplasm (act index 1), Sozu (act index 1), PhilosophersStone (act index "
               ">= 1), VelvetChoker (act index >= 1)", cite="m:GenerateInitialOptions"),
    ],
    outcomes=dict(relics=["one of 3 Darv relics"], hp=["arrival heal"]),
    prompts=[],
    rng_streams=[R("EventRng", "GenerateInitialOptions", "NextItem per valid set (11 in act 2, 9 in act 3; one draw "
                   "even for single-relic sets), UnstableShuffle (count-1 draws), NextBool", "m:GenerateInitialOptions")],
    mechanics=["relic choice (boss-relic style)", "arrival heal"],
    kernel_requirements=ANCIENT_KR + ["RunState.Modifiers (ClearsPlayerDeck)", "DustyTome.SetupForPlayer",
                                      "shared ancient subset (RunManager.GenerateRooms)"],
    content_deps=dict(relics=["Astrolabe", "BlackStar", "CallingBell", "EmptyCage", "PandorasBox", "RunicPyramid",
                              "SneckoEye", "Ectoplasm", "Sozu", "PhilosophersStone", "VelvetChoker", "DustyTome"]),
    ascension="A2 WearyTraveler: arrival heal x0.8",
    unlock_progress=["DarvEpoch"],
    version=V("rule-change", "v0.107.1 had 9 sets: {Ectoplasm, Sozu} as one act-2 set and {PhilosophersStone, "
              "VelvetChoker} as one act-3-only set; v0.111.0 splits them into 4 single sets and makes "
              "PhilosophersStone/VelvetChoker available from act 2 (CurrentActIndex >= 1)", mech="fingerprint-differs"),
    tier=2, tier_reason="ancient framework",
    port_batch=1,
    notes=["Rng.NextItem draws even for one-element arrays (Random/Rng.cs:213-223), so every valid set costs one draw."],
    open_questions=[],
)

DATA["TheArchitect"] = dict(
    category="final_event",
    appearance=dict(pool="none", allowed_act_indices=[], is_allowed=None, unlock_gate=None,
                    entry="EventRoom(TheArchitect) after the last act's boss (Runs/RunManager.cs:821-872); victory room"),
    layout="Combat",
    options=[
        O("INITIAL", "<continue> per dialogue line", "advance the dialogue (no state change)",
          cond="dialogue drawn by EventRng.NextItem over lines selected by profile wins", cite="m:AdvanceDialogue"),
        O("INITIAL", "PROCEED", "WinRun: attack VFX, RunManager.WinRun() (run won), empty options",
          cite="m:CreateProceedOption"),
    ],
    outcomes=dict(other=["run victory"]),
    prompts=[],
    rng_streams=[
        R("RunRng.Niche", "room entry, CombatLayout", "CreateCreature unique-HP draw for the Architect layout creature",
          "Combat/CombatState.cs:717"),
        R("EventRng", "GenerateInitialOptions / WinRun", "NextItem(dialogue), Shuffle(attack VFX), DivideWildly "
          "(score split): cosmetic only", "m:LoadDialogue"),
    ],
    mechanics=["victory", "dialogue"],
    kernel_requirements=["CombatLayout (EventCombatSynchronizer.InitializeForEvent)", "RunManager.WinRun / run end",
                         "ScoreUtility.CalculateScore (display)", "profile Progress wins (dialogue choice, cosmetic)"],
    content_deps=dict(encounters=["TheArchitectEventEncounter"], monsters=["Architect"]),
    ascension="A10 DoubleBoss only changes how the room is reached",
    unlock_progress=["dialogue set depends on profile wins (cosmetic)"],
    version=V("cosmetic", "v0.107.1 ended through ActChangeSynchronizer.SetLocalPlayerReady; v0.111.0 calls "
              "RunManager.WinRun() directly and its attack VFX consume the event Rng; both end the run as a win",
              mech="fingerprint-differs"),
    tier=2, tier_reason="combat layout + terminal victory; dialogue has no rule effect",
    port_batch=1,
    notes=["Batch 1 because every complete run ends here (W2 self-play gate); only the terminal victory is needed.",
           "The sim can end the run on entering this room (design-run-layer.md 3.14) but must still perform the "
           "layout Niche draw if anything after it is compared (nothing is: the run ends)."],
    open_questions=["Diff gate: are the dialogue 'continue' clicks recorded as event decisions? If so they need a "
                    "no-op mapping."],
)

DATA["DeprecatedEvent"] = dict(
    category="deprecated",
    port_scope="exclude",
    appearance=dict(pool="none", allowed_act_indices=[], is_allowed=None, unlock_gate=None,
                    entry="SaveUtil.EventOrDeprecated placeholder for unknown saved event ids"),
    layout="Default",
    options=[],
    outcomes={},
    prompts=[],
    rng_streams=[],
    mechanics=["save-compat placeholder (no options: finished at once)"],
    kernel_requirements=[],
    content_deps={},
    ascension="none",
    unlock_progress=[],
    version=V("same", "identical"),
    tier=1, tier_reason="empty",
    port_batch=0,
    notes=["Save importer: map unknown event ids to a finished event (Saves/SaveUtil.cs)."],
    open_questions=[],
)

DATA["DeprecatedAncientEvent"] = dict(
    category="deprecated",
    port_scope="exclude",
    appearance=dict(pool="none", allowed_act_indices=[], is_allowed=None, unlock_gate=None,
                    entry="SaveUtil placeholder for unknown saved ancient ids"),
    layout="Ancient",
    options=[],
    outcomes={},
    prompts=[],
    rng_streams=[],
    mechanics=["save-compat placeholder (no options: StartPreFinished)"],
    kernel_requirements=[],
    content_deps={},
    ascension="none",
    unlock_progress=[],
    version=V("same", "identical"),
    tier=1, tier_reason="empty",
    port_batch=0,
    notes=["DefineDialogues throws NotImplementedException; never begin it."],
    open_questions=[],
)

DATA["MockEventModel"] = dict(
    category="mock",
    port_scope="exclude",
    appearance=dict(pool="none", allowed_act_indices=[], is_allowed=None, unlock_gate=None,
                    entry="tests only (Models.Events.Mocks)"),
    layout="Default",
    options=[O("INITIAL", "TEST x2", "records optionChosen", cite="m:DefaultInitialOptions")],
    outcomes={},
    prompts=[],
    rng_streams=[],
    mechanics=["test mock"],
    kernel_requirements=[],
    content_deps={},
    ascension="none",
    unlock_progress=[],
    version=V("cosmetic", "v0.111.0 adds the IsMock override (test infrastructure only)"),
    tier=1, tier_reason="test mock",
    port_batch=0,
    notes=["Useful as a template for sim framework unit tests (configurable shared flag and option generator)."],
    open_questions=[],
)

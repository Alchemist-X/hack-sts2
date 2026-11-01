# Inventory: relics

2026-11-01 · Route C full port, W0 semantic classification · game v0.111.0 · 299 relic models (300 files; `VakuuCardSelector.cs` is a non-model `ICardSelector` used by WhisperingEarring).

Machine-readable companion: [`sim/specs/inventory/relics.classified.json`](../../sim/specs/inventory/relics.classified.json) (one entry per relic, same order and ids as [`relics.json`](../../sim/specs/inventory/relics.json); every raw field is kept, the added fields are listed in its `added_fields`, tiers and batches in `tier_legend` and `port_batch_legend`).
Rule truth: decompile tree v0.111.0 (`output/server-bootstrap/linux-decompiled`), cited as `<ns-dir>/<File>.cs:<line>` with `MegaCrit.Sts2.Core.` dropped. The v0.107.1 tree (`artifacts/knowledge/decompiled-v0.107.1`, async bodies intact) was used for the version diff and to read bodies of files whose normalised features are identical. Scratch scripts (git-ignored, Python 3 stdlib): `tmp/sim-port/relics-potions/` (`condense.py` state-machine reader, `vdiff.py` normalised feature diff, `annot_*.py` per-model annotations, `build.py` merger, `gen_md.py` this document).

Every relic needs the relic runtime first (W1 deliverable): `RelicModel : AbstractModel` with per-instance state, `PlayerState.Relics` as a combat listener after the player powers (melted relics skipped, `Combat/CombatState.cs:877`) and as a run-scope listener (`Runs/RunState.cs:876`), the `AfterObtained` pickup pipeline (`RelicCmd.Obtain`). That prerequisite is common to all entries and does not raise a tier. `RelicStatus` and `Flash` are display only: no rule reads `Status` except PaelsFlesh, which gates only its own flash (`Models.Relics/PaelsFlesh.cs:76`).

## 1. Summary

|  | Count |
|---|--:|
| tier 1 | 91 |
| tier 2 | 45 |
| tier 3 | 158 |
| tier 4 | 5 |
| combat-time behaviour only | 159 |
| run-time behaviour only | 115 |
| both | 21 |
| no effect (Circlet, DeprecatedRelic, FakeMerchantsRug, WongoCustomerAppreciationBadge) | 4 |
| with a player prompt entry | 43 |
| with persistent state (SavedProperty) | 40 |
| rule change vs v0.107.1 | 21 |
| new in v0.111.0 | 2 |
| already ported (P0) | 0 |

Tiers (`tier_legend`): 1 = combat behaviour through hooks and facades the sim already has (var overloads of ported Cmd methods count as existing; power content from the power wave is a dependency, not a raise); 2 = needs hooks, facades or card/combat-state APIs that exist in the game but not in the sim; 3 = needs a prompt or a new subsystem (orbs, Osty/pets, stars, forge, card generation, combat history, potion slots, extra turns, enchantments) or any run-layer behaviour (the run layer is a new subsystem); 4 = decompile loss or cross-cutting. `tier_combat` rates the combat part alone; `run_complexity` rates the run part (`simple`, `modifier`, `prompt`, `cross-cutting`). An `AfterCombatEnd` override that only resets display state or state that is reset again before it is read (e.g. at `BeforeSideTurnStart`) does not raise the combat tier; the entry note says so.

### 1.1 Tier by pool

| pool (RelicModel.Pool) | relics | tier 1 | tier 2 | tier 3 | tier 4 | combat part | combat tier 1/2/3/4 | run part |
|---|--:|--:|--:|--:|--:|--:|--:|--:|
| IRONCLAD | 8 | 4 | 4 | 0 | 0 | 8 | 4/4/0/0 | 1 |
| SILENT | 8 | 6 | 2 | 0 | 0 | 8 | 6/2/0/0 | 0 |
| DEFECT | 8 | 0 | 2 | 6 | 0 | 8 | 0/2/6/0 | 0 |
| NECROBINDER | 8 | 2 | 3 | 3 | 0 | 8 | 2/3/3/0 | 0 |
| REGENT | 8 | 2 | 0 | 6 | 0 | 8 | 2/0/6/0 | 0 |
| SHARED | 115 | 46 | 18 | 51 | 0 | 76 | 48/20/8/0 | 45 |
| EVENT | 142 | 29 | 16 | 92 | 5 | 64 | 32/19/12/1 | 90 |
| FALLBACK | 1 | 1 | 0 | 0 | 0 | 0 | 0/0/0/0 | 0 |
| DEPRECATED | 1 | 1 | 0 | 0 | 0 | 0 | 0/0/0/0 | 0 |
| **all** | 299 | 91 | 45 | 158 | 5 | 180 | 94/50/35/1 | 136 |

Relics have no act; acts gate only the ancients that offer EVENT-pool Ancient relics (section 2). `RelicModel.Pool` is the first pool of `ModelDb.AllRelicPools` that lists the relic (`Models/RelicModel.cs:192`); LastingCandy, RazorTooth and SparklingRouge are listed in both EventRelicPool and SharedRelicPool and resolve to EVENT, but the shared and player bags are built from pool member lists, so they still drop from bags.

### 1.2 Tier by rarity

| rarity | relics | tier 1 | tier 2 | tier 3 | tier 4 |
|---|--:|--:|--:|--:|--:|
| Starter | 10 | 2 | 2 | 6 | 0 |
| Common | 30 | 11 | 7 | 12 | 0 |
| Uncommon | 40 | 21 | 6 | 13 | 0 |
| Rare | 50 | 19 | 12 | 19 | 0 |
| Shop | 30 | 8 | 4 | 18 | 0 |
| Event | 35 | 16 | 5 | 14 | 0 |
| Ancient | 102 | 12 | 9 | 76 | 5 |
| None | 2 | 2 | 0 | 0 | 0 |

### 1.3 Combat-time versus run-time behaviour

Each entry splits its behaviour into `combat_behaviour` (hooks that fire while a combat exists, including the combat-room `AfterRoomEntered` and the combat-end hooks) and `run_behaviour` (with a `phase`: `pickup`, `offer`, `room_entry`, `combat_end`, `rewards`, `card_reward`, `shop`, `rest`, `map`, `treasure`, `deck_change`, `gold`, `potion`). Run parts by complexity: 57 simple, 31 modifier, 38 prompt, 4 cross-cutting. 5 more have only an in-combat `AfterObtained` path (BeltBuckle, FakeSneckoEye, PaelsEye, RedSkull, SneckoEye), unreachable in single player because no v0.111.0 card, power or monster obtains a relic mid-combat (`RelicCmd.Obtain/Replace/Melt` callers are events, rewards and LargeCapsule, PaelsWing, SwordOfStone, TouchOfOrobas, ToyBox); Pantograph's room-entry hook only sets its status.

Two timing facts drive most of the combat-time list:

- `AfterRoomEntered` for a combat room runs after `CombatManager.SetUpCombat` (combat state reset, piles populated and shuffled with `RunRng.Shuffle`) and before `StartCombatInternal` (creature `AfterCreatureAdded` loop, then `BeforeCombatStart`) (`Rooms/CombatRoom.cs:417-420`, `Combat/CombatManager.cs:6575-6612`, `:4469`, `:4491`). It is a run-scope hook (relics and other run listeners only). 20 relics act there on combat rooms: BronzeScales, BurningSticks, DataDisk, DivineRight, EmberTea, GhostSeed, Girya, Gorget, Metronome, OddlySmoothStone, Permafrost, PhilosophersStone, RedSkull, SlingOfCourage, StoneCalendar, StoneCracker, SwordOfJade, ThrowingAxe, Vajra, VelvetChoker.
- `AfterCombatEnd` (both scope, 40 relics) and `AfterCombatVictory[Early]` (6) run at combat end; the combat-only sim must call them or carry the post-combat state out. Rule-relevant uses: ArtOfWar, CentennialPuzzle, ChosenCheese, FishingRod, JossPaper, PaelsEye, PaelsLegion, PaelsTears, PaelsTooth, PumpkinCandle, RedSkull, RuinedHelmet, ThrowingAxe, ToyBox, WongosMysteryTicket, BurningSticks; the others reset display state or turn state that is reset again before use.

## 2. Relic classes per character

There is no boss relic in v0.111.0: boss rewards are gold, a potion and a card reward (`docs/sim-port/design-run-layer.md` 3.11). The relic step between acts is the ancient event on the first row of the next act, which offers EVENT-pool `Ancient` relics; Neow opens act 1. `RelicRarity` is None, Starter, Common, Uncommon, Rare, Shop, Event, Ancient (`Entities.Relics/RelicRarity.cs`). Merchant price by rarity: Common 175, Uncommon 225, Rare 275, Shop 200, Starter/Event/Ancient not sold (`Models/RelicModel.cs:307-318`); the Fake* event relics override `MerchantCost` to 50. Tradable = not used up, no pickup effect, not melted, no pets, not Starter/Event/Ancient (`Models/RelicModel.cs:194-221`).

| character | starter | starter upgrade (TouchOfOrobas) | common | uncommon | rare | shop | potion pool |
|---|---|---|---|---|---|---|---|
| Ironclad | BurningBlood | BlackBlood | RedSkull (t2) | PaperPhrog (t2), SelfFormingClay (t1) | CharonsAshes (t1), DemonTongue (t1), RuinedHelmet (t2) | Brimstone (t1) | BloodPotion, SoldiersStew, Ashwater |
| Silent | RingOfTheSnake | RingOfTheDrake | SneckoSkull (t1) | Tingsha (t1), TwistedFunnel (t1) | HelicalDart (t1), PaperKrane (t2), ToughBandages (t1) | NinjaScroll (t2) | PoisonPotion, GhostInAJar, CunningPotion |
| Defect | CrackedCore | InfusedCore | DataDisk (t2) | GoldPlatedCables (t3), SymbioticVirus (t3) | EmotionChip (t3), Metronome (t3), PowerCell (t2) | RunicCapacitor (t3) | FocusPotion, EssenceOfDarkness, PotionOfCapacity |
| Necrobinder | BoundPhylactery | PhylacteryUnbound | BoneFlute (t3) | BookRepairKnife (t2), FuneraryMask (t1) | BigHat (t3), Bookmark (t2), IvoryTile (t2) | UndyingSigil (t1) | PotionOfDoom, PotOfGhouls, BoneBrew |
| Regent | DivineRight | DivineDestiny | FencingManual (t3) | GalacticDust (t3), Regalite (t1) | LunarPastry (t3), MiniRegent (t3), OrangeDough (t3) | VitruvianMinion (t1) | StarPotion, CosmicConcoction, KingsCourage |

Character relic pools are filtered by `<Char>3Epoch`/`<Char>6Epoch` unlocks, the shared pool by Relic1-5 epochs (`pools.json` `unlock_gates`). The player relic bag holds shared + character pool relics of rarity Common, Uncommon, Rare and Shop (one deque per rarity, `UnstableShuffle` on `RunRng.UpFront`); elite/event/relic rewards pull from the front with a `PlayerRng.Rewards` rarity roll (`NextFloat` < 0.5 Common, < 0.83 Uncommon, else Rare, `Factories/RelicFactory.cs:54-63`), shops from the back; the shared bag (treasure rooms) holds the whole shared pool without a rarity filter, which is how the Ancient-rarity LoomingFruit and VeryHotCocoa and the Event-rarity FresnelLens can come from chests (`design-run-layer.md` 5.4).

Shared pool (118 relics):

| rarity | n | relics (tier) |
|---|---|---|
| Common | 25 | AmethystAubergine (t3), Anchor (t1), BagOfMarbles (t1), BagOfPreparation (t1), BloodVial (t1), BookOfFiveRings (t3), BronzeScales (t2), CentennialPuzzle (t2), FestivePopper (t1), Gorget (t2), HappyFlower (t1), JuzuBracelet (t3), Lantern (t1), MealTicket (t3), OddlySmoothStone (t2), Pendulum (t1), PotionBelt (t3), RedMask (t1), RegalPillow (t3), Strawberry (t3), StrikeDummy (t1), Vajra (t2), VenerableTeaSet (t3), WarPaint (t3), Whetstone (t3) |
| Uncommon | 30 | Akabeko (t1), BowlerHat (t3), Candelabra (t1), EternalFeather (t3), GremlinHorn (t1), HornCleat (t1), JossPaper (t2), Kusarigama (t1), LastingCandy (t3), LetterOpener (t1), LuckyFysh (t3), MercuryHourglass (t1), MiniatureCannon (t1), Nunchaku (t1), Orichalcum (t1), OrnamentalFan (t1), Pantograph (t2), ParryingShield (t1), Pear (t3), PenNib (t1), Permafrost (t2), PetrifiedToad (t3), Planisphere (t3), ReptileTrinket (t3), RippleBasin (t3), SparklingRouge (t1), StoneCracker (t2), TinyMailbox (t3), TuningFork (t1), Vambrace (t1) |
| Rare | 35 | ArtOfWar (t2), BeatingRemnant (t1), Bellows (t2), CaptainsWheel (t1), Chandelier (t1), CloakClasp (t1), FrozenEgg (t3), GamblingChip (t3), GamePiece (t1), Girya (t3), IceCream (t1), IntimidatingHelmet (t2), Kunai (t1), LizardTail (t1), Mango (t3), MeatOnTheBone (t2), MoltenEgg (t3), MummifiedHand (t2), OldCoin (t3), Pocketwatch (t1), PrayerWheel (t3), RainbowRing (t1), RazorTooth (t2), Shovel (t3), Shuriken (t1), StoneCalendar (t1), SturdyClamp (t1), TheCourier (t3), ToxicEgg (t3), TungstenRod (t1), UnceasingTop (t1), UnsettlingLamp (t2), VexingPuzzlebox (t3), WhiteBeastStatue (t3), WhiteStar (t3) |
| Shop | 25 | BeltBuckle (t3), Bread (t1), BurningSticks (t2), Cauldron (t3), ChemicalX (t1), DingyRug (t3), DollysMirror (t3), DragonFruit (t3), GhostSeed (t2), GnarledHammer (t3), Kifuda (t3), LavaLamp (t3), LeesWaffle (t3), MembershipCard (t3), MiniatureTent (t3), MysticLighter (t3), Orrery (t3), PunchDagger (t3), RingingTriangle (t1), RoyalStamp (t3), ScreamingFlagon (t1), SlingOfCourage (t2), TheAbacus (t1), Toolbox (t3), WingCharm (t3) |
| Event | 1 | FresnelLens (t3) |
| Ancient | 2 | LoomingFruit (t3), VeryHotCocoa (t1) |

Ancient relics by offering ancient (`events.json` `relic_options` and code references; Neow's pool is the act-1 option list, see `design-run-layer.md` 3.11):

| ancient | act / pool | n | tier 1/2/3/4 | relics (tier) |
|---|---|---|---|---|
| Neow | Overgrowth, Underdocks | 30 | 0/1/29/0 | ArcaneScroll (t3), BoomingConch (t2), CursedPearl (t3), DowsingRod (t3), FishingRod (t3), GoldenPearl (t3), HeftyTablet (t3), Kaleidoscope (t3), LargeCapsule (t3), LavaRock (t3), LeadPaperweight (t3), LeafyPoultice (t3), LostCoffer (t3), MassiveScroll (t3), NeowsBones (t3), NeowsSacrifice (t3), NeowsTalisman (t3), NeowsTorment (t3), NewLeaf (t3), NutritiousOyster (t3), PhialHolster (t3), Pomander (t3), PrecariousShears (t3), PreciseScissors (t3), ScrollBoxes (t3), SilkenTress (t3), SilverCrucible (t3), SmallCapsule (t3), StoneHumidifier (t3), WingedBoots (t3) |
| Orobas | Hive | 10 | 0/1/8/1 | AlchemicalCoffer (t3), ArchaicTooth (t3), Driftwood (t3), ElectricShrymp (t3), GlassEye (t4), PrismaticGem (t3), RadiantPearl (t2), SandCastle (t3), SeaGlass (t3), TouchOfOrobas (t3) |
| Pael | Hive | 10 | 2/1/7/0 | PaelsBlood (t1), PaelsClaw (t3), PaelsEye (t3), PaelsFlesh (t1), PaelsGrowth (t3), PaelsHorn (t3), PaelsLegion (t3), PaelsTears (t2), PaelsTooth (t3), PaelsWing (t3) |
| Tezcatara | Hive | 9 | 0/1/7/1 | BiiigHug (t3), GoldenCompass (t4), NutritiousSoup (t3), PumpkinCandle (t3), SealOfGold (t2), Storybook (t3), ToastyMittens (t3), ToyBox (t3), YummyCookie (t3) |
| Nonupeipe | Glory | 9 | 2/1/5/1 | BeautifulBracelet (t3), BlessedAntler (t2), BrilliantScarf (t1), DelicateFrond (t3), DiamondDiadem (t1), FurCoat (t4), Glitter (t3), JewelryBox (t3), SignetRing (t3) |
| Tanx | Glory | 10 | 3/1/6/0 | Claws (t3), Crossbow (t3), IronClub (t1), MeatCleaver (t3), Sai (t1), SpikedGauntlets (t1), TanxsWhistle (t3), ThrowingAxe (t2), TriBoomerang (t3), WarHammer (t3) |
| Vakuu | Glory | 10 | 1/2/5/2 | BloodSoakedRose (t3), ChoicesParadox (t3), DistinguishedCape (t3), Fiddle (t1), JeweledMask (t2), LordsParasol (t4), MusicBox (t2), PreservedFog (t3), SereTalon (t3), WhisperingEarring (t4) |
| Darv | AllSharedAncients | 12 | 3/1/8/0 | Astrolabe (t3), BlackStar (t3), CallingBell (t3), DustyTome (t3), Ectoplasm (t3), EmptyCage (t3), PandorasBox (t3), PhilosophersStone (t2), RunicPyramid (t1), SneckoEye (t1), Sozu (t3), VelvetChoker (t1) |

Event relics by granting event:

| event | n | relics (rarity, tier) |
|---|---|---|
| ColossalFlower | 1 | PollinousCore (Event, t1) |
| DollRoom | 3 | BingBong (Event, t3), DaughterOfTheWind (Event, t1), MrStruggles (Event, t1) |
| FakeMerchant | 10 | FakeAnchor (Event, t1), FakeBloodVial (Event, t1), FakeHappyFlower (Event, t1), FakeLeesWaffle (Event, t3), FakeMango (Event, t3), FakeMerchantsRug (Event, t1), FakeOrichalcum (Event, t1), FakeSneckoEye (Event, t1), FakeStrikeDummy (Event, t1), FakeVenerableTeaSet (Event, t3) |
| GraveOfTheForgotten | 1 | ForgottenSoul (Event, t1) |
| HungryForMushrooms | 2 | BigMushroom (Event, t3), FragrantMushroom (Event, t3) |
| LostWisp | 1 | LostWisp (Event, t1) |
| RoomFullOfCheese | 1 | ChosenCheese (Event, t2) |
| RoundTeaParty | 1 | RoyalPoison (Event, t1) |
| SunkenStatue | 1 | SwordOfStone (Event, t3) |
| TeaMaster | 3 | BoneTea (Event, t2), EmberTea (Event, t2), TeaOfDiscourtesy (Event, t2) |
| TrashHeap | 5 | DarkstonePeriapt (Event, t3), DreamCatcher (Event, t3), HandDrill (Event, t1), MawBank (Event, t3), TheBoot (Event, t1) |
| WarHistorianRepy | 1 | HistoryCourse (Event, t3) |
| WelcomeToWongos | 2 | WongosMysteryTicket (Event, t3), WongoCustomerAppreciationBadge (Event, t1) |

Other EVENT-pool relics: the five Starter-rarity upgrades BlackBlood, DivineDestiny, InfusedCore, PhylacteryUnbound, RingOfTheDrake come only from TouchOfOrobas (`RelicCmd.Replace`); SwordOfJade replaces SwordOfStone after 5 elites; Byrdpip is obtained by the Hatch rest-site option (`Entities.RestSite/HatchRestSiteOption.cs:52`); LastingCandy, RazorTooth and SparklingRouge are also SharedRelicPool members and drop from the bags.

Fallback: Circlet (`RelicFactory.FallbackRelic`, stackable; also the RandomCharacter placeholder starter). Deprecated: DeprecatedRelic (save compatibility).

Availability gates: 20 relics override `IsAllowed` (17 of them call `IsBeforeAct3TreasureChest`: TotalFloor < 41 in single player, `Models/RelicModel.cs:539-543`, LastingCandy also excludes an Ironclad first-ever run; MassiveScroll multiplayer only; SilverCrucible and WingedBoots single player only). `IsAllowedAtNeow`: Kaleidoscope (every character card pool unlocked), ScrollBoxes (>=4 Commons and >=2 Uncommons unlocked). `IsAllowedInShops` false: AmethystAubergine, BowlerHat, LuckyFysh, OldCoin, TheCourier.

## 3. Port batches

| batch | contents | n | relics |
|---|---|---|---|
| 1 | combat, tier 1, only ported powers (Strength, Dexterity, Vulnerable, Weak, Thorns, Frail, Shrink) or none | 80 | Anchor, BagOfMarbles, BagOfPreparation, BeatingRemnant, BloodVial, Bread, BrilliantScarf, Brimstone, Candelabra, CaptainsWheel, Chandelier, CharonsAshes, ChemicalX, Circlet, CloakClasp, DaughterOfTheWind, DemonTongue, FakeAnchor, FakeBloodVial, FakeHappyFlower, FakeMerchantsRug, FakeOrichalcum, FakeStrikeDummy, FestivePopper, Fiddle, ForgottenSoul, GamePiece, GremlinHorn, HandDrill, HappyFlower, HornCleat, IceCream, IronClub, Kunai, Kusarigama, Lantern, LetterOpener, LizardTail, LostWisp, MercuryHourglass, MiniatureCannon, MrStruggles, Nunchaku, Orichalcum, OrnamentalFan, PaelsBlood, PaelsFlesh, ParryingShield, Pendulum, PenNib, Pocketwatch, PollinousCore, RainbowRing, RedMask, Regalite, RingingTriangle, RingOfTheDrake, RingOfTheSnake, RoyalPoison, RunicPyramid, Sai, ScreamingFlagon, Shuriken, SparklingRouge, SpikedGauntlets, StoneCalendar, StrikeDummy, SturdyClamp, TheAbacus, TheBoot, Tingsha, ToughBandages, TungstenRod, TuningFork, UnceasingTop, Vambrace, VelvetChoker, VeryHotCocoa, VitruvianMinion, WongoCustomerAppreciationBadge |
| 2 | combat, tier 1, needs power/card content from earlier content batches (Vigor, Plating, Poison, Blur, Confused, Soul, ...) | 10 | Akabeko, DiamondDiadem, FakeSneckoEye, FuneraryMask, HelicalDart, SelfFormingClay, SneckoEye, SneckoSkull, TwistedFunnel, UndyingSigil |
| 3 | combat, tier 2 (missing hooks/facades: combat-end hooks, combat-room AfterRoomEntered, in-combat upgrade/keyword/clone, room type, gold) | 45 | ArtOfWar, Bellows, BlackBlood, BlessedAntler, BoneTea, Bookmark, BookRepairKnife, BoomingConch, BronzeScales, BurningBlood, BurningSticks, CentennialPuzzle, ChosenCheese, DataDisk, EmberTea, GhostSeed, Gorget, IntimidatingHelmet, IvoryTile, JeweledMask, JossPaper, MeatOnTheBone, MummifiedHand, MusicBox, NinjaScroll, OddlySmoothStone, PaelsTears, Pantograph, PaperKrane, PaperPhrog, Permafrost, PhilosophersStone, PowerCell, RadiantPearl, RazorTooth, RedSkull, RuinedHelmet, SealOfGold, SlingOfCourage, StoneCracker, SwordOfJade, TeaOfDiscourtesy, ThrowingAxe, UnsettlingLamp, Vajra |
| 4 | combat, tier 3 subsystems except character mechanics (card generation, combat history, auto-play, extra turns, potion slots, enchantment reads) | 12 | BeltBuckle, BigHat, Crossbow, DelicateFrond, HistoryCourse, MysticLighter, OrangeDough, PaelsEye, PetrifiedToad, ReptileTrinket, RippleBasin, VexingPuzzlebox |
| 5 | character mechanics (orbs, Osty/pets, stars, forge); port with the character subsystem | 18 | BoneFlute, BoundPhylactery, Byrdpip, CrackedCore, DivineDestiny, DivineRight, EmotionChip, FencingManual, GalacticDust, GoldPlatedCables, InfusedCore, LunarPastry, Metronome, MiniRegent, PaelsLegion, PhylacteryUnbound, RunicCapacitor, SymbioticVirus |
| 6 | in-combat prompts (turn-setup prompt sites) | 4 | ChoicesParadox, GamblingChip, ToastyMittens, Toolbox |
| 7 | run layer, simple effects (pickup, room entry, combat end): max HP, heal, gold, potion slots, fixed cards, curses, random upgrades/transforms/enchants | 52 | AlchemicalCoffer, ArchaicTooth, BeautifulBracelet, BigMushroom, BingBong, BloodSoakedRose, BookOfFiveRings, CursedPearl, DarkstonePeriapt, DistinguishedCape, DowsingRod, DragonFruit, DustyTome, EternalFeather, FakeLeesWaffle, FakeMango, FakeVenerableTeaSet, FishingRod, FragrantMushroom, GoldenPearl, JewelryBox, LeafyPoultice, LeesWaffle, LoomingFruit, LuckyFysh, Mango, MawBank, MealTicket, NeowsSacrifice, NeowsTalisman, NeowsTorment, NutritiousOyster, NutritiousSoup, OldCoin, PaelsClaw, PaelsHorn, PandorasBox, Pear, PhialHolster, Planisphere, PotionBelt, SandCastle, SereTalon, SignetRing, Storybook, Strawberry, TanxsWhistle, TouchOfOrobas, VenerableTeaSet, WarHammer, WarPaint, Whetstone |
| 8 | run layer, modifiers and generated offers: rewards, card reward options, shop, rest site options, map, relic bags, reward screens | 49 | AmethystAubergine, ArcaneScroll, BlackStar, BowlerHat, CallingBell, Cauldron, DingyRug, DreamCatcher, Driftwood, Ectoplasm, FresnelLens, FrozenEgg, Girya, GlassEye, Glitter, JuzuBracelet, Kaleidoscope, LargeCapsule, LastingCandy, LavaLamp, LavaRock, LostCoffer, MeatCleaver, MembershipCard, MiniatureTent, MoltenEgg, NeowsBones, Orrery, PaelsWing, PrayerWheel, PrismaticGem, PumpkinCandle, RegalPillow, Shovel, SilkenTress, SilverCrucible, SmallCapsule, Sozu, StoneHumidifier, SwordOfStone, TheCourier, TinyMailbox, ToxicEgg, ToyBox, WhiteBeastStatue, WhiteStar, WingedBoots, WingCharm, WongosMysteryTicket |
| 9 | run layer, deck prompts (remove/transform/upgrade/enchant/generic, choose card/bundle, simple grid) | 23 | Astrolabe, BiiigHug, Claws, DollysMirror, ElectricShrymp, EmptyCage, GnarledHammer, HeftyTablet, Kifuda, LeadPaperweight, NewLeaf, PaelsGrowth, PaelsTooth, Pomander, PrecariousShears, PreciseScissors, PreservedFog, PunchDagger, RoyalStamp, ScrollBoxes, SeaGlass, TriBoomerang, YummyCookie |
| 10 | tier 4, cross-cutting, multiplayer-only or deprecated | 6 | DeprecatedRelic, FurCoat, GoldenCompass, LordsParasol, MassiveScroll, WhisperingEarring |

Batches follow the W3 content order (powers, cards, monsters, then relics). A model is ported once, so a relic with combat and run parts sits in the later of the two batches; batch 1-6 relics can be ported as soon as W1 lands, batches 7-9 need the W2 run layer. Starter relics are spread by dependency: RingOfTheSnake in 1, BurningBlood in 3, CrackedCore, BoundPhylactery and DivineRight in 5; a run-capable sim needs all five, plus the five TouchOfOrobas upgrades, before any character can play a full run.

## 4. Dependency order

### 4.1 Kernel features that must precede the relics

From `kernel_requirements` (the relic runtime itself, needed by all, is not repeated). Three groups: hook dispatch, Cmd facades, and subsystems or model APIs. Labels with one user are in the JSON only, except hooks.

**Hook dispatch (missing in the sim)**

| requirement | relics | which |
|---|---|---|
| Hook.AfterCombatEnd (both) | 40 | ArtOfWar, BeltBuckle, BrilliantScarf, BurningSticks, CentennialPuzzle, ChosenCheese, EmotionChip, FakeHappyFlower, FishingRod, HappyFlower, JossPaper, Kunai, Kusarigama, LetterOpener, Metronome, MiniRegent, MusicBox, OrnamentalFan, PaelsEye, PaelsFlesh, PaelsLegion, PaelsTears, PaelsTooth, Pendulum, Pocketwatch, PollinousCore, PumpkinCandle, RainbowRing, RedSkull, Regalite, RippleBasin, RuinedHelmet, Shuriken, StoneCalendar, ThrowingAxe, ToyBox, UnsettlingLamp, Vambrace, VelvetChoker, WongosMysteryTicket |
| Hook.AfterRoomEntered (run) | 33 | BigMushroom, BronzeScales, BurningSticks, DataDisk, DivineRight, EmberTea, EternalFeather, FakeVenerableTeaSet, GhostSeed, Girya, Gorget, LavaLamp, LordsParasol, MawBank, MealTicket, Metronome, OddlySmoothStone, Pantograph, Permafrost, PhilosophersStone, Planisphere, RedSkull, RegalPillow, SilverCrucible, SlingOfCourage, StoneCalendar, StoneCracker, SwordOfJade, ThrowingAxe, Vajra, VelvetChoker, VenerableTeaSet, WingedBoots |
| Hook.TryModifyCardRewardOptionsLate (run) | 9 | FresnelLens, FrozenEgg, Glitter, LavaLamp, MoltenEgg, SilkenTress, SilverCrucible, ToxicEgg, WingCharm |
| Hook.TryModifyRewards (run) | 6 | AmethystAubergine, BlackStar, LavaRock, PrayerWheel, WhiteStar, WongosMysteryTicket |
| Hook.AfterCombatVictory (both) | 5 | BeltBuckle, BlackBlood, BurningBlood, SwordOfStone, WarHammer |
| Hook.TryModifyRestSiteOptions (run) | 5 | Girya, MeatCleaver, PaelsGrowth, PumpkinCandle, Shovel |
| Hook.AfterCardChangedPiles (both) | 4 | BingBong, BookOfFiveRings, DarkstonePeriapt, LuckyFysh |
| Hook.ModifyExtraRestSiteHealText (run) | 4 | DreamCatcher, RegalPillow, StoneHumidifier, TinyMailbox |
| Hook.ModifyMerchantCardCreationResults (run) | 4 | FresnelLens, FrozenEgg, MoltenEgg, ToxicEgg |
| Hook.TryModifyCardBeingAddedToDeck (run) | 4 | FresnelLens, FrozenEgg, MoltenEgg, ToxicEgg |
| Hook.AfterModifyingCardRewardOptions (run) | 2 | SilkenTress, SilverCrucible |
| Hook.AfterModifyingGoldGained (both) | 2 | BowlerHat, Ectoplasm |
| Hook.AfterModifyingRewards (run) | 2 | AmethystAubergine, WongosMysteryTicket |
| Hook.AfterPotionUsed (both) | 2 | BeltBuckle, ReptileTrinket |
| Hook.AfterRestSiteHeal (run) | 2 | RegalPillow, StoneHumidifier |
| Hook.AfterStarsSpent (combat) | 2 | GalacticDust, MiniRegent |
| Hook.ModifyCardRewardCreationOptions (run) | 2 | DingyRug, PrismaticGem |
| Hook.ModifyGoldGained (both) | 2 | BowlerHat, Ectoplasm |
| Hook.ModifyMerchantPrice (run) | 2 | MembershipCard, TheCourier |
| Hook.ModifyUnknownMapPointRoomTypes (run) | 2 | GoldenCompass, JuzuBracelet |
| Hook.TryModifyRestSiteHealRewards (run) | 2 | DreamCatcher, TinyMailbox |
| Hook.AfterAutoPrePlayPhaseEntered (combat) | 1 | HistoryCourse |
| Hook.AfterAutoPrePlayPhaseEnteredLate (combat) | 1 | WhisperingEarring |
| Hook.AfterCombatVictoryEarly (both) | 1 | MeatOnTheBone |
| Hook.AfterDiedToDoom (combat) | 1 | BookRepairKnife |
| Hook.AfterGoldGained (run) | 1 | DragonFruit |
| Hook.AfterItemPurchased (run) | 1 | MawBank |
| Hook.AfterModifyingOrbPassiveTriggerCount (combat) | 1 | GoldPlatedCables |
| Hook.AfterOrbChanneled (combat) | 1 | Metronome |
| Hook.AfterPotionDiscarded (both) | 1 | BeltBuckle |
| Hook.AfterPotionProcured (both) | 1 | BeltBuckle |
| Hook.BeforeCombatRewardOffered (run) | 1 | LastingCandy |
| Hook.ModifyCardRewardCreationOptions + TryModifyCardRewardOptions run inside CreateForReward | 1 | ArcaneScroll |
| Hook.ModifyGeneratedMap (run) | 1 | GoldenCompass |
| Hook.ModifyGeneratedMapLate (run) | 1 | FurCoat |
| Hook.ModifyOrbPassiveTriggerCounts (combat) | 1 | GoldPlatedCables |
| Hook.ModifyOrbValue (combat) | 1 | InfusedCore |
| Hook.ModifyRestSiteHealAmount (run) | 1 | RegalPillow |
| Hook.ShouldAllowFreeTravel (run) | 1 | WingedBoots |
| Hook.ShouldDisableRemainingRestSiteOptions (run) | 1 | MiniatureTent |
| Hook.ShouldForcePotionReward (run) | 1 | WhiteBeastStatue |
| Hook.ShouldGenerateTreasure (run) | 1 | SilverCrucible |
| Hook.ShouldProcurePotion (both) | 1 | Sozu |
| Hook.ShouldRefillMerchantEntry (run) | 1 | TheCourier |
| Hook.TryModifyCardRewardAlternatives (run) | 1 | PaelsWing |
| Hook.TryModifyCardRewardOptions (run) | 1 | LastingCandy |
| Hook.TryModifyRewardsLate (run) | 1 | Driftwood |

**Cmd facades (missing or partial in the sim; var overloads included)**

| requirement | relics | which |
|---|---|---|
| RelicCmd.Obtain pipeline (RelicModel.AfterObtained) | 84 | AlchemicalCoffer, ArcaneScroll, ArchaicTooth, Astrolabe, BeautifulBracelet, BeltBuckle, BigMushroom, BiiigHug, BloodSoakedRose, Byrdpip, CallingBell, Cauldron, Claws, CursedPearl, DistinguishedCape, DollysMirror, DowsingRod, DustyTome, ElectricShrymp, EmptyCage, FakeLeesWaffle, FakeMango, FakeSneckoEye, FragrantMushroom, FurCoat, GlassEye, GnarledHammer, GoldenCompass, GoldenPearl, HeftyTablet, JewelryBox, Kaleidoscope, Kifuda, LargeCapsule, LeadPaperweight, LeafyPoultice, LeesWaffle, LoomingFruit, LostCoffer, Mango, MassiveScroll, NeowsBones, NeowsSacrifice, NeowsTalisman, NeowsTorment, NewLeaf, NutritiousOyster, NutritiousSoup, OldCoin, Orrery, PaelsClaw, PaelsEye, PaelsGrowth, PaelsHorn, PaelsLegion, PaelsTooth, PandorasBox, Pear, PhialHolster, Pomander, PotionBelt, PrecariousShears, PreciseScissors, PreservedFog, PumpkinCandle, PunchDagger, RoyalStamp, SandCastle, ScrollBoxes, SeaGlass, SereTalon, SignetRing, SilkenTress, SmallCapsule, SneckoEye, Storybook, Strawberry, TanxsWhistle, TouchOfOrobas, ToyBox, TriBoomerang, WarPaint, Whetstone, YummyCookie |
| CardCmd.Upgrade | 23 | ArchaicTooth, Astrolabe, Bellows, BoneTea, Claws, DustyTome, FishingRod, FragrantMushroom, FrozenEgg, LavaLamp, MoltenEgg, NeowsTalisman, PaelsTooth, Pomander, RazorTooth, SandCastle, SilverCrucible, StoneCracker, ToxicEgg, WarHammer, WarPaint, Whetstone, YummyCookie |
| CardPileCmd.Add (partial in sim) | 23 | ArcaneScroll, BingBong, DistinguishedCape, DollysMirror, DowsingRod, DustyTome, HeftyTablet, JeweledMask, JewelryBox, LargeCapsule, LeadPaperweight, MassiveScroll, NeowsBones, NeowsSacrifice, NeowsTorment, PaelsHorn, PaelsTooth, PowerCell, ScrollBoxes, SeaGlass, SereTalon, Storybook, TanxsWhistle |
| CreatureCmd.GainBlock(BlockVar) overload | 17 | Anchor, BoneFlute, CaptainsWheel, DaughterOfTheWind, DiamondDiadem, FakeAnchor, FakeOrichalcum, HornCleat, IntimidatingHelmet, Orichalcum, OrnamentalFan, Permafrost, Regalite, Sai, TheAbacus, ToughBandages, TuningFork |
| CardCmd.Enchant | 16 | ArchaicTooth, BeautifulBracelet, Claws, ElectricShrymp, FresnelLens, Glitter, GnarledHammer, Kifuda, NutritiousSoup, PaelsClaw, PaelsGrowth, PunchDagger, RoyalStamp, SilkenTress, TriBoomerang, WingCharm |
| CreatureCmd.Damage(DamageVar) overload | 12 | FestivePopper, ForgottenSoul, Kusarigama, LetterOpener, MercuryHourglass, Metronome, ParryingShield, PrecariousShears, RoyalPoison, ScreamingFlagon, StoneCalendar, Tingsha |
| RewardsCmd.OfferCustom | 8 | CallingBell, Cauldron, GlassEye, Kaleidoscope, LostCoffer, Orrery, SmallCapsule, ToyBox |
| CardSelectCmd.FromDeckForEnchantment | 7 | ElectricShrymp, GnarledHammer, Kifuda, PaelsGrowth, PunchDagger, RoyalStamp, TriBoomerang |
| PlayerCmd.GainGold | 7 | BowlerHat, CursedPearl, GoldenPearl, LuckyFysh, MawBank, OldCoin, SignetRing |
| CardPileCmd.RemoveFromDeck | 6 | BiiigHug, EmptyCage, PaelsTooth, PrecariousShears, PreciseScissors, PreservedFog |
| CardSelectCmd.FromDeckForRemoval | 6 | BiiigHug, EmptyCage, PaelsTooth, PrecariousShears, PreciseScissors, PreservedFog |
| CardCmd.Transform | 5 | ArchaicTooth, Astrolabe, Claws, LeafyPoultice, PandorasBox |
| CardPileCmd.AddGeneratedCardsToCombat | 5 | BigHat, BlessedAntler, Crossbow, OrangeDough, RadiantPearl |
| PotionCmd.TryToProcure | 5 | AlchemicalCoffer, DelicateFrond, NeowsSacrifice, PetrifiedToad, PhialHolster |
| CardPileCmd.AddCurseToDeck | 4 | BloodSoakedRose, CallingBell, CursedPearl, PreservedFog |
| CardSelectCmd.FromChooseACardScreen | 4 | HeftyTablet, LeadPaperweight, MassiveScroll, Toolbox |
| CardCmd.ApplyKeyword | 3 | ChoicesParadox, GhostSeed, MusicBox |
| CardSelectCmd.FromDeckForTransformation | 3 | Astrolabe, Claws, NewLeaf |
| OrbCmd.Channel | 3 | CrackedCore, InfusedCore, SymbioticVirus |
| PlayerCmd.GainMaxPotionCount | 3 | AlchemicalCoffer, PhialHolster, PotionBelt |
| PlayerCmd.GainStars | 3 | DivineDestiny, DivineRight, LunarPastry |
| CardCmd.AutoPlay | 2 | HistoryCourse, WhisperingEarring |
| CardSelectCmd.FromDeckForUpgrade | 2 | Pomander, YummyCookie |
| OstyCmd.Summon | 2 | BoundPhylactery, PhylacteryUnbound |
| PlayerCmd.AddPet | 2 | Byrdpip, PaelsLegion |
| PlayerCmd.LoseGold | 2 | SealOfGold, SilkenTress |
| RelicCmd.Obtain | 2 | LargeCapsule, PaelsWing |
| RelicCmd.Replace | 2 | SwordOfStone, TouchOfOrobas |

**Subsystems and model APIs**

| requirement | relics | which |
|---|---|---|
| side-turn hook participants (parameter dropped in P0) | 55 | Akabeko, ArtOfWar, BagOfMarbles, BeatingRemnant, BigHat, BoomingConch, Bread, BrilliantScarf, Brimstone, Candelabra, Chandelier, CloakClasp, CrackedCore, Crossbow, DemonTongue, DiamondDiadem, DivineDestiny, FakeHappyFlower, FakeOrichalcum, FencingManual, HappyFlower, InfusedCore, JossPaper, Kunai, Kusarigama, Lantern, LetterOpener, LunarPastry, MiniRegent, MusicBox, OrangeDough, Orichalcum, OrnamentalFan, PaelsEye, PaelsFlesh, PaelsLegion, PaelsTears, ParryingShield, PhylacteryUnbound, Pocketwatch, PowerCell, RainbowRing, RedMask, Regalite, RippleBasin, RunicCapacitor, Sai, ScreamingFlagon, SealOfGold, Shuriken, StoneCalendar, SymbioticVirus, TwistedFunnel, VelvetChoker, VeryHotCocoa |
| CardFactory.CreateForReward / CardCreationOptions (reward card generation, CardRarityOdds) | 25 | ArcaneScroll, DingyRug, DreamCatcher, FresnelLens, FrozenEgg, GlassEye, Glitter, HeftyTablet, Kaleidoscope, LastingCandy, LavaLamp, LeadPaperweight, LostCoffer, MassiveScroll, MoltenEgg, Orrery, PrayerWheel, PrismaticGem, ScrollBoxes, SeaGlass, SilkenTress, SilverCrucible, ToxicEgg, WhiteStar, WingCharm |
| combat-room entry phase: Hook.AfterRoomEntered after CombatManager.SetUpCombat, before StartCombatInternal (Rooms/CombatRoom.cs:417-420) | 20 | BronzeScales, BurningSticks, DataDisk, DivineRight, EmberTea, GhostSeed, Girya, Gorget, Metronome, OddlySmoothStone, Permafrost, PhilosophersStone, RedSkull, SlingOfCourage, StoneCalendar, StoneCracker, SwordOfJade, ThrowingAxe, Vajra, VelvetChoker |
| room context (RunState.CurrentRoom.RoomType) | 15 | AmethystAubergine, BlackStar, BoomingConch, DreamCatcher, FishingRod, GoldenCompass, JuzuBracelet, LavaLamp, LavaRock, Pantograph, PrayerWheel, SlingOfCourage, SwordOfStone, WarHammer, WhiteStar |
| card clones/dupes (CardModel.CreateClone, RunState.CloneCard, CreateDupe) | 14 | BingBong, BurningSticks, DollysMirror, FresnelLens, FrozenEgg, Glitter, HistoryCourse, LavaLamp, MoltenEgg, MusicBox, SilkenTress, SilverCrucible, ToxicEgg, WingCharm |
| rest site (RestSiteOption, rest heal) | 13 | DreamCatcher, EternalFeather, FakeVenerableTeaSet, Girya, MeatCleaver, MiniatureTent, PaelsGrowth, PumpkinCandle, RegalPillow, Shovel, StoneHumidifier, TinyMailbox, VenerableTeaSet |
| gold (PlayerCmd.GainGold/LoseGold, Hook.ModifyGoldGained) | 11 | AmethystAubergine, BowlerHat, CursedPearl, Ectoplasm, GoldenPearl, LuckyFysh, MawBank, OldCoin, SealOfGold, SignetRing, SilkenTress |
| relic grab bags and RelicCmd (RelicFactory.PullNextRelicFromFront) | 11 | BlackStar, CallingBell, LargeCapsule, LavaRock, NeowsBones, PaelsWing, SmallCapsule, SwordOfStone, TouchOfOrobas, ToyBox, WongosMysteryTicket |
| Osty/pets (OstyCmd.Summon, PlayerCmd.AddPet, Creature.PetOwner) | 9 | BoneFlute, BoundPhylactery, Byrdpip, HandDrill, PaelsLegion, PenNib, PhylacteryUnbound, RegalPillow, TheBoot |
| Stars (PlayerCombatState.Stars, star costs) | 8 | BrilliantScarf, ChemicalX, DivineDestiny, DivineRight, GalacticDust, LunarPastry, MiniRegent, MummifiedHand |
| Defect orbs (OrbQueue, OrbCmd) | 7 | CrackedCore, EmotionChip, GoldPlatedCables, InfusedCore, Metronome, RunicCapacitor, SymbioticVirus |
| potion slots and procurement (PotionCmd.TryToProcure, PotionFactory) | 7 | AlchemicalCoffer, BeltBuckle, DelicateFrond, NeowsSacrifice, PetrifiedToad, PhialHolster, PotionBelt |
| CardFactory combat generation over unlock-filtered pools (CardPoolModel.GetUnlockedCards(UnlockState)) | 6 | BigHat, ChoicesParadox, Crossbow, OrangeDough, Toolbox, VexingPuzzlebox |
| map (ActMap, MapPoint, travel) | 6 | FurCoat, GoldenCompass, JuzuBracelet, Pantograph, Planisphere, WingedBoots |
| card transformation options (CardFactory.GetDefaultTransformationOptions) | 5 | Astrolabe, Claws, LeafyPoultice, NewLeaf, PandorasBox |
| shop (MerchantInventory, merchant entries) | 5 | LordsParasol, MawBank, MealTicket, MembershipCard, TheCourier |
| temporary card cost modifiers (SetToFreeThisTurn/Combat, AddUntilPlayed, SetThisTurnOrUntilPlayed) | 5 | Bookmark, Crossbow, JeweledMask, MummifiedHand, VexingPuzzlebox |
| random pile insertion (RunRng.Shuffle NextInt(count+1), Commands/CardPileCmd.cs:3431) | 4 | BiiigHug, BlessedAntler, FuneraryMask, TeaOfDiscourtesy |
| enchantments (EnchantmentModel on cards, CardCmd.Enchant) | 3 | ArchaicTooth, Claws, MysticLighter |
| model-seeded Rng(player, ModelId) (seed = RunSeed + player slot + hash(id), Random/Rng.cs:37-40) | 3 | Byrdpip, FurCoat, PaelsLegion |
| CardPlay.Resources.EnergyValue (energy paid for the play) | 2 | IntimidatingHelmet, IvoryTile |
| combat context: RunState.CurrentRoom.RoomType | 2 | BoomingConch, Pantograph |
| combat history (CardPlayFinishedEntry.HappenedThisTurn) | 2 | PaelsEye, RippleBasin |
| enchantments (Glam) | 2 | Glitter, SilkenTress |
| enchantments (Swift) | 2 | BeautifulBracelet, WingCharm |
| rewards screen (RewardsCmd.OfferCustom / RewardsSet.Offer) | 2 | LastingCandy, NeowsBones |

Hooks missing from the sim that relics override, by scope: combat (AfterAutoPrePlayPhaseEntered, AfterAutoPrePlayPhaseEnteredLate, AfterDiedToDoom, AfterModifyingOrbPassiveTriggerCount, AfterOrbChanneled, AfterStarsSpent, ModifyOrbPassiveTriggerCounts, ModifyOrbValue); both (AfterCardChangedPiles, AfterCombatEnd, AfterCombatVictory, AfterCombatVictoryEarly, AfterModifyingGoldGained, AfterPotionDiscarded, AfterPotionProcured, AfterPotionUsed, ModifyGoldGained, ShouldProcurePotion); run (AfterGoldGained, AfterItemPurchased, AfterModifyingCardRewardOptions, AfterModifyingRewards, AfterRestSiteHeal, AfterRoomEntered, BeforeCombatRewardOffered, ModifyCardRewardCreationOptions, ModifyExtraRestSiteHealText, ModifyGeneratedMap, ModifyGeneratedMapLate, ModifyMerchantCardCreationResults, ModifyMerchantPrice, ModifyRestSiteHealAmount, ModifyUnknownMapPointRoomTypes, ShouldAllowFreeTravel, ShouldDisableRemainingRestSiteOptions, ShouldForcePotionReward, ShouldGenerateTreasure, ShouldRefillMerchantEntry, TryModifyCardBeingAddedToDeck, TryModifyCardRewardAlternatives, TryModifyCardRewardOptions, TryModifyCardRewardOptionsLate, TryModifyRestSiteHealRewards, TryModifyRestSiteOptions, TryModifyRewards, TryModifyRewardsLate).

### 4.2 Content that must precede the relics

| power | ported in P0 | relics |
|---|---|---|
| BlurPower | no | DiamondDiadem |
| ConfusedPower | no | FakeSneckoEye, SneckoEye |
| FocusPower | no | DataDisk |
| HelicalDartPower | no | HelicalDart |
| PlatingPower | no | Gorget |
| PoisonPower | no | TwistedFunnel |
| ReptileTrinketPower | no | ReptileTrinket |
| SelfFormingClayPower | no | SelfFormingClay |
| VigorPower | no | Akabeko |
| DexterityPower | yes | BeltBuckle, Kunai, OddlySmoothStone, RainbowRing, SparklingRouge |
| StrengthPower | yes | Brimstone, EmberTea, Girya, MiniRegent, PhilosophersStone, RainbowRing, RedSkull, Shuriken, SlingOfCourage, SparklingRouge, SwordOfJade, ToastyMittens, Vajra |
| ThornsPower | yes | BronzeScales |
| VulnerablePower | yes | BagOfMarbles, HandDrill |
| WeakPower | yes | RedMask |
| WeakPower / VulnerablePower (consulted) | yes | PaperKrane, PaperPhrog: the ported powers must query the relic (`Models.Powers/WeakPower.cs`, `VulnerablePower.cs`) |
| DoomPower (read) | no | BookRepairKnife (AfterDiedToDoom), UndyingSigil (dealer Doom amount) |
| ArtifactPower (read) | no | UnsettlingLamp |
| ITemporaryPower (InternallyAppliedPower) | no | UnsettlingLamp |

Cards created or transformed into by relics: Apotheosis (JewelryBox); Apparition (DistinguishedCape); Bash (ArchaicTooth); Break (ArchaicTooth); BrightestFlame (Storybook); ByrdSwoop (Byrdpip); ByrdonisEgg (Byrdpip); Claw (ScrollBoxes); CurseOfTheBell (CallingBell); Dazed (BlessedAntler, TeaOfDiscourtesy); DefendIronclad (LargeCapsule); Doubt (ArchaicTooth); Dowsing (DowsingRod); Dualcast (ArchaicTooth); Enthralled (BloodSoakedRose); FallingStar (ArchaicTooth); Folly (PreservedFog); Greed (CursedPearl); Guilty (NeowsSacrifice); Injury (HeftyTablet); Luminesce (RadiantPearl); Maul (Claws); MeteorShower (ArchaicTooth); NeowsFury (NeowsTorment); Neutralize (ArchaicTooth); Protector (ArchaicTooth); Quadcast (ArchaicTooth); Relax (PaelsHorn); Shiv (HelicalDart, NinjaScroll); Soot (BiiigHug); Soul (FuneraryMask); SovereignBlade (FencingManual); StrikeIronclad (LargeCapsule); Suppress (ArchaicTooth); Unleash (ArchaicTooth); Whistle (TanxsWhistle); Wish (SereTalon); plus the curse pool (DistinguishedCape, NeowsBones: unlocked `CurseCardPool` cards with `CanBeGeneratedByModifiers`, sorted by id), the colorless pool (Toolbox, OrangeDough, LeadPaperweight, DingyRug) and the character pools (generation and transformation relics).

Enchantments: Swift (BeautifulBracelet, WingCharm), Imbued (ElectricShrymp), Sharp (GnarledHammer), Adroit (Kifuda), Nimble (FresnelLens), Glam (Glitter, SilkenTress), Clone (PaelsGrowth), Momentum (PunchDagger), RoyallyApproved (RoyalStamp), Instinct (TriBoomerang), Goopy (PaelsClaw), TezcatarasEmber (NutritiousSoup); MysticLighter reads any enchantment. Potions: PotionShapedRock (PetrifiedToad), Ambergris (NeowsSacrifice), random potions (Cauldron, AlchemicalCoffer, PhialHolster, DelicateFrond, LostCoffer, TinyMailbox). Monsters: Osty (Necrobinder relics), Byrdpip and PaelsLegion pets (`PlayerCmd.AddPet<T>`). Rest options: Lift, Cook, Clone, Kindle, Dig (`Entities.RestSite/*RestSiteOption.cs`).

## 5. Run-layer hooks used by relics

| hook / member | scope | dispatcher | relics | which |
|---|---|---|---|---|
| `AfterObtained` | RelicModel member | - | 84 | AlchemicalCoffer, ArcaneScroll, ArchaicTooth, Astrolabe, BeautifulBracelet, BeltBuckle, BigMushroom, BiiigHug, BloodSoakedRose, Byrdpip, CallingBell, Cauldron, Claws, CursedPearl, DistinguishedCape, DollysMirror, DowsingRod, DustyTome, ElectricShrymp, EmptyCage, FakeLeesWaffle, FakeMango, FakeSneckoEye, FragrantMushroom, FurCoat, GlassEye, GnarledHammer, GoldenCompass, GoldenPearl, HeftyTablet, JewelryBox, Kaleidoscope, Kifuda, LargeCapsule, LeadPaperweight, LeafyPoultice, LeesWaffle, LoomingFruit, LostCoffer, Mango, MassiveScroll, NeowsBones, NeowsSacrifice, NeowsTalisman, NeowsTorment, NewLeaf, NutritiousOyster, NutritiousSoup, OldCoin, Orrery, PaelsClaw, PaelsEye, PaelsGrowth, PaelsHorn, PaelsLegion, PaelsTooth, PandorasBox, Pear, PhialHolster, Pomander, PotionBelt, PrecariousShears, PreciseScissors, PreservedFog, PumpkinCandle, PunchDagger, RoyalStamp, SandCastle, ScrollBoxes, SeaGlass, SereTalon, SignetRing, SilkenTress, SmallCapsule, SneckoEye, Storybook, Strawberry, TanxsWhistle, TouchOfOrobas, ToyBox, TriBoomerang, WarPaint, Whetstone, YummyCookie |
| `HasUponPickupEffect` | RelicModel member | - | 50 | AlchemicalCoffer, Astrolabe, BigMushroom, BloodSoakedRose, Byrdpip, CallingBell, Cauldron, DistinguishedCape, DollysMirror, DowsingRod, ElectricShrymp, EmptyCage, FakeLeesWaffle, FakeMango, FragrantMushroom, GnarledHammer, GoldenCompass, GoldenPearl, HeftyTablet, JewelryBox, Kaleidoscope, Kifuda, LeesWaffle, LoomingFruit, LostCoffer, Mango, NeowsSacrifice, NeowsTalisman, NeowsTorment, NewLeaf, NutritiousOyster, OldCoin, Orrery, PaelsTooth, PandorasBox, Pear, PhialHolster, PotionBelt, PrecariousShears, PreciseScissors, PunchDagger, RoyalStamp, SandCastle, SeaGlass, SereTalon, Strawberry, ToyBox, WarPaint, Whetstone, YummyCookie |
| `AfterCombatEnd` | both | `Hook.AfterCombatEnd` | 40 | ArtOfWar, BeltBuckle, BrilliantScarf, BurningSticks, CentennialPuzzle, ChosenCheese, EmotionChip, FakeHappyFlower, FishingRod, HappyFlower, JossPaper, Kunai, Kusarigama, LetterOpener, Metronome, MiniRegent, MusicBox, OrnamentalFan, PaelsEye, PaelsFlesh, PaelsLegion, PaelsTears, PaelsTooth, Pendulum, Pocketwatch, PollinousCore, PumpkinCandle, RainbowRing, RedSkull, Regalite, RippleBasin, RuinedHelmet, Shuriken, StoneCalendar, ThrowingAxe, ToyBox, UnsettlingLamp, Vambrace, VelvetChoker, WongosMysteryTicket |
| `AfterRoomEntered` | run | `Hook.AfterRoomEntered` | 33 | BigMushroom, BronzeScales, BurningSticks, DataDisk, DivineRight, EmberTea, EternalFeather, FakeVenerableTeaSet, GhostSeed, Girya, Gorget, LavaLamp, LordsParasol, MawBank, MealTicket, Metronome, OddlySmoothStone, Pantograph, Permafrost, PhilosophersStone, Planisphere, RedSkull, RegalPillow, SilverCrucible, SlingOfCourage, StoneCalendar, StoneCracker, SwordOfJade, ThrowingAxe, Vajra, VelvetChoker, VenerableTeaSet, WingedBoots |
| `IsAllowed` | RelicModel member | - | 20 | AmethystAubergine, BookOfFiveRings, BowlerHat, DragonFruit, FrozenEgg, Girya, JuzuBracelet, LastingCandy, LuckyFysh, MassiveScroll, MealTicket, MoltenEgg, OldCoin, Planisphere, Shovel, SilverCrucible, ToxicEgg, WhiteBeastStatue, WhiteStar, WingedBoots |
| `BeforeCombatStart` | both | `Hook.BeforeCombatStart` | 19 | Anchor, BeltBuckle, BoundPhylactery, Byrdpip, DelicateFrond, FakeAnchor, FakeSneckoEye, FurCoat, Kusarigama, LetterOpener, MeatOnTheBone, PaelsFlesh, PaelsLegion, Pantograph, PhylacteryUnbound, SneckoEye, TeaOfDiscourtesy, UnsettlingLamp, Vambrace |
| `TryModifyCardRewardOptionsLate` | run | `Hook.TryModifyCardRewardOptions` | 9 | FresnelLens, FrozenEgg, Glitter, LavaLamp, MoltenEgg, SilkenTress, SilverCrucible, ToxicEgg, WingCharm |
| `AfterDamageReceived` | both | `Hook.AfterDamageReceived` | 6 | BeatingRemnant, CentennialPuzzle, DemonTongue, EmotionChip, LavaLamp, SelfFormingClay |
| `TryModifyRewards` | run | `Hook.ModifyRewards` | 6 | AmethystAubergine, BlackStar, LavaRock, PrayerWheel, WhiteStar, WongosMysteryTicket |
| `AfterCombatVictory` | both | `Hook.AfterCombatVictory` | 5 | BeltBuckle, BlackBlood, BurningBlood, SwordOfStone, WarHammer |
| `TryModifyRestSiteOptions` | run | `Hook.ModifyRestSiteOptions` | 5 | Girya, MeatCleaver, PaelsGrowth, PumpkinCandle, Shovel |
| `AfterCardChangedPiles` | both | `Hook.AfterCardChangedPiles` | 4 | BingBong, BookOfFiveRings, DarkstonePeriapt, LuckyFysh |
| `ModifyDamageAdditive` | both | `Hook.ModifyDamageInternal` | 4 | FakeStrikeDummy, MiniatureCannon, MysticLighter, StrikeDummy |
| `ModifyExtraRestSiteHealText` | run | `Hook.ModifyExtraRestSiteHealText` | 4 | DreamCatcher, RegalPillow, StoneHumidifier, TinyMailbox |
| `ModifyMerchantCardCreationResults` | run | `Hook.ModifyMerchantCardCreationResults` | 4 | FresnelLens, FrozenEgg, MoltenEgg, ToxicEgg |
| `TryModifyCardBeingAddedToDeck` | run | `Hook.ModifyCardBeingAddedToDeck` | 4 | FresnelLens, FrozenEgg, MoltenEgg, ToxicEgg |
| `AfterModifyingHpLostAfterOsty` | both | `Hook.AfterModifyingHpLostAfterOsty` | 3 | BeatingRemnant, TheBoot, TungstenRod |
| `ModifyDamageMultiplicative` | both | `Hook.ModifyDamageInternal` | 3 | PenNib, UndyingSigil, VitruvianMinion |
| `SetupForPlayer (offer time)` | offer time (called by the ancient) | - | 3 | ArchaicTooth, DustyTome, TouchOfOrobas |
| `AfterCurrentHpChanged` | both | `Hook.AfterCurrentHpChanged` | 2 | MeatOnTheBone, RedSkull |
| `AfterModifyingCardRewardOptions` | run | `Hook.AfterModifyingCardRewardOptions` | 2 | SilkenTress, SilverCrucible |
| `AfterModifyingGoldGained` | both | `Hook.AfterModifyingGoldGained` | 2 | BowlerHat, Ectoplasm |
| `AfterModifyingRewards` | run | `Hook.AfterModifyingRewards` | 2 | AmethystAubergine, WongosMysteryTicket |
| `AfterPotionUsed` | both | `Hook.AfterPotionUsed` | 2 | BeltBuckle, ReptileTrinket |
| `AfterRestSiteHeal` | run | `Hook.AfterRestSiteHeal` | 2 | RegalPillow, StoneHumidifier |
| `IsAllowedAtNeow` | RelicModel member | - | 2 | Kaleidoscope, ScrollBoxes |
| `ModifyCardRewardCreationOptions` | run | `Hook.ModifyCardRewardCreationOptions` | 2 | DingyRug, PrismaticGem |
| `ModifyGoldGained` | both | `Hook.ModifyGoldGained` | 2 | BowlerHat, Ectoplasm |
| `ModifyHpLostAfterOsty` | both | `Hook.ModifyHpLost` | 2 | BeatingRemnant, TungstenRod |
| `ModifyMerchantPrice` | run | `Hook.ModifyMerchantPrice` | 2 | MembershipCard, TheCourier |
| `ModifyUnknownMapPointRoomTypes` | run | `Hook.ModifyUnknownMapPointRoomTypes` | 2 | GoldenCompass, JuzuBracelet |
| `TryModifyRestSiteHealRewards` | run | `Hook.ModifyRestSiteHealRewards` | 2 | DreamCatcher, TinyMailbox |
| `AfterCombatVictoryEarly` | both | `Hook.AfterCombatVictory` | 1 | MeatOnTheBone |
| `AfterDeath` | both | `Hook.AfterDeath` | 1 | GremlinHorn |
| `AfterGoldGained` | run | `Hook.AfterGoldGained` | 1 | DragonFruit |
| `AfterItemPurchased` | run | `Hook.AfterItemPurchased` | 1 | MawBank |
| `AfterPotionDiscarded` | both | `Hook.AfterPotionDiscarded` | 1 | BeltBuckle |
| `AfterPotionProcured` | both | `Hook.AfterPotionProcured` | 1 | BeltBuckle |
| `AfterPreventingDeath` | both | `Hook.AfterPreventingDeath` | 1 | LizardTail |
| `BeforeCombatRewardOffered` | run | `Hook.BeforeCombatRewardOffered` | 1 | LastingCandy |
| `BeforeCombatStartLate` | both | `Hook.BeforeCombatStart` | 1 | PetrifiedToad |
| `ModifyGeneratedMap` | run | `Hook.ModifyGeneratedMap` | 1 | GoldenCompass |
| `ModifyGeneratedMapLate` | run | `Hook.ModifyGeneratedMapLate` | 1 | FurCoat |
| `ModifyHpLostAfterOstyLate` | both | `Hook.ModifyHpLost` | 1 | TheBoot |
| `ModifyRestSiteHealAmount` | run | `Hook.ModifyRestSiteHealAmount` | 1 | RegalPillow |
| `ShouldAllowFreeTravel` | run | `Hook.ShouldAllowFreeTravel` | 1 | WingedBoots |
| `ShouldDieLate` | both | `Hook.ShouldDie` | 1 | LizardTail |
| `ShouldDisableRemainingRestSiteOptions` | run | `Hook.ShouldDisableRemainingRestSiteOptions` | 1 | MiniatureTent |
| `ShouldForcePotionReward` | run | `Hook.ShouldForcePotionReward` | 1 | WhiteBeastStatue |
| `ShouldGenerateTreasure` | run | `Hook.ShouldGenerateTreasure` | 1 | SilverCrucible |
| `ShouldProcurePotion` | both | `Hook.ShouldProcurePotion` | 1 | Sozu |
| `ShouldRefillMerchantEntry` | run | `Hook.ShouldRefillMerchantEntry` | 1 | TheCourier |
| `TryModifyCardRewardAlternatives` | run | `Hook.ModifyCardRewardAlternatives` | 1 | PaelsWing |
| `TryModifyCardRewardOptions` | run | `Hook.TryModifyCardRewardOptions` | 1 | LastingCandy |
| `TryModifyRewardsLate` | run | `Hook.ModifyRewards` | 1 | Driftwood |

`both`-scope hooks (`AfterCombatEnd`, `AfterCombatVictory`, `AfterCardChangedPiles`, `ModifyGoldGained`, `ShouldProcurePotion`, potion hooks, damage/HP modifiers, `BeforeCombatStart`) iterate `runState.IterateHookListeners(combatState)`; they are listed here because they also fire with run-level effect or outside combat. Pure run-scope hooks iterate `runState.IterateHookListeners(null)` (`sim/specs/inventory/hooks.json` scope legend).

## 6. Relics with counter, charge or carried state

`[SavedProperty]` fields persist in the save and across combats; the run layer must carry them and the combat-only sim must take them as inputs. Non-saved fields are per-instance state that the relic object keeps across combats inside a run (cleared by its own hooks).

| kind | n | relic: state |
|---|---|---|
| persistent-counter | 16 | BookOfFiveRings: CardsAdded (int, SavedProperty, persistent counter); FakeHappyFlower: TurnsSeen (SavedProperty, persists across combats); FishingRod: CombatsSeen (SavedProperty counter); GalacticDust: StarsSpent (SavedProperty, persists across combats); Girya: TimesLifted (SavedProperty counter, max 3); HappyFlower: TurnsSeen (SavedProperty, persists across combats); IronClub: CardsPlayed (SavedProperty, persists across combats); JossPaper: CardsExhausted (SavedProperty SaveIfNotTypeDefault, persists); EtherealCount (combat-scoped); LastingCandy: CombatRewardsSeen (SavedProperty counter); Nunchaku: AttacksPlayed (SavedProperty, persists across combats); PaelsWing: RewardsSacrificed (SavedProperty counter); Pendulum: TurnsSeen (SavedProperty, persists across combats); PenNib: AttacksPlayed (SavedProperty, mod 10, persists); _attackToDouble (play-scoped); PollinousCore: TurnsSeen (SavedProperty, persists across combats); SwordOfStone: ElitesDefeated (SavedProperty counter); TuningFork: SkillsPlayed (SavedProperty, persists across combats) |
| charges | 9 | BoneTea: CombatsLeft=1 (SavedProperty charges; IsUsedUp at 0); EmberTea: CombatsLeft=5 (SavedProperty charges); LizardTail: WasUsed (bool, SavedProperty; IsUsedUp); MawBank: HasItemBeenBought (bool, SavedProperty; IsUsedUp); PumpkinCandle: KindleCount (SavedProperty charges); SilkenTress: IsUsed (bool, SavedProperty; IsUsedUp); TeaOfDiscourtesy: CombatsLeft=1 (SavedProperty charges); ToyBox: CombatsSeen (SavedProperty; IsUsedUp at 15); WingedBoots: TimesUsed (SavedProperty charges, 3) |
| persistent-flag | 4 | FakeVenerableTeaSet: GainEnergyInNextCombat (bool, SavedProperty); LavaLamp: TookDamageThisCombat (bool, SavedProperty); LavaRock: HasTriggered (bool, SavedProperty); VenerableTeaSet: GainEnergyInNextCombat (bool, SavedProperty) |
| persistent-list | 1 | PaelsTooth: SerializableCards (list, SavedProperty) |
| persistent-other | 4 | FurCoat: FurCoatActIndex, FurCoatCoordCols/Rows, FurCoatCoordsSet (SavedProperty); GoldenCompass: GoldenPathAct (SavedProperty); SilverCrucible: TimesUsed, TreasureRoomsEntered (SavedProperty); WongosMysteryTicket: CombatsFinished, GaveRelic (SavedProperty) |
| setup-data | 4 | ArchaicTooth: StarterCard, AncientCard (SerializableCard, SavedProperty, display only); DustyTome: AncientCard (ModelId, SavedProperty); SeaGlass: CharacterId (ModelId, SavedProperty; set by the offering ancient, defaults to Ironclad); TouchOfOrobas: StarterRelic, UpgradedRelic (ModelId, SavedProperty, set once) |
| combat | 15 | ArtOfWar: _anyAttacksPlayedLastTurn/_anyAttacksPlayedThisTurn (combat-scoped bools); BeatingRemnant: _damageReceivedThisTurn (decimal, reset at player side-turn start); BeltBuckle: _dexterityApplied (bool, cleared AfterCombatVictory); BurningSticks: _wasUsedThisCombat (combat-scoped); CentennialPuzzle: _usedThisCombat (combat-scoped); Metronome: _orbsChanneled (combat-scoped); PaelsEye: _usedThisCombat (combat-scoped), _wasOwnerPartOfLastPlayerTurn; PaelsLegion: _cooldown, _triggeredBlockLastTurn, _affectedCardPlay (combat-scoped); Skin (SavedProperty, cosmetic); PaelsTears: _hadLeftoverEnergy (combat-scoped); Permafrost: _activatedThisCombat (combat-scoped); RedSkull: _strengthApplied (combat-scoped); RuinedHelmet: _usedThisCombat (combat-scoped); ThrowingAxe: _usedThisCombat (combat-scoped); UnsettlingLamp: _triggeringCard, _doubledPowers, _isFinishedTriggering (combat-scoped); Vambrace: _triggeringCard, _blockGainedThisCombat (combat-scoped) |
| turn | 15 | BrilliantScarf: _cardsPlayedThisTurn (turn-scoped); DemonTongue: _triggeredThisTurn (turn-scoped); FakeOrichalcum: _shouldTrigger (turn-scoped); Kunai: _attacksPlayedThisTurn (turn-scoped); Kusarigama: _attacksPlayedThisTurn (turn-scoped); LetterOpener: _skillsPlayedThisTurn (turn-scoped); MiniRegent: _usedThisTurn (turn-scoped); MusicBox: _wasUsedThisTurn, _cardBeingPlayed (turn-scoped); Orichalcum: _shouldTrigger (turn-scoped); OrnamentalFan: _attacksPlayedThisTurn (turn-scoped); Pocketwatch: _cardsPlayedThisTurn, _cardsPlayedLastTurn (turn-scoped); RainbowRing: per-type counters + activation count (turn-scoped); Regalite: _usedThisTurn (turn-scoped); Shuriken: _attacksPlayedThisTurn (turn-scoped); VelvetChoker: _cardsPlayedThisTurn (turn-scoped) |
| transient | 1 | BingBong: _cardsToSkip (HashSet of clones, transient) |
| cosmetic | 1 | Byrdpip: Skin (string, SavedProperty, cosmetic) |
| stack-count | 1 | Circlet: StackCount (stackable) |

Counter display (`ShowCounter`/`DisplayAmount`) is UI only. `IsUsedUp` does not remove a relic from hook iteration (only melted relics are skipped, `Runs/RunState.cs:876`); every used-up relic guards its own hooks.

## 7. Player prompts

| prompt type | sites | game call | relics (min..max) |
|---|---|---|---|
| RewardsScreen | 9 | RewardsCmd.OfferCustom, RewardsSet.Offer | CallingBell, Cauldron, GlassEye, Kaleidoscope, LostCoffer, NeowsBones, Orrery, SmallCapsule, ToyBox |
| DeckRemove | 7 | CardSelectCmd.FromDeckForRemoval, MerchantCardRemovalEntry.OnTryPurchaseWrapper | BiiigHug (4..4), EmptyCage (2..2), LordsParasol (1..1), PaelsTooth (5..5), PrecariousShears (2..2), PreciseScissors (1..1), PreservedFog (3..3) |
| DeckEnchant | 7 | CardSelectCmd.FromDeckForEnchantment, CardSelectCmd.FromDeckForEnchantment(cards) | ElectricShrymp (1..1), GnarledHammer (0..3), Kifuda (0..3), PaelsGrowth (1..1), PunchDagger (1..1), RoyalStamp (1..1), TriBoomerang (3..3) |
| RestSiteOption | 5 | RestSiteOption list | Girya (1..1), MeatCleaver (1..1), PaelsGrowth (1..1), PumpkinCandle (1..1), Shovel (1..1) |
| ChooseCard | 4 | CardSelectCmd.FromChooseACardScreen | HeftyTablet (1..1), LeadPaperweight (1..1), MassiveScroll (1..1), Toolbox (1..1) |
| DeckTransform | 3 | CardSelectCmd.FromDeckForTransformation | Astrolabe (3..3), Claws (0..6), NewLeaf (1..1) |
| HandSelect | 2 | CardSelectCmd.FromHand, CardSelectCmd.FromHandForDiscard | GamblingChip (0..999999999), ToastyMittens (1..1) |
| DeckUpgrade | 2 | CardSelectCmd.FromDeckForUpgrade | Pomander (1..1), YummyCookie (4..4) |
| SimpleGrid | 1 | CardSelectCmd.FromSimpleGrid | ChoicesParadox (1..1) |
| DeckGeneric | 1 | CardSelectCmd.FromDeckGeneric | DollysMirror (1..1) |
| CardRewardAlternative | 1 | CardRewardAlternative SACRIFICE | PaelsWing (1..1) |
| ChooseBundle | 1 | CardSelectCmd.FromChooseABundleScreen | ScrollBoxes (1..1) |
| SimpleGridRewards | 1 | CardSelectCmd.FromSimpleGridForRewards | SeaGlass (0..15) |

In-combat prompt kinds follow `ChoicePromptKind` in `design-combat-kernel.md` 4.5 (HandSelect, HandUpgrade, PileSelect, ChooseCard, SimpleGrid). All four in-combat relic sites are turn-setup sites (`SetupPlayerTurn` context): ChoicesParadox, GamblingChip and ToastyMittens in `AfterPlayerTurnStart`, Toolbox in `BeforeHandDraw` (before the opening draw); `FromHand`/`FromSimpleGrid` signal the pause before counting options, so GamblingChip with an empty hand still reorders the turn start (`design-combat-kernel.md` 4.6). Run-layer kinds (`design-run-layer.md` 3.12): DeckRemove, DeckTransform, DeckUpgrade, DeckEnchant, DeckGeneric, ChooseCard, ChooseBundle, SimpleGridRewards (deck prompts, `CardSelectorPrefs(prompt, n)` = exactly n unless min/max given; `(0, N)` with `RequireManualConfirmation`); RewardsScreen (`RewardsCmd.OfferCustom` / `RewardsSet.Offer`, D2); RestSiteOption (adds an option to the D5 menu); CardRewardAlternative (PaelsWing adds SACRIFICE to every card reward). WhisperingEarring suppresses all prompts of its auto-played turn with `VakuuCardSelector` (take the first MaxSelect options, `Models.Relics/VakuuCardSelector.cs:14`).

## 8. RNG streams

| stream | relics | which |
|---|---|---|
| PlayerRng.Rewards | 25 | ArcaneScroll, BlackStar, Cauldron, DreamCatcher, DustyTome, GlassEye, HeftyTablet, LargeCapsule, LastingCandy, LavaRock, LeadPaperweight, LostCoffer, NeowsBones, Orrery, PaelsTooth, PaelsWing, PrayerWheel, ScrollBoxes, SeaGlass, Shovel, SmallCapsule, TinyMailbox, ToyBox, WhiteStar, WongosMysteryTicket |
| RunRng.Niche | 14 | Astrolabe, BeautifulBracelet, DistinguishedCape, FishingRod, FragrantMushroom, Kaleidoscope, NewLeaf, PandorasBox, RoyalStamp, SandCastle, WarHammer, WarPaint, Whetstone, WingCharm |
| RunRng.CombatCardGeneration | 6 | BigHat, ChoicesParadox, Crossbow, OrangeDough, Toolbox, VexingPuzzlebox |
| RunRng.CombatCardSelection | 5 | Bookmark, JeweledMask, MummifiedHand, PowerCell, StoneCracker |
| RunRng.CombatTargets | 5 | ForgottenSoul, Kusarigama, ParryingShield, Tingsha, WhisperingEarring |
| RunRng.Shuffle | 4 | BiiigHug, BlessedAntler, FuneraryMask, TeaOfDiscourtesy |
| RunRng.CombatPotionGeneration | 3 | AlchemicalCoffer, DelicateFrond, PhialHolster |
| Rng(player, id) | 2 | Byrdpip, FurCoat |
| PlayerRng.Transformations | 2 | Claws, LeafyPoultice |
| RunRng.CombatEnergyCosts | 2 | FakeSneckoEye, SneckoEye |
| player relic grab bag | 1 | CallingBell |
| Rng.Chaotic | 1 | YummyCookie |

Per-entry `rng_streams` give the call order. Implicit consumers: `CardFactory.CreateForReward` (rarity roll via `PlayerOdds`, `NextItem` and upgrade roll on `options.RngOverride ?? PlayerRng.Rewards`, `Factories/CardFactory.cs:71-90`, `:168-197`, `:239-258`); `RelicReward(player)` and `PullNextRelicFromFront(player)` (rarity roll on `PlayerRng.Rewards`); `PotionReward(player)` (`CreateRandomPotionOutOfCombat` on `PlayerRng.Rewards`); `CardPilePosition.Random` inserts (`RunRng.Shuffle.NextInt(count+1)` per card, `Commands/CardPileCmd.cs:3431`); `Rng.NextItem` on an empty list consumes nothing (`Random/Rng.cs:213-223`). `new Rng(player, id, mixin)` is seeded with RunSeed + player slot + hash(id.Entry) + mixin and touches no shared stream (`Random/Rng.cs:37-40`): Byrdpip and PaelsLegion skins (cosmetic) and FurCoat map marks (rule-relevant).

## 9. Version delta v0.107.1 -> v0.111.0

Mechanical comparison (`vdiff.py`): per file, literal property overrides, `CanonicalVars`, constants, Cmd/Hook/factory/RNG/ModelDb call sets, numeric literals and non-UI identifiers of the logic, after normalising decompiler style (state machines condensed, explicit `Enumerable` calls, typed locals, qualified names); every candidate was then read side by side. Result: 224 same, 52 cosmetic, 21 rule-change, 2 new, 0 removed.

| relic | delta |
|---|---|
| BeautifulBracelet | v0.107.1: choose 3 deck cards (FromDeckForEnchantment) for Swift 3; v0.111.0: 4 random enchantable deck cards (RunRng.Niche TakeRandom), Swift 2, no prompt |
| BoneTea | trigger moved from AfterSideTurnStart to AfterPlayerTurnStart (different point of the turn-start sequence) |
| DiamondDiadem | redesigned: v0.107.1 applied DiamondDiademPower at end of a turn with <=2 cards played; v0.111.0 gives 20 block and Blur on turn 1 |
| DistinguishedCape | v0.107.1: lose 9 max HP + 3 Apparitions; v0.111.0: 2 random curses (RunRng.Niche) + 3 Apparitions |
| DivineDestiny | turn-1 stars 6 -> 7 |
| DowsingRod | not present in v0.107.1 |
| Fiddle | hand-draw bonus moved from ModifyHandDrawLate to ModifyHandDraw |
| FurCoat | marked combats 7 -> 8; adds a map-history flag |
| HandDrill | trigger moved from AfterDamageGiven(result.WasBlockBroken) to the AfterBlockBroken hook |
| HistoryCourse | replays only Attacks (v0.107.1: Attacks or Skills); owner test uses CardPlay.Player |
| JeweledMask | prefers non-Innate Powers when choosing the draw-pile Power |
| LastingCandy | counter now advances on combat reward screens that contain a card reward (BeforeCombatRewardOffered, odd counts trigger) instead of every combat end; requires IsCardReward|IsFromCombat flags |
| NeowsSacrifice | not present in v0.107.1 |
| Pendulum | v0.107.1 drew 1 card at AfterPlayerTurnStart every 3rd turn; v0.111.0 adds +1 to the hand draw (BeforeHandDraw counter + ModifyHandDraw) |
| Planisphere | heals only on the first room of a map point (CurrentRoomCount<=1) |
| PollinousCore | counter moved from BeforeSideTurnStart to BeforeHandDraw; bonus when TurnsSeen>=4 (was ==4) |
| Regalite | block 2 -> 4 and once per turn (adds a per-turn flag) |
| SealOfGold | gold cost per turn 5 -> 3 |
| SereTalon | v0.107.1: 2 random curses + 3 Wishes; v0.111.0: lose 9 max HP + 3 Wishes |
| SignetRing | gold 999 -> 888 |
| ToastyMittens | v0.107.1: BeforeHandDraw exhaust the top draw-pile card (non-Innate on turn 1) + 1 Strength; v0.111.0: AfterPlayerTurnStart choose a hand card to exhaust (prompt) + 1 Strength |
| ToyBox | wax relics offered 4 -> 5 (used-up threshold Combats x Relics follows) |
| UnsettlingLamp | skips targets that have Artifact |

Cosmetic deltas worth knowing: `ModifyDamageAdditive/Multiplicative` gained a `CardPlay` parameter (FakeStrikeDummy, MiniatureCannon, MysticLighter, StrikeDummy, UndyingSigil); `CreatureCmd.LoseBlock` gained a choice context (SturdyClamp); `CardCreationOptions.CustomCardPool` was removed (DingyRug, PrismaticGem, MassiveScroll, same results); LordsParasol adds a run-in-progress guard. Consequence for recordings made on v0.107.1: the rule-change relics above must be version-gated or excluded from differential tests.

## 10. Decompile-unreadable code

- `Models.Relics/GlassEye.cs:57-58`: `CardRarity[5]` initialised by `RuntimeHelpers.InitializeArray` with a lost field token (`OpCode not supported: LdMemberToken`). v0.107.1 has {Common, Common, Uncommon, Uncommon, Rare}; re-read the v0.111.0 blob from IL (`<PrivateImplementationDetails>`, ildasm or ILSpy IL view) before porting.
- Indirect: GoldenCompass builds `GoldenPathActMap`, whose 16 point types are a lost blob (`Map/GoldenPathActMap.cs:42`); PreservedFog adds `Folly`, whose card data has a lost blob (`Models.Cards/Folly.cs:19`).
- No other relic body is broken. The `//IL_xxxx: Unknown result type` comments (284 relic/potion files) and `/*cast due to .constrained prefix*/` markers are noise; state machines were read through `condense.py` and checked against v0.107.1 async bodies.

## 11. Port notes

- Side-turn hooks need the `participants` list restored (dropped in P0): 55 relics test `participants.Contains(owner)`.
- `CreatureCmd.GainBlock(BlockVar)` / `CreatureCmd.Damage(DamageVar)` var overloads are thin wrappers over ported methods; 29 relics use them.
- Turn-number tests: relics use `TurnNumber<=1` or `==1` for "first turn" interchangeably; both are kept as written.
- Out-of-combat damage: FragrantMushroom and PrecariousShears call `CreatureCmd.Damage` on the owner outside combat.
- Gold in combat: SealOfGold reads `Player.Gold` and loses gold mid-combat; BowlerHat and Ectoplasm modify every gold gain, including combat gold gains from cards.
- Shared mutable sets: BingBong keeps a `HashSet<CardModel>` of its own clones; UnsettlingLamp and Vambrace keep card references; store ids, not references (`design-combat-kernel.md` 5).
- TestMode branches: CallingBell and Cauldron offer fixed reward lists under `TestMode.IsOn`; the sim follows `TestMode.IsOff`.

## 12. Open questions

- ArchaicTooth: AfterObtained calls Transform with a null card when the deck has no transcendence starter, and offer-time SetupForPlayer returns false then (Models.Relics/ArchaicTooth.cs:156-168): confirm the ancient never offers it in that case
- Driftwood: reroll RNG consumption and the reroll option source live in Rewards/CardReward.cs and are not re-derived here
- FragrantMushroom: CreatureCmd.Damage outside combat: confirm HP-loss hooks and death handling in the run layer
- GlassEye: re-read the GlassEye rarity array from v0.111.0 IL before porting
- GoldenCompass: re-read GoldenPathActMap types from v0.111.0 IL
- LordsParasol: purchase order with full potion slots: does OnTryPurchaseWrapper fail silently or prompt?
- PrecariousShears: out-of-combat CreatureCmd.Damage: confirm block/HP-loss hooks and death at Neow
- Turn-start order of AfterSideTurnStart versus AfterPlayerTurnStart relative to the opening draw matters for BoneTea and Pendulum (both changed hooks in v0.111.0); confirm on a recording with BoneTea.
- AfterRoomEntered on combat rooms runs while the combat is set up but not started: which Cmd gates (IsInProgress, IsOverOrEnding) apply to PowerCmd.Apply, CardCmd.Upgrade and CardCmd.ApplyKeyword at that point (StoneCracker, GhostSeed, Vajra, ...)?
- Do relic-applied powers at AfterRoomEntered get ShouldReceiveCombatHooks listeners before AfterCreatureAdded runs (ordering of power hooks on the first AfterCreatureAdded)?
- Reward-screen relics (CallingBell, Cauldron, Orrery, ...) open a RewardsSet outside a combat room: does the reward screen allow skipping each reward and are unclaimed rewards lost (NeowsBones disallows skipping)?

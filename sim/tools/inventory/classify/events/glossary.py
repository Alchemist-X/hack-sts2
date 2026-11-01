GLOSSARY = [
    ("EventFramework",
     "EventModel lifecycle: BeginEvent sets Owner and the private Rng, then BeforeEventStarted, CalculateVars, "
     "SetInitialEventState -> GenerateInitialOptions (Models/EventModel.cs:68-144, :576-611); SetEventState / "
     "SetEventFinished (empty option list = finished, Models/EventModel.cs:760-810); EventOption legality "
     "(locked when OnChosen is null, Events/EventOption.cs:279, :292; DisableOnChosen guard :66; IsProceed :303-307). WillKillPlayer is a "
     "UI warning only in single player (Nodes.Events/NEventOptionButton.cs:599-623 blocks only when Players > 1)."),
    ("EventRng",
     "EventModel.Rng = new Rng(RunSeed + (IsShared ? 0 : slot) + XXH64(Id.Entry)) (Models/EventModel.cs:91). Sim "
     "already has Random/Rng.cs ForPlayerModel + Random/DeterministicHash.cs; slot is 0 in single player so IsShared "
     "does not change the seed there."),
    ("AncientFramework",
     "AncientEventModel: arrival heal in BeforeEventStarted (Neow first sets HP to 0; heal (MaxHp-CurrentHp) x0.8 at "
     "A2 WearyTraveler; Models/AncientEventModel.cs:76-105), Hook.ShouldAllowAncient gate (no overriding model in "
     "v0.111.0; :275-289), StartPreFinished/Done (:328-351), RelicOption -> RelicCmd.Obtain + Done (:353-377; the "
     "local-function body is missing from the v0.111.0 decompile, see decompile list)."),
    ("EventCombat",
     "EventModel.EnterCombatWithoutExitingEvent (shared events only; Models/EventModel.cs:812-839) -> "
     "EventCombatSynchronizer: CombatRoom{ParentEventId, ShouldResumeParentEventAfterCombat, ExtraRewards} entered "
     "with EnterRoomWithoutExitingCurrentRoom; normal room rewards (unless ShouldGiveRewards=false) + ExtraRewards."),
    ("EventResume",
     "EventModel.Resume(exitedRoom) override called from EventRoom.Resume after the event combat "
     "(Rooms/EventRoom.cs:373-379)."),
    ("CombatLayout",
     "EventLayoutType.Combat: at room entry EventCombatSynchronizer.InitializeForEvent builds a layout CombatState "
     "(Multiplayer.Game/EventCombatSynchronizer.cs:83-128): encounter GenerateMonstersWithSlots on a fresh mutable "
     "copy + CombatState.CreateCreature per monster, which draws RunRng.Niche for the unique HP roll "
     "(Combat/CombatState.cs:709-724, Entities.Creatures/Creature.cs:972-984). Hidden RNG use before any option."),
    ("OutOfCombatDamage",
     "CreatureCmd.Damage / Heal / GainMaxHp (SetMaxHp then Heal of the gain) / LoseMaxHp / Kill with no live "
     "combat: HP hooks still dispatch; CreatureCmd.Kill or lethal damage ends the run (design-run-layer.md 3.14)."),
    ("GoldFacades",
     "PlayerCmd.GainGold (Hook.ModifyGoldGained, then Gold += (int)amount, Hook.AfterGoldGained; "
     "Commands/PlayerCmd.cs:237-335) and PlayerCmd.LoseGold (Gold = max(0, Gold - (int)amount), no hooks; :896-918)."),
    ("DeckFacades",
     "RunState.CreateCard / CloneCard, CardPileCmd.Add(card, Deck) (Hook.ModifyCardBeingAddedToDeck), "
     "CardPileCmd.AddCurseToDeck<T> / AddCursesToDeck, CardPileCmd.RemoveFromDeck, CardCmd.Upgrade / Downgrade."),
    ("Transform",
     "CardCmd.TransformToRandom(card, rng) -> CardTransformation + CardFactory.CreateRandomCardForTransform: "
     "rng.NextItem over the card pool's unlocked cards filtered by rarity/id (Commands/CardCmd.cs:1949-2060, "
     "Factories/CardFactory.cs:123-163); CardCmd.TransformTo<T> (fixed replacement)."),
    ("Enchant",
     "CardCmd.Enchant<T>(card, amount) + EnchantmentModel.CanEnchant filters (Commands/CardCmd.cs:2146-2180); the "
     "enchantment models themselves are enchantments-kind content."),
    ("RewardsScreen",
     "RewardsCmd.OfferCustom(player, rewards) opens the rewards screen (decision D2): CardReward (pick 1 of N or "
     "skip; Populate -> CardFactory.CreateForReward), PotionReward (needs a free slot), RelicReward, "
     "SpecialCardReward, GoldReward; random rewards populate on PlayerRng.Rewards unless SetRng/RngOverride."),
    ("RelicGrabBag",
     "RelicFactory.PullNextRelicFromFront: rarity roll PlayerRng.Rewards.NextFloat (<0.5 Common, <0.83 Uncommon, "
     "else Rare) unless a rarity is given, then RelicGrabBag.PullFromFront removes the relic (and from the shared "
     "bag) (Factories/RelicFactory.cs:15-61, Runs/RelicGrabBag.cs:156-178)."),
    ("CardRewardGen",
     "CardFactory.CreateForReward(player, n, CardCreationOptions): per card Hook.ModifyCardRewardCreationOptions, "
     "rarity roll unless Uniform (PlayerOdds.CardRarity on PlayerRng.Rewards; RollWithBaseOdds for non-encounter "
     "sources, so no pity change), rng.NextItem, then an upgrade roll NextFloat unless CardCreationFlags.NoUpgradeRoll "
     "(set by ForNonCombatWithDefaultOdds/UniformOdds, Runs/CardCreationOptions.cs:109-117; WithFlags ORs) "
     "(rng = RngOverride ?? PlayerRng.Rewards; A7 Scarcity halves the per-act upgrade scaling and lowers base rarity "
     "odds) (Factories/CardFactory.cs:71-91, :166-238, Odds/CardRarityOdds.cs:53-105)."),
    ("PotionPools",
     "Character PotionPool + SharedPotionPool GetUnlockedPotions(UnlockState) concatenation (unlock-state "
     "dependent candidate list), drawn with PlayerRng.Rewards.NextItem."),
    ("DeckPrompt",
     "CardSelectCmd deck prompts (FromDeckForRemoval/Upgrade/Transformation/Enchantment/Generic): candidates in "
     "deck order (removal: curses first), auto-resolve when candidates <= MinSelect and no manual confirmation, "
     "empty result when no candidates (Commands/CardSelectCmd.cs:3441-3666). Needs the W1 resumable prompt API."),
    ("GridPrompt",
     "CardSelectCmd.FromSimpleGridForRewards via EventModel.SelectCardsToAddToDeckFromGrid (adds the picks to the "
     "deck, logs the rest; Models/EventModel.cs:153-300, :865-878)."),
    ("StableShuffle",
     "ListExtensions.StableShuffle = List.Sort with model CompareTo (ModelId ordinal category then entry, then "
     "card upgrade level; Models/AbstractModel.cs:126-137, Models/CardModel.cs:3649-3671) then UnstableShuffle; "
     "sim has Random/RngCollectionExtensions.cs. Equal keys keep .NET introsort order, so the sim must use "
     "List<T>.Sort on the same initial order."),
    ("PotionLock",
     "Player.CanUseOrRemovePotions=false between BeforeEventStarted and OnEventFinished: disables potion use and "
     "discard (node-layer rule Nodes.Potions/NPotionPopup.cs:328-334, :556-565)."),
]

from lib import M

K = "modifiers"
CORE = "kernel:run_modifiers"
OUT = "custom_modes_only"
NEOW = "GenerateNeowOption: adds a Neow option (modifier Neow chain)"

M(K, "AllStar", OUT, [
    NEOW + " that adds 5 random colorless cards to the deck",
    "each: CardFactory.CreateForReward(1, uniform odds over ColorlessCardPool, NoRarityModification | NoCardPoolModifications) -> CardPileCmd.Add(Deck)",
], reqs=[CORE, "kernel:card_generation", "run:deck", "run:neow_and_ancients"], hooks=["GenerateNeowOption"], tier=3, batch=1,
  rng=["the Neow event Rng (eventModel.Rng) for the card rolls"], vd=("refactor_only", "loop rewritten as an async state machine; CreateForReward returns CardCreationResult wrappers"))
M(K, "BigGameHunter", OUT, [
    "ModifyGeneratedMap: regenerates the act map (StandardActMap or SpoilsActMap) with 2.5x elites (rounded) that ignore placement rules, on a fresh Rng(seed, act_<n>_map)",
    "ModifyCardRewardCreationOptions: elite encounter card rewards become uniform odds over Rare cards (falls back to the character pool when empty)",
], reqs=[CORE, "ModifyGeneratedMap", "ModifyCardRewardCreationOptions", "run:map_generation", "run:rewards"],
  hooks=["ModifyGeneratedMap", "ModifyCardRewardCreationOptions"], tier=3, batch=1,
  vd=("refactor_only", "card-pool option API rewritten (WithCustomPool -> WithCardPools/WithFilter/WithRarityOdds); same rare-only uniform elite rewards"))
M(K, "CharacterCards", OUT, [
    "[SavedProperty] CharacterModel: the extra character",
    "ModifyMerchantCardPool: adds that character's unlocked cards to the merchant pool when the offer is the player's own pool",
    "ModifyCardRewardCreationOptions: card rewards also draw from that character's pool",
], reqs=[CORE, "ModifyMerchantCardPool", "ModifyCardRewardCreationOptions", "run:merchant", "run:rewards", "run:save_import",
         "run:unlock_profile"], hooks=["ModifyMerchantCardPool", "ModifyCardRewardCreationOptions", "IsEquivalent"], tier=3, batch=1,
  vd=("refactor_only", "card-pool option API rewritten (WithCustomPool -> WithCardPools with Union)"))
M(K, "CursedRun", OUT, [
    "AfterActEntered: every player gains a random curse (RunRng.Niche over CurseCardPool unlocked cards with CanBeGeneratedByModifiers) into the deck",
], reqs=[CORE, "AfterActEntered", "run:deck", "kernel:card_generation"], hooks=["AfterActEntered"], tier=3, batch=1,
  rng=["RunRngType.Niche: NextItem over the eligible curses, once per player per act"],
  vd=("refactor_only", "per-player loop rewritten as an async state machine"))
M(K, "DeadlyEvents", OUT, [
    "AfterRunCreated: removes JuzuBracelet from every relic grab bag; unknown-room elite odds 0.1 (base odds too)",
    "AfterRunLoaded: re-applies the elite base odds",
    "ModifyOddsIncreaseForUnrolledRoomType: treasure odds increase doubled",
], reqs=[CORE, "ModifyOddsIncreaseForUnrolledRoomType", "run:odds", "run:relic_grab_bags", "ModifierModel.AfterRunCreated",
         "ModifierModel.AfterRunLoaded"], hooks=["AfterRunCreated", "AfterRunLoaded", "ModifyOddsIncreaseForUnrolledRoomType"], tier=3, batch=1)
M(K, "DeprecatedModifier", "deprecated", ["empty placeholder"], tier=1, batch=None)
M(K, "Draft", OUT, [
    "ClearsPlayerDeck = true",
    NEOW + ": 10 CardRewards of 3 cards (character pool, regular encounter odds, NoUpgradeRoll), CanSkip = false, then PandorasBox leaves every grab bag",
], reqs=[CORE, "choice:CardReward", "run:rewards", "run:relic_grab_bags", "run:neow_and_ancients"], hooks=["GenerateNeowOption", "ClearsPlayerDeck"],
  vd=("refactor_only", "loop rewritten as an async state machine"),
  prompts=[{"kind": "CardReward", "entry": "CardReward.SelectUnsynchronized", "site": "DRAFT:GenerateNeowOption", "opened_by": "modifiers:DRAFT",
            "via": None, "pile": "generated", "min": 1, "max": 1, "require_confirm": False, "can_skip": False, "cancelable": False,
            "context": "Inline", "filter": None, "cite": "Models.Modifiers/Draft.cs:62", "notes": "10 rewards in a row, 3 cards each, no skip"}], tier=3, batch=1)
M(K, "Flight", OUT, ["ShouldAllowFreeTravel: true (map travel ignores connections)"], reqs=[CORE, "ShouldAllowFreeTravel", "run:map_generation"],
  hooks=["ShouldAllowFreeTravel"], tier=3, batch=1)
M(K, "Hoarder", OUT, [
    "AfterCardChangedPiles: a card entering the deck from nowhere (old pile None, not a clone) adds 2 clones to the bottom of the deck (RunState.CloneCard), skipping the clones themselves",
    "ShouldAllowMerchantCardRemoval: false",
], reqs=[CORE, "AfterCardChangedPiles", "ShouldAllowMerchantCardRemoval", "run:deck", "run:merchant", "kernel:per_instance_state"],
  hooks=["AfterCardChangedPiles", "ShouldAllowMerchantCardRemoval"], tier=3, batch=1,
  vd=("refactor_only", "loop and inverted guard rewritten by the state machine (oldPileType != None -> == None branch)"))
M(K, "Insanity", OUT, [
    "ClearsPlayerDeck = true",
    NEOW + ": 30 cards from the character pool (uniform odds, NoRarityModification) into the deck, then PandorasBox leaves every grab bag",
], reqs=[CORE, "kernel:card_generation", "run:deck", "run:relic_grab_bags", "run:neow_and_ancients"], hooks=["GenerateNeowOption", "ClearsPlayerDeck"],
  rng=["the Neow event Rng for the card rolls"], tier=3, batch=1, vd=("refactor_only", "loop rewritten as an async state machine; CreateForReward returns CardCreationResult wrappers"))
M(K, "Midas", OUT, [
    "TryModifyRewardsLate: every GoldReward amount doubled",
    "TryModifyRestSiteOptions: removes Smith",
], reqs=[CORE, "TryModifyRewardsLate", "TryModifyRestSiteOptions", "run:rewards", "run:rest_site"],
  hooks=["TryModifyRewardsLate", "TryModifyRestSiteOptions"], tier=3, batch=1)
M(K, "Murderous", OUT, [
    "AfterRoomEntered (combat room): StrengthPower 3 to every creature in combat",
    "AfterCreatureAddedToCombat: StrengthPower 3 to every non-player creature added later",
], reqs=[CORE, "AfterRoomEntered", "AfterCreatureAddedToCombat", "run:room_flow"], hooks=["AfterRoomEntered", "AfterCreatureAddedToCombat"],
  deps={"powers": ["StrengthPower"]}, tier=3, batch=1, notes="one of the two modifiers that listen to combat hooks")
M(K, "NightTerrors", OUT, [
    "ModifyRestSiteHealAmount: heal to full (MaxHp)",
    "AfterRestSiteHeal (not mimicked): CreatureCmd.LoseMaxHp(min(5, MaxHp - 1))",
], reqs=[CORE, "ModifyRestSiteHealAmount", "AfterRestSiteHeal", "ModifyExtraRestSiteHealText", "run:rest_site"],
  hooks=["ModifyRestSiteHealAmount", "AfterRestSiteHeal", "ModifyExtraRestSiteHealText"], tier=3, batch=1)
M(K, "SealedDeck", OUT, [
    "ClearsPlayerDeck = true",
    NEOW + ": 30 generated character cards (regular encounter odds, NoUpgradeRoll | ForceRarityOddsChange | IsCardReward) shown in a grid sorted by rarity; pick exactly 10 (CardSelectorPrefs(prompt, 10), RequireManualConfirmation, not cancelable); picks go to the deck; PandorasBox leaves every grab bag",
], reqs=[CORE, "kernel:card_generation", "CardSelectCmd.FromSimpleGridForRewards", "run:deck", "run:relic_grab_bags", "run:neow_and_ancients"],
  hooks=["GenerateNeowOption", "ClearsPlayerDeck"],
  prompts=[{"kind": "RewardsGrid", "entry": "CardSelectCmd.FromSimpleGridForRewards", "site": "SEALED_DECK:GenerateNeowOption",
            "opened_by": "modifiers:SEALED_DECK", "via": None, "pile": "generated", "min": 10, "max": 10, "require_confirm": True,
            "can_skip": False, "cancelable": False, "context": "Inline", "filter": None, "cite": "Models.Modifiers/SealedDeck.cs:71",
            "notes": "CardSelectorPrefs(prompt, 10) with Cancelable = false, RequireManualConfirmation = true, Comparison = rarity order (:162)"}],
  tier=3, batch=1, notes="grid order: rarity, then localized Title (LocManager culture; CompareCards :162)",
  vd=("refactor_only", "enum CompareTo and CompareOptions spelling only"))
M(K, "Specialized", OUT, [
    NEOW + ": one random character card (uniform odds) cloned 5 times into the deck",
], reqs=[CORE, "kernel:card_generation", "run:deck", "run:neow_and_ancients"], hooks=["GenerateNeowOption"],
  rng=["the Neow event Rng for the card roll"], tier=3, batch=1, vd=("refactor_only", "loop rewritten as an async state machine; CreateForReward returns CardCreationResult wrappers"))
M(K, "Terminal", OUT, [
    "AfterRoomEntered (a new base room): every player loses 1 max HP (skipped at 1/1); in a combat room every player creature gains PlatingPower 5",
], reqs=[CORE, "AfterRoomEntered", "run:room_flow"], hooks=["AfterRoomEntered"], deps={"powers": ["PlatingPower"]}, tier=3, batch=1,
  vd=("refactor_only", "guards inverted by the state machine; same max-HP loss and Plating"))
M(K, "Vintage", OUT, [
    "TryModifyRewardsLate: in normal (Monster) combat rooms every CardReward becomes a RelicReward",
], reqs=[CORE, "TryModifyRewardsLate", "run:rewards"], hooks=["TryModifyRewardsLate"], tier=3, batch=1)

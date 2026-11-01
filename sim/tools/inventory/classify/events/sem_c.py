from semlib import O, P, R, CB, V

DATA = {}

DATA["TeaMaster"] = dict(
    category="shared_event",
    appearance=dict(pool="shared", allowed_act_indices=[0, 1], is_allowed="CurrentActIndex < 2 and every player Gold >= 150",
                    is_allowed_cite="m:IsAllowed", unlock_gate=None, entry="shared event pool (every act list)"),
    layout="Default",
    options=[
        O("INITIAL", "BONE_TEA", "LoseGold 50 (Spent), obtain BoneTea", cond="BONE_TEA_LOCKED when Gold < 50",
          cite="m:BoneTea"),
        O("INITIAL", "EMBER_TEA", "LoseGold 150 (Spent), obtain EmberTea", cond="EMBER_TEA_LOCKED when Gold < 150",
          cite="m:EmberTea"),
        O("INITIAL", "TEA_OF_DISCOURTESY", "obtain TeaOfDiscourtesy (free)", cite="m:TeaOfDiscourtesy"),
    ],
    outcomes=dict(gold=["-50 or -150"], relics=["BoneTea, EmberTea or TeaOfDiscourtesy"]),
    prompts=[],
    rng_streams=[],
    mechanics=["pay gold for fixed relic", "obtain fixed relic"],
    kernel_requirements=["PlayerCmd.LoseGold (Spent)", "RelicCmd.Obtain<T>"],
    content_deps=dict(relics=["BoneTea", "EmberTea", "TeaOfDiscourtesy"]),
    ascension="none",
    unlock_progress=[],
    version=V("same", "only decompiler style differs"),
    tier=1, tier_reason="gold and fixed relics",
    port_batch=1,
    notes=["The lock checks are the only use of current gold; IsAllowed already guarantees >= 150 on entry."],
    open_questions=[],
)

DATA["TheFutureOfPotions"] = dict(
    category="shared_event",
    appearance=dict(pool="shared", allowed_act_indices=[0, 1, 2], is_allowed="every player holds >= 2 potions",
                    is_allowed_cite="m:IsAllowed", unlock_gate=None, entry="shared event pool (every act list)"),
    layout="Default",
    options=[
        O("INITIAL", "POTION (one per held potion, first 3 in slot order)", "PotionCmd.Discard(that potion), then "
          "OfferCustom CardReward(3 character-pool cards of the mapped rarity and the pre-rolled type, Uniform, "
          "NoRarityModification|NoCardPoolModifications), every generated card upgraded (AfterGenerated)",
          cond="rarity map: Rare/Event -> Rare, Uncommon -> Uncommon, Common/Token -> Common", cite="m:Trade"),
    ],
    outcomes=dict(potions=["-1 chosen potion"], cards=["card reward of 3 upgraded cards"]),
    prompts=[P("RewardsCmd.OfferCustom", 0, 1, "CardReward x1 (3 upgraded cards)", "POTION", "re:OfferCustom")],
    rng_streams=[
        R("EventRng", "GenerateInitialOptions (first PotionToCardType access)", "NextItem over {Attack, Skill, Power} "
          "(Power removed for Common/Token) for every held potion in Owner.Potions order", "m:PotionToCardType"),
        R("PlayerRng.Rewards", "reward populate", "3 x NextItem (Uniform, NoUpgradeRoll); AfterGenerated then upgrades every card"),
    ],
    mechanics=["trade potion for card reward", "upgrade generated cards", "lock potions during the event"],
    kernel_requirements=["PotionLock (Player.CanUseOrRemovePotions)", "PotionCmd.Discard",
                         "CardReward.AfterGenerated event", "CardCreationOptions (Uniform, rarity+type filter)",
                         "RewardsCmd.OfferCustom (CardReward)", "CardCmd.Upgrade"],
    content_deps={},
    ascension="none",
    unlock_progress=["candidates = unlocked character-pool cards"],
    version=V("cosmetic", "Player.CanRemovePotions renamed CanUseOrRemovePotions (same popup behaviour)",
              mech="identifier-rename"),
    tier=3, tier_reason="rewards screen",
    port_batch=3,
    notes=["The card type of every held potion is rolled once, even for potions beyond the first three options."],
    open_questions=["If no card matches the rarity+type filter, CardFactory throws; is that reachable with the "
                    "unlocked pools of every character (e.g. Rare Power with few unlocks)?"],
)

DATA["TheLanternKey"] = dict(
    category="act_event",
    appearance=dict(pool="act", allowed_act_indices=[1], is_allowed=None, unlock_gate=None,
                    entry="act event pool (Hive)"),
    layout="Combat",
    is_shared=True,
    options=[
        O("INITIAL", "RETURN_THE_KEY", "GainGold 100", cite="m:ReturnTheKey"),
        O("INITIAL", "KEEP_THE_KEY", "nothing yet", nxt="page KEEP_THE_KEY {FIGHT}", cite="m:KeepTheKey"),
        O("KEEP_THE_KEY", "FIGHT", "combat MysteriousKnightEventEncounter with extra SpecialCardReward(LanternKey); "
          "no resume", nxt="event combat", cite="m:Fight"),
    ],
    outcomes=dict(gold=["+100 (RETURN_THE_KEY)"], cards=["LanternKey quest card (FIGHT reward)"],
                  combat=["MysteriousKnight"]),
    combats=[CB("MysteriousKnightEventEncounter", "EnterCombatWithoutExitingEvent", False,
                ["SpecialCardReward(LanternKey)"], "m:Fight", "normal Monster-room rewards plus the key")],
    prompts=[P("EventCombat", None, None, "combat", "FIGHT", "m:Fight")],
    rng_streams=[
        R("RunRng.Niche", "room entry, CombatLayout", "CreateCreature unique-HP draw for the layout MysteriousKnight",
          "Combat/CombatState.cs:717"),
        R("combat streams", "FIGHT", "combat kernel + reward rolls"),
    ],
    mechanics=["gain gold", "event combat (no resume)", "quest card reward", "combat-layout event"],
    kernel_requirements=["CombatLayout (EventCombatSynchronizer.InitializeForEvent)", "EventCombat (extra rewards)",
                         "SpecialCardReward", "PlayerCmd.GainGold"],
    content_deps=dict(cards=["LanternKey"], encounters=["MysteriousKnightEventEncounter"], monsters=["MysteriousKnight"]),
    ascension="A3 Poverty scales the fight's gold; A8/A9 via MysteriousKnight",
    unlock_progress=[],
    version=V("cosmetic", "v0.107.1 built one SpecialCardReward per player; v0.111.0 only for the owner"),
    tier=3, tier_reason="combat layout + event combat",
    port_batch=4,
    notes=["LanternKey in the deck makes Hook.ModifyNextEvent replace the next pulled event with WarHistorianRepy "
           "while CurrentActIndex == 2 (Models.Cards/LanternKey.cs:32-39)."],
    open_questions=[],
)

DATA["TheLegendsWereTrue"] = dict(
    category="shared_event",
    appearance=dict(pool="shared", allowed_act_indices=[0],
                    is_allowed="CurrentActIndex == 0, every player has a non-empty deck and CurrentHp >= 10",
                    is_allowed_cite="m:IsAllowed", unlock_gate=None, entry="shared event pool (every act list)"),
    layout="Default",
    options=[
        O("INITIAL", "NAB_THE_MAP", "add SpoilsMap (quest card)", cite="m:NabTheMap"),
        O("INITIAL", "SLOWLY_FIND_AN_EXIT", "Damage 8 (Unblockable|Unpowered), OfferCustom PotionReward(random unlocked "
          "character+shared potion)", cite="m:SlowlyFindAnExit"),
    ],
    outcomes=dict(hp=["-8"], cards=["+SpoilsMap"], potions=["random potion reward"]),
    prompts=[P("RewardsCmd.OfferCustom", 0, 1, "PotionReward (random)", "SLOWLY_FIND_AN_EXIT", "re:OfferCustom")],
    rng_streams=[R("PlayerRng.Rewards", "SLOWLY_FIND_AN_EXIT", "NextItem over unlocked potions",
                   "re:PlayerRng\\.Rewards")],
    mechanics=["add quest card", "lose HP", "potion reward"],
    kernel_requirements=["RunState.CreateCard<SpoilsMap> (AfterCreated sets SpoilsActIndex=1)",
                         "Hook.ModifyGeneratedMap (SpoilsMap -> SpoilsActMap for act index 1)",
                         "CreatureCmd.Damage (out of combat)", "PotionPools", "RewardsCmd.OfferCustom (PotionReward)"],
    content_deps=dict(cards=["SpoilsMap"]),
    ascension="A4 TightBelt affects whether the potion fits",
    unlock_progress=["potion candidates follow unlock state"],
    version=V("same", "only decompiler style differs"),
    tier=3, tier_reason="rewards screen; SpoilsMap is cross-act map content",
    port_batch=3,
    notes=["SpoilsMap changes the act-2 map (SpoilsActMap, design-run-layer.md 4.3) and treasure gold "
           "(OneOffSynchronizer.TryHandleSpoilsMap)."],
    open_questions=[],
)

DATA["ThisOrThat"] = dict(
    category="shared_event",
    appearance=dict(pool="shared", allowed_act_indices=[0, 1, 2], is_allowed=None, unlock_gate=None,
                    entry="shared event pool (every act list)"),
    layout="Default",
    options=[
        O("INITIAL", "PLAIN", "Damage 6 (Unblockable|Unpowered), GainGold Gold (NextInt(41,69) = 41..68)", cite="m:Plain"),
        O("INITIAL", "ORNATE", "obtain PullNextRelicFromFront, then AddCurseToDeck<Clumsy>", cite="m:Ornate"),
    ],
    outcomes=dict(hp=["-6"], gold=["+41..68"], relics=["random relic"], cards=["+Clumsy"]),
    prompts=[],
    rng_streams=[
        R("EventRng", "CalculateVars (event start)", "NextInt(41,69)", "m:CalculateVars"),
        R("PlayerRng.Rewards", "ORNATE", "relic rarity NextFloat", "re:PullNextRelicFromFront"),
    ],
    mechanics=["lose HP", "gain gold", "obtain random relic", "add curse"],
    kernel_requirements=["CreatureCmd.Damage (out of combat)", "PlayerCmd.GainGold", "RelicFactory.PullNextRelicFromFront",
                         "CardPileCmd.AddCurseToDeck<T>"],
    content_deps=dict(cards=["Clumsy"]),
    ascension="none",
    unlock_progress=["relic grab bag contents follow unlock state"],
    version=V("same", "only decompiler style differs"),
    tier=2, tier_reason="relic grab bag",
    port_batch=2,
    notes=[],
    open_questions=[],
)

DATA["TinkerTime"] = dict(
    category="act_event",
    appearance=dict(pool="act", allowed_act_indices=[2], is_allowed=None, unlock_gate=None,
                    entry="act event pool (Glory)"),
    layout="Default",
    options=[
        O("INITIAL", "CHOOSE_CARD_TYPE", "nothing yet", nxt="page CHOOSE_CARD_TYPE: TakeRandom(2) of {ATTACK, SKILL, "
          "POWER}", cite="m:ChooseCardType"),
        O("CHOOSE_CARD_TYPE", "ATTACK / SKILL / POWER", "set ChosenCardType", nxt="page CHOOSE_RIDER: TakeRandom(2) of "
          "the type's 3 riders", cite="m:ChooseRiderEffect"),
        O("CHOOSE_RIDER", "<rider>", "create MadScience with TinkerTimeType and TinkerTimeRider, add to deck",
          cite="m:RiderChosen",
          notes="riders ([v107] arrays): Attack {Sapping, Violence, Choking}; Skill {Energized, Wisdom, Chaos}; "
                "Power {Expertise, Curious, Improvement}"),
    ],
    outcomes=dict(cards=["+MadScience configured by type and rider"]),
    prompts=[],
    rng_streams=[
        R("EventRng", "CHOOSE_CARD_TYPE", "TakeRandom(2): UnstableShuffle of 3 (2 NextInt draws), take 2",
          "m:ChooseCardType"),
        R("EventRng", "type chosen", "TakeRandom(2) of the 3 riders (2 NextInt draws)", "m:ChooseRiderEffect"),
    ],
    mechanics=["build a configurable card", "multi-page"],
    kernel_requirements=["MadScience card state (TinkerTimeType, TinkerTimeRider) cloned/serialized with the deck",
                         "IEnumerableExtensions.TakeRandom", "RunState.CreateCard<MadScience> (hover-tip creations have "
                         "no rule effect)"],
    content_deps=dict(cards=["MadScience"], powers=["WeakPower", "VulnerablePower", "StranglePower", "StrengthPower",
                                                    "DexterityPower"]),
    ascension="none",
    unlock_progress=[],
    version=V("same", "fingerprint equal; v0.111.0 lost the three RiderEffect arrays (RuntimeHelpers.InitializeArray)"),
    tier=4, tier_reason="rider arrays unreadable in v0.111.0; MadScience rider state is cross-cutting card content",
    port_batch=6,
    notes=["CanonicalVars (Damage 12, Block 8, SappingWeak/Vulnerable 2, ViolenceHits 3, ChokingDamage 6, "
           "EnergizedEnergy 2, WisdomCards 3, ExpertiseStrength/Dexterity 2, CuriousReduction 1) describe MadScience "
           "riders; the effects live in Models.Cards/MadScience.cs."],
    open_questions=["Confirm the rider array contents and order in v0.111.0 IL (Models.Events/TinkerTime.cs:186-205); "
                    "order matters for TakeRandom."],
)

DATA["TrashHeap"] = dict(
    category="act_event",
    appearance=dict(pool="act", allowed_act_indices=[0], is_allowed="every player CurrentHp > 5",
                    is_allowed_cite="m:IsAllowed", unlock_gate="Event1Epoch",
                    entry="act event pool (Underdocks); removed unless Event1Epoch is revealed (Models/ActModel.cs:247-250)"),
    layout="Default",
    options=[
        O("INITIAL", "DIVE_IN", "Damage 8 (Unblockable|Unpowered), obtain a random relic of {DarkstonePeriapt, "
          "DreamCatcher, HandDrill, MawBank, TheBoot}", cite="m:DiveIn"),
        O("INITIAL", "GRAB", "GainGold 100, add a random card of {Caltrops, Clash, Distraction, DualWield, Entrench, "
          "HelloWorld, Outmaneuver, Rebound, RipAndTear, Stack}", cite="m:Grab"),
    ],
    outcomes=dict(hp=["-8"], gold=["+100"], relics=["1 of 5 fixed"], cards=["1 of 10 fixed"]),
    prompts=[],
    rng_streams=[R("EventRng", "chosen option", "NextItem over the 5 relics or the 10 cards", "re:Rng\\.NextItem")],
    mechanics=["lose HP", "obtain random relic from a fixed list", "gain gold", "add random card from a fixed list"],
    kernel_requirements=["CreatureCmd.Damage (out of combat)", "RelicCmd.Obtain", "PlayerCmd.GainGold",
                         "RunState.CreateCard", "CardPileCmd.Add (Deck)"],
    content_deps=dict(relics=["DarkstonePeriapt", "DreamCatcher", "HandDrill", "MawBank", "TheBoot"],
                      cards=["Caltrops", "Clash", "Distraction", "DualWield", "Entrench", "HelloWorld", "Outmaneuver",
                             "Rebound", "RipAndTear", "Stack"]),
    ascension="none",
    unlock_progress=["Event1Epoch gate"],
    version=V("same", "only decompiler style differs"),
    tier=1, tier_reason="fixed lists on the event Rng",
    port_batch=1,
    notes=["The cards are other characters' cards, so the event needs those card models regardless of character."],
    open_questions=[],
)

DATA["Trial"] = dict(
    category="act_event",
    appearance=dict(pool="act", allowed_act_indices=[2], is_allowed=None, unlock_gate=None,
                    entry="act event pool (Glory)"),
    layout="Default",
    options=[
        O("INITIAL", "ACCEPT", "EventRng.NextInt(3) picks the case", nxt="page MERCHANT, NOBLE or NONDESCRIPT "
          "{GUILTY, INNOCENT}", cite="m:Accept"),
        O("INITIAL", "REJECT", "nothing", nxt="page REJECT {ACCEPT, DOUBLE_DOWN}", cite="m:Reject"),
        O("REJECT", "DOUBLE_DOWN", "opens the abandon-run confirmation (node layer); not disabled after use, IsProceed",
          cite="m:DoubleDown"),
        O("MERCHANT", "GUILTY", "AddCurseToDeck<Regret>, obtain PullNextRelicFromFront x2", cite="m:MerchantGuilty"),
        O("MERCHANT", "INNOCENT", "AddCurseToDeck<Shame>, upgrade 2 chosen cards", cite="m:MerchantInnocent"),
        O("NOBLE", "GUILTY", "Heal 10", cite="m:NobleGuilty"),
        O("NOBLE", "INNOCENT", "AddCurseToDeck<Regret>, GainGold 300", cite="m:NobleInnocent"),
        O("NONDESCRIPT", "GUILTY", "AddCurseToDeck<Doubt>, OfferCustom 2 CardRewards (character pool, default odds, 3 "
          "cards)", cite="m:NondescriptGuilty"),
        O("NONDESCRIPT", "INNOCENT", "AddCurseToDeck<Doubt>, transform 2 chosen cards (event Rng)",
          cite="m:NondescriptInnocent"),
    ],
    outcomes=dict(hp=["+10 (NOBLE GUILTY)"], gold=["+300 (NOBLE INNOCENT)"],
                  cards=["curse Regret/Shame/Doubt", "upgrade 2", "transform 2", "2 card rewards"],
                  relics=["2 random relics (MERCHANT GUILTY)"], other=["DOUBLE_DOWN: abandon run"]),
    prompts=[
        P("CardSelectCmd.FromDeckForUpgrade", 2, 2, "deck", "MERCHANT INNOCENT", "re:FromDeckForUpgrade",
          filt="IsUpgradable", auto="auto when <= 2 candidates"),
        P("CardSelectCmd.FromDeckForTransformation", 2, 2, "deck", "NONDESCRIPT INNOCENT",
          "re:FromDeckForTransformation", filt="Type != Quest && IsTransformable", auto="auto when <= 2 candidates"),
        P("RewardsCmd.OfferCustom", 0, 2, "CardReward x2", "NONDESCRIPT GUILTY", "re:OfferCustom"),
        P("AbandonRunConfirm", None, None, "modal", "DOUBLE_DOWN", "m:DoubleDown",
          notes="NAbandonRunConfirmPopup: confirm = abandon (run lost), cancel = back to the REJECT page"),
    ],
    rng_streams=[
        R("Rng.Chaotic", "CalculateVars", "EntrantNumber display only (non-deterministic)", "m:CalculateVars"),
        R("EventRng", "ACCEPT", "NextInt(3)", "re:Rng\\.NextInt\\(3\\)"),
        R("PlayerRng.Rewards", "MERCHANT GUILTY", "2 x relic rarity NextFloat", "re:PullNextRelicFromFront"),
        R("EventRng", "NONDESCRIPT INNOCENT", "TransformToRandom NextItem x2", "re:TransformToRandom"),
        R("PlayerRng.Rewards", "NONDESCRIPT GUILTY populate", "2 x 3 cards: rarity NextFloat (base odds) + NextItem; no upgrade roll"),
    ],
    mechanics=["random case", "add curse", "obtain random relics", "upgrade cards (choice, 2)", "heal", "gain gold",
               "card rewards", "transform cards (choice, 2)", "abandon run"],
    kernel_requirements=["CardPileCmd.AddCurseToDeck<T>", "RelicFactory.PullNextRelicFromFront",
                         "CardSelectCmd.FromDeckForUpgrade", "CardCmd.Upgrade", "CardSelectCmd.FromDeckForTransformation",
                         "CardCmd.TransformToRandom(rng)", "RewardsCmd.OfferCustom (CardReward)", "CreatureCmd.Heal",
                         "PlayerCmd.GainGold", "abandon-run terminal action (node-layer popup)"],
    content_deps=dict(cards=["Regret", "Shame", "Doubt"]),
    ascension="A7 Scarcity changes NONDESCRIPT GUILTY base rarity odds",
    unlock_progress=[],
    version=V("same", "only decompiler style differs"),
    tier=3, tier_reason="upgrade/transform prompts, rewards screen, abandon modal",
    port_batch=3,
    notes=["DOUBLE_DOWN is the only event option that can end the run by abandonment; the sim can model it as a "
           "terminal loss action or exclude it from the legal set by policy."],
    open_questions=["How should the sim and the diff gate treat DOUBLE_DOWN (abandon) and its cancel path?"],
)

DATA["UnrestSite"] = dict(
    category="act_event",
    appearance=dict(pool="act", allowed_act_indices=[0], is_allowed="every player CurrentHp <= MaxHp x 0.70",
                    is_allowed_cite="m:IsAllowed", unlock_gate=None, entry="act event pool (Overgrowth)"),
    layout="Default",
    options=[
        O("INITIAL", "REST", "Heal MaxHp - CurrentHp (computed in CalculateVars), AddCursesToDeck([PoorSleep])",
          cite="m:Rest"),
        O("INITIAL", "KILL", "LoseMaxHp 8, obtain PullNextRelicFromFront", cite="m:Kill"),
    ],
    outcomes=dict(hp=["full heal (REST)"], max_hp=["-8 (KILL)"], cards=["+PoorSleep (REST)"],
                  relics=["random relic (KILL)"]),
    prompts=[],
    rng_streams=[R("PlayerRng.Rewards", "KILL", "relic rarity NextFloat", "re:PullNextRelicFromFront")],
    mechanics=["heal to full", "add curse", "lose max HP", "obtain random relic"],
    kernel_requirements=["CreatureCmd.Heal", "CardPileCmd.AddCursesToDeck", "CreatureCmd.LoseMaxHp",
                         "RelicFactory.PullNextRelicFromFront"],
    content_deps=dict(cards=["PoorSleep"]),
    ascension="none",
    unlock_progress=["relic grab bag contents follow unlock state"],
    version=V("same", "only decompiler style differs"),
    tier=2, tier_reason="relic grab bag",
    port_batch=2,
    notes=["Trap: the heal amount is fixed at event start (CalculateVars), not when REST is chosen."],
    open_questions=[],
)

DATA["WarHistorianRepy"] = dict(
    category="hook_injected_event",
    appearance=dict(pool="shared", allowed_act_indices=[2], is_allowed="always false (never drawn normally)",
                    is_allowed_cite="m:IsAllowed", unlock_gate=None,
                    entry="Hook.ModifyNextEvent: a LanternKey in the deck replaces the pulled event while "
                          "CurrentActIndex == 2 (Models/ActModel.cs:380-386, Models.Cards/LanternKey.cs:32-39)"),
    layout="Default",
    is_shared=True,
    options=[
        O("INITIAL", "UNLOCK_CAGE", "consume the first LanternKey (CompleteQuest + remove), set ExtraFields.FreedRepy, "
          "obtain HistoryCourse", nxt="page UNLOCK_CAGE {UNLOCK_CHEST} while another LanternKey remains, else finish",
          cite="m:InitialUnlockCage"),
        O("INITIAL", "UNLOCK_CHEST", "consume the first LanternKey, OfferCustom [PotionReward, PotionReward, "
          "RelicReward, RelicReward] (all random)", nxt="page UNLOCK_CHEST {UNLOCK_CAGE} while another LanternKey "
          "remains, else finish", cite="m:InitialUnlockChest"),
        O("UNLOCK_CAGE / UNLOCK_CHEST", "second unlock", "finish first, consume every remaining LanternKey, then the "
          "other unlock", cite="m:SecondUnlockCage"),
    ],
    outcomes=dict(cards=["-LanternKey(s)"], relics=["HistoryCourse and/or 2 random relic rewards"],
                  potions=["2 random potion rewards"], other=["RunState.ExtraFields.FreedRepy = true (Queen boss "
                                                            "background VFX only)"]),
    prompts=[P("RewardsCmd.OfferCustom", 0, 4, "2 PotionReward + 2 RelicReward", "UNLOCK_CHEST", "m:UnlockChest")],
    rng_streams=[R("PlayerRng.Rewards", "UNLOCK_CHEST populate", "2 potion rolls + 2 relic rarity NextFloat")],
    mechanics=["consume quest card", "obtain fixed relic", "potion and relic rewards", "set run flag"],
    kernel_requirements=["Hook.ModifyNextEvent (deck-card listener at run scope)", "PlayerCmd.CompleteQuest",
                         "CardPileCmd.RemoveFromDeck", "RelicCmd.Obtain<T>", "RewardsCmd.OfferCustom",
                         "ExtraRunFields.FreedRepy (save field)"],
    content_deps=dict(cards=["LanternKey"], relics=["HistoryCourse"]),
    ascension="none",
    unlock_progress=[],
    version=V("same", "only decompiler style differs"),
    tier=3, tier_reason="rewards screen; reached only through the LanternKey chain",
    port_batch=5,
    notes=["A second reward needs >= 2 LanternKeys in the deck in single player (e.g. after Reflections SHATTER).",
           "Listed in ModelDb.AllSharedEvents, so it sits in every act's shuffled list but IsAllowed always skips it."],
    open_questions=[],
)

DATA["WaterloggedScriptorium"] = dict(
    category="act_event",
    appearance=dict(pool="act", allowed_act_indices=[0], is_allowed="every player Gold >= 55",
                    is_allowed_cite="m:IsAllowed", unlock_gate=None, entry="act event pool (Underdocks)"),
    layout="Default",
    options=[
        O("INITIAL", "BLOODY_INK", "GainMaxHp 6", cite="m:BloodyInk"),
        O("INITIAL", "TENTACLE_QUILL", "LoseGold 55 (Spent), enchant 1 chosen card with Steady(1)",
          cond="TENTACLE_QUILL_LOCKED when Gold < 55", cite="m:TentacleQuill"),
        O("INITIAL", "PRICKLY_SPONGE", "LoseGold 99 (Spent), enchant 2 chosen cards with Steady(1)",
          cond="PRICKLY_SPONGE_LOCKED when Gold < 99", cite="m:PricklySponge"),
    ],
    outcomes=dict(max_hp=["+6"], gold=["-55 or -99"], cards=["Steady on 1 or 2 cards"]),
    prompts=[
        P("CardSelectCmd.FromDeckForEnchantment", 1, 1, "deck", "TENTACLE_QUILL", "re:EnchantSelectionPrompt, 1\\)",
          filt="Steady.CanEnchant", auto="auto when 1 candidate"),
        P("CardSelectCmd.FromDeckForEnchantment", 2, 2, "deck", "PRICKLY_SPONGE", "re:Cards\\.IntValue",
          filt="Steady.CanEnchant", auto="auto when <= 2 candidates"),
    ],
    rng_streams=[],
    mechanics=["gain max HP", "pay gold to enchant (choice)"],
    kernel_requirements=["CreatureCmd.GainMaxHp", "PlayerCmd.LoseGold (Spent)", "CardSelectCmd.FromDeckForEnchantment",
                         "CardCmd.Enchant<Steady>"],
    content_deps=dict(enchantments=["Steady"]),
    ascension="none",
    unlock_progress=[],
    version=V("same", "only decompiler style differs"),
    tier=3, tier_reason="enchant prompts",
    port_batch=3,
    notes=[],
    open_questions=[],
)

DATA["WelcomeToWongos"] = dict(
    category="shared_event",
    appearance=dict(pool="shared", allowed_act_indices=[1], is_allowed="CurrentActIndex == 1 and every player Gold >= 100",
                    is_allowed_cite="m:IsAllowed", unlock_gate=None, entry="shared event pool (every act list)"),
    layout="Default",
    options=[
        O("INITIAL", "BARGAIN_BIN", "LoseGold 100, obtain PullNextRelicFromFront(Common, IsAllowedInShops), +32 Wongo "
          "points", cond="_LOCKED when Gold < 100", cite="m:BuyBargainBin"),
        O("INITIAL", "FEATURED_ITEM", "LoseGold 200, obtain the pre-pulled Rare FeaturedItem, +16 points",
          cond="_LOCKED when Gold < 200", cite="m:BuyFeaturedItem"),
        O("INITIAL", "MYSTERY_BOX", "LoseGold 300, obtain WongosMysteryTicket, +8 points", cond="_LOCKED when Gold < 300",
          cite="m:BuyMysteryBox"),
        O("INITIAL", "LEAVE", "downgrade 1 random upgraded card (if any)", cite="m:Leave"),
    ],
    outcomes=dict(gold=["-100 / -200 / -300"], relics=["random Common, the featured Rare, or WongosMysteryTicket",
                                                      "WongoCustomerAppreciationBadge when profile points cross 2000"],
                  cards=["downgrade 1 random (LEAVE)"]),
    prompts=[],
    rng_streams=[
        R("grab bag (no RNG draw)", "GenerateInitialOptions", "PullNextRelicFromFront(Rare, IsAllowedInShops): the "
          "featured Rare leaves the grab bag even if not bought", "m:GenerateInitialOptions"),
        R("grab bag (no RNG draw)", "BARGAIN_BIN", "PullNextRelicFromFront(Common, IsAllowedInShops)", "m:BuyBargainBin"),
        R("EventRng", "LEAVE", "NextItem over upgraded cards", "m:Leave"),
    ],
    mechanics=["pay gold for relic", "profile-progress reward (badge)", "downgrade random card"],
    kernel_requirements=["RelicFactory.PullNextRelicFromFront(rarity, filter) (no rarity roll)", "RelicModel.IsAllowedInShops",
                         "PlayerCmd.LoseGold (Spent)", "RelicCmd.Obtain", "CardCmd.Downgrade",
                         "profile input SaveManager.Progress.WongoPoints (explicit sim parameter)",
                         "ExtraPlayerFields.WongoPoints (save field)"],
    content_deps=dict(relics=["WongosMysteryTicket", "WongoCustomerAppreciationBadge"]),
    ascension="none",
    unlock_progress=["badge relic depends on profile WongoPoints (Models.Events/WelcomeToWongos.cs:630-651)"],
    version=V("same", "v0.111.0 decompile emits CheckObtainWongoBadge twice (state machine + async body); same logic",
              mech="fingerprint-differs (duplicate decompile)"),
    tier=2, tier_reason="grab bag + profile parameter (no prompt)",
    port_batch=2,
    notes=["MysteryBoxRelicCount 3 and MysteryBoxCombatCount 5 describe the WongosMysteryTicket relic, which carries "
           "the effect."],
    open_questions=["Profile WongoPoints for the diff gate: recordings must carry the profile value at run start, "
                    "or the badge branch is unknowable."],
)

DATA["Wellspring"] = dict(
    category="act_event",
    appearance=dict(pool="act", allowed_act_indices=[0], is_allowed=None, unlock_gate=None,
                    entry="act event pool (Overgrowth)"),
    layout="Default",
    options=[
        O("INITIAL", "BOTTLE", "OfferCustom PotionReward(random unlocked character+shared potion)", cite="m:Bottle"),
        O("INITIAL", "BATHE", "remove 1 chosen card, AddCursesToDeck(Guilty x BatheCurses(1))", cite="m:Bathe"),
    ],
    outcomes=dict(potions=["random potion reward"], cards=["-1 card, +Guilty"]),
    prompts=[
        P("RewardsCmd.OfferCustom", 0, 1, "PotionReward (random)", "BOTTLE", "re:OfferCustom"),
        P("CardSelectCmd.FromDeckForRemoval", 1, 1, "deck", "BATHE", "re:FromDeckForRemoval",
          filt="IsRemovable; curses first", auto="auto when 1 candidate"),
    ],
    rng_streams=[R("PlayerRng.Rewards", "BOTTLE", "NextItem over unlocked potions", "re:PlayerRng\\.Rewards")],
    mechanics=["potion reward", "remove card (choice)", "add curse"],
    kernel_requirements=["PotionPools", "RewardsCmd.OfferCustom (PotionReward)", "CardSelectCmd.FromDeckForRemoval",
                         "CardPileCmd.RemoveFromDeck", "CardPileCmd.AddCursesToDeck"],
    content_deps=dict(cards=["Guilty"]),
    ascension="A4 TightBelt affects whether the potion fits",
    unlock_progress=["potion candidates follow unlock state"],
    version=V("same", "only decompiler style differs"),
    tier=3, tier_reason="removal prompt + rewards screen",
    port_batch=3,
    notes=[],
    open_questions=[],
)

DATA["WhisperingHollow"] = dict(
    category="act_event",
    appearance=dict(pool="act", allowed_act_indices=[0], is_allowed="every player Gold >= 44",
                    is_allowed_cite="m:IsAllowed", unlock_gate=None, entry="act event pool (Overgrowth)"),
    layout="Default",
    options=[
        O("INITIAL", "GOLD", "LoseGold Gold (35 + NextInt(-9,10) = 26..44, Spent), OfferCustom 2 random PotionRewards",
          cite="m:Gold"),
        O("INITIAL", "HUG", "transform 1 chosen card (event Rng), then Damage 9 (Unblockable|Unpowered)", cite="m:Hug"),
    ],
    outcomes=dict(gold=["-26..44"], potions=["2 random potion rewards"], cards=["transform 1"], hp=["-9"]),
    prompts=[
        P("RewardsCmd.OfferCustom", 0, 2, "2 PotionRewards (random)", "GOLD", "re:OfferCustom"),
        P("CardSelectCmd.FromDeckForTransformation", 1, 1, "deck", "HUG", "re:FromDeckForTransformation",
          filt="Type != Quest && IsTransformable", auto="auto when 1 candidate"),
    ],
    rng_streams=[
        R("EventRng", "CalculateVars (event start)", "NextInt(-9,10)", "m:CalculateVars"),
        R("PlayerRng.Rewards", "GOLD reward populate", "2 potion rolls"),
        R("EventRng", "HUG", "TransformToRandom NextItem", "re:TransformToRandom"),
    ],
    mechanics=["pay gold for potion rewards", "transform card (choice)", "lose HP"],
    kernel_requirements=["PlayerCmd.LoseGold (Spent)", "RewardsCmd.OfferCustom (PotionReward random)",
                         "CardSelectCmd.FromDeckForTransformation", "CardCmd.TransformToRandom(rng)",
                         "CreatureCmd.Damage (out of combat)"],
    content_deps={},
    ascension="A4 TightBelt affects how many potions fit",
    unlock_progress=["potion/transform candidates follow unlock state"],
    version=V("same", "only decompiler style differs"),
    tier=3, tier_reason="transform prompt + rewards screen",
    port_batch=3,
    notes=[],
    open_questions=[],
)

DATA["WoodCarvings"] = dict(
    category="act_event",
    appearance=dict(pool="act", allowed_act_indices=[0], is_allowed="every player has a removable Basic deck card",
                    is_allowed_cite="m:IsAllowed", unlock_gate=None, entry="act event pool (Overgrowth)"),
    layout="Default",
    options=[
        O("INITIAL", "BIRD", "pick 1 transformable Basic card, CardCmd.TransformTo<Peck>", cite="m:Bird"),
        O("INITIAL", "SNAKE", "enchant 1 chosen card with Slither(1)", cond="SNAKE_LOCKED when no Slither-enchantable "
          "card", cite="m:Snake"),
        O("INITIAL", "TORUS", "pick 1 transformable Basic card, CardCmd.TransformTo<ToricToughness>", cite="m:Torus"),
    ],
    outcomes=dict(cards=["Basic -> Peck", "Slither on 1 card", "Basic -> ToricToughness"]),
    prompts=[
        P("CardSelectCmd.FromDeckGeneric", 1, 1, "deck", "BIRD / TORUS", "re:FromDeckGeneric",
          filt="IsTransformable && Rarity == Basic", auto="auto when 1 candidate"),
        P("CardSelectCmd.FromDeckForEnchantment", 1, 1, "deck", "SNAKE", "re:FromDeckForEnchantment",
          filt="Slither.CanEnchant", auto="auto when 1 candidate"),
    ],
    rng_streams=[],
    mechanics=["transform to fixed card (choice)", "enchant card (choice)"],
    kernel_requirements=["CardSelectCmd.FromDeckGeneric(filter)", "CardCmd.TransformTo<T>",
                         "CardSelectCmd.FromDeckForEnchantment", "CardCmd.Enchant<Slither>"],
    content_deps=dict(cards=["Peck", "ToricToughness"], enchantments=["Slither"]),
    ascension="none",
    unlock_progress=[],
    version=V("same", "only decompiler style differs"),
    tier=3, tier_reason="deck prompts",
    port_batch=3,
    notes=["IsAllowed tests IsRemovable but the BIRD/TORUS filter tests IsTransformable; a deck whose Basics are "
           "removable but not transformable gets an empty prompt and no change."],
    open_questions=[],
)

DATA["ZenWeaver"] = dict(
    category="act_event",
    appearance=dict(pool="act", allowed_act_indices=[1], is_allowed="every player Gold >= 125 (EmotionalAwarenessCost)",
                    is_allowed_cite="m:IsAllowed", unlock_gate=None, entry="act event pool (Hive)"),
    layout="Default",
    options=[
        O("INITIAL", "BREATHING_TECHNIQUES", "LoseGold 50 (Spent), add 2 Enlightenment", cite="m:BreathingTechniques"),
        O("INITIAL", "EMOTIONAL_AWARENESS", "remove 1 chosen card, then LoseGold 125", cond="LOCKED when Gold < 125",
          cite="m:EmotionalAwareness"),
        O("INITIAL", "ARACHNID_ACUPUNCTURE", "remove 2 chosen cards, then LoseGold 250", cond="LOCKED when Gold < 250",
          cite="m:ArachnidAcupuncture"),
    ],
    outcomes=dict(gold=["-50 / -125 / -250"], cards=["+2 Enlightenment", "-1 or -2 cards"]),
    prompts=[
        P("CardSelectCmd.FromDeckForRemoval", 1, 1, "deck", "EMOTIONAL_AWARENESS", "m:RemoveCardsAndProceed",
          filt="IsRemovable; curses first", auto="auto when 1 candidate"),
        P("CardSelectCmd.FromDeckForRemoval", 2, 2, "deck", "ARACHNID_ACUPUNCTURE", "m:RemoveCardsAndProceed",
          filt="IsRemovable; curses first", auto="auto when <= 2 candidates"),
    ],
    rng_streams=[],
    mechanics=["pay gold for cards", "pay gold to remove cards (choice)"],
    kernel_requirements=["PlayerCmd.LoseGold (Spent)", "RunState.CreateCard<T>", "CardPileCmd.Add (array)",
                         "CardSelectCmd.FromDeckForRemoval", "CardPileCmd.RemoveFromDeck"],
    content_deps=dict(cards=["Enlightenment"]),
    ascension="none",
    unlock_progress=[],
    version=V("same", "only decompiler style differs"),
    tier=3, tier_reason="removal prompt",
    port_batch=3,
    notes=["Gold is paid after the removal prompt."],
    open_questions=[],
)

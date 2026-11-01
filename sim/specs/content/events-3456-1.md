# W3 events-3456-1 — index (wave C)

Common to all: HoverTips, VFX/SFX, loc vars (StringVar titles), `PreviewCardPileAdd`, map-history entries and `ThatDoesDamage`/`ThatDecreasesMaxHp` UI flags dropped. Rewards are offered through `RewardsCmd.OfferCustom` and the event finishes right after the offer (the game awaits the rewards screen first). Shared helpers in `Run/Events/Defs/EventKit3456.cs`: out-of-combat `Damage`/`Kill`/`LoseMaxHp`/`GainMaxHp`/`Heal` (Commands/CreatureCmd.cs Damage__12, Kill__14/15, LoseMaxHp__23, GainMaxHp__22, Heal__20 with combat state null; history `DamageTaken`/`MaxHpLost` dropped), deck transform `TransformToRandomInDeck` (Commands/CardCmd.cs:1990-2141 deck branch + Factories/CardFactory.cs:130-134; `FloorAddedToDeck` and the transform history entry dropped; unported options are removed from the draw with a fidelity flag), deck prompts `FromDeckForTransformation`/`FromDeckForEnchantment` (Commands/CardSelectCmd.cs:3485-3605), `SelectCardsToAddToDeckFromGrid` (Models/EventModel.cs:155-292 + CardSelectCmd.cs:3236-3290; unpicked-card history dropped), unported relics/potions skipped with a fidelity flag.

- AMALGAMATOR — Models.Events/Amalgamator.cs:23 — basic removable Strike/Defend filter; remove 2 → add UltimateStrike/UltimateDefend.
- AROMA_OF_CHAOS — Models.Events/AromaOfChaos.cs:19 — LetGo transform on the event Rng; MaintainControl upgrade; `AromaPrinciple` loc var dropped.
- BRAIN_LEECH — Models.Events/BrainLeech.cs:24 — Rip: 5 unblockable/unpowered damage then 1 colorless card reward (3 options); ShareKnowledge: 5 character cards, pick 1 into the deck.
- COLORFUL_PHILOSOPHERS — Models.Events/ColorfulPhilosophers.cs:21 — pool order Necrobinder/Ironclad/Regent/Silent/Defect, own pool excluded, trimmed to 3 by `NextInt(count)` removals; pool carried in `EventOption.StringArg`; 3 uniform-odds card rewards (common/uncommon/rare).
- DOORS_OF_LIGHT_AND_DARK — Models.Events/DoorsOfLightAndDark.cs:18 — Light: StableShuffle (sim `CardPileCmd.CompareForShuffle` order) of upgradable deck cards, upgrade 2; Dark: remove 1.
- DROWNING_BEACON — Models.Events/DrowningBeacon.cs:19 — Bottle: GlowwaterPotion reward; Climb: lose 13 max HP, obtain FRESNEL_LENS (unported at port time → skipped + fidelity flag).
- ENDLESS_CONVEYOR — Models.Events/EndlessConveyor.cs:28 — dishes kept as ids with a switch (no delegates, clone-safe); RollDish weights, every-5th SeapunkSalad, last-dish exclusion, `NextFloat` roll kept; GoldenFysh free; canonical writes last dish, grab count, current dish.
- FIELD_OF_MAN_SIZED_HOLES — Models.Events/FieldOfManSizedHoles.cs:25 — Resist: remove 2 then add Normality; EnterYourHole: PerfectFit ×1. GoldVar(75) is text-only in the game.
- GRAVE_OF_THE_FORGOTTEN — Models.Events/GraveOfTheForgotten.cs:26 — Confront: Decay curse then SoulsPower ×1 (locked if no enchantable card); Accept: ForgottenSoul.
- LUMINOUS_CHOIR — Models.Events/LuminousChoir.cs:21 — CalculateVars gold = 149 − `NextInt(0,50)`; IsAllowed on the base 149 + grab bag availability; tribute = LoseGold(Spent) → `RelicFactory.PullNextRelicFromFront` → Obtain; Reach: remove 2 then SporeMind.
- MORPHIC_GROVE — Models.Events/MorphicGrove.cs:21 — IsShared (rng seed without slot); Group: lose all gold (Stolen), transform 2; Loner: +5 max HP.
- POTION_COURIER — Models.Events/PotionCourier.cs:21 — 3 FoulPotion rewards; Ransack: uncommon from character+shared unlocked potions on `PlayerRng.Rewards`.
- ROOM_FULL_OF_CHEESE — Models.Events/RoomFullOfCheese.cs:24 — Gorge: 8 uniform-odds commons, pick 2; Search: 14 damage then ChosenCheese.
- SAPPHIRE_SEED — Models.Events/SapphireSeed.cs:22 — Eat: heal 9 then upgrade 1; Plant: Sown ×1.
- SELF_HELP_BOOK — Models.Events/SelfHelpBook.cs:24 — Sharp/Nimble/Swift ×2 by card type; locked options; NO_OPTIONS when none available.
- SPIRALING_WHIRLPOOL — Models.Events/SpiralingWhirlpool.cs:23 — heal = (int)(MaxHp × 0.33m) from CalculateVars; Observe: Spiral ×1.
- SPIRIT_GRAFTER — Models.Events/SpiritGrafter.cs:21 — LetItIn: heal 25 then Metamorphosis; Rejection: upgrade 1 then 10 damage.
- STONE_OF_ALL_TIME — Models.Events/StoneOfAllTime.cs:25 — potion lock in BeforeEventStarted / unlock in OnEventFinished; the chosen potion is kept as a slot index (clone-safe); Lift: discard, +10 max HP, `NextInt(100)`; Push: 6 damage, Vigorous ×8, `NextInt(100)`.

Gap: the R6 random full-run soak is PENDING in this base (no act defs ported), so these events have not run inside `RunSim`. Each option is covered by `Content_events_3456_1Tests`.

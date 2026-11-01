# relics-7-1 (W3 wave C) — index

Paths are under `MegaCrit.Sts2.Core.Models.Relics/`. Dropped everywhere: `Flash()`, hover tips, `InvokeDisplayAmountChanged`, `AssertMutable`, VFX / card-preview calls, map-history `HpHealed` / `CardsTransformed` entries, `FloorAddedToDeck` (no card field in the sim). Out-of-combat HP ops go through the new `CreatureCmd.{Heal,GainMaxHp,LoseMaxHp,Damage}(RunState, …)` overloads (delegate to the combat versions when `rs.Combat != null`); out-of-combat death runs no Kill / death-prevention (the run loop sees `IsDead`). Deck transforms go through the new `CardCmd.Transform(RunState, …)`.

- ALCHEMICAL_COFFER — AlchemicalCoffer.cs:16 — +4 slots, 4 potions from RunRng.CombatPotionGeneration procured into slots original+i.
- ARCHAIC_TOOTH — ArchaicTooth.cs:19 — IEventRelicSetup; stores starter/ancient ids (not SerializableCard); no transcendence starter in deck → Fidelity record + return (game NREs).
- BEAUTIFUL_BRACELET — BeautifulBracelet.cs:19 — 4 random Swift-enchantable deck cards (Niche TakeRandom), Swift 2.
- BIG_MUSHROOM — BigMushroom.cs:16 — +20 max HP; -2 hand draw on turn 1; AfterRoomEntered/Grow is VFX only, dropped.
- BING_BONG — BingBong.cs:13 — CardsToSkip kept as instance ids; clone = CloneForCombat(new id) with DeckVersionId cleared, added with clonedBy = this.
- BLOOD_SOAKED_ROSE — BloodSoakedRose.cs:16 — ENTHRALLED curse on pickup, +1 max energy.
- BOOK_OF_FIVE_RINGS — BookOfFiveRings.cs:17 — heal 20 every 5th deck add; IsActivating display state dropped; IsAllowed via RelicCatalog.IsBeforeAct3TreasureChest.
- CURSED_PEARL — CursedPearl.cs:15 — GREED curse then +333 gold.
- DARKSTONE_PERIAPT — DarkstonePeriapt.cs:14 — +6 max HP per curse entering the deck.
- DISTINGUISHED_CAPE — DistinguishedCape.cs:18 — 2 distinct curses (curse pool, CanBeGeneratedByModifiers, ordinal id order, Niche) then 3 APPARITION; rs.Unlocks stands in for Owner.UnlockState.
- DOWSING_ROD — DowsingRod.cs:15 — adds DOWSING; gap: card not registered yet → Fidelity record, nothing added.
- DRAGON_FRUIT — DragonFruit.cs:15 — +1 max HP per gold gain.
- DUSTY_TOME — DustyTome.cs:19 — IEventRelicSetup (PlayerRng.Rewards over the character's ancient cards minus transcendence cards); not set up → Fidelity record + return (game NREs).
- ETERNAL_FEATHER — EternalFeather.cs:19 — on rest-site entry heal 3 × floor(deck/5); fullscreen heal VFX dropped.
- FAKE_LEES_WAFFLE — FakeLeesWaffle.cs:13 — heal 10% max HP; merchant cost 50.
- FAKE_MANGO — FakeMango.cs:13 — +3 max HP; merchant cost 50.
- FAKE_VENERABLE_TEA_SET — FakeVenerableTeaSet.cs:17 — rest site arms +1 energy at the next AfterEnergyReset; merchant cost 50.
- FISHING_ROD — FishingRod.cs:14 — every 3rd Monster combat upgrade a random upgradable deck card (Niche); null room (standalone) ignored.
- FRAGRANT_MUSHROOM — FragrantMushroom.cs:21 — 15 Unblockable|Unpowered damage, then 2 StableShuffle(Niche) upgradable deck cards upgraded.
- GOLDEN_PEARL — GoldenPearl.cs:13 — +150 gold.
- JEWELRY_BOX — JewelryBox.cs:15 — adds APOTHEOSIS to the deck.
- LEAFY_POULTICE — LeafyPoultice.cs:17 — lose 12 max HP, transform first basic Strike + Defend with PlayerRng.Transformations; no PlayerRng (standalone) → Fidelity record, no transform.
- LEES_WAFFLE — LeesWaffle.cs:14 — +7 max HP then full heal.
- LOOMING_FRUIT — LoomingFruit.cs:14 — +31 max HP; Cornucopia icon variant dropped.
- LUCKY_FYSH — LuckyFysh.cs:15 — +15 gold per deck add; not in shops; IsAllowed before act-3 chest.
- MANGO — Mango.cs:13 — +14 max HP.

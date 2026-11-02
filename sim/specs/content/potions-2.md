# potions-2 (wave A) index

All files: sim/Sts2Sim.Core/Potions/Defs/<Class>.cs. Dropped everywhere: VFX/SFX/splash, hover tips, async plumbing.

- REGEN_POTION — Models.Potions/RegenPotion.cs:18 — REGEN_POWER via PowerRegistry (other owner); var name "RegenPower".
- SHACKLING_POTION — Models.Potions/ShacklingPotion.cs:21 — SHACKLING_POTION_POWER via registry onto HittableEnemies, amount Strength.IntValue; splash loop dropped.
- SHIP_IN_A_BOTTLE — Models.Potions/ShipInABottle.cs:19 — GainBlock then BLOCK_NEXT_TURN_POWER (registry) with Block.IntValue.
- SPEED_POTION — Models.Potions/SpeedPotion.cs:18 — SPEED_POTION_POWER via registry, PowerVar<DexterityPower>(5).
- STABLE_SERUM — Models.Potions/StableSerum.cs:18 — RETAIN_HAND_POWER via registry, RepeatVar(2).
- BLESSING_OF_THE_FORGE — Models.Potions/BlessingOfTheForge.cs:15 — upgrades every upgradable hand card.
- BOTTLED_POTENTIAL — Models.Potions/BottledPotential.cs:18 — hand -> draw (snapshot list), Shuffle, Draw 5.
- CUNNING_POTION — Models.Potions/CunningPotion.cs:20 — Shiv.CreateInHand inlined (SHIV by id, other owner), then Upgrade each.
- POT_OF_GHOULS — Models.Potions/PotOfGhouls.cs:17 — Soul.Create + AddGeneratedCardsToCombat(Hand, Owner).
- SNECKO_OIL — Models.Potions/SneckoOil.cs:22 — Draw 7; non-X hand cards with cost >= 0 get CombatEnergyCosts.NextInt(4) this turn; TestEnergyCostOverride (TestMode) and NCard anim dropped.
- SOLDIERS_STEW — Models.Potions/SoldiersStew.cs:15 — BaseReplayCount++ on every Strike-tagged card in AllPiles.
- BONE_BREW — Models.Potions/BoneBrew.cs:19 — OstyCmd.Summon 15.
- COSMIC_CONCOCTION — Models.Potions/CosmicConcoction.cs:20 — 3 distinct colorless; Upgrade then AddGeneratedCardToCombat each (lazy enumeration kept).
- DISTILLED_CHAOS — Models.Potions/DistilledChaos.cs:18 — AutoPlayFromDrawPile 3, Top, no forced exhaust.
- ENTROPIC_BREW — Models.Potions/EntropicBrew.cs:16 — while open slot: CreateRandomPotionOutOfCombat(CombatPotionGeneration) + TryToProcure, stop on failure; works out of combat.
- ESSENCE_OF_DARKNESS — Models.Potions/EssenceOfDarkness.cs:18 — channels DarkOrb OrbQueue.Capacity times.
- KINGS_COURAGE — Models.Potions/KingsCourage.cs:18 — ForgeCmd.Forge 15.
- OROBIC_ACID — Models.Potions/OrobicAcid.cs:18 — 1 attack + 1 skill + 1 power (character pool), free this turn, added to hand together.
- POTION_OF_CAPACITY — Models.Potions/PotionOfCapacity.cs:18 — OrbCmd.AddSlots 2.
- POTION_OF_DOOM — Models.Potions/PotionOfDoom.cs:20 — DOOM_POWER 33 via registry.
- STAR_POTION — Models.Potions/StarPotion.cs:16 — GainStars 3.
- COLORLESS_POTION — Models.Potions/ColorlessPotion.cs:19 — choose 1 of 3 colorless (skippable), free this turn, to hand.
- DROPLET_OF_PRECOGNITION — Models.Potions/DropletOfPrecognition.cs:18 — pick 1 from draw pile to hand.
- GAMBLERS_BREW — Models.Potions/GamblersBrew.cs:19 — FromHandForDiscard 0..999999999, DiscardAndDraw(n).
- LIQUID_MEMORIES — Models.Potions/LiquidMemories.cs:20 — pick 1 from discard, free this turn, to hand.
- POWER_POTION — Models.Potions/PowerPotion.cs:18 — choose 1 of 3 character powers, free this turn.
- SKILL_POTION — Models.Potions/SkillPotion.cs:18 — choose 1 of 3 character skills, free this turn.
- TOUCH_OF_INSANITY — Models.Potions/TouchOfInsanity.cs:18 — pick 1 hand card costing energy/stars (local or global), free this combat.
- FOUL_POTION — Models.Potions/FoulPotion.cs:32 — combat: 12 unpowered to every non-pet creature (players included). Gaps: TargetType fixed AllEnemies (game: TargetedNoCreature out of combat), PassesCustomUsabilityCheck left true, out-of-combat merchant / FakeMerchant throw = NotImplementedException("W3 run-layer").
- ASHWATER / ATTACK_POTION / FAIRY_IN_A_BOTTLE — already ported by the W1 pilot, untouched.

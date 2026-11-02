# relics-8-2 (wave C) port index

Common: Flash, HoverTips, InvokeDisplayAmountChanged, AssertMutable dropped. `RunState.CloneCard` (Runs/RunState.cs:711) is `MoltenEgg.CloneRunCard` (CloneForCombat with a fresh run instance id, DeckVersionId reset, as CombatState.CloneCard). `IsBeforeAct3TreasureChest(IRunState)` uses the int overload with `rs.Players.Count, rs.TotalFloor` (the RunState overload in RelicModel still throws). Pickup offers (`RewardsCmd.OfferCustom` / `RewardsSet.Offer`) push onto `rs.RewardsStack` and return, so code after an awaited Offer runs before the player claims (game awaits the screen). Pickup offers are skipped with a fidelity record when the owner has no PlayerRng/PlayerOdds (standalone combats, CONTRACTS C4).

MOLTEN_EGG — Models.Relics/MoltenEgg.cs:13 — EggRelicHelper (Helpers.Models/EggRelicHelper.cs) inlined as MoltenEgg.UpgradeValidCards; CardPreviewStyle dropped
NEOWS_BONES — Models.Relics/NeowsBones.cs:21 — Neow.AllPossibleOptions rebuilt from Neow id lists (curse, positive, LAVA_ROCK, NEOWS_TALISMAN, NUTRITIOUS_OYSTER, POMANDER, SMALL_CAPSULE, STONE_HUMIDIFIER); unported relics: IsAllowedAtNeow from Neow's fallback rule, and skipped from the offer after the shuffle/take (fidelity); curses added before the claim (see common); PreviewCardPileAdd/Wait dropped
ORRERY — Models.Relics/Orrery.cs:15 — 5 CardReward(3) sharing one CardCreationOptions, as the game
PAELS_WING — Models.Relics/PaelsWing.cs:20 — DoActivateVisuals/IsActivating dropped (display only); alternative callback is static and resolves the relic from reward.Player (clone-safe)
PRAYER_WHEEL — Models.Relics/PrayerWheel.cs:10 — none
PRISMATIC_GEM — Models.Relics/PrismaticGem.cs:12 — pools union by reference (CardPools singletons)
PUMPKIN_CANDLE — Models.Relics/PumpkinCandle.cs:14 — kernel edit: KindleRestSiteOption.OnSelect calls Rekindle (Entities.RestSite/KindleRestSiteOption.cs)
REGAL_PILLOW — Models.Relics/RegalPillow.cs:15 — ModifyExtraRestSiteHealText (UI text) dropped; pet owner via MonsterState.PetOwnerId
SHOVEL — Models.Relics/Shovel.cs:9 — none
SILKEN_TRESS — Models.Relics/SilkenTress.cs:18 — CanEnchant on a fresh Glam instead of ModelDb canonical
SILVER_CRUCIBLE — Models.Relics/SilverCrucible.cs:15 — none
SMALL_CAPSULE — Models.Relics/SmallCapsule.cs:14 — holds the shared HasRunLayer guard
SOZU — Models.Relics/Sozu.cs:9 — none
STONE_HUMIDIFIER — Models.Relics/StoneHumidifier.cs:17 — out of combat GainMaxHp is SetMaxHpInternal + HealInternal (as CookRestSiteOption), no AfterCurrentHpChanged; ModifyExtraRestSiteHealText dropped
SWORD_OF_STONE — Models.Relics/SwordOfStone.cs:15 — none
THE_COURIER — Models.Relics/TheCourier.cs:9 — none
TINY_MAILBOX — Models.Relics/TinyMailbox.cs:12 — ModifyExtraRestSiteHealText dropped
TOXIC_EGG — Models.Relics/ToxicEgg.cs:13 — no CurrentUpgradeLevel check in TryModifyCardBeingAddedToDeck (as the game, unlike MoltenEgg)
TOY_BOX — Models.Relics/ToyBox.cs:20 — DoActivateVisuals/IsActivating/CustomScaledWait dropped; v0.111 values Relics 5, Combats 3
WHITE_BEAST_STATUE — Models.Relics/WhiteBeastStatue.cs:8 — IsCombatRoom inlined ((uint)(roomType-1) <= 2)
WHITE_STAR — Models.Relics/WhiteStar.cs:10 — none
WINGED_BOOTS — Models.Relics/WingedBoots.cs:13 — CurrentMapPoint = rs.Map.GetPoint(rs.CurrentMapCoord)
WING_CHARM — Models.Relics/WingCharm.cs:15 — CanEnchant on a fresh Swift instead of ModelDb canonical
WONGOS_MYSTERY_TICKET — Models.Relics/WongosMysteryTicket.cs:12 — none

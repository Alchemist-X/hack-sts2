# relics-5-1 (wave A) port index

BONE_FLUTE — Models.Relics/BoneFlute.cs:13 — Flash dropped; Osty check via MonsterState.Def is Osty, pet owner via CombatStatus.PetOwnerOf
BOUND_PHYLACTERY — Models.Relics/BoundPhylactery.cs:17 — HoverTips dropped; SummonPet via OstyCmd.Summon
BYRDPIP — Models.Relics/Byrdpip.cs:21 — Skin roll (cosmetic, local Rng) dropped; deck egg transform outside combat throws W3 run-layer, deck transform inside combat throws in kernel (W2); pet summon skipped while monster BYRDPIP is unregistered (temporary, keeps W1 gate playouts running)
CRACKED_CORE — Models.Relics/CrackedCore.cs:19 — HoverTips dropped
DIVINE_DESTINY — Models.Relics/DivineDestiny.cs:16 — none
DIVINE_RIGHT — Models.Relics/DivineRight.cs:14 — Hook.AfterRoomEntered has no call site yet (W2 room entry); stars land once W2 dispatches it with rs.Combat set
EMOTION_CHIP — Models.Relics/EmotionChip.cs:21 — Flash and Cmd.Wait(0.25) dropped; LostHpInPreviousTurn via History.AnyLastPlayerTurn<DamageReceivedEntry>
FENCING_MANUAL — Models.Relics/FencingManual.cs:18 — HoverTips dropped
GALACTIC_DUST — Models.Relics/GalacticDust.cs:19 — DoActivateVisuals wait and InvokeDisplayAmountChanged dropped; Mathf.FloorToInt(float a/b) as int division (non-negative)
GOLD_PLATED_CABLES — Models.Relics/GoldPlatedCables.cs:6 — AfterModifyingOrbPassiveTriggerCount is Flash only (empty override)
INFUSED_CORE — Models.Relics/InfusedCore.cs:19 — HoverTips dropped
LUNAR_PASTRY — Models.Relics/LunarPastry.cs:17 — none
METRONOME — Models.Relics/Metronome.cs:20 — game class is non-sealed (no subclass), ported sealed; ShowCounter IsInProgress UI check dropped; AfterRoomEntered reset awaits W2 dispatch (AfterCombatEnd also resets)
MINI_REGENT — Models.Relics/MiniRegent.cs:21 — Flash dropped
PAELS_LEGION — Models.Relics/PaelsLegion.cs:24 — Skin roll and CreatureCmd.TriggerAnim (Sleep/WakeUp/BlockTrigger) dropped; AffectedCardPlay stored as CardPlay.PlayId; DisplayAmount IsCanonical/IsInProgress UI checks dropped; pet summon skipped while monster PAELS_LEGION is unregistered (temporary)
PHYLACTERY_UNBOUND — Models.Relics/PhylacteryUnbound.cs:18 — HoverTips dropped
RUNIC_CAPACITOR — Models.Relics/RunicCapacitor.cs:16 — Flash dropped
SYMBIOTIC_VIRUS — Models.Relics/SymbioticVirus.cs:19 — HoverTips dropped

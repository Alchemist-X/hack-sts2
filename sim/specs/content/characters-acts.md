# characters-acts (W3 wave C) — index

Base: `Run/Characters/CharacterDef.cs` (game `Models/CharacterModel.cs`), registry `CharacterRegistry` (`Run/Characters/CharacterRegistry.cs`); `RunLoop.GetCharacterStart` resolves through it. Acts derive from `Run/State/ActModel.cs`.

- DEFECT — Models.Characters/Defect.cs:17 — dropped Gender/visuals/SFX; GetArchitectAttackVfx kept as count 5; BaseOrbSlotCount 3; StartingPotions empty
- IRONCLAD — Models.Characters/Ironclad.cs:18 — dropped visuals; vfx count 5; UnlocksAfterRunAs null
- NECROBINDER — Models.Characters/Necrobinder.cs:16 — dropped visuals; vfx count 4; Osty is summoned by BOUND_PHYLACTERY (relic owner)
- REGENT — Models.Characters/Regent.cs:18 — dropped ShouldAlwaysShowStarCounter (UI) and visuals; vfx count 5
- SILENT — Models.Characters/Silent.cs:18 — dropped visuals; vfx count 4
- GLORY — Models.Acts/Glory.cs:12 — dropped SFX/music/colors; all ancients always unlocked
- HIVE — Models.Acts/Hive.cs:13 — AllEvents decompile lost COLORFUL_PHILOSOPHERS at index 2, restored from IL; OROBAS gated by OROBAS_EPOCH
- OVERGROWTH — Models.Acts/Overgrowth.cs:15 — first-run (NumberOfRuns == 0) SwapToOrCreateAtIndex order ported; NEOW gated by NEOW_EPOCH
- UNDERDOCKS — Models.Acts/Underdocks.cs:13 — IsUnlocked = UNDERDOCKS_EPOCH; NEOW gated by NEOW_EPOCH

Gap (all acts): `GenerateAllEncounters` skips encounters not registered in `EncounterRegistry` (currently SimExcluded GREMLIN_MERC_NORMAL, THIEVING_HOPPER_WEAK); room-set RNG matches the game only once those are ported.

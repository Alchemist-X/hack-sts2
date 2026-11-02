# relics-10-1 (wave C) index

- FUR_COAT — Models.Relics/FurCoat.cs:24 — VFX/Flash dropped; history flag IsAffectedByFurCoat dropped (sim MapPointHistoryEntry has no per-player entry); quests via copy-on-write ActMap overlay, key "FUR_COAT#<CombatId>" ("q is FurCoat" = key prefix match); AfterObtained skips marking when rs.Map is null (standalone combat); null current map coord treated as unmarked (game would NRE).
- GOLDEN_COMPASS — Models.Relics/GoldenCompass.cs:15 — map regeneration via RunLoop.GenerateMap (made public; mirrors RunManager.GenerateMap non-saved branch, SavedMapsToLoad branch absent in sim); regeneration skipped when rs.Map is null (standalone combat harness).
- LORDS_PARASOL — Models.Relics/LordsParasol.cs:25 — UI blocking, Cmd.Wait, Sentry logging dropped; TaskHelper.RunSafely fire-and-forget run inline (synchronous rewrite); GetLocalInventory = inventory whose Player is Owner (fallback Inventories[0]); RunManager.IsInProgress = !RunLoop.IsGameOver(rs).
- WHISPERING_EARRING — Models.Relics/WhisperingEarring.cs:23 — TalkCmd/Flash/hover tips dropped; CombatManager.IsPlayerReadyToEndTurn dropped (sim has no ready-to-end-turn state; never true mid-hook in a synchronous single-player loop); AnyEnemy target passed explicitly as first hittable enemy.
- MASSIVE_SCROLL — skipped: multiplayer-only (IsAllowed requires >1 player).

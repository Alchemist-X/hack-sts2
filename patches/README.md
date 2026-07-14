# Patches

## sts2mcp-0.4.0-v0107.1-compat.patch

Makes [STS2MCP](https://github.com/Gennadiyev/STS2MCP) v0.4.0 (commit `20eadeb`)
compile and run against game v0.107.1 (upstream targets v0.103.2; upstream issue
#114 tracks the breakage). Three drift families:

1. `CombatManager.IsPlayPhase` (removed) → per-player
   `player.PlayerCombatState?.Phase == PlayerTurnPhase.Play`
2. `Creature.CombatState` is now the `ICombatState` interface
3. `MerchantRoom.Inventory` (removed) → per-player `Inventories` +
   `GetLocalInventory()` (guarded by `Count > 0` to preserve null-until-entered
   semantics)

Apply from an STS2MCP checkout at `20eadeb`:

```bash
git apply /path/to/sts2mcp-0.4.0-v0107.1-compat.patch
dotnet build STS2_MCP.csproj -c Release -p:STS2GameDir="<game dir>"
```

Used here as the action-injection channel for Level-2 recorder verification
(`scripts/level2_run.sh`). Candidate for an upstream PR.

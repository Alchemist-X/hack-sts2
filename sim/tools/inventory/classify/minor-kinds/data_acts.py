from lib import M

K = "acts"
BASE = ["run:acts_room_sets", "run:map_generation"]
POOLS = ("GenerateAllEncounters split by room kind (weak/normal/elite/boss, Models/ActModel.cs:123-129), AllEvents, "
         "AllAncients and BossDiscoveryOrder; RoomSet shuffles them with the run Rng")

M(K, "DeprecatedAct", "deprecated", ["empty pools, Index -1, zero rooms (save compatibility)"], tier=1, batch=None,
  vd=("refactor_only", "Array.Empty spelling only"))
M(K, "Glory", "game", [
    "act index 2 (default); 13 base rooms, 2 weak encounters; " + POOLS,
    "ancients: Nonupeipe, Tanx, Vakuu (no epoch gate)",
    "GetMapPointTypes(mapRng): rest sites NextInt(5, 7), unknown rooms StandardRandomUnknownCount(mapRng) - 1",
], reqs=BASE, hooks=["GetMapPointTypes", "GetUnlockedAncients"],
  rng=["map Rng (Rng(seed, act_<n>_map)): rest-site count, then the unknown-room count"], tier=1, batch=1,
  deps={"events": ["Nonupeipe", "Tanx", "Vakuu"]})
M(K, "Hive", "game", [
    "act index 1 (default); 14 base rooms, 2 weak encounters; " + POOLS,
    "ancients: Orobas, Pael, Tezcatara; GetUnlockedAncients drops Orobas unless OrobasEpoch is revealed",
    "GetMapPointTypes(mapRng): rest sites NextGaussianInt(6, 1, 6, 7), unknown rooms StandardRandomUnknownCount(mapRng) - 1",
], reqs=BASE + ["run:unlock_profile"], hooks=["GetMapPointTypes", "GetUnlockedAncients"],
  rng=["map Rng: rest-site Gaussian draw, then the unknown-room count"], tier=3, batch=1,
  deps={"events": ["Orobas", "Pael", "Tezcatara"]})
M(K, "Overgrowth", "game", [
    "act index 0 (default); 15 base rooms, 3 weak encounters; " + POOLS,
    "ancient: Neow, dropped unless NeowEpoch is revealed",
    "ApplyActDiscoveryOrderModifications: on the first run ever (UnlockState.NumberOfRuns == 0) fixes the first 7 normal encounters, 2 events and 2 elites (RoomSet.SwapToOrCreateAtIndex)",
    "GetMapPointTypes(mapRng): rest sites NextGaussianInt(7, 1, 6, 7), unknown rooms StandardRandomUnknownCount(mapRng)",
], reqs=BASE + ["run:unlock_profile"], hooks=["GetMapPointTypes", "GetUnlockedAncients", "ApplyActDiscoveryOrderModifications"],
  rng=["map Rng: rest-site Gaussian draw, then the unknown-room count"], tier=3, batch=1, deps={"events": ["Neow"]},
  notes="the first-run order only matters for a profile with zero runs")
M(K, "Underdocks", "game", [
    "act index 0 (alternative to Overgrowth, IsDefault false); IsUnlocked needs UnderdocksEpoch; 15 base rooms, 3 weak encounters; " + POOLS,
    "ancient: Neow, dropped unless NeowEpoch is revealed",
    "GetMapPointTypes(mapRng): rest sites NextGaussianInt(7, 1, 6, 7), unknown rooms StandardRandomUnknownCount(mapRng)",
], reqs=BASE + ["run:unlock_profile"], hooks=["GetMapPointTypes", "GetUnlockedAncients", "IsUnlocked"],
  rng=["map Rng: rest-site Gaussian draw, then the unknown-room count"], tier=3, batch=1, deps={"events": ["Neow"]},
  notes="which of Overgrowth/Underdocks a run uses is chosen at run creation (run layer)")

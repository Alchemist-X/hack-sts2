from lib import M

K = "characters"
BASE = ["run:run_state"]
VFX = ("GetArchitectAttackVfx: %d entries; TheArchitect shuffles this list with the event Rng, so its length is rule-relevant "
       "(Models/CharacterModel.cs:215, Models.Events/TheArchitect.cs:284-285)")

M(K, "Defect", "game", [
    "StartingHp 75, StartingGold 99, MaxEnergy 3 (default), BaseOrbSlotCount 3",
    "StartingDeck: StrikeDefect x4, DefendDefect x4, Zap, Dualcast; StartingRelics: CrackedCore",
    "pools: DefectCardPool, DefectRelicPool, DefectPotionPool; UnlocksAfterRunAs Necrobinder",
    VFX % 5,
], reqs=BASE + ["kernel:orbs"], deps={"cards": ["StrikeDefect", "DefendDefect", "Zap", "Dualcast"], "relics": ["CrackedCore"]},
  tier=3, batch=1, notes="BaseOrbSlotCount sets the OrbQueue capacity at combat start; UnlocksAfterRunAs is profile progression (no in-run rule)", vd=("refactor_only", "animator graph replaced by AnimationStates (UI only)"))
M(K, "DeprecatedCharacter", "deprecated", [
    "not playable; 1000 HP, 100 energy, MockCardPool (test placeholder)", VFX % 1,
], tier=1, batch=None, vd=("refactor_only", "Array.Empty spelling only"))
M(K, "Deprived", "test_only", [
    "IsMock, not playable; 1000 HP, 100 energy; card pool DeprivedCardPool unless a mock pool is set (test seam)", VFX % 0,
], tier=4, batch=None, vd=("refactor_only", "test seam: mock pool now defaults to DeprivedCardPool and is set with SetMockCardPool"))
M(K, "Ironclad", "game", [
    "StartingHp 80, StartingGold 99, MaxEnergy 3, no orb slots",
    "StartingDeck: StrikeIronclad x5, DefendIronclad x4, Bash; StartingRelics: BurningBlood",
    "pools: IroncladCardPool, IroncladRelicPool, IroncladPotionPool",
    VFX % 5,
], reqs=BASE, deps={"cards": ["StrikeIronclad", "DefendIronclad", "Bash"], "relics": ["BurningBlood"]},
  tier=1, batch=1, vd=("refactor_only", "animator graph replaced by AnimationStates (UI only)"))
M(K, "Necrobinder", "game", [
    "StartingHp 66, StartingGold 99, MaxEnergy 3",
    "StartingDeck: StrikeNecrobinder x4, DefendNecrobinder x4, Bodyguard, Unleash; StartingRelics: BoundPhylactery (summons Osty)",
    "pools: NecrobinderCardPool, NecrobinderRelicPool, NecrobinderPotionPool; UnlocksAfterRunAs Regent",
    VFX % 4,
], reqs=BASE, deps={"cards": ["StrikeNecrobinder", "DefendNecrobinder", "Bodyguard", "Unleash"], "relics": ["BoundPhylactery"]},
  tier=1, batch=1, notes="Osty comes from the starting relic BoundPhylactery (a dependency); UnlocksAfterRunAs is profile progression", vd=("refactor_only", "animator graph replaced by AnimationStates (UI only)"))
M(K, "RandomCharacter", "not_playable", [
    "IsPlayable false: the lobby replaces it with a random playable character before the run (Multiplayer.Game.Lobby/StartRunLobby.cs)",
    "placeholder stats 1 HP, 1 gold, Circlet, a mixed Strike/Defend deck", VFX % 0,
], tier=1, batch=None)
M(K, "Regent", "game", [
    "StartingHp 75, StartingGold 99, MaxEnergy 3; ShouldAlwaysShowStarCounter (UI)",
    "StartingDeck: StrikeRegent x4, DefendRegent x4, FallingStar, Venerate; StartingRelics: DivineRight",
    "pools: RegentCardPool, RegentRelicPool, RegentPotionPool; UnlocksAfterRunAs Silent",
    VFX % 5,
], reqs=BASE, deps={"cards": ["StrikeRegent", "DefendRegent", "FallingStar", "Venerate"], "relics": ["DivineRight"]},
  tier=1, batch=1, notes="stars come from its cards and DivineRight (dependencies); ShouldAlwaysShowStarCounter is UI only", vd=("refactor_only", "animator graph replaced by AnimationStates; sovereign-blade animation helpers removed (UI only)"))
M(K, "Silent", "game", [
    "StartingHp 70, StartingGold 99, MaxEnergy 3",
    "StartingDeck: StrikeSilent x5, DefendSilent x5, Neutralize, Survivor; StartingRelics: RingOfTheSnake",
    "pools: SilentCardPool, SilentRelicPool, SilentPotionPool",
    VFX % 4,
], reqs=BASE, deps={"cards": ["StrikeSilent", "DefendSilent", "Neutralize", "Survivor"], "relics": ["RingOfTheSnake"]},
  tier=1, batch=1, vd=("refactor_only", "animator graph replaced by AnimationStates (UI only)"))

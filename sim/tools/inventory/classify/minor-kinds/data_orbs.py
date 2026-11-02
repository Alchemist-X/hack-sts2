from lib import M

K = "orbs"
CORE = "kernel:orbs"
TRIGGER = ("turn triggers call OrbModel.TriggerPassive (Hook.ModifyOrbPassiveTriggerCount, then "
           "AfterModifyingOrbPassiveTriggerCount, then Passive per count; Models/OrbModel.cs:481)")

M(K, "DarkOrb", "game", [
    "PassiveVal = ModifyOrbValue(6); EvokeVal = the accumulated _evokeVal (starts at 6, not hook-modified)",
    "BeforeTurnEndOrbTrigger: " + TRIGGER,
    "Passive: _evokeVal += PassiveVal (no target allowed)",
    "Evoke: CreatureCmd.Damage(lowest-HP hittable enemy, EvokeVal, Unpowered) (MinBy CurrentHp: first in list order on ties); nothing when no hittable enemy",
], reqs=[CORE, "ModifyOrbValue", "ModifyOrbPassiveTriggerCounts", "AfterModifyingOrbPassiveTriggerCount", "kernel:per_instance_state"],
  hooks=["PassiveVal", "EvokeVal", "BeforeTurnEndOrbTrigger", "Passive", "Evoke"], tier=2, batch=1,
  vd=("refactor_only", "Trigger() -> TriggerPassive/ActivatePassive/ActivateEvoke; the passive-count loop moved from OrbQueue (v0.107.1 Entities/Orbs/OrbQueue.cs:90) into OrbModel.TriggerPassive"))
M(K, "FrostOrb", "game", [
    "PassiveVal = ModifyOrbValue(2); EvokeVal = ModifyOrbValue(5)",
    "BeforeTurnEndOrbTrigger: " + TRIGGER,
    "Passive: CreatureCmd.GainBlock(owner, PassiveVal, Unpowered); with HibernatePower also every other player",
    "Evoke: CreatureCmd.GainBlock(owner, EvokeVal, Unpowered); with HibernatePower also every other player",
], reqs=[CORE, "ModifyOrbValue", "ModifyOrbPassiveTriggerCounts", "AfterModifyingOrbPassiveTriggerCount"],
  hooks=["PassiveVal", "EvokeVal", "BeforeTurnEndOrbTrigger", "Passive", "Evoke"], reads={"powers": ["HibernatePower"]}, tier=2, batch=1,
  notes="the HibernatePower branch only adds block to other players: no effect in single player",
  vd=("refactor_only", "HibernatePower makes Frost passive/evoke block also apply to the other players: multiplayer only "
                       "(Hibernate is MultiplayerOnly, Models.Cards/Hibernate.cs:123); trigger refactor as DarkOrb"))
M(K, "GlassOrb", "game", [
    "PassiveVal = ModifyOrbValue(_passiveVal) with _passiveVal starting at 4; EvokeVal = PassiveVal x 2",
    "BeforeTurnEndOrbTrigger: " + TRIGGER,
    "Passive: when PassiveVal > 0, damage every hittable enemy by PassiveVal (Unpowered) after decrementing _passiveVal by 1 (floored at 0)",
    "Evoke: when EvokeVal > 0, CreatureCmd.Damage(all hittable enemies, EvokeVal, Unpowered)",
], reqs=[CORE, "ModifyOrbValue", "ModifyOrbPassiveTriggerCounts", "AfterModifyingOrbPassiveTriggerCount", "kernel:per_instance_state"],
  hooks=["PassiveVal", "EvokeVal", "BeforeTurnEndOrbTrigger", "Passive", "Evoke"], tier=2, batch=1,
  notes="not in ModelDb.Orbs (so never rolled by GetRandomOrb); channelled explicitly (OrbCmd.Channel<GlassOrb>)",
  vd=("refactor_only", "trigger refactor as DarkOrb"))
M(K, "LightningOrb", "game", [
    "PassiveVal = ModifyOrbValue(3); EvokeVal = ModifyOrbValue(8)",
    "BeforeTurnEndOrbTrigger: " + TRIGGER,
    "Passive/Evoke: CreatureCmd.Damage(target or a random hittable opponent, value, Unpowered); nothing when no hittable opponent",
], reqs=[CORE, "ModifyOrbValue", "ModifyOrbPassiveTriggerCounts", "AfterModifyingOrbPassiveTriggerCount"],
  hooks=["PassiveVal", "EvokeVal", "BeforeTurnEndOrbTrigger", "Passive", "Evoke"], tier=2, batch=1,
  rng=["RunRngType.CombatTargets: NextItem over the hittable opponents when no explicit target (passive and evoke)"],
  vd=("refactor_only", "trigger refactor as DarkOrb"))
M(K, "MockCombatCleanupOrb", "test_only", ["Evoke removes the owner's creature from the combat state"], hooks=["Evoke"], tier=4, batch=None,
  vd=("refactor_only", "IsMock => true added (test mock)"))
M(K, "MockTurnEndSuspendOrb", "test_only", ["BeforeTurnEndOrbTrigger awaits a static TaskCompletionSource (test seam)"],
  hooks=["BeforeTurnEndOrbTrigger"], tier=4, batch=None,
  vd=("new", "test mock added after v0.107.1"))
M(K, "PlasmaOrb", "game", [
    "PassiveVal = 1; EvokeVal = 2 (not hook-modified)",
    "AfterTurnStartOrbTrigger: " + TRIGGER,
    "Passive: PlayerCmd.GainEnergy(1); Evoke: PlayerCmd.GainEnergy(2)",
], reqs=[CORE, "ModifyOrbPassiveTriggerCounts", "AfterModifyingOrbPassiveTriggerCount"],
  hooks=["PassiveVal", "EvokeVal", "AfterTurnStartOrbTrigger", "Passive", "Evoke"], tier=2, batch=1,
  vd=("refactor_only", "trigger refactor as DarkOrb"))

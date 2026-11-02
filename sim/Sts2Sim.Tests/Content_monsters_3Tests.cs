using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_monsters_3Tests
{
    private static readonly string[] MonsterIds =
    {
        "SNAPPING_JAXFRUIT", "SNEAKY_GREMLIN", "SOUL_FYSH", "SOUL_NEXUS", "SPINY_TOAD", "STABBOT",
        "TERROR_EEL", "THE_FORGOTTEN", "THE_INSATIABLE", "THE_LOST", "TORCH_HEAD_AMALGAM",
        "TRACKER_RUBY_RAIDER", "TUNNELER", "TURRET_OPERATOR", "ZAPBOT", "AEONGLASS", "CHOMPER",
        "HAUNTED_SHIP", "MECHA_KNIGHT", "MYTE", "TOUGH_EGG", "VANTOM", "WRIGGLER",
    };

    private static readonly string[] PowerIds =
    {
        "SHRIEK_POWER", "POSSESS_SPEED_POWER", "POSSESS_STRENGTH_POWER", "SANDPIT_POWER",
        "BURROWED_POWER", "HIGH_VOLTAGE_POWER", "WITHERING_PRESENCE_POWER", "HATCH_POWER",
    };

    public static void Test_Content_monsters_3_AllIdsRegistered()
    {
        var monsters = new HashSet<string>(MonsterRegistry.AllIds, StringComparer.Ordinal);
        foreach (var id in MonsterIds)
        {
            Assert.True(monsters.Contains(id), $"{id} is registered");
            Assert.Equal(id, MonsterRegistry.Get(id).Id, $"{id} round-trips");
            Assert.Equal(id, DeterministicHash.Slugify(MonsterRegistry.Get(id).GetType().Name), $"{id} is the slugified class name");
        }
        var powers = new HashSet<string>(PowerRegistry.AllIds, StringComparer.Ordinal);
        foreach (var id in PowerIds)
        {
            Assert.True(powers.Contains(id), $"{id} is registered");
            Assert.Equal(id, PowerRegistry.Create(id).Id, $"{id} round-trips");
        }
    }

    public static void Test_Content_monsters_3_EveryMonsterPlaysTwelveTurnsAlone()
    {
        foreach (var ascension in new[] { 0, 10 })
        {
            foreach (var id in MonsterIds)
            {
                var cs = NewCombat(ascension, 31UL);
                string? slot = id switch
                {
                    "MYTE" => Myte.FirstSlot,
                    "WRIGGLER" => Wriggler.Slot2,
                    _ => null
                };
                var monster = SpawnAndAdd(cs, new MonsterSpawn(MonsterRegistry.Get(id), slot));
                MonsterLifecycle.AfterCreatureAdded(cs, monster);
                for (int turn = 0; turn < 12 && monster.IsAlive && !cs.IsOver; turn++)
                {
                    cs.Player.CurrentHp = cs.Player.MaxHp;
                    MonsterLifecycle.PerformMove(cs, monster);
                    if (!cs.Monsters.Contains(monster))
                    {
                        break;
                    }
                    MonsterLifecycle.RollMove(cs, monster);
                }
                var clone = cs.DeepClone();
                Assert.Equal(StateHasher.Hash(cs), StateHasher.Hash(clone), $"{id} A{ascension} clone hash");
            }
        }
    }

    public static void Test_Content_monsters_3_TerrorEelShriekStunsIntoTerror()
    {
        var cs = NewCombat(0, 5UL);
        var eel = SpawnAndAdd(cs, new MonsterSpawn(MonsterRegistry.Get(TerrorEel.MonsterIdEntry)));
        MonsterLifecycle.AfterCreatureAdded(cs, eel);
        Assert.Equal(TerrorEel.ShriekAmountBase, eel.GetPowerAmount<ShriekPower>());
        CreatureCmd.Damage(cs, eel, eel.CurrentHp - TerrorEel.ShriekAmountBase, ValueProp.Unpowered, cs.Player, null, null);
        Assert.False(eel.HasPower<ShriekPower>(), "Shriek removes itself");
        Assert.True(eel.IsStunned, "eel is stunned");
        Assert.Equal(TerrorEel.TerrorMoveId, eel.Ai.NextMove!.FollowUpStateId, "stun follows up with TERROR_MOVE");
    }

    public static void Test_Content_monsters_3_TunnelerBlockBreakStunsAndUnburrows()
    {
        var cs = NewCombat(0, 9UL);
        var tunneler = SpawnAndAdd(cs, new MonsterSpawn(MonsterRegistry.Get(Tunneler.MonsterIdEntry)));
        MonsterLifecycle.AfterCreatureAdded(cs, tunneler);
        MonsterLifecycle.PerformMove(cs, tunneler);
        MonsterLifecycle.RollMove(cs, tunneler);
        Assert.Equal(Tunneler.BurrowMoveId, tunneler.Ai.NextMove!.Id);
        MonsterLifecycle.PerformMove(cs, tunneler);
        Assert.True(tunneler.HasPower<BurrowedPower>(), "burrowed");
        Assert.Equal(Tunneler.BlockGainBase, tunneler.Block);
        MonsterLifecycle.RollMove(cs, tunneler);
        CreatureCmd.Damage(cs, tunneler, Tunneler.BlockGainBase + 1, ValueProp.Unpowered, cs.Player, null, null);
        Assert.False(tunneler.HasPower<BurrowedPower>(), "block break removes Burrowed");
        Assert.True(tunneler.IsStunned, "block break stuns");
        Assert.Equal(Tunneler.BiteMoveId, tunneler.Ai.NextMove!.FollowUpStateId);
        Assert.Equal(0, tunneler.Block);
    }

    private static CombatState NewCombat(int ascension, ulong seed)
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, 80, 80, StartingDecks.IroncladMaxEnergy);
        var cs = new CombatState(player, "CONTENT_MONSTERS_3_TEST", ascension, new RunRngSet(new Rng(seed)));
        cs.NextCreatureId = 1;
        cs.CurrentSide = CombatSide.Player;
        cs.IsInProgress = true;
        return cs;
    }

    private static MonsterState SpawnAndAdd(CombatState cs, MonsterSpawn spawn)
    {
        var monster = MonsterSpawner.Spawn(cs, spawn);
        cs.Monsters.Add(monster);
        MonsterLifecycle.SetUpForCombat(cs, monster);
        return monster;
    }
}

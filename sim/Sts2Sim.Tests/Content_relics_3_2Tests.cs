using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Relics;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_relics_3_2Tests
{
    private static readonly string[] Ids =
    {
        "MUSIC_BOX", "NINJA_SCROLL", "ODDLY_SMOOTH_STONE", "PAELS_TEARS", "PANTOGRAPH", "PERMAFROST",
        "PHILOSOPHERS_STONE", "POWER_CELL", "RADIANT_PEARL", "RAZOR_TOOTH", "RED_SKULL", "RUINED_HELMET",
        "SEAL_OF_GOLD", "SLING_OF_COURAGE", "STONE_CRACKER", "SWORD_OF_JADE", "TEA_OF_DISCOURTESY",
        "THROWING_AXE", "UNSETTLING_LAMP", "VAJRA"
    };

    private static CombatState NewCombat(int hp = 80)
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, hp, hp, 3);
        var run = RunState.CreateStandalone(player, new RunRngSet(777UL), 0);
        var cs = new CombatState(run, player, "RELICS_3_2_TEST");
        cs.IsInProgress = true;
        cs.Monsters.Add(new MonsterState(1, new MonsterDouble(), 50, 50, null, null));
        cs.NextCreatureId = 2;
        return cs;
    }

    private sealed class MonsterDouble : MonsterDef
    {
        public MonsterDouble()
            : base("RELICS_3_2_TEST_MONSTER")
        {
        }

        public override int MinInitialHp(CombatState cs) => 50;

        public override int MaxInitialHp(CombatState cs) => 50;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self) => throw new NotSupportedException();
    }

    private static T Equip<T>(CombatState cs, T relic) where T : RelicModel
    {
        relic.AttachTo(cs.Player);
        cs.Player.Relics.Add(relic);
        return relic;
    }

    public static void Test_Content_relics_3_2_AllIdsRegistered()
    {
        var all = new HashSet<string>(RelicRegistry.AllIds);
        foreach (var id in Ids)
        {
            Assert.True(all.Contains(id), id + " is registered");
            Assert.Equal(id, RelicRegistry.Create(id).Id, id + " creates itself");
        }
    }

    public static void Test_Content_relics_3_2_RedSkullTogglesStrengthAroundHalfHp()
    {
        var cs = NewCombat();
        var skull = Equip(cs, new RedSkull());
        cs.Player.CurrentHp = 40;
        skull.AfterCurrentHpChanged(cs.Run, cs.Player, -40m);
        Assert.Equal(3, cs.Player.GetPowerAmount<StrengthPower>(), "at exactly 50% the skull applies 3 Strength");
        Assert.Equal(RelicStatus.Active, skull.Status, "active below the threshold");
        skull.AfterCurrentHpChanged(cs.Run, cs.Player, 0m);
        Assert.Equal(3, cs.Player.GetPowerAmount<StrengthPower>(), "no double application");
        cs.Player.CurrentHp = 41;
        skull.AfterCurrentHpChanged(cs.Run, cs.Player, 1m);
        Assert.Equal(0, cs.Player.GetPowerAmount<StrengthPower>(), "above 50% the Strength is removed");
        Assert.Equal(RelicStatus.Normal, skull.Status, "normal above the threshold");
    }

    public static void Test_Content_relics_3_2_RuinedHelmetAndThrowingAxeFireOncePerCombat()
    {
        var cs = NewCombat();
        var helmet = Equip(cs, new RuinedHelmet());
        var strength = new StrengthPower();
        Assert.True(helmet.TryModifyPowerAmountReceived(cs, strength, cs.Player, 2m, cs.Player, out var doubled), "first positive Strength is modified");
        Assert.Equal(4m, doubled, "doubled");
        Assert.True(!helmet.TryModifyPowerAmountReceived(cs, strength, cs.Player, -2m, cs.Player, out _), "negative Strength is not modified");
        helmet.AfterModifyingPowerAmountReceived(cs, strength);
        Assert.True(!helmet.TryModifyPowerAmountReceived(cs, strength, cs.Player, 2m, cs.Player, out _), "used up for the combat");
        helmet.AfterCombatEnd(cs.Run, null);
        Assert.True(helmet.TryModifyPowerAmountReceived(cs, strength, cs.Player, 1m, cs.Player, out _), "reset at combat end");

        var axe = Equip(cs, new ThrowingAxe());
        var card = cs.CreateCard(Bash.CardId, cs.Player);
        Assert.Equal(2, axe.ModifyCardPlayCount(cs, card, null, 1), "first card plays twice");
        axe.AfterModifyingCardPlayCount(cs, card);
        Assert.Equal(1, axe.ModifyCardPlayCount(cs, card, null, 1), "only once per combat");
    }
}

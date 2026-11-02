using System.Reflection;
using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Cards.Afflictions;
using Sts2Sim.Core.Cards.Enchantments;
using Sts2Sim.Core.Choice;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Modifiers;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Orbs;
using Sts2Sim.Core.Potions;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Relics;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class HookTests
{
    private sealed class Recorder
    {
        public List<string> Log { get; } = new();

        public bool FullMode { get; set; }

        public List<(string Tag, ChoiceContextKind? Top, int Depth)> Contexts { get; } = new();

        public void Hit(string tag, string hook) => Log.Add(tag + ":" + hook);

        public T Track<T>(T model) where T : AbstractModel
        {
            if (FullMode)
            {
                model.SetOverriddenHooks(HookMask.All);
            }
            return model;
        }
    }

    private interface IProbe
    {
        string Tag { get; }

        Recorder Rec { get; }
    }

    private sealed class HookMonsterDef : MonsterDef
    {
        public HookMonsterDef()
            : base("HOOKS_TEST_MONSTER")
        {
        }

        public override int MinInitialHp(CombatState cs) => 40;

        public override int MaxInitialHp(CombatState cs) => 40;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self) => throw new NotSupportedException();
    }

    private sealed class HookCardDef : CardDef
    {
        public HookCardDef()
            : base("HOOKS_TEST_CARD", 1, CardType.Skill, CardRarity.Basic, TargetType.Self)
        {
        }

        public override void OnPlay(CombatState cs, CardPlay play)
        {
        }
    }

    private sealed class PowerProbe : PowerModel, IProbe
    {
        public PowerProbe(Recorder rec, string tag)
        {
            Rec = rec;
            Tag = tag;
        }

        public string Tag { get; set; }

        public Recorder Rec { get; set; }

        public decimal DamageBonus { get; set; }

        public decimal BlockFactor { get; set; } = 1m;

        public bool PreventsDeath { get; set; }

        public bool AllowsHitting { get; set; } = true;

        public bool TakesExtraTurn { get; set; }

        public decimal HandDrawBonus { get; set; }

        public Action<CombatState, PowerProbe, string>? OnHit { get; set; }

        public override string Id => "HOOKS_PROBE_POWER";

        public override PowerType Type => PowerType.Buff;

        public override PowerStackType StackType => PowerStackType.Counter;

        public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;

        private void Hit(CombatState? cs, string hook)
        {
            Rec.Hit(Tag, hook);
            if (cs != null)
            {
                OnHit?.Invoke(cs, this, hook);
            }
        }

        public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay) => Hit(cs, nameof(AfterCardPlayed));

        public override void AfterCardPlayedLate(CombatState cs, CardPlay cardPlay) => Hit(cs, nameof(AfterCardPlayedLate));

        public override void BeforeSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
        {
            RecordContext(cs);
            Hit(cs, nameof(BeforeSideTurnStart));
        }

        public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
        {
            RecordContext(cs);
            Hit(cs, nameof(AfterSideTurnStart));
        }

        public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
        {
            RecordContext(cs);
            Hit(cs, nameof(AfterSideTurnEnd));
        }

        public override void BeforeFlush(CombatState cs, PlayerState player)
        {
            RecordContext(cs);
            Hit(cs, nameof(BeforeFlush));
        }

        public override void AfterDeath(RunState rs, CreatureState creature, bool wasRemovalPrevented)
        {
            Rec.Contexts.Add((Tag, rs.Choice.ContextDepth > 0 ? rs.Choice.Contexts[rs.Choice.ContextDepth - 1].Kind : null, rs.Choice.ContextDepth));
            Hit(rs.Combat, nameof(AfterDeath));
        }

        public override void AfterCurrentHpChanged(RunState rs, CreatureState creature, decimal delta) => Hit(rs.Combat, nameof(AfterCurrentHpChanged));

        public override void AfterCardChangedPiles(RunState rs, CardInstance card, PileType oldPileType, AbstractModel? clonedBy) => Hit(rs.Combat, nameof(AfterCardChangedPiles));

        public override decimal ModifyDamageAdditive(RunState rs, CreatureState? target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
        {
            Rec.Hit(Tag, nameof(ModifyDamageAdditive) + "(" + amount + ")");
            return DamageBonus;
        }

        public override decimal ModifyBlockMultiplicative(CombatState cs, CreatureState target, decimal block, ValueProp props, CardInstance? cardSource, CardPlay? cardPlay)
        {
            Rec.Hit(Tag, nameof(ModifyBlockMultiplicative) + "(" + block + ")");
            return BlockFactor;
        }

        public override bool ShouldAllowHitting(CombatState cs, CreatureState creature)
        {
            Hit(cs, nameof(ShouldAllowHitting));
            return AllowsHitting;
        }

        public override bool ShouldDie(RunState rs, CreatureState creature)
        {
            Hit(rs.Combat, nameof(ShouldDie));
            return !PreventsDeath;
        }

        public override void AfterPreventingDeath(RunState rs, CreatureState creature) => Hit(rs.Combat, nameof(AfterPreventingDeath));

        public override void AfterModifyingDamageAmount(RunState rs, CardInstance? cardSource) => Hit(rs.Combat, nameof(AfterModifyingDamageAmount));

        public override decimal ModifyHandDraw(CombatState cs, PlayerState player, decimal count)
        {
            Hit(cs, nameof(ModifyHandDraw));
            return count + HandDrawBonus;
        }

        public override bool ShouldTakeExtraTurn(CombatState cs, PlayerState player)
        {
            Hit(cs, nameof(ShouldTakeExtraTurn));
            return TakesExtraTurn;
        }

        public override bool TryModifyKeywordsInCombat(CombatState cs, CardInstance card, ISet<CardKeyword> keywords)
        {
            Hit(cs, nameof(TryModifyKeywordsInCombat));
            return keywords.Add(CardKeyword.Retain);
        }

        private void RecordContext(CombatState cs)
        {
            var choice = cs.Choice;
            Rec.Contexts.Add((Tag, choice.ContextDepth > 0 ? choice.Contexts[choice.ContextDepth - 1].Kind : null, choice.ContextDepth));
        }
    }

    private sealed class QuietPowerProbe : PowerModel, IProbe
    {
        public QuietPowerProbe(Recorder rec, string tag)
        {
            Rec = rec;
            Tag = tag;
        }

        public string Tag { get; set; }

        public Recorder Rec { get; set; }

        public decimal DamageFactor { get; set; } = 1m;

        public override string Id => "HOOKS_QUIET_PROBE_POWER";

        public override PowerType Type => PowerType.Buff;

        public override PowerStackType StackType => PowerStackType.Counter;

        public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;

        public override void AfterCardPlayedLate(CombatState cs, CardPlay cardPlay) => Rec.Hit(Tag, nameof(AfterCardPlayedLate));

        public override decimal ModifyDamageMultiplicative(RunState rs, CreatureState? target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
        {
            Rec.Hit(Tag, nameof(ModifyDamageMultiplicative) + "(" + amount + ")");
            return DamageFactor;
        }

        public override void BeforeSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
        {
            var choice = cs.Choice;
            Rec.Contexts.Add((Tag, choice.ContextDepth > 0 ? choice.Contexts[choice.ContextDepth - 1].Kind : null, choice.ContextDepth));
            Rec.Hit(Tag, nameof(BeforeSideTurnEnd));
        }
    }

    private sealed class RelicProbe : RelicModel, IProbe
    {
        public RelicProbe(Recorder rec, string tag)
        {
            Rec = rec;
            Tag = tag;
        }

        public string Tag { get; set; }

        public Recorder Rec { get; set; }

        public decimal DamageFactor { get; set; } = 1m;

        public bool PreventsDeath { get; set; }

        public bool ForcesPotionReward { get; set; }

        public decimal PriceDelta { get; set; }

        public Action<CombatState, RelicProbe, string>? OnHit { get; set; }

        public override string Id => "HOOKS_PROBE_RELIC";

        public override RelicRarity Rarity => RelicRarity.Common;

        private void Hit(CombatState? cs, string hook)
        {
            Rec.Hit(Tag, hook);
            if (cs != null)
            {
                OnHit?.Invoke(cs, this, hook);
            }
        }

        public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay) => Hit(cs, nameof(AfterCardPlayed));

        public override void AfterGoldGained(RunState rs, PlayerState player) => Hit(rs.Combat, nameof(AfterGoldGained));

        public override void AfterCurrentHpChanged(RunState rs, CreatureState creature, decimal delta) => Hit(rs.Combat, nameof(AfterCurrentHpChanged));

        public override decimal ModifyDamageMultiplicative(RunState rs, CreatureState? target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
        {
            Rec.Hit(Tag, nameof(ModifyDamageMultiplicative) + "(" + amount + ")");
            return DamageFactor;
        }

        public override bool ShouldDie(RunState rs, CreatureState creature)
        {
            Hit(rs.Combat, nameof(ShouldDie));
            return !PreventsDeath;
        }

        public override void AfterPreventingDeath(RunState rs, CreatureState creature) => Hit(rs.Combat, nameof(AfterPreventingDeath));

        public override decimal ModifyMerchantPrice(RunState rs, PlayerState player, MerchantEntry entry, decimal cost)
        {
            Rec.Hit(Tag, nameof(ModifyMerchantPrice) + "(" + cost + ")");
            return cost + PriceDelta;
        }

        public override bool ShouldForcePotionReward(RunState rs, PlayerState player, RoomType roomType)
        {
            Rec.Hit(Tag, nameof(ShouldForcePotionReward));
            return ForcesPotionReward;
        }

        public override void AfterSideTurnEndLate(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
        {
            var choice = cs.Choice;
            Rec.Contexts.Add((Tag, choice.ContextDepth > 0 ? choice.Contexts[choice.ContextDepth - 1].Kind : null, choice.ContextDepth));
            Hit(cs, nameof(AfterSideTurnEndLate));
        }

        public override void BeforeSideTurnEndEarly(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
        {
            var choice = cs.Choice;
            Rec.Contexts.Add((Tag, choice.ContextDepth > 0 ? choice.Contexts[choice.ContextDepth - 1].Kind : null, choice.ContextDepth));
            Hit(cs, nameof(BeforeSideTurnEndEarly));
        }

        public override void AfterDiedToDoom(CombatState cs, IReadOnlyList<CreatureState> creatures)
        {
            var choice = cs.Choice;
            Rec.Contexts.Add((Tag, choice.ContextDepth > 0 ? choice.Contexts[choice.ContextDepth - 1].Kind : null, choice.ContextDepth));
            Hit(cs, nameof(AfterDiedToDoom));
        }
    }

    private sealed class PotionProbe : PotionModel, IProbe
    {
        public PotionProbe(Recorder rec, string tag)
        {
            Rec = rec;
            Tag = tag;
        }

        public string Tag { get; set; }

        public Recorder Rec { get; set; }

        public decimal BlockBonus { get; set; }

        public bool ForcesPotionReward { get; set; }

        public override string Id => "HOOKS_PROBE_POTION";

        public override PotionRarity Rarity => PotionRarity.Common;

        public override PotionUsage Usage => PotionUsage.CombatOnly;

        public override TargetType TargetType => TargetType.Self;

        public override void AfterCardPlayedLate(CombatState cs, CardPlay cardPlay) => Rec.Hit(Tag, nameof(AfterCardPlayedLate));

        public override decimal ModifyBlockAdditive(CombatState cs, CreatureState target, decimal block, ValueProp props, CardInstance? cardSource, CardPlay? cardPlay)
        {
            Rec.Hit(Tag, nameof(ModifyBlockAdditive) + "(" + block + ")");
            return BlockBonus;
        }

        public override bool ShouldForcePotionReward(RunState rs, PlayerState player, RoomType roomType)
        {
            Rec.Hit(Tag, nameof(ShouldForcePotionReward));
            return ForcesPotionReward;
        }

        public override void AfterPotionUsed(RunState rs, PotionModel potion, CreatureState? target) => Rec.Hit(Tag, nameof(AfterPotionUsed));

        public override void BeforeFlushLate(CombatState cs, PlayerState player)
        {
            var choice = cs.Choice;
            Rec.Contexts.Add((Tag, choice.ContextDepth > 0 ? choice.Contexts[choice.ContextDepth - 1].Kind : null, choice.ContextDepth));
            Rec.Hit(Tag, nameof(BeforeFlushLate));
        }

        public override void AfterCardDrawn(CombatState cs, CardInstance card, bool fromHandDraw) => Rec.Hit(Tag, nameof(AfterCardDrawn));
    }

    private sealed class OrbProbe : OrbModel, IProbe
    {
        public OrbProbe(Recorder rec, string tag)
        {
            Rec = rec;
            Tag = tag;
        }

        public string Tag { get; set; }

        public Recorder Rec { get; set; }

        public override string Id => "HOOKS_PROBE_ORB";

        public override decimal PassiveVal(CombatState cs) => 0m;

        public override decimal EvokeVal(CombatState cs) => 0m;

        public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay) => Rec.Hit(Tag, nameof(AfterCardPlayed));

        public override decimal ModifyOrbValue(CombatState cs, OrbModel orb, decimal value)
        {
            Rec.Hit(Tag, nameof(ModifyOrbValue));
            return value + 1m;
        }
    }

    private sealed class CardListenerProbe : AbstractModel, IProbe
    {
        public CardListenerProbe(Recorder rec, string tag)
        {
            Rec = rec;
            Tag = tag;
        }

        public string Tag { get; set; }

        public Recorder Rec { get; set; }

        public decimal DamageCap { get; set; } = decimal.MaxValue;

        public bool AllowsPlay { get; set; } = true;

        public Action<CombatState, CardListenerProbe, string>? OnHit { get; set; }

        private void Hit(CombatState? cs, string hook)
        {
            Rec.Hit(Tag, hook);
            if (cs != null)
            {
                OnHit?.Invoke(cs, this, hook);
            }
        }

        public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay) => Hit(cs, nameof(AfterCardPlayed));

        public override void AfterCardDrawnEarly(CombatState cs, CardInstance card, bool fromHandDraw) => Hit(cs, nameof(AfterCardDrawnEarly));

        public override void AfterCardChangedPiles(RunState rs, CardInstance card, PileType oldPileType, AbstractModel? clonedBy) => Hit(rs.Combat, nameof(AfterCardChangedPiles));

        public override void AfterCurrentHpChanged(RunState rs, CreatureState creature, decimal delta) => Hit(rs.Combat, nameof(AfterCurrentHpChanged));

        public override decimal ModifyDamageCap(RunState rs, CreatureState? target, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
        {
            Rec.Hit(Tag, nameof(ModifyDamageCap));
            return DamageCap;
        }

        public override bool ShouldPlay(CombatState cs, CardInstance card, AutoPlayType autoPlayType)
        {
            Hit(cs, nameof(ShouldPlay));
            return AllowsPlay;
        }

        public override void AfterGoldGained(RunState rs, PlayerState player) => Hit(rs.Combat, nameof(AfterGoldGained));

        public override void BeforeSideTurnEndVeryEarly(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
        {
            var choice = cs.Choice;
            Rec.Contexts.Add((Tag, choice.ContextDepth > 0 ? choice.Contexts[choice.ContextDepth - 1].Kind : null, choice.ContextDepth));
            Hit(cs, nameof(BeforeSideTurnEndVeryEarly));
        }
    }

    private sealed class MonsterListenerProbe : AbstractModel, IProbe
    {
        public MonsterListenerProbe(Recorder rec, string tag)
        {
            Rec = rec;
            Tag = tag;
        }

        public string Tag { get; set; }

        public Recorder Rec { get; set; }

        public bool StopsCombatFromEnding { get; set; }

        public decimal DamageBonus { get; set; }

        public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay) => Rec.Hit(Tag, nameof(AfterCardPlayed));

        public override void BeforeSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
        {
            var choice = cs.Choice;
            Rec.Contexts.Add((Tag, choice.ContextDepth > 0 ? choice.Contexts[choice.ContextDepth - 1].Kind : null, choice.ContextDepth));
            Rec.Hit(Tag, nameof(BeforeSideTurnStart));
        }

        public override void AfterDeath(RunState rs, CreatureState creature, bool wasRemovalPrevented)
        {
            var choice = rs.Choice;
            Rec.Contexts.Add((Tag, choice.ContextDepth > 0 ? choice.Contexts[choice.ContextDepth - 1].Kind : null, choice.ContextDepth));
            Rec.Hit(Tag, nameof(AfterDeath));
        }

        public override bool ShouldStopCombatFromEnding(CombatState cs)
        {
            Rec.Hit(Tag, nameof(ShouldStopCombatFromEnding));
            return StopsCombatFromEnding;
        }

        public override decimal ModifyDamageAdditive(RunState rs, CreatureState? target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
        {
            Rec.Hit(Tag, nameof(ModifyDamageAdditive) + "(" + amount + ")");
            return DamageBonus;
        }

        public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants) => Rec.Hit(Tag, nameof(AfterSideTurnEnd));
    }

    private sealed class ModifierProbe : ModifierModel, IProbe
    {
        public ModifierProbe(Recorder rec, string tag)
        {
            Rec = rec;
            Tag = tag;
        }

        public string Tag { get; set; }

        public Recorder Rec { get; set; }

        public decimal DamageCap { get; set; } = decimal.MaxValue;

        public bool AllowsFreeTravel { get; set; }

        public decimal PriceFactor { get; set; } = 1m;

        public override string Id => "HOOKS_PROBE_MODIFIER";

        public override void AfterCardPlayedLate(CombatState cs, CardPlay cardPlay) => Rec.Hit(Tag, nameof(AfterCardPlayedLate));

        public override void AfterGoldGained(RunState rs, PlayerState player) => Rec.Hit(Tag, nameof(AfterGoldGained));

        public override decimal ModifyDamageCap(RunState rs, CreatureState? target, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
        {
            Rec.Hit(Tag, nameof(ModifyDamageCap));
            return DamageCap;
        }

        public override bool ShouldAllowFreeTravel(RunState rs)
        {
            Rec.Hit(Tag, nameof(ShouldAllowFreeTravel));
            return AllowsFreeTravel;
        }

        public override decimal ModifyMerchantPrice(RunState rs, PlayerState player, MerchantEntry entry, decimal cost)
        {
            Rec.Hit(Tag, nameof(ModifyMerchantPrice) + "(" + cost + ")");
            return cost * PriceFactor;
        }
    }

    private sealed class EnchantmentProbe : EnchantmentModel, IProbe
    {
        public EnchantmentProbe(Recorder rec, string tag)
        {
            Rec = rec;
            Tag = tag;
        }

        public string Tag { get; set; }

        public Recorder Rec { get; set; }

        public decimal BlockAdd { get; set; }

        public decimal BlockMul { get; set; } = 1m;

        public decimal DamageAdd { get; set; }

        public decimal DamageMul { get; set; } = 1m;

        public override string Id => "HOOKS_PROBE_ENCHANTMENT";

        public override decimal EnchantBlockAdditive(decimal originalBlock)
        {
            Rec.Hit(Tag, nameof(EnchantBlockAdditive) + "(" + originalBlock + ")");
            return BlockAdd;
        }

        public override decimal EnchantBlockMultiplicative(decimal originalBlock)
        {
            Rec.Hit(Tag, nameof(EnchantBlockMultiplicative) + "(" + originalBlock + ")");
            return BlockMul;
        }

        public override decimal EnchantDamageAdditive(decimal originalDamage, ValueProp props)
        {
            Rec.Hit(Tag, nameof(EnchantDamageAdditive) + "(" + originalDamage + ")");
            return DamageAdd;
        }

        public override decimal EnchantDamageMultiplicative(decimal originalDamage, ValueProp props)
        {
            Rec.Hit(Tag, nameof(EnchantDamageMultiplicative) + "(" + originalDamage + ")");
            return DamageMul;
        }

        public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay) => Rec.Hit(Tag, nameof(AfterCardPlayed));

        public override decimal ModifyDamageAdditive(RunState rs, CreatureState? target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
        {
            Rec.Hit(Tag, nameof(ModifyDamageAdditive) + "(" + amount + ")");
            return 100m;
        }

        public override void AfterCardChangedPiles(RunState rs, CardInstance card, PileType oldPileType, AbstractModel? clonedBy) => Rec.Hit(Tag, nameof(AfterCardChangedPiles));
    }

    private sealed class AfflictionProbe : AfflictionModel, IProbe
    {
        public AfflictionProbe(Recorder rec, string tag)
        {
            Rec = rec;
            Tag = tag;
        }

        public string Tag { get; set; }

        public Recorder Rec { get; set; }

        public decimal BlockBonus { get; set; }

        public override string Id => "HOOKS_PROBE_AFFLICTION";

        public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay) => Rec.Hit(Tag, nameof(AfterCardPlayed));

        public override decimal ModifyBlockAdditive(CombatState cs, CreatureState target, decimal block, ValueProp props, CardInstance? cardSource, CardPlay? cardPlay)
        {
            Rec.Hit(Tag, nameof(ModifyBlockAdditive) + "(" + block + ")");
            return BlockBonus;
        }
    }

    private sealed class HookMerchantEntry : MerchantEntry
    {
        public HookMerchantEntry() : base(null!)
        {
        }
    }

    private sealed class HookRoom : AbstractRoom
    {
        public override RoomType RoomType => RoomType.Map;
    }

    private sealed class HookMap : ActMap
    {
        public override MapPoint BossMapPoint => throw new NotSupportedException();

        public override MapPoint StartingMapPoint => throw new NotSupportedException();

        protected override MapPoint?[,] Grid => throw new NotSupportedException();
    }

    private sealed class HookEvent : EventModel
    {
        public override string Id => "HOOK_EVENT";
    }

    private abstract class BaseOverride : AbstractModel
    {
        public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
        {
        }
    }

    private sealed class DerivedOverride : BaseOverride
    {
        public sealed override bool ShouldDie(RunState rs, CreatureState creature) => true;
    }

    private sealed class HidingModel : AbstractModel
    {
        public new void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
        {
        }
    }

    private sealed class EmptyModel : AbstractModel
    {
    }

    private static CombatState NewCombat(int monsters = 2)
    {
        var player = new PlayerState(0, "IRONCLAD", 70, 80, 3);
        var cs = new CombatState(player, "HOOKS_TEST", 0, new RunRngSet(new Rng(4242UL)));
        cs.IsInProgress = true;
        var def = new HookMonsterDef();
        for (int i = 0; i < monsters; i++)
        {
            cs.Monsters.Add(new MonsterState(cs.AllocateCreatureId(), def, 40, 40, null, null));
        }
        return cs;
    }

    private static T Power<T>(CreatureState owner, T power) where T : PowerModel
    {
        power.ApplyInternal(owner, 1m);
        return power;
    }

    private static RelicProbe Relic(PlayerState player, RelicProbe relic)
    {
        player.AddRelicInternal(relic);
        return relic;
    }

    private static PotionProbe Potion(PlayerState player, PotionProbe potion, int slot)
    {
        var result = player.AddPotionInternal(potion, slot);
        Assert.True(result.Success, "test potion placed in its slot");
        return potion;
    }

    private static CardInstance Card(CombatState cs, CardPile pile, AbstractModel? listener)
    {
        var card = new CardInstance(new HookCardDef(), cs.AllocateCardInstanceId());
        card.AttachTo(cs.Player);
        card.Listener = listener;
        pile.AddInternal(card);
        return card;
    }

    private static CardInstance DeckCard(CombatState cs, AbstractModel? listener)
    {
        var card = new CardInstance(new HookCardDef(), cs.AllocateCardInstanceId());
        card.AttachTo(cs.Player);
        card.Listener = listener;
        cs.Player.Deck.Add(card);
        return card;
    }

    private static void BindAffliction(AfflictionModel affliction, CardInstance card)
    {
        card.AfflictInternal(affliction, affliction.Amount);
        Assert.True(ReferenceEquals(card, affliction.Card), "AfflictInternal binds the affliction to its card");
    }

    private static CardPlay PlayOf(CombatState cs) => new(cs, new CardInstance(new HookCardDef(), cs.AllocateCardInstanceId()), null);

    private static List<string> Tags(IEnumerable<AbstractModel> models) => models.Select(m => m is IProbe p ? p.Tag : m.GetType().Name).ToList();

    private static List<string> FullCombatList(CombatState cs)
    {
        var buffer = new List<AbstractModel>();
        Hook.IterateHookListeners(cs, buffer);
        return Tags(buffer);
    }

    public static void Test_Hooks_CombatListOrderFollowsTheGame()
    {
        var rec = new Recorder();
        var cs = NewCombat(2);
        var player = cs.Player;
        Power(player, new PowerProbe(rec, "P1"));
        Power(player, new QuietPowerProbe(rec, "P2"));
        Relic(player, new RelicProbe(rec, "R1"));
        var melted = Relic(player, new RelicProbe(rec, "R2"));
        melted.IsWax = true;
        player.MeltRelicInternal(melted);
        Relic(player, new RelicProbe(rec, "R3"));
        Potion(player, new PotionProbe(rec, "Q1"), 0);
        Potion(player, new PotionProbe(rec, "Q2"), 2);
        Card(cs, player.PlayPile, new CardListenerProbe(rec, "CPlay"));
        Card(cs, player.ExhaustPile, new CardListenerProbe(rec, "CExhaust"));
        Card(cs, player.Hand, new CardListenerProbe(rec, "CHand1"));
        Card(cs, player.Hand, null);
        Card(cs, player.Hand, new CardListenerProbe(rec, "CHand2"));
        Card(cs, player.DrawPile, new CardListenerProbe(rec, "CDraw"));
        Card(cs, player.DiscardPile, new CardListenerProbe(rec, "CDiscard"));
        var m1 = cs.Monsters[0];
        var m2 = cs.Monsters[1];
        Power(m1, new PowerProbe(rec, "M1P"));
        m1.Listener = new MonsterListenerProbe(rec, "M1L");
        Power(m2, new QuietPowerProbe(rec, "M2P1"));
        Power(m2, new PowerProbe(rec, "M2P2"));
        m2.Listener = new MonsterListenerProbe(rec, "M2L");
        cs.Run.Modifiers.Add(new ModifierProbe(rec, "Mod1"));
        cs.Run.Modifiers.Add(new ModifierProbe(rec, "Mod2"));

        Assert.SequenceEqual(
            new[] { "P1", "P2", "R1", "R3", "Q1", "Q2", "CHand1", "CHand2", "CDraw", "CDiscard", "CExhaust", "CPlay", "M1P", "M1L", "M2P1", "M2P2", "M2L", "Mod1", "Mod2" },
            FullCombatList(cs),
            "allies then enemies; per creature its powers, then the monster listener or the player's relics without melted ones, potion slots without empty ones and the pile cards in Hand, Draw, Discard, Exhaust, Play order; then the run modifiers");

        Hook.AfterCardPlayed(cs, PlayOf(cs));
        Assert.SequenceEqual(
            new[]
            {
                "P1:AfterCardPlayed", "R1:AfterCardPlayed", "R3:AfterCardPlayed", "CHand1:AfterCardPlayed", "CHand2:AfterCardPlayed", "CDraw:AfterCardPlayed", "CDiscard:AfterCardPlayed",
                "CExhaust:AfterCardPlayed", "CPlay:AfterCardPlayed", "M1P:AfterCardPlayed", "M1L:AfterCardPlayed", "M2P2:AfterCardPlayed", "M2L:AfterCardPlayed",
                "P1:AfterCardPlayedLate", "P2:AfterCardPlayedLate", "Q1:AfterCardPlayedLate", "Q2:AfterCardPlayedLate", "M1P:AfterCardPlayedLate", "M2P1:AfterCardPlayedLate", "M2P2:AfterCardPlayedLate",
                "Mod1:AfterCardPlayedLate", "Mod2:AfterCardPlayedLate"
            },
            rec.Log,
            "each pass visits the listeners that override its virtual, in combat-list order");
    }

    public static void Test_Hooks_InactivePlayerContributesOnlyItsPowersWhichAreNotLive()
    {
        var rec = new Recorder();
        var cs = NewCombat(1);
        var player = cs.Player;
        Power(player, new PowerProbe(rec, "P"));
        Relic(player, new RelicProbe(rec, "R"));
        Potion(player, new PotionProbe(rec, "Q"), 0);
        Card(cs, player.Hand, new CardListenerProbe(rec, "C"));
        cs.Monsters[0].Listener = new MonsterListenerProbe(rec, "ML");
        player.DeactivateHooks();
        Assert.SequenceEqual(new[] { "ML" }, FullCombatList(cs), "an inactive player adds powers only, and their liveness needs an active owner");
        Hook.AfterCardPlayed(cs, PlayOf(cs));
        Assert.SequenceEqual(new[] { "ML:AfterCardPlayed" }, rec.Log);
        player.ActivateHooks();
        Assert.SequenceEqual(new[] { "P", "R", "Q", "C", "ML" }, FullCombatList(cs));
    }

    public static void Test_Hooks_RemovedPowerRelicCardAndMonsterAreSkippedWithinThePass()
    {
        var rec = new Recorder();
        var cs = NewCombat(2);
        var player = cs.Player;
        var p1 = Power(player, new PowerProbe(rec, "P1"));
        var p2 = Power(player, new PowerProbe(rec, "P2"));
        var r1 = Relic(player, new RelicProbe(rec, "R1"));
        var r2 = Relic(player, new RelicProbe(rec, "R2"));
        var q1 = Potion(player, new PotionProbe(rec, "Q1"), 0);
        var c1 = Card(cs, player.Hand, new CardListenerProbe(rec, "C1"));
        Card(cs, player.Hand, new CardListenerProbe(rec, "C2"));
        var m1 = cs.Monsters[0];
        var m2 = cs.Monsters[1];
        Power(m2, new PowerProbe(rec, "M2P"));
        m2.Listener = new MonsterListenerProbe(rec, "M2L");
        p1.OnHit = (s, self, hook) =>
        {
            if (hook != nameof(AbstractModel.AfterCardPlayed))
            {
                return;
            }
            player.RemoveRelicInternal(r2);
            c1.HasBeenRemovedFromState = true;
            q1.RemoveBeforeUse();
            s.Monsters.Remove(m2);
            Power(player, new PowerProbe(rec, "P3"));
        };
        r1.OnHit = (s, self, hook) =>
        {
            if (hook == nameof(AbstractModel.AfterCardPlayed))
            {
                p2.Rec.Log.Add("r1-sees-p2-still-listed:" + player.Powers.Contains(p2));
            }
        };
        Hook.AfterCardPlayed(cs, PlayOf(cs));
        Assert.SequenceEqual(
            new[]
            {
                "P1:AfterCardPlayed", "P2:AfterCardPlayed", "R1:AfterCardPlayed", "r1-sees-p2-still-listed:True", "C2:AfterCardPlayed",
                "P1:AfterCardPlayedLate", "P2:AfterCardPlayedLate", "P3:AfterCardPlayedLate"
            },
            rec.Log,
            "pass 1 keeps the list built at its first MoveNext: R2 (HasBeenRemovedFromState), the potion, C1 (HasBeenRemovedFromState) and M2 with its power and listener (no longer in the combat) are skipped at yield; P3 added in pass 1 runs only in pass 2's fresh snapshot");
    }

    public static void Test_Hooks_PowerRemovedFromItsListIsStillLiveWhileItsOwnerIsAttached()
    {
        var rec = new Recorder();
        var cs = NewCombat(1);
        var player = cs.Player;
        var p1 = Power(player, new PowerProbe(rec, "P1"));
        var p2 = Power(player, new PowerProbe(rec, "P2"));
        p1.OnHit = (s, self, hook) =>
        {
            if (hook == nameof(AbstractModel.AfterCardPlayed))
            {
                p2.RemoveInternal();
            }
        };
        Hook.AfterCardPlayed(cs, PlayOf(cs));
        Assert.SequenceEqual(
            new[] { "P1:AfterCardPlayed", "P2:AfterCardPlayed", "P1:AfterCardPlayedLate" },
            rec.Log,
            "the game's power rule is Owner.CombatState != null && (Owner.Player?.IsActiveForHooks ?? true): a power taken off its creature's list inside the pass keeps its owner, so the pass still visits it; the next pass's list no longer holds it");
    }

    public static void Test_Hooks_PlayerDeactivatedMidPassSkipsItsRemainingListeners()
    {
        var rec = new Recorder();
        var cs = NewCombat(1);
        var player = cs.Player;
        var p1 = Power(player, new PowerProbe(rec, "P1"));
        Power(player, new PowerProbe(rec, "P2"));
        Relic(player, new RelicProbe(rec, "R"));
        Card(cs, player.Hand, new CardListenerProbe(rec, "C"));
        cs.Monsters[0].Listener = new MonsterListenerProbe(rec, "ML");
        p1.OnHit = (s, self, hook) =>
        {
            if (hook == nameof(AbstractModel.AfterCardPlayed))
            {
                player.DeactivateHooks();
            }
        };
        Hook.AfterCardPlayed(cs, PlayOf(cs));
        Assert.SequenceEqual(new[] { "P1:AfterCardPlayed", "ML:AfterCardPlayed" }, rec.Log);
    }

    public static void Test_Hooks_RelicMeltedMidPassIsStillVisitedUntilTheNextSnapshot()
    {
        var rec = new Recorder();
        var cs = NewCombat(1);
        var player = cs.Player;
        var r1 = Relic(player, new RelicProbe(rec, "R1"));
        var r2 = Relic(player, new RelicProbe(rec, "R2"));
        r2.IsWax = true;
        r1.OnHit = (s, self, hook) =>
        {
            if (hook == nameof(AbstractModel.AfterCardPlayed) && !r2.IsMelted)
            {
                player.MeltRelicInternal(r2);
            }
        };
        Hook.AfterCardPlayed(cs, PlayOf(cs));
        Hook.AfterCardPlayed(cs, PlayOf(cs));
        Assert.SequenceEqual(
            new[] { "R1:AfterCardPlayed", "R2:AfterCardPlayed", "R1:AfterCardPlayed" },
            rec.Log,
            "IsMelted filters relics when the list is built, the yield filter checks only HasBeenRemovedFromState and the owner");
    }

    public static void Test_Hooks_GuardedSourceIsSilentOnceTheCombatIsOverOrEnding()
    {
        var rec = new Recorder();
        var cs = NewCombat(1);
        Power(cs.Player, new PowerProbe(rec, "P"));
        cs.Monsters[0].CurrentHp = 0;
        Assert.True(CombatStatus.IsOverOrEnding(cs));
        Hook.AfterSideTurnStart(cs, CombatSide.Player, cs.Allies);
        Hook.BeforeFlush(cs, cs.Player);
        Assert.Equal(0, rec.Log.Count, "guarded dispatchers yield nothing while IsOverOrEnding && !IsStarting");
        Hook.AfterCardPlayed(cs, PlayOf(cs));
        Assert.SequenceEqual(new[] { "P:AfterCardPlayed", "P:AfterCardPlayedLate" }, rec.Log, "the combat source is unguarded");
        rec.Log.Clear();
        Hook.AfterCurrentHpChanged(cs, cs.Player, 1m);
        Assert.SequenceEqual(new[] { "P:AfterCurrentHpChanged" }, rec.Log, "the run source is unguarded");
        rec.Log.Clear();
        cs.IsStarting = true;
        Hook.AfterSideTurnStart(cs, CombatSide.Player, cs.Allies);
        Assert.SequenceEqual(new[] { "P:AfterSideTurnStart" }, rec.Log, "while the combat is starting the guarded source visits");
        var buffer = new List<AbstractModel>();
        cs.IsStarting = false;
        Hook.IterateCombatHookListeners(cs, buffer);
        Assert.Equal(0, buffer.Count);
        Hook.IterateHookListeners(cs, buffer);
        Assert.Equal(1, buffer.Count);
    }

    public static void Test_Hooks_GuardIsEvaluatedOncePerPass()
    {
        foreach (bool kill in new[] { false, true })
        {
            var rec = new Recorder();
            var cs = NewCombat(1);
            var monster = cs.Monsters[0];
            var p1 = Power(cs.Player, new PowerProbe(rec, "P1"));
            Power(cs.Player, new PowerProbe(rec, "P2"));
            Potion(cs.Player, new PotionProbe(rec, "Q"), 0);
            p1.OnHit = (s, self, hook) =>
            {
                if (kill && hook == nameof(AbstractModel.BeforeFlush))
                {
                    monster.CurrentHp = 0;
                }
            };
            Hook.BeforeFlush(cs, cs.Player);
            var expected = kill
                ? new[] { "P1:BeforeFlush", "P2:BeforeFlush" }
                : new[] { "P1:BeforeFlush", "P2:BeforeFlush", "Q:BeforeFlushLate" };
            Assert.SequenceEqual(
                expected,
                rec.Log,
                "the guard runs at each pass's first MoveNext: the pass that ends the combat finishes, the next guarded pass yields nothing");
        }
    }

    public static void Test_Hooks_EachPassTakesAFreshSnapshot()
    {
        var rec = new Recorder();
        var cs = NewCombat(1);
        var p1 = Power(cs.Player, new PowerProbe(rec, "P1"));
        p1.OnHit = (s, self, hook) =>
        {
            if (hook == nameof(AbstractModel.AfterCardPlayed))
            {
                Power(cs.Player, new QuietPowerProbe(rec, "Q"));
                Power(cs.Player, new PowerProbe(rec, "P2"));
            }
        };
        Hook.AfterCardPlayed(cs, PlayOf(cs));
        Assert.SequenceEqual(
            new[] { "P1:AfterCardPlayed", "P1:AfterCardPlayedLate", "Q:AfterCardPlayedLate", "P2:AfterCardPlayedLate" },
            rec.Log,
            "listeners added during pass 1 are not in pass 1's snapshot and are in pass 2's");
    }

    public static void Test_Hooks_ModifyBlockAppliesTheEnchantmentPrePassFirst()
    {
        var rec = new Recorder();
        var cs = NewCombat(1);
        var card = new CardInstance(new HookCardDef(), cs.AllocateCardInstanceId());
        var enchantment = new EnchantmentProbe(rec, "E") { BlockAdd = 3m, BlockMul = 1.5m };
        card.Enchantment = enchantment;
        enchantment.ClearInternal();
        var potion = Potion(cs.Player, new PotionProbe(rec, "Q") { BlockBonus = 2m }, 0);
        var power = Power(cs.Player, new PowerProbe(rec, "P") { BlockFactor = 0.75m });
        List<AbstractModel>? modifiers = new() { power };
        var play = new CardPlay(cs, card, null);
        decimal block = Hook.ModifyBlock(cs, cs.Player, 5m, ValueProp.Move, card, play, ref modifiers);
        Assert.Equal(10.5m, block, "(5 + 3) x 1.5 = 12 from the enchantment, then + 2 additive, then x 0.75 multiplicative");
        Assert.SequenceEqual(
            new[] { "E:EnchantBlockAdditive(5)", "E:EnchantBlockMultiplicative(8)", "Q:ModifyBlockAdditive(12.0)", "P:ModifyBlockMultiplicative(14.0)" },
            rec.Log);
        Assert.SequenceEqual(new AbstractModel[] { potion, power }, modifiers!, "the enchantment terms never enter the modifier list, and the ref list starts empty like the game's fresh out list");
        Assert.False(enchantment.HasCard, "the pre-pass reads cardSource.Enchantment and does not need a bound enchantment");
    }

    public static void Test_Hooks_ModifyDamagePrePassIsGatedByHookType()
    {
        var cases = new (ModifyDamageHookType Type, decimal Expected, string[] Calls)[]
        {
            (ModifyDamageHookType.All, 20m, new[] { "E:EnchantDamageAdditive(6)", "E:EnchantDamageMultiplicative(10)" }),
            (ModifyDamageHookType.Additive, 10m, new[] { "E:EnchantDamageAdditive(6)" }),
            (ModifyDamageHookType.Multiplicative, 12m, new[] { "E:EnchantDamageMultiplicative(6)" }),
            (ModifyDamageHookType.Cap, 6m, Array.Empty<string>()),
            (ModifyDamageHookType.Additive | ModifyDamageHookType.Cap, 10m, new[] { "E:EnchantDamageAdditive(6)" })
        };
        foreach (var (type, expected, calls) in cases)
        {
            var rec = new Recorder();
            var cs = NewCombat(1);
            var card = new CardInstance(new HookCardDef(), cs.AllocateCardInstanceId());
            card.Enchantment = new EnchantmentProbe(rec, "E") { DamageAdd = 4m, DamageMul = 2m };
            decimal damage = Hook.ModifyDamage(cs, cs.Monsters[0], cs.Player, 6m, ValueProp.Move, card, null, type);
            Assert.Equal(expected, damage, $"{type}: additive when the Additive flag is set, then multiplicative when the Multiplicative flag is set");
            Assert.SequenceEqual(calls, rec.Log, type.ToString());
        }
    }

    public static void Test_Hooks_ModifyDamageListenerPassesFollowThePrePass()
    {
        var rec = new Recorder();
        var cs = NewCombat(1);
        var card = new CardInstance(new HookCardDef(), cs.AllocateCardInstanceId());
        card.Enchantment = new EnchantmentProbe(rec, "E") { DamageAdd = 4m, DamageMul = 2m };
        var power = Power(cs.Player, new PowerProbe(rec, "P") { DamageBonus = 1m });
        var relic = Relic(cs.Player, new RelicProbe(rec, "R") { DamageFactor = 1.5m });
        var capper = Card(cs, cs.Player.Hand, new CardListenerProbe(rec, "C") { DamageCap = 25m });
        List<AbstractModel>? modifiers = null;
        decimal damage = Hook.ModifyDamage(cs, cs.Monsters[0], cs.Player, 6m, ValueProp.Move, card, null, ModifyDamageHookType.All, ref modifiers);
        Assert.Equal(25m, damage, "(6 + 4) x 2 = 20, + 1 = 21, x 1.5 = 31.5, capped at 25");
        Assert.SequenceEqual(
            new[] { "E:EnchantDamageAdditive(6)", "E:EnchantDamageMultiplicative(10)", "P:ModifyDamageAdditive(20)", "R:ModifyDamageMultiplicative(21)", "C:ModifyDamageCap" },
            rec.Log);
        Assert.SequenceEqual(new AbstractModel[] { power, relic, capper.Listener! }, modifiers!);
    }

    public static void Test_Hooks_SixPerListenerDispatchersPushAPerListenerContext()
    {
        var rec = new Recorder();
        var cs = NewCombat(1);
        var player = cs.Player;
        Power(player, new PowerProbe(rec, "P"));
        Power(player, new QuietPowerProbe(rec, "QP"));
        Relic(player, new RelicProbe(rec, "R"));
        Potion(player, new PotionProbe(rec, "Q"), 0);
        Card(cs, player.Hand, new CardListenerProbe(rec, "C"));
        cs.Monsters[0].Listener = new MonsterListenerProbe(rec, "ML");
        using var outer = cs.Choice.PushContext(ChoiceContextKind.TurnSetup);
        Hook.BeforeSideTurnStart(cs, CombatSide.Player, cs.Allies);
        Hook.BeforeSideTurnEnd(cs, CombatSide.Player, cs.Allies);
        Hook.AfterSideTurnEnd(cs, CombatSide.Player, cs.Allies);
        Hook.BeforeFlush(cs, player);
        Hook.AfterDeath(cs, cs.Monsters[0], false);
        Hook.AfterDiedToDoom(cs, cs.Monsters);
        Assert.True(rec.Contexts.Count >= 10, "every per-listener probe recorded its context");
        foreach (var (tag, top, depth) in rec.Contexts)
        {
            Assert.Equal(ChoiceContextKind.PerListener, top, tag + ": the listener runs inside its own PerListener context");
            Assert.Equal(2, depth, tag + ": one context per listener on top of the caller's");
        }
        Assert.Equal(1, cs.Choice.ContextDepth, "every per-listener context is popped");
        rec.Contexts.Clear();
        Hook.AfterSideTurnStart(cs, CombatSide.Player, cs.Allies);
        Assert.SequenceEqual(new (string, ChoiceContextKind?, int)[] { ("P", ChoiceContextKind.TurnSetup, 1) }, rec.Contexts, "a dispatcher without HookPlayerChoiceContext keeps the caller's context");
        Assert.SequenceEqual(
            new[] { "P:BeforeSideTurnStart", "ML:BeforeSideTurnStart", "C:BeforeSideTurnEndVeryEarly", "R:BeforeSideTurnEndEarly", "QP:BeforeSideTurnEnd" },
            rec.Log.Take(5),
            "the per-listener dispatchers keep the combat-list order within each pass");
    }

    public static void Test_Hooks_RunListWithCombatVisitsDeckCardsThenTheCombatListOnce()
    {
        var rec = new Recorder();
        var cs = NewCombat(1);
        var player = cs.Player;
        DeckCard(cs, new CardListenerProbe(rec, "D1"));
        DeckCard(cs, null);
        DeckCard(cs, new CardListenerProbe(rec, "D2"));
        Power(player, new PowerProbe(rec, "P"));
        Relic(player, new RelicProbe(rec, "R"));
        Potion(player, new PotionProbe(rec, "Q"), 1);
        Card(cs, player.Hand, new CardListenerProbe(rec, "C"));
        cs.Monsters[0].Listener = new MonsterListenerProbe(rec, "ML");
        cs.Run.Modifiers.Add(new ModifierProbe(rec, "Mod"));
        var buffer = new List<AbstractModel>();
        Hook.IterateRunHookListeners(cs.Run, cs, buffer);
        Assert.SequenceEqual(
            new[] { "D1", "D2", "P", "R", "Q", "C", "ML", "Mod" },
            Tags(buffer),
            "deck cards first; with a combat, relics, potions and modifiers come only through the combat list");
        Hook.IterateRunHookListeners(cs, buffer);
        Assert.SequenceEqual(new[] { "D1", "D2", "P", "R", "Q", "C", "ML", "Mod" }, Tags(buffer), "the P0 overload is the same list");
        Hook.AfterCurrentHpChanged(cs, player, -1m);
        Assert.SequenceEqual(new[] { "D1:AfterCurrentHpChanged", "D2:AfterCurrentHpChanged", "P:AfterCurrentHpChanged", "R:AfterCurrentHpChanged", "C:AfterCurrentHpChanged" }, rec.Log);
    }

    public static void Test_Hooks_RunListWithoutCombatVisitsDeckRelicsPotionsModifiers()
    {
        var rec = new Recorder();
        var cs = NewCombat(1);
        var player = cs.Player;
        var rs = cs.Run;
        DeckCard(cs, new CardListenerProbe(rec, "D"));
        Power(player, new PowerProbe(rec, "P"));
        Relic(player, new RelicProbe(rec, "R1"));
        var melted = Relic(player, new RelicProbe(rec, "R2"));
        melted.IsWax = true;
        player.MeltRelicInternal(melted);
        Potion(player, new PotionProbe(rec, "Q"), 2);
        Card(cs, player.Hand, new CardListenerProbe(rec, "C"));
        cs.Monsters[0].Listener = new MonsterListenerProbe(rec, "ML");
        rs.Modifiers.Add(new ModifierProbe(rec, "Mod"));
        var buffer = new List<AbstractModel>();
        Hook.IterateRunHookListeners(rs, null, buffer);
        Assert.SequenceEqual(new[] { "D", "R1", "Q", "Mod" }, Tags(buffer), "without a combat: deck cards, relics without melted ones, potions, modifiers");

        Hook.AfterGoldGained(rs, player);
        Assert.SequenceEqual(new[] { "D:AfterGoldGained", "R1:AfterGoldGained", "Mod:AfterGoldGained" }, rec.Log, "run-only dispatchers iterate IterateHookListeners(null) even while a combat is attached");
        rec.Log.Clear();
        Hook.AfterCurrentHpChanged(rs, null, player, 3m);
        Assert.SequenceEqual(new[] { "D:AfterCurrentHpChanged", "R1:AfterCurrentHpChanged" }, rec.Log, "the cs == null path of a both-scope dispatcher visits the run listeners only");
        rec.Log.Clear();
        player.DeactivateHooks();
        Hook.IterateRunHookListeners(rs, null, buffer);
        Assert.SequenceEqual(new[] { "Mod" }, Tags(buffer), "an inactive player contributes nothing to the run list; the run modifiers stay");
        Hook.AfterGoldGained(rs, player);
        Assert.SequenceEqual(new[] { "Mod:AfterGoldGained" }, rec.Log, "modifiers do not belong to a player");
    }

    public static void Test_Hooks_RunSourceBuildsTheCombatListAfterTheDeckListeners()
    {
        var rec = new Recorder();
        var cs = NewCombat(1);
        var player = cs.Player;
        var deckListener = new CardListenerProbe(rec, "D");
        deckListener.OnHit = (s, self, hook) =>
        {
            if (hook == nameof(AbstractModel.AfterCurrentHpChanged))
            {
                Power(player, new PowerProbe(rec, "P2"));
            }
        };
        DeckCard(cs, deckListener);
        Power(player, new PowerProbe(rec, "P1"));
        Hook.AfterCurrentHpChanged(cs, player, -2m);
        Assert.SequenceEqual(
            new[] { "D:AfterCurrentHpChanged", "P1:AfterCurrentHpChanged", "P2:AfterCurrentHpChanged" },
            rec.Log,
            "the combat list of a run iteration is built when the deck part is exhausted, after the deck listeners ran, so a power a deck listener adds is visited in the same pass");
    }

    public static void Test_Hooks_PreventerAndModifierFiltersUseLiveMembership()
    {
        var rec = new Recorder();
        var cs = NewCombat(1);
        var player = cs.Player;
        var power = Power(player, new PowerProbe(rec, "P") { PreventsDeath = true });
        var relic = Relic(player, new RelicProbe(rec, "R"));
        Assert.False(Hook.ShouldDie(cs, player, out var preventer));
        Assert.True(ReferenceEquals(power, preventer), "the first listener that says no is the preventer");
        Hook.AfterPreventingDeath(cs, preventer!, player);
        Assert.SequenceEqual(new[] { "P:ShouldDie", "P:AfterPreventingDeath" }, rec.Log);
        rec.Log.Clear();
        Hook.AfterPreventingDeath(cs, relic, player);
        Assert.SequenceEqual(new[] { "R:AfterPreventingDeath" }, rec.Log, "AfterPreventingDeath calls the preventer when it is in the live run+combat list");
        rec.Log.Clear();
        player.RemoveRelicInternal(relic);
        Hook.AfterPreventingDeath(cs, relic, player);
        Assert.Equal(0, rec.Log.Count, "a removed relic is not in the live list, so it is not called");
        var stranger = new PowerProbe(rec, "S");
        Hook.AfterModifyingDamageAmount(cs, null, new AbstractModel[] { stranger, power });
        Assert.SequenceEqual(new[] { "P:AfterModifyingDamageAmount" }, rec.Log, "only modifiers that are live listeners are called");
    }

    public static void Test_Hooks_RefModifierListsStartFresh()
    {
        var rec = new Recorder();
        var cs = NewCombat(1);
        Power(cs.Player, new PowerProbe(rec, "P") { DamageBonus = 2m });
        var stale = new PowerProbe(rec, "Stale");
        var earlier = new List<AbstractModel> { stale };
        List<AbstractModel>? modifiers = earlier;
        Hook.ModifyDamage(cs, cs.Monsters[0], cs.Player, 6m, ValueProp.Move, null, null, ModifyDamageHookType.All, ref modifiers);
        Assert.Equal(1, modifiers!.Count, "the game's out lists are always new lists");
        Assert.SequenceEqual(new AbstractModel[] { stale }, earlier, "the caller's previous list is untouched");
        modifiers = earlier;
        Hook.ModifyHandDraw(cs, cs.Player, 5m, ref modifiers);
        Assert.Null(modifiers, "no listener changed the hand draw: no list is allocated");
    }

    public static void Test_Hooks_GatePatternsFollowTheGame()
    {
        var rec = new Recorder();
        var cs = NewCombat(1);
        var rs = cs.Run;
        var player = cs.Player;
        Relic(player, new RelicProbe(rec, "R") { ForcesPotionReward = true });
        Potion(player, new PotionProbe(rec, "Q") { ForcesPotionReward = true }, 0);
        Assert.True(Hook.ShouldForcePotionReward(rs, player, RoomType.Monster));
        Assert.SequenceEqual(new[] { "R:ShouldForcePotionReward" }, rec.Log, "flag = flag || ... short-circuits once true, the iteration continues without calling");
        rec.Log.Clear();
        rs.Modifiers.Add(new ModifierProbe(rec, "M1"));
        rs.Modifiers.Add(new ModifierProbe(rec, "M2") { AllowsFreeTravel = true });
        rs.Modifiers.Add(new ModifierProbe(rec, "M3"));
        Assert.True(Hook.ShouldAllowFreeTravel(rs));
        Assert.SequenceEqual(new[] { "M1:ShouldAllowFreeTravel", "M2:ShouldAllowFreeTravel" }, rec.Log);
        rec.Log.Clear();
        Power(player, new PowerProbe(rec, "P1"));
        Power(player, new PowerProbe(rec, "P2") { TakesExtraTurn = true });
        Power(player, new PowerProbe(rec, "P3"));
        Assert.True(Hook.ShouldTakeExtraTurn(cs, player));
        Assert.SequenceEqual(new[] { "P1:ShouldTakeExtraTurn", "P2:ShouldTakeExtraTurn" }, rec.Log, "any-true gates return at the first true");
        rec.Log.Clear();
        Power(cs.Monsters[0], new PowerProbe(rec, "M") { AllowsHitting = false });
        Assert.False(Hook.ShouldAllowHitting(cs, cs.Monsters[0]));
        Assert.SequenceEqual(new[] { "P1:ShouldAllowHitting", "P2:ShouldAllowHitting", "P3:ShouldAllowHitting", "M:ShouldAllowHitting" }, rec.Log, "all-true gates return at the first false");
    }

    public static void Test_Hooks_RunOnlyValueChainsAndModifierLists()
    {
        var rec = new Recorder();
        var cs = NewCombat(1);
        var rs = cs.Run;
        var player = cs.Player;
        Relic(player, new RelicProbe(rec, "R") { PriceDelta = -10m });
        var modifier = new ModifierProbe(rec, "M") { PriceFactor = 0.5m };
        rs.Modifiers.Add(modifier);
        Assert.Equal(45m, Hook.ModifyMerchantPrice(rs, player, new HookMerchantEntry(), 100m), "(100 - 10) x 0.5 in run-list order");
        Assert.SequenceEqual(new[] { "R:ModifyMerchantPrice(100)", "M:ModifyMerchantPrice(90)" }, rec.Log);
        var types = new HashSet<RoomType> { RoomType.Monster };
        var result = Hook.ModifyUnknownMapPointRoomTypes(rs, types);
        Assert.False(ReferenceEquals(types, result), "the dispatcher starts from a copy of the set");
        Assert.True(result.SetEquals(types));
        Assert.True(Hook.ShouldAllowMerchantCardRemoval(rs, player));
        Assert.False(Hook.ShouldAllowSelectingMoreCardRewards(rs, player, new CardReward(player, new CardCreationOptions(Array.Empty<CardPool>(), CardRarityOddsType.RegularEncounter), 3)));
        Assert.True(Hook.ShouldDisableRemainingRestSiteOptions(rs, player), "no listener: the all-true default");
        Assert.Equal(0, Hook.ModifyRewards(rs, player, new List<Reward>(), new HookRoom()).Count(), "the modifier list of ModifyRewards is empty without modifiers");
        var map = new HookMap();
        Assert.True(ReferenceEquals(map, Hook.ModifyGeneratedMap(rs, map, 0)));
        var evt = new HookEvent();
        Assert.True(ReferenceEquals(evt, Hook.ModifyNextEvent(rs, evt)));
        var card = DeckCard(cs, null);
        Assert.True(ReferenceEquals(card, Hook.ModifyCardBeingAddedToDeck(rs, card)));
        Assert.True(Hook.ShouldAddToDeck(rs, card, out var preventer));
        Assert.Null(preventer);
        Assert.Equal(1.25m, Hook.ModifyCardRewardUpgradeOdds(rs, player, card, 1.25m));
        Assert.False(Hook.TryModifyCardRewardOptions(rs, player, new List<CardCreationResult>(), new CardCreationOptions(Array.Empty<CardPool>(), CardRarityOddsType.RegularEncounter)));
    }

    public static void Test_Hooks_DetachedCombatIsRejectedByBothScopeDispatchers()
    {
        var cs = NewCombat(1);
        var other = NewCombat(1);
        Assert.Throws<InvalidOperationException>(() => Hook.AfterCurrentHpChanged(cs.Run, other, cs.Player, 1m));
        Assert.Throws<InvalidOperationException>(() => Hook.ModifyDamage(cs.Run, other, null, null, 1m, ValueProp.Move, null, null));
        Assert.Equal(1m, Hook.ModifyDamage(cs.Run, null, null, null, 1m, ValueProp.Move, null, null), "cs == null dispatches the run listeners without a combat");
    }

    public static void Test_Hooks_OverflowBeyondTheInlineBufferKeepsOrder()
    {
        var rec = new Recorder();
        var cs = NewCombat(1);
        int count = ListenerSnapshot.InlineCapacity * 5 + 3;
        var expected = new List<string>();
        for (int i = 0; i < count; i++)
        {
            Power(cs.Player, new PowerProbe(rec, "P" + i));
            expected.Add("P" + i + ":AfterCardPlayed");
        }
        Hook.AfterCardPlayed(cs, PlayOf(cs));
        Assert.SequenceEqual(expected, rec.Log.Take(count));
        Assert.Equal(count * 2, rec.Log.Count);
    }

    public static void Test_Hooks_NestedDispatchesKeepTheirOwnSnapshots()
    {
        var rec = new Recorder();
        var cs = NewCombat(1);
        var player = cs.Player;
        var outer = Power(player, new PowerProbe(rec, "O"));
        Power(player, new PowerProbe(rec, "I"));
        int depth = 0;
        outer.OnHit = (s, self, hook) =>
        {
            if (hook == nameof(AbstractModel.AfterCardPlayed) && depth < 2)
            {
                depth++;
                Hook.AfterCurrentHpChanged(s, player, -1m);
                Power(player, new QuietPowerProbe(rec, "N" + depth));
                Hook.AfterCardPlayed(s, PlayOf(s));
            }
        };
        Hook.AfterCardPlayed(cs, PlayOf(cs));
        Assert.SequenceEqual(
            new[]
            {
                "O:AfterCardPlayed", "O:AfterCurrentHpChanged", "I:AfterCurrentHpChanged",
                "O:AfterCardPlayed", "O:AfterCurrentHpChanged", "I:AfterCurrentHpChanged",
                "O:AfterCardPlayed", "I:AfterCardPlayed",
                "O:AfterCardPlayedLate", "I:AfterCardPlayedLate", "N1:AfterCardPlayedLate", "N2:AfterCardPlayedLate",
                "I:AfterCardPlayed",
                "O:AfterCardPlayedLate", "I:AfterCardPlayedLate", "N1:AfterCardPlayedLate", "N2:AfterCardPlayedLate",
                "I:AfterCardPlayed",
                "O:AfterCardPlayedLate", "I:AfterCardPlayedLate", "N1:AfterCardPlayedLate", "N2:AfterCardPlayedLate"
            },
            rec.Log);
    }

    public static void Test_Hooks_OwnerlessCardListenersFollowTheirPileHolder()
    {
        var rec = new Recorder();
        var cs = NewCombat(1);
        var card = new CardInstance(new HookCardDef(), cs.AllocateCardInstanceId());
        card.Listener = new CardListenerProbe(rec, "C");
        cs.Player.Hand.AddInternal(card);
        Assert.False(card.HasOwner);
        Hook.AfterCardPlayed(cs, PlayOf(cs));
        Assert.SequenceEqual(new[] { "C:AfterCardPlayed" }, rec.Log, "a P0 combat card without an owner is checked against the player whose pile holds it");
    }

    public static void Test_Hooks_MaskBitsMatchTheOverriddenVirtuals()
    {
        var virtuals = typeof(AbstractModel).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Where(m => m.IsVirtual).Select(m => m.Name).ToList();
        Assert.Equal(182, virtuals.Count, "AbstractModel declares the 182 hook virtuals");
        Assert.Equal(182, HookMask.HookCount);
        Assert.SequenceEqual(Enum.GetNames<HookId>().OrderBy(n => n, StringComparer.Ordinal), virtuals.OrderBy(n => n, StringComparer.Ordinal), "one HookId per virtual");
        Assert.Equal(0, HookMask.For(typeof(EmptyModel)).Count);
        var derived = HookMask.For(typeof(DerivedOverride));
        Assert.True(derived.Overrides(HookId.AfterCardPlayed), "an override in a base class counts for the derived type");
        Assert.True(derived.Overrides(HookId.ShouldDie), "a sealed override counts");
        Assert.Equal(2, derived.Count);
        Assert.Equal(0, HookMask.For(typeof(HidingModel)).Count, "a new method that hides the virtual is not an override");
        Assert.Equal(HookMask.HookCount, HookMask.All.Count);
        Assert.Equal(0, HookMask.None.Count);
        foreach (var type in ConcreteModelTypes())
        {
            var mask = HookMask.For(type);
            var overridden = IndependentOverrideNames(type);
            foreach (var name in virtuals)
            {
                var hook = Enum.Parse<HookId>(name);
                Assert.Equal(overridden.Contains(name), mask.Overrides(hook), $"{type.Name}.{name}");
            }
        }
    }

    private static IEnumerable<Type> ConcreteModelTypes()
        => typeof(AbstractModel).Assembly.GetTypes().Concat(typeof(HookTests).Assembly.GetTypes()).Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(AbstractModel)));

    private static HashSet<string> IndependentOverrideNames(Type type)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var t = type; t != null && t != typeof(AbstractModel); t = t.BaseType)
        {
            foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (m.IsVirtual && (m.Attributes & MethodAttributes.NewSlot) == 0)
                {
                    names.Add(m.Name);
                }
            }
        }
        return names;
    }

    public static void Test_Hooks_MaskIsCachedPerInstanceAndSharedByClones()
    {
        var cs = NewCombat(1);
        var strength = PowerCmd.Apply<StrengthPower>(cs, cs.Player, 2m, cs.Player, null)!;
        var mask = strength.OverriddenHooks;
        Assert.True(mask.Overrides(HookId.ModifyDamageAdditive));
        Assert.False(mask.Overrides(HookId.AfterCardPlayed));
        Assert.True(ReferenceEquals(mask, HookMask.For(typeof(StrengthPower))), "Core types come from the lazily built table");
        var clone = cs.DeepClone();
        Assert.True(ReferenceEquals(mask, clone.Player.Powers[0].OverriddenHooks), "MemberwiseClone copies the cached reference");
        foreach (var field in typeof(HookMask).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
        {
            Assert.True(field.IsInitOnly && field.FieldType.IsPrimitive, $"HookMask.{field.Name} is a readonly primitive, so sharing the mask between clones is safe");
        }
        Assert.Equal(StateHasher.Hash(cs), StateHasher.Hash(clone), "the mask is not part of the canonical form");
    }

    public static void Test_Hooks_PipelineKeepsNoStaticMutableState()
    {
        foreach (var type in new[] { typeof(Hook), typeof(HookMask), typeof(AbstractModel) }.Concat(typeof(Hook).Assembly.GetTypes().Where(t => t.Namespace == "Sts2Sim.Core.Combat.Pipeline" && t.Name.StartsWith("Listener", StringComparison.Ordinal))))
        {
            foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (field.IsLiteral)
                {
                    continue;
                }
                Assert.True(field.IsInitOnly, $"{type.Name}.{field.Name} is static and writable");
                var fieldType = field.FieldType;
                bool allowed = fieldType == typeof(HookMask) || (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(Lazy<>));
                Assert.True(allowed, $"{type.Name}.{field.Name} ({fieldType.Name}) is a static field other than an immutable mask or a lazily built immutable table");
            }
        }
    }

    public static void Test_Hooks_OrbsFollowPotionsInQueueOrder()
    {
        W1Pending.Run(nameof(Test_Hooks_OrbsFollowPotionsInQueueOrder), "character-mechanics", () =>
        {
            var rec = new Recorder();
            var cs = NewCombat(1);
            var player = cs.Player;
            Potion(player, new PotionProbe(rec, "Q"), 0);
            Card(cs, player.Hand, new CardListenerProbe(rec, "C"));
            player.OrbQueue.AddCapacity(3);
            foreach (var tag in new[] { "O1", "O2", "O3" })
            {
                var orb = new OrbProbe(rec, tag);
                orb.AttachTo(player);
                Assert.True(player.OrbQueue.TryEnqueue(orb));
            }
            Assert.SequenceEqual(new[] { "Q", "O1", "O2", "O3", "C" }, FullCombatList(cs), "orbs in queue order after the potion slots, before the piles");
            var dead = (OrbModel)player.OrbQueue.Orbs[1];
            dead.RemoveInternal();
            Assert.SequenceEqual(new[] { "Q", "O1", "O3", "C" }, FullCombatList(cs), "an orb with HasBeenRemovedFromState is not live");
            Assert.Equal(3m, Hook.ModifyOrbValue(cs, dead, 1m), "ModifyOrbValue chains through the live orb listeners");
        });
    }

    public static void Test_Hooks_CardThenAfflictionThenEnchantment()
    {
        W1Pending.Run(nameof(Test_Hooks_CardThenAfflictionThenEnchantment), "cards-kernel", () =>
        {
            var rec = new Recorder();
            var cs = NewCombat(1);
            var player = cs.Player;
            var first = Card(cs, player.Hand, new CardListenerProbe(rec, "C1"));
            var second = Card(cs, player.DrawPile, new CardListenerProbe(rec, "C2"));
            var affliction = new AfflictionProbe(rec, "A1");
            BindAffliction(affliction, first);
            var enchantment = new EnchantmentProbe(rec, "E1");
            enchantment.ApplyInternal(first, 1m);
            first.Enchantment = enchantment;
            var lone = new EnchantmentProbe(rec, "E2");
            lone.ApplyInternal(second, 1m);
            second.Enchantment = lone;
            Assert.SequenceEqual(new[] { "C1", "A1", "E1", "C2", "E2" }, FullCombatList(cs), "each card, then its affliction, then its enchantment");
            first.HasBeenRemovedFromState = true;
            Assert.SequenceEqual(new[] { "C2", "E2" }, FullCombatList(cs), "affliction and enchantment liveness follow their card");
            first.HasBeenRemovedFromState = false;
            Hook.AfterCardPlayed(cs, PlayOf(cs));
            Assert.SequenceEqual(new[] { "C1:AfterCardPlayed", "A1:AfterCardPlayed", "E1:AfterCardPlayed", "C2:AfterCardPlayed", "E2:AfterCardPlayed" }, rec.Log);
            DeckCard(cs, new CardListenerProbe(rec, "D"));
            var deckEnchantment = new EnchantmentProbe(rec, "DE");
            deckEnchantment.ApplyInternal(player.Deck[0], 1m);
            player.Deck[0].Enchantment = deckEnchantment;
            var buffer = new List<AbstractModel>();
            Hook.IterateRunHookListeners(cs.Run, null, buffer);
            Assert.SequenceEqual(new[] { "D", "DE" }, Tags(buffer), "deck cards are followed by their enchantment and never by an affliction");
        });
    }

    public static void Test_Hooks_AfflictionListenerFollowsItsCard()
    {
        var rec = new Recorder();
        var cs = NewCombat(1);
        var player = cs.Player;
        var first = Card(cs, player.Hand, new CardListenerProbe(rec, "C1"));
        Card(cs, player.Hand, new CardListenerProbe(rec, "C2"));
        var affliction = new AfflictionProbe(rec, "A") { BlockBonus = 2m };
        BindAffliction(affliction, first);
        Assert.SequenceEqual(new[] { "C1", "A", "C2" }, FullCombatList(cs), "a card's affliction comes right after the card");
        Assert.Equal(7m, Hook.ModifyBlock(cs, player, 5m, ValueProp.Move, null, null));
        first.HasBeenRemovedFromState = true;
        Assert.SequenceEqual(new[] { "C2" }, FullCombatList(cs), "HasCard && !Card.HasBeenRemovedFromState && Card.Owner.IsActiveForHooks");
        var unbound = new AfflictionProbe(rec, "U");
        player.Hand[1].Affliction = unbound;
        unbound.ClearInternal();
        Assert.SequenceEqual(new[] { "C2" }, FullCombatList(cs), "an affliction without a card is not live");
    }

    public static void Test_Hooks_PetsComeAfterThePlayerAndBeforeTheEnemies()
    {
        W1Pending.Run(nameof(Test_Hooks_PetsComeAfterThePlayerAndBeforeTheEnemies), "state", () =>
        {
            var rec = new Recorder();
            var cs = NewCombat(1);
            var player = cs.Player;
            Power(player, new PowerProbe(rec, "P"));
            Relic(player, new RelicProbe(rec, "R"));
            var pet = new MonsterState(cs.AllocateCreatureId(), new HookMonsterDef(), 10, 10, null, null) { Side = CombatSide.Player, PetOwnerId = player.CombatId };
            Power(pet, new PowerProbe(rec, "PetP"));
            pet.Listener = new MonsterListenerProbe(rec, "PetL");
            cs.AddAlly(pet);
            Power(cs.Monsters[0], new PowerProbe(rec, "MP"));
            cs.Monsters[0].Listener = new MonsterListenerProbe(rec, "ML");
            Assert.SequenceEqual(new[] { "P", "R", "PetP", "PetL", "MP", "ML" }, FullCombatList(cs), "allies (the player, then pets) before enemies; a pet adds its monster listener");
            player.DeactivateHooks();
            Assert.SequenceEqual(new[] { "PetP", "PetL", "MP", "ML" }, FullCombatList(cs), "a pet's powers are not tied to the player's hook activity: Creature.Player is null for pets");
        });
    }

    public static void Test_Hooks_MaskedEqualsFullOnRandomStates()
    {
        for (ulong seed = 1; seed <= 24; seed++)
        {
            CompareMaskedWithFull(seed, includePending: false);
        }
    }

    public static void Test_Hooks_MaskedEqualsFullWithOrbsEnchantmentsAndPets()
    {
        W1Pending.Run(nameof(Test_Hooks_MaskedEqualsFullWithOrbsEnchantmentsAndPets), "character-mechanics, cards-kernel, state", () =>
        {
            for (ulong seed = 101; seed <= 112; seed++)
            {
                CompareMaskedWithFull(seed, includePending: true);
            }
        });
    }

    public static void Test_Hooks_MaskedSequenceEqualsFilteredFullSequence()
    {
        var virtuals = typeof(AbstractModel).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Where(m => m.IsVirtual).Select(m => m.Name).ToList();
        var masked = new List<AbstractModel>();
        var full = new List<AbstractModel>();
        var overrides = new HashSet<(Type, string)>();
        foreach (var type in ConcreteModelTypes())
        {
            foreach (var name in IndependentOverrideNames(type))
            {
                overrides.Add((type, name));
            }
        }
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var world = RandomWorld.Build(seed, new Recorder(), includePending: false);
            var gen = new SplitMix(seed * 7919UL);
            for (int round = 0; round < 6; round++)
            {
                world.DirectWrite(gen);
                var cs = world.Cs;
                foreach (var source in new[] { ListenerSource.Guarded, ListenerSource.Combat, ListenerSource.Run })
                {
                    foreach (bool withCombat in new[] { true, false })
                    {
                        if (!withCombat && source != ListenerSource.Run)
                        {
                            continue;
                        }
                        var combat = withCombat ? cs : null;
                        FillFull(source, cs, combat, full);
                        foreach (var name in virtuals)
                        {
                            var hook = Enum.Parse<HookId>(name);
                            Hook.IterateListenersFor(hook, source, cs.Run, combat, masked);
                            var expected = full.Where(m => overrides.Contains((m.GetType(), name))).ToList();
                            Assert.True(expected.SequenceEqual(masked, ReferenceEqualityComparer.Instance), $"seed {seed} round {round} {source} combat={withCombat} {name}: masked [{string.Join(",", Tags(masked))}] vs full filtered [{string.Join(",", Tags(expected))}]");
                        }
                    }
                }
            }
        }
    }

    private static void FillFull(ListenerSource source, CombatState cs, CombatState? combat, List<AbstractModel> buffer)
    {
        switch (source)
        {
            case ListenerSource.Guarded:
                Hook.IterateCombatHookListeners(cs, buffer);
                break;
            case ListenerSource.Combat:
                Hook.IterateHookListeners(cs, buffer);
                break;
            default:
                Hook.IterateRunHookListeners(cs.Run, combat, buffer);
                break;
        }
    }

    private static void CompareMaskedWithFull(ulong seed, bool includePending)
    {
        var maskedRec = new Recorder();
        var fullRec = new Recorder { FullMode = true };
        var masked = RandomWorld.Build(seed, maskedRec, includePending);
        var full = RandomWorld.Build(seed, fullRec, includePending);
        full.ForceFullMasks();
        Assert.Equal(StateHasher.Hash(masked.Cs), StateHasher.Hash(full.Cs), $"seed {seed}: identical builds");
        var gen = new SplitMix(seed * 104729UL);
        int steps = 40;
        for (int step = 0; step < steps; step++)
        {
            ulong choice = gen.Next();
            ulong writeSeed = gen.Next();
            string a = masked.Dispatch(choice);
            string b = full.Dispatch(choice);
            Assert.Equal(a, b, $"seed {seed} step {step}: result");
            if ((writeSeed & 3UL) == 0UL)
            {
                masked.DirectWrite(new SplitMix(writeSeed));
                full.DirectWrite(new SplitMix(writeSeed));
                full.ForceFullMasks();
            }
            Assert.SequenceEqual(maskedRec.Log, fullRec.Log, $"seed {seed} step {step} ({a}): listener calls");
            Assert.Equal(StateHasher.Hash(masked.Cs), StateHasher.Hash(full.Cs), $"seed {seed} step {step} ({a}): state hash");
        }
        Assert.True(maskedRec.Log.Count > 0, $"seed {seed}: the script reached listeners");
    }

    private sealed class SplitMix
    {
        private ulong _state;

        public SplitMix(ulong seed)
        {
            _state = seed;
        }

        public ulong Next()
        {
            ulong z = _state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public int Next(int max) => (int)(Next() % (ulong)max);
    }

    private sealed class RandomWorld
    {
        private readonly Recorder _rec;
        private readonly bool _includePending;
        private int _tagCounter;

        private RandomWorld(CombatState cs, Recorder rec, bool includePending)
        {
            Cs = cs;
            _rec = rec;
            _includePending = includePending;
        }

        public CombatState Cs { get; }

        public static RandomWorld Build(ulong seed, Recorder rec, bool includePending)
        {
            var gen = new SplitMix(seed);
            var cs = NewCombat(1 + gen.Next(3));
            var world = new RandomWorld(cs, rec, includePending);
            var player = cs.Player;
            for (int i = gen.Next(4); i > 0; i--)
            {
                world.AddPower(player, gen);
            }
            for (int i = gen.Next(4); i > 0; i--)
            {
                world.AddRelic(gen);
            }
            for (int slot = 0; slot < player.PotionSlots.Length; slot++)
            {
                if (gen.Next(2) == 0)
                {
                    world.AddPotion(slot, gen);
                }
            }
            for (int i = 3 + gen.Next(6); i > 0; i--)
            {
                world.AddCard(gen);
            }
            for (int i = gen.Next(3); i > 0; i--)
            {
                var card = new CardInstance(new HookCardDef(), cs.AllocateCardInstanceId());
                card.AttachTo(player);
                card.Listener = gen.Next(2) == 0 ? world.Track(new CardListenerProbe(rec, world.NextTag("D"))) : null;
                player.Deck.Add(card);
            }
            foreach (var monster in cs.Monsters)
            {
                for (int i = gen.Next(3); i > 0; i--)
                {
                    world.AddPower(monster, gen);
                }
                if (gen.Next(3) != 0)
                {
                    monster.Listener = world.Track(new MonsterListenerProbe(rec, world.NextTag("ML")) { DamageBonus = gen.Next(3), StopsCombatFromEnding = gen.Next(4) == 0 });
                }
            }
            for (int i = gen.Next(3); i > 0; i--)
            {
                cs.Run.Modifiers.Add(world.Track(new ModifierProbe(rec, world.NextTag("Mod")) { DamageCap = gen.Next(2) == 0 ? 30m + gen.Next(10) : decimal.MaxValue, AllowsFreeTravel = gen.Next(3) == 0, PriceFactor = 0.5m + gen.Next(3) * 0.25m }));
            }
            if (includePending)
            {
                player.OrbQueue.AddCapacity(3);
                for (int i = 1 + gen.Next(3); i > 0; i--)
                {
                    var orb = world.Track(new OrbProbe(rec, world.NextTag("O")));
                    orb.AttachTo(player);
                    player.OrbQueue.TryEnqueue(orb);
                }
                var host = player.Hand.Count > 0 ? player.Hand[0] : player.DrawPile.Count > 0 ? player.DrawPile[0] : null;
                if (host != null)
                {
                    var enchantment = world.Track(new EnchantmentProbe(rec, world.NextTag("E")) { DamageAdd = 1m, DamageMul = 1.5m, BlockAdd = 1m });
                    enchantment.ApplyInternal(host, 1m);
                    host.Enchantment = enchantment;
                }
                var pet = new MonsterState(cs.AllocateCreatureId(), new HookMonsterDef(), 12, 12, null, null) { Side = CombatSide.Player, PetOwnerId = player.CombatId };
                world.AddPower(pet, gen);
                pet.Listener = world.Track(new MonsterListenerProbe(rec, world.NextTag("PetL")));
                cs.AddAlly(pet);
            }
            return world;
        }

        private string NextTag(string prefix) => prefix + (_tagCounter++);

        private T Track<T>(T model) where T : AbstractModel => _rec.Track(model);

        private void AddPower(CreatureState owner, SplitMix gen)
        {
            switch (gen.Next(2))
            {
                case 0:
                    var power = Track(new PowerProbe(_rec, NextTag("P")) { DamageBonus = gen.Next(3), BlockFactor = gen.Next(2) == 0 ? 1m : 0.75m, PreventsDeath = gen.Next(5) == 0, AllowsHitting = gen.Next(6) != 0, TakesExtraTurn = gen.Next(5) == 0, HandDrawBonus = gen.Next(2) });
                    power.OnHit = OnPowerHit;
                    power.ApplyInternal(owner, 1m);
                    break;
                default:
                    Track(new QuietPowerProbe(_rec, NextTag("QP")) { DamageFactor = gen.Next(2) == 0 ? 1m : 1.5m }).ApplyInternal(owner, 1m);
                    break;
            }
        }

        private void AddRelic(SplitMix gen)
        {
            var relic = Track(new RelicProbe(_rec, NextTag("R")) { DamageFactor = gen.Next(2) == 0 ? 1m : 2m, PreventsDeath = gen.Next(6) == 0, ForcesPotionReward = gen.Next(3) == 0, PriceDelta = gen.Next(3) - 1 });
            relic.OnHit = OnRelicHit;
            Cs.Player.Relics.Add(relic);
            relic.AttachTo(Cs.Player);
        }

        private void AddPotion(int slot, SplitMix gen)
        {
            var potion = Track(new PotionProbe(_rec, NextTag("Q")) { BlockBonus = gen.Next(3), ForcesPotionReward = gen.Next(4) == 0 });
            potion.AttachTo(Cs.Player);
            Cs.Player.PotionSlots[slot] = potion;
        }

        private void AddCard(SplitMix gen)
        {
            var player = Cs.Player;
            var piles = player.AllPiles;
            var pile = piles[gen.Next(piles.Count)];
            var card = new CardInstance(new HookCardDef(), Cs.AllocateCardInstanceId());
            card.AttachTo(player);
            if (gen.Next(3) != 0)
            {
                var listener = Track(new CardListenerProbe(_rec, NextTag("C")) { DamageCap = gen.Next(3) == 0 ? 20m + gen.Next(10) : decimal.MaxValue, AllowsPlay = gen.Next(5) != 0 });
                listener.OnHit = OnCardHit;
                card.Listener = listener;
            }
            if (gen.Next(4) == 0)
            {
                BindAffliction(Track(new AfflictionProbe(_rec, NextTag("A")) { BlockBonus = gen.Next(2) }), card);
            }
            pile.MutableCards.Add(card);
        }

        private void OnPowerHit(CombatState cs, PowerProbe self, string hook)
        {
            if (hook == nameof(AbstractModel.AfterCardPlayed) && self.Tag.Length % 2 == 0 && self.HasOwner && self.Owner.Powers.Count < 6)
            {
                Track(new QuietPowerProbe(_rec, NextTag("QP"))).ApplyInternal(self.Owner, 1m);
            }
            else if (hook == nameof(AbstractModel.BeforeSideTurnStart) && cs.Player.Relics.Count > 0)
            {
                var relic = cs.Player.Relics[^1];
                cs.Player.RemoveRelicInternal(relic);
            }
            else if (hook == nameof(AbstractModel.AfterCurrentHpChanged) && cs.Player.Hand.Count > 0)
            {
                cs.Player.Hand[0].HasBeenRemovedFromState = !cs.Player.Hand[0].HasBeenRemovedFromState;
            }
        }

        private void OnRelicHit(CombatState cs, RelicProbe self, string hook)
        {
            if (hook == nameof(AbstractModel.AfterCardPlayed) && cs.Monsters.Count > 1)
            {
                cs.Monsters.RemoveAt(cs.Monsters.Count - 1);
            }
            else if (hook == nameof(AbstractModel.AfterGoldGained) && cs.Player.PotionSlots[0] != null)
            {
                cs.Player.PotionSlots[0] = null;
            }
        }

        private void OnCardHit(CombatState cs, CardListenerProbe self, string hook)
        {
            if (hook == nameof(AbstractModel.AfterCardDrawnEarly) && cs.Player.Hand.Count > 0)
            {
                var card = cs.Player.Hand[^1];
                cs.Player.Hand.RemoveInternal(card);
                cs.Player.DiscardPile.AddInternal(card);
            }
            else if (hook == nameof(AbstractModel.ShouldPlay))
            {
                cs.Player.Gold++;
            }
        }

        public void ForceFullMasks()
        {
            var cs = Cs;
            var player = cs.Player;
            foreach (var creature in cs.Allies.Concat(cs.Monsters))
            {
                foreach (var power in creature.Powers)
                {
                    power.SetOverriddenHooks(HookMask.All);
                }
                if (creature is MonsterState monster && monster.Listener != null)
                {
                    monster.Listener.SetOverriddenHooks(HookMask.All);
                }
            }
            foreach (var relic in player.Relics)
            {
                relic.SetOverriddenHooks(HookMask.All);
            }
            foreach (var potion in player.PotionSlots)
            {
                potion?.SetOverriddenHooks(HookMask.All);
            }
            foreach (var orb in player.OrbQueue.Orbs)
            {
                orb.SetOverriddenHooks(HookMask.All);
            }
            foreach (var card in player.AllPiles.SelectMany(p => p.Cards).Concat(player.Deck))
            {
                card.Listener?.SetOverriddenHooks(HookMask.All);
                card.Affliction?.SetOverriddenHooks(HookMask.All);
                card.Enchantment?.SetOverriddenHooks(HookMask.All);
            }
            foreach (var modifier in cs.Run.Modifiers)
            {
                modifier.SetOverriddenHooks(HookMask.All);
            }
        }

        public void DirectWrite(SplitMix gen)
        {
            var cs = Cs;
            var player = cs.Player;
            switch (gen.Next(9))
            {
                case 0:
                    AddRelic(gen);
                    break;
                case 1:
                    int slot = gen.Next(player.PotionSlots.Length);
                    if (player.PotionSlots[slot] == null)
                    {
                        AddPotion(slot, gen);
                    }
                    else
                    {
                        player.PotionSlots[slot] = null;
                    }
                    break;
                case 2:
                    AddCard(gen);
                    break;
                case 3:
                    if (player.Powers.Count > 0)
                    {
                        player.Powers.RemoveAt(gen.Next(player.Powers.Count));
                    }
                    break;
                case 4:
                    AddPower(cs.Monsters.Count > 0 && gen.Next(2) == 0 ? cs.Monsters[gen.Next(cs.Monsters.Count)] : player, gen);
                    break;
                case 5:
                    if (player.IsActiveForHooks && gen.Next(3) == 0)
                    {
                        player.DeactivateHooks();
                    }
                    else
                    {
                        player.ActivateHooks();
                    }
                    break;
                case 6:
                    var source = player.AllPiles[gen.Next(5)];
                    if (source.Count > 0)
                    {
                        var card = source[gen.Next(source.Count)];
                        source.MutableCards.Remove(card);
                        player.AllPiles[gen.Next(5)].MutableCards.Insert(0, card);
                    }
                    break;
                case 7:
                    if (player.Relics.Count > 0)
                    {
                        var relic = player.Relics[gen.Next(player.Relics.Count)];
                        relic.IsMelted = !relic.IsMelted;
                    }
                    break;
                default:
                    cs.Run.Modifiers.Add(Track(new ModifierProbe(_rec, NextTag("Mod"))));
                    break;
            }
        }

        public string Dispatch(ulong choice)
        {
            var cs = Cs;
            var rs = cs.Run;
            var player = cs.Player;
            var monster = cs.Monsters.Count > 0 ? cs.Monsters[0] : null;
            var target = (CreatureState?)monster ?? player;
            switch ((int)(choice % 22UL))
            {
                case 0:
                    Hook.AfterCardPlayed(cs, new CardPlay(cs, new CardInstance(new HookCardDef(), cs.AllocateCardInstanceId()), null));
                    return "AfterCardPlayed";
                case 1:
                    Hook.BeforeSideTurnStart(cs, CombatSide.Player, cs.Allies);
                    return "BeforeSideTurnStart";
                case 2:
                    Hook.AfterSideTurnEnd(cs, CombatSide.Enemy, cs.Monsters);
                    return "AfterSideTurnEnd";
                case 3:
                    Hook.BeforeSideTurnEnd(cs, CombatSide.Player, cs.Allies);
                    return "BeforeSideTurnEnd";
                case 4:
                    Hook.BeforeFlush(cs, player);
                    return "BeforeFlush";
                case 5:
                    Hook.AfterCurrentHpChanged(cs, target, -2m);
                    return "AfterCurrentHpChanged";
                case 6:
                    Hook.AfterDeath(cs, target, false);
                    return "AfterDeath";
                case 7:
                {
                    bool dies = Hook.ShouldDie(cs, target, out var preventer);
                    if (!dies)
                    {
                        Hook.AfterPreventingDeath(cs, preventer!, target);
                    }
                    return "ShouldDie=" + dies + " preventer=" + (preventer is IProbe p ? p.Tag : "-");
                }
                case 8:
                {
                    List<AbstractModel>? modifiers = null;
                    var card = player.Hand.Count > 0 ? player.Hand[0] : null;
                    decimal damage = Hook.ModifyDamage(cs, target, player, 7m, ValueProp.Move, card, null, ModifyDamageHookType.All, ref modifiers);
                    Hook.AfterModifyingDamageAmount(cs, card, modifiers);
                    return "ModifyDamage=" + damage + " modifiers=" + string.Join(",", Tags(modifiers ?? new List<AbstractModel>()));
                }
                case 9:
                {
                    List<AbstractModel>? modifiers = null;
                    var card = player.Hand.Count > 0 ? player.Hand[0] : null;
                    decimal block = Hook.ModifyBlock(cs, player, 5m, ValueProp.Move, card, null, ref modifiers);
                    return "ModifyBlock=" + block + " modifiers=" + string.Join(",", Tags(modifiers ?? new List<AbstractModel>()));
                }
                case 10:
                    return "ShouldAllowHitting=" + Hook.ShouldAllowHitting(cs, target);
                case 11:
                    return "ShouldTakeExtraTurn=" + Hook.ShouldTakeExtraTurn(cs, player);
                case 12:
                    return "ShouldStopCombatFromEnding=" + Hook.ShouldStopCombatFromEnding(cs);
                case 13:
                {
                    var card = player.Hand.Count > 0 ? player.Hand[0] : new CardInstance(new HookCardDef(), cs.AllocateCardInstanceId());
                    bool play = Hook.ShouldPlay(cs, card, out var preventer, AutoPlayType.None);
                    return "ShouldPlay=" + play + " preventer=" + (preventer is IProbe p ? p.Tag : "-");
                }
                case 14:
                {
                    var keywords = new HashSet<CardKeyword>();
                    var card = new CardInstance(new HookCardDef(), cs.AllocateCardInstanceId());
                    Hook.ModifyKeywordsInCombat(cs, card, keywords);
                    return "Keywords=" + string.Join(",", keywords);
                }
                case 15:
                {
                    var card = player.Hand.Count > 0 ? player.Hand[^1] : new CardInstance(new HookCardDef(), cs.AllocateCardInstanceId());
                    Hook.AfterCardDrawn(cs, card, true);
                    return "AfterCardDrawn";
                }
                case 16:
                {
                    var card = player.Hand.Count > 0 ? player.Hand[0] : new CardInstance(new HookCardDef(), cs.AllocateCardInstanceId());
                    Hook.AfterCardChangedPiles(rs, cs, card, PileType.Draw, null);
                    return "AfterCardChangedPiles";
                }
                case 17:
                    Hook.AfterGoldGained(rs, player);
                    return "AfterGoldGained";
                case 18:
                    return "ModifyMerchantPrice=" + Hook.ModifyMerchantPrice(rs, player, new HookMerchantEntry(), 80m) + " force=" + Hook.ShouldForcePotionReward(rs, player, RoomType.Elite) + " travel=" + Hook.ShouldAllowFreeTravel(rs);
                case 19:
                {
                    List<AbstractModel>? modifiers = null;
                    decimal draw = Hook.ModifyHandDraw(cs, player, 5m, ref modifiers);
                    Hook.AfterModifyingHandDraw(cs, modifiers);
                    return "ModifyHandDraw=" + draw;
                }
                case 20:
                {
                    decimal hp = Hook.ModifyHpLost(cs, target, 9m, ValueProp.Move, player, null, HpLossHookPhase.All);
                    var others = new List<AbstractModel>();
                    Hook.IterateRunHookListeners(rs, null, others);
                    Hook.AfterCurrentHpChanged(rs, null, player, 1m);
                    return "ModifyHpLost=" + hp + " runOnly=" + others.Count;
                }
                default:
                    Hook.AfterDiedToDoom(cs, cs.Monsters);
                    if (player.OrbQueue.Orbs.Count > 0)
                    {
                        return "Orb=" + Hook.ModifyOrbValue(cs, player.OrbQueue.Orbs[0], 2m);
                    }
                    return "AfterDiedToDoom";
            }
        }
    }
}

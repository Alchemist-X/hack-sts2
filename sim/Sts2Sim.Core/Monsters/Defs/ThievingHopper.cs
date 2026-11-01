namespace Sts2Sim.Core.Monsters;

public sealed class ThievingHopperExtraState : MonsterExtraState
{
    public bool IsHovering { get; set; }

    public override MonsterExtraState DeepClone() => new ThievingHopperExtraState
    {
        IsHovering = IsHovering
    };

    public override void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(MonsterCanonicalTags.ThievingHopperExtra);
        w.WriteBool(IsHovering);
    }
}

public sealed class ThievingHopper : MonsterDef
{
    public const string MonsterIdEntry = "THIEVING_HOPPER";
    public const string ThieveryMoveId = "THIEVERY_MOVE";
    public const string NabMoveId = "NAB_MOVE";
    public const string HatTrickMoveId = "HAT_TRICK_MOVE";
    public const string FlutterMoveId = "FLUTTER_MOVE";
    public const string EscapeMoveId = "ESCAPE_MOVE";
    public const string SwipePowerId = "SWIPE_POWER";

    public const int InitialHpBase = 79;
    public const int InitialHpTough = 84;
    public const int TheftDamageBase = 17;
    public const int TheftDamageDeadly = 19;
    public const int HatTrickDamageBase = 21;
    public const int HatTrickDamageDeadly = 23;
    public const int NabDamageBase = 14;
    public const int NabDamageDeadly = 16;
    public const decimal EscapeArtistAmount = 5m;
    public const decimal FlutterAmount = 5m;
    public const decimal SwipeAmount = 1m;

    private static readonly Func<CardInstance, bool>[] StealPriorities = new Func<CardInstance, bool>[]
    {
        c => c.Enchantment is not Imbued && c.Rarity == CardRarity.Uncommon,
        c => c.Enchantment is not Imbued && c.Rarity is CardRarity.Common or CardRarity.Rare or CardRarity.Event,
        c => c.Enchantment is not Imbued && c.Rarity is CardRarity.Basic or CardRarity.Quest,
        c => c.Rarity == CardRarity.Ancient || c.Enchantment is Imbued
    };

    public ThievingHopper()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, InitialHpTough, InitialHpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int TheftDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, TheftDamageDeadly, TheftDamageBase);

    public static int HatTrickDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, HatTrickDamageDeadly, HatTrickDamageBase);

    public static int NabDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, NabDamageDeadly, NabDamageBase);

    public static ThievingHopperExtraState GetExtra(MonsterState monster)
        => monster.Extra as ThievingHopperExtraState ?? throw new InvalidOperationException($"Monster {monster.MonsterId} has no ThievingHopper state");

    public override MonsterExtraState? CreateExtraState() => new ThievingHopperExtraState();

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply<EscapeArtistPower>(cs, self, EscapeArtistAmount, self, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var thievery = new MoveStateDef(ThieveryMoveId, ThieveryMove, new SingleAttackIntent(TheftDamage(cs)), Intents.CardDebuff);
        var nab = new MoveStateDef(NabMoveId, NabMove, new SingleAttackIntent(NabDamage(cs)));
        var hatTrick = new MoveStateDef(HatTrickMoveId, HatTrickMove, new SingleAttackIntent(HatTrickDamage(cs)));
        var flutter = new MoveStateDef(FlutterMoveId, FlutterMove, Intents.Buff);
        var escape = new MoveStateDef(EscapeMoveId, EscapeMove, Intents.Escape);
        thievery.FollowUpState = flutter;
        flutter.FollowUpState = hatTrick;
        hatTrick.FollowUpState = nab;
        nab.FollowUpState = escape;
        escape.FollowUpState = escape;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { thievery, nab, hatTrick, flutter, escape }, thievery);
    }

    private static void ThieveryMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        var cardsToSteal = new List<CardInstance>();
        foreach (var current2 in targets.ToList())
        {
            if (current2.IsDead)
            {
                continue;
            }
            var player = current2 as PlayerState ?? CombatStatus.FindCreature(cs, ((MonsterState)current2).PetOwnerId) as PlayerState;
            ArgumentNullException.ThrowIfNull(player);
            var val7 = player.DrawPile.Cards.Concat(player.DiscardPile.Cards).Where(c => c.FindDeckVersion() != null).ToList();
            IEnumerable<CardInstance> enumerable = val7;
            foreach (var priority in StealPriorities)
            {
                var enumerable2 = val7.Where(priority);
                if (enumerable2.Any())
                {
                    enumerable = enumerable2;
                    break;
                }
            }
            if (!enumerable.Any())
            {
                continue;
            }
            var cardToSteal = cs.Run.Rng.CombatCardGeneration.NextItem(enumerable)!;
            CardPileCmd.RemoveFromCombat(cs, cardToSteal);
            cardsToSteal.Add(cardToSteal);
        }
        foreach (var current in cardsToSteal)
        {
            SwipeSteal(cs, self, current);
        }
        DamageCmd.Attack(cs, TheftDamage(cs)).FromMonster(self).Execute();
    }

    private static void SwipeSteal(CombatState cs, MonsterState self, CardInstance card)
    {
        var swipe = new SwipePower();
        swipe.Steal(cs, card);
        PowerCmd.Apply(cs, swipe, self, SwipeAmount, self, null);
    }

    private static void NabMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, NabDamage(cs)).FromMonster(self).Execute();
    }

    private static void HatTrickMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, HatTrickDamage(cs)).FromMonster(self).Execute();
    }

    private static void FlutterMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        GetExtra(self).IsHovering = true;
        PowerCmd.Apply<FlutterPower>(cs, self, FlutterAmount, self, null);
    }

    private static void EscapeMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        var extra = GetExtra(self);
        if (extra.IsHovering)
        {
            extra.IsHovering = false;
        }
        CreatureCmd.Escape(cs, self);
    }
}

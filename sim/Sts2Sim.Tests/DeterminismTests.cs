using System.Buffers.Binary;
using System.Reflection;
using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class DeterminismTests
{
    private const int MaxSteps = 400;
    private const string PreferredEncounterId = "NIBBITS_WEAK";
    private static readonly ulong[] Seeds = { 42UL, 7UL, 1001UL };

    public static void Test_State_WriterEncodesPrimitivesLittleEndian()
    {
        var w = new CanonicalWriter(16);
        w.WriteByte(0xAB);
        w.WriteBool(true);
        w.WriteBool(false);
        w.WriteInt32(0x01020304);
        w.WriteInt32(-2);
        w.WriteUInt32(0xA1B2C3D4u);
        w.WriteInt64(0x0102030405060708L);
        w.WriteUInt64(0xF1F2F3F4F5F6F7F8UL);
        w.WriteTag(0x5301);
        w.WriteCount(3);
        w.WriteSingle(1f);
        w.WriteDouble(-2.0);
        w.WriteNullableInt32(null);
        w.WriteNullableInt32(5);
        var expected = new byte[]
        {
            0xAB,
            0x01,
            0x00,
            0x04, 0x03, 0x02, 0x01,
            0xFE, 0xFF, 0xFF, 0xFF,
            0xD4, 0xC3, 0xB2, 0xA1,
            0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01,
            0xF8, 0xF7, 0xF6, 0xF5, 0xF4, 0xF3, 0xF2, 0xF1,
            0x01, 0x53,
            0x03, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x80, 0x3F,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC0,
            0x00,
            0x01, 0x05, 0x00, 0x00, 0x00,
        };
        Assert.SequenceEqual(expected, w.ToArray());
        Assert.Equal(expected.Length, w.Length);
    }

    public static void Test_State_WriterEncodesStrings()
    {
        var bytes = BytesOf(w =>
        {
            w.WriteString(null);
            w.WriteString(string.Empty);
            w.WriteString("AB");
            w.WriteString(new string((char)0xE9, 1));
        });
        var expected = new byte[]
        {
            0xFF, 0xFF, 0xFF, 0xFF,
            0x00, 0x00, 0x00, 0x00,
            0x02, 0x00, 0x00, 0x00, 0x41, 0x42,
            0x02, 0x00, 0x00, 0x00, 0xC3, 0xA9,
        };
        Assert.SequenceEqual(expected, bytes);
        Assert.NotEqual(HashOf(w => w.WriteString(null)), HashOf(w => w.WriteString(string.Empty)));
        Assert.NotEqual(
            HashOf(w => { w.WriteString("AB"); w.WriteString("C"); }),
            HashOf(w => { w.WriteString("A"); w.WriteString("BC"); }));
    }

    public static void Test_State_WriterNormalizesDecimalScale()
    {
        var onePointFive = BytesOf(w => w.WriteDecimal(1.5m));
        Assert.SequenceEqual(new byte[] { 1, 0, 15, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, onePointFive);
        Assert.SequenceEqual(onePointFive, BytesOf(w => w.WriteDecimal(1.50m)));
        Assert.SequenceEqual(onePointFive, BytesOf(w => w.WriteDecimal(1.500000m)));
        Assert.SequenceEqual(onePointFive, BytesOf(w => w.WriteDecimal(3m / 2m)));
        Assert.SequenceEqual(onePointFive, BytesOf(w => w.WriteDecimal(new decimal(150, 0, 0, false, 2))));

        var zero = BytesOf(w => w.WriteDecimal(0m));
        Assert.SequenceEqual(new byte[14], zero);
        Assert.SequenceEqual(zero, BytesOf(w => w.WriteDecimal(0.000m)));
        Assert.SequenceEqual(zero, BytesOf(w => w.WriteDecimal(decimal.Negate(0m))));
        Assert.SequenceEqual(zero, BytesOf(w => w.WriteDecimal(new decimal(0, 0, 0, true, 5))));

        Assert.SequenceEqual(BytesOf(w => w.WriteDecimal(10m)), BytesOf(w => w.WriteDecimal(10.0m)));
        Assert.SequenceEqual(new byte[] { 0, 0, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, BytesOf(w => w.WriteDecimal(10.00m)));
        Assert.SequenceEqual(new byte[] { 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, BytesOf(w => w.WriteDecimal(0.1000m)));
        Assert.SequenceEqual(new byte[] { 28, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, BytesOf(w => w.WriteDecimal(new decimal(1, 0, 0, false, 28))));

        var max = new byte[14];
        Array.Fill(max, (byte)0xFF, 2, 12);
        Assert.SequenceEqual(max, BytesOf(w => w.WriteDecimal(decimal.MaxValue)));
        max[1] = 1;
        Assert.SequenceEqual(max, BytesOf(w => w.WriteDecimal(decimal.MinValue)));

        Assert.NotEqual(HashOf(w => w.WriteDecimal(1.5m)), HashOf(w => w.WriteDecimal(-1.5m)));
        Assert.NotEqual(HashOf(w => w.WriteDecimal(1.5m)), HashOf(w => w.WriteDecimal(15m)));
        Assert.NotEqual(HashOf(w => w.WriteDecimal(10m)), HashOf(w => w.WriteDecimal(1m)));
    }

    public static void Test_State_WriterUsesFloatBitPatterns()
    {
        Assert.SequenceEqual(new byte[] { 0x00, 0x00, 0x80, 0x3F }, BytesOf(w => w.WriteSingle(1f)));
        float negativeZero = BitConverter.UInt32BitsToSingle(0x80000000u);
        Assert.SequenceEqual(new byte[] { 0x00, 0x00, 0x00, 0x80 }, BytesOf(w => w.WriteSingle(negativeZero)));
        Assert.NotEqual(HashOf(w => w.WriteSingle(0f)), HashOf(w => w.WriteSingle(negativeZero)));
        Assert.SequenceEqual(new byte[] { 0x9A, 0x99, 0x99, 0x99, 0x99, 0x99, 0xB9, 0x3F }, BytesOf(w => w.WriteDouble(0.1)));
    }

    public static void Test_State_WriterGrowsAndResets()
    {
        var w = new CanonicalWriter(1);
        Assert.True(w.Capacity >= 16, "capacity is clamped to a sane minimum");
        for (int i = 0; i < 5000; i++)
        {
            w.WriteInt32(i);
        }
        Assert.Equal(20000, w.Length);
        var ints = w.ToArray();
        for (int i = 0; i < 5000; i += 97)
        {
            Assert.Equal(i, BinaryPrimitives.ReadInt32LittleEndian(ints.AsSpan(i * 4, 4)), $"int at {i}");
        }
        w.WriteString(new string('x', 70000));
        Assert.Equal(20000 + 4 + 70000, w.Length);
        var snapshot = w.ToArray();
        w.Reset();
        Assert.Equal(0, w.Length);
        Assert.Equal(90004, snapshot.Length);
        Assert.Equal((byte)'x', snapshot[^1]);
        w.WriteByte(7);
        Assert.SequenceEqual(new byte[] { 7 }, w.ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => w.WriteCount(-1));
    }

    public static void Test_State_HasherMatchesXxHash64Vectors()
    {
        Assert.Equal(0xEF46DB3751D8E999UL, StateHasher.Hash(ReadOnlySpan<byte>.Empty));
        Assert.Equal(0xD24EC4F1A98C6E5BUL, StateHasher.Hash("a"u8));
        Assert.Equal(0x44BC2CF5AD770999UL, StateHasher.Hash("abc"u8));
        Assert.Equal(0xFBCEA83C8A378BF1UL, StateHasher.Hash("Nobody inspects the spammish repetition"u8));
        var sequential = new (int Length, ulong Expected)[]
        {
            (8, 0x884A173614B81B8DUL),
            (31, 0xC346D2B59B4D8EE1UL),
            (32, 0xCBF59C5116FF32B4UL),
            (33, 0x0C535D1ACAFB8EADUL),
            (48, 0x8FE437632DA06964UL),
            (63, 0xE26AA9E2A95F8E4FUL),
            (64, 0xF7C67301DB6713F0UL),
            (100, 0x6AC1E58032166597UL),
            (257, 0x80381162756D40E6UL),
        };
        foreach (var (length, expected) in sequential)
        {
            var data = new byte[length];
            for (int i = 0; i < length; i++)
            {
                data[i] = (byte)i;
            }
            Assert.Equal(expected, StateHasher.Hash(data), $"sequential {length} bytes");
        }
    }

    public static void Test_State_HasherAgreesWithRngXxHash64()
    {
        var data = new byte[300];
        ulong x = 0x0123456789ABCDEFUL;
        for (int i = 0; i < data.Length; i++)
        {
            x ^= x << 13;
            x ^= x >> 7;
            x ^= x << 17;
            data[i] = (byte)x;
        }
        for (int length = 0; length <= data.Length; length++)
        {
            ReadOnlySpan<byte> span = data.AsSpan(0, length);
            Assert.Equal(DeterministicHash.XxHash64(span, 0L), StateHasher.Hash(span), $"length {length}");
        }
    }

    public static void Test_State_HashOfCanonicalMatchesHashOfBytes()
    {
        var blob = new BlobCanonical(new byte[] { 1, 2, 3, 4, 5 });
        var w = new CanonicalWriter();
        blob.WriteCanonical(w);
        ulong direct = StateHasher.Hash(w.WrittenSpan);
        Assert.Equal(direct, StateHasher.Hash(blob));
        var scratch = new CanonicalWriter(16);
        scratch.WriteString("stale content that must be discarded before hashing");
        Assert.Equal(direct, StateHasher.Hash(blob, scratch));
        Assert.Equal(direct, StateHasher.Hash(blob, scratch));
        Assert.SequenceEqual(w.ToArray(), scratch.ToArray());
        Assert.SequenceEqual(w.ToArray(), StateHasher.CanonicalBytes(blob));
        Assert.NotEqual(direct, StateHasher.Hash(new BlobCanonical(new byte[] { 1, 2, 3, 4, 6 })));
    }

    public static void Test_State_CardPileFollowsGamePileRules()
    {
        var def = new TestCardDef("STATE_TEST_STRIKE");
        var pile = new CardPile(PileType.Draw);
        var a = new CardInstance(def, 0);
        var b = new CardInstance(def, 1);
        var c = new CardInstance(def, 2);
        var d = new CardInstance(def, 3);
        pile.AddInternal(a);
        pile.AddInternal(b);
        pile.AddInternal(c, 0);
        AssertIds(pile, 2, 0, 1);
        Assert.Throws<InvalidOperationException>(() => pile.AddInternal(a));
        Assert.Throws<InvalidOperationException>(() => pile.RemoveInternal(d));
        Assert.False(pile.TryRemoveInternal(d));
        pile.MoveToBottomInternal(c);
        AssertIds(pile, 0, 1, 2);
        pile.MoveToTopInternal(b);
        AssertIds(pile, 1, 0, 2);
        Assert.True(pile.RemoveInternal(a));
        AssertIds(pile, 1, 2);
        Assert.True(ReferenceEquals(c, pile.RemoveAtInternal(1)));
        AssertIds(pile, 1);
        Assert.Throws<InvalidOperationException>(() => pile.MoveToTopInternal(a));
        Assert.Throws<InvalidOperationException>(() => pile.MoveToBottomInternal(a));
        Assert.Equal(-1, pile.IndexOf(a));
        Assert.False(pile.Contains(a));
        Assert.True(pile.Contains(b));
        Assert.Equal(1, pile.Count);
        pile.ClearInternal();
        Assert.True(pile.IsEmpty);
        Assert.Equal(10, CardPile.MaxCardsInHand);
        Assert.False(CardPile.IsCombatPileType(PileType.None));
        Assert.True(CardPile.IsCombatPileType(PileType.Draw));
        Assert.True(CardPile.IsCombatPileType(PileType.Hand));
        Assert.True(CardPile.IsCombatPileType(PileType.Discard));
        Assert.True(CardPile.IsCombatPileType(PileType.Exhaust));
        Assert.True(CardPile.IsCombatPileType(PileType.Play));
        Assert.False(CardPile.IsCombatPileType(PileType.Deck));
    }

    public static void Test_State_CardPileCloneIsIndependent()
    {
        var def = new TestCardDef("STATE_TEST_STRIKE");
        var pile = new CardPile(PileType.Hand);
        for (int i = 0; i < 4; i++)
        {
            pile.AddInternal(new CardInstance(def, i));
        }
        var clone = pile.DeepClone();
        Assert.Equal(pile.Type, clone.Type);
        Assert.SequenceEqual(StateHasher.CanonicalBytes(pile), StateHasher.CanonicalBytes(clone));
        for (int i = 0; i < pile.Count; i++)
        {
            Assert.False(ReferenceEquals(pile[i], clone[i]), $"card {i} must be copied");
            Assert.True(ReferenceEquals(pile[i].Def, clone[i].Def), $"card {i} def must be shared");
            Assert.Equal(pile[i].InstanceId, clone[i].InstanceId);
        }
        ulong before = StateHasher.Hash(pile);
        clone.MoveToTopInternal(clone[3]);
        clone[0].ExhaustOnNextPlay = true;
        clone[1].Vars.Damage.BaseValue += 3m;
        clone.RemoveAtInternal(2);
        Assert.Equal(before, StateHasher.Hash(pile));
        Assert.NotEqual(before, StateHasher.Hash(clone));
        AssertIds(pile, 0, 1, 2, 3);
        for (int i = 0; i < pile.Count; i++)
        {
            Assert.Equal(6m, pile[i].Vars.Damage.BaseValue, $"original card {i} damage");
            Assert.False(pile[i].ExhaustOnNextPlay, $"original card {i} flag");
        }
        var swapped = pile.DeepClone();
        (swapped.MutableCards[0], swapped.MutableCards[1]) = (swapped.MutableCards[1], swapped.MutableCards[0]);
        Assert.NotEqual(before, StateHasher.Hash(swapped), "pile order is part of the state");
    }

    public static void Test_State_CreatureSettersAndPowerLookups()
    {
        var p = new PlayerState(0, "IRONCLAD", 30, 40, 3);
        Assert.True(p.IsAlive);
        Assert.False(p.IsDead);
        Assert.True(p.IsPlayer);
        Assert.False(p.IsMonster);
        Assert.False(p.IsEnemy);
        Assert.Equal(CombatSide.Player, p.Side);
        p.Block = 5;
        Assert.Equal(5, p.Block);
        Assert.Throws<ArgumentException>(() => p.Block = -1);
        Assert.Throws<ArgumentException>(() => p.CurrentHp = -1);
        Assert.Equal(5, p.Block);
        Assert.Equal(30, p.CurrentHp);
        p.CurrentHp = 0;
        Assert.True(p.IsDead);

        var monster = new MonsterState(1, new TestMonsterDef(), 10, 12, null, "front");
        Assert.True(monster.IsEnemy);
        Assert.True(monster.IsMonster);
        Assert.False(monster.IsPlayer);
        Assert.Equal("STATE_TEST_MONSTER", monster.MonsterId);
        Assert.Equal("front", monster.SlotName);
        Assert.False(monster.HasAi);
        Assert.False(monster.IsStunned);
        Assert.Throws<InvalidOperationException>(() => _ = monster.Ai);

        var buff = new TestBuffPower();
        var debuff = new TestDebuffPower();
        AttachPower(buff, p, 3);
        AttachPower(debuff, p, 1);
        Assert.True(ReferenceEquals(buff, p.GetPower<TestBuffPower>()));
        Assert.True(ReferenceEquals(debuff, p.GetPower("STATE_TEST_DEBUFF")));
        Assert.True(p.HasPower<TestDebuffPower>());
        Assert.True(p.HasPower("STATE_TEST_BUFF"));
        Assert.False(p.HasPower("STATE_TEST_MISSING"));
        Assert.Equal(3, p.GetPowerAmount<TestBuffPower>());
        Assert.Equal(0, monster.GetPowerAmount<TestBuffPower>());
        Assert.Null(monster.GetPower<TestBuffPower>());
    }

    public static void Test_State_PlayerAndCombatStartValues()
    {
        var p = new PlayerState(0, "IRONCLAD", 80, 80, 3);
        Assert.Equal(1, p.TurnNumber);
        Assert.True(p.IsActiveForHooks);
        Assert.Equal(PlayerTurnPhase.None, p.Phase);
        Assert.Equal(0, p.Energy);
        Assert.Equal(0, p.Stars);
        Assert.Equal(3, p.BaseMaxEnergy);
        Assert.False(new PlayerState(0, "IRONCLAD", 0, 80, 3).IsActiveForHooks);
        p.DeactivateHooks();
        Assert.False(p.IsActiveForHooks);
        p.ActivateHooks();
        Assert.True(p.IsActiveForHooks);

        p.GainStars(3m);
        p.LoseStars(1m);
        Assert.Equal(2, p.Stars);
        p.LoseStars(10m);
        Assert.Equal(0, p.Stars);
        p.IncrementTurnNumber();
        Assert.Equal(2, p.TurnNumber);

        Assert.SequenceEqual(
            new[] { PileType.Hand, PileType.Draw, PileType.Discard, PileType.Exhaust, PileType.Play },
            p.AllPiles.Select(pile => pile.Type));
        Assert.True(ReferenceEquals(p.Hand, p.GetPile(PileType.Hand)));
        Assert.Throws<ArgumentOutOfRangeException>(() => p.GetPile(PileType.Deck));
        Assert.Throws<ArgumentOutOfRangeException>(() => p.GetPile(PileType.None));

        var cs = new CombatState(p, "STATE_TEST_ENCOUNTER", 3, new RunRngSet(new Rng(1UL)));
        Assert.Equal(1, cs.RoundNumber);
        Assert.Equal(CombatSide.Player, cs.CurrentSide);
        Assert.Equal(CombatOutcome.InProgress, cs.Outcome);
        Assert.False(cs.IsOver);
        Assert.False(cs.IsInProgress, "a new combat state has no turn state yet: not in progress");
        Assert.False(cs.IsStarting, "a new combat state has no turn state yet: not starting");
        Assert.Equal(1, cs.NextCreatureId);
        Assert.Equal(1, cs.AllocateCreatureId());
        Assert.Equal(2, cs.NextCreatureId);
        Assert.Equal(0, cs.AllocateCardInstanceId());
        Assert.True(cs.HasAscension(AscensionLevel.Poverty));
        Assert.False(cs.HasAscension(AscensionLevel.TightBelt));
        Assert.Equal(9, cs.GetValueIfAscension(AscensionLevel.WearyTraveler, 9, 4));
        Assert.Equal(4m, cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 9m, 4m));
    }

    public static void Test_State_CreatureQueriesFollowAlliesThenEnemies()
    {
        var cs = BuildManualState();
        Assert.True(ReferenceEquals(cs.Player, cs.GetCreatureById(0)));
        Assert.True(ReferenceEquals(cs.Monsters[0], cs.GetCreatureById(1)));
        Assert.True(ReferenceEquals(cs.Monsters[1], cs.GetCreatureById(2)));
        Assert.Null(cs.GetCreatureById(99));

        var buffer = new List<CreatureState> { cs.Monsters[1] };
        cs.GetCreatures(buffer);
        Assert.Equal(3, buffer.Count);
        Assert.True(ReferenceEquals(cs.Player, buffer[0]));
        Assert.True(ReferenceEquals(cs.Monsters[0], buffer[1]));
        Assert.True(ReferenceEquals(cs.Monsters[1], buffer[2]));

        Assert.Throws<InvalidOperationException>(() => cs.AddMonster(cs.Monsters[0]));
        var stray = new MonsterState(50, new TestMonsterDef(), 5, 5, null, null);
        Assert.Throws<InvalidOperationException>(() => cs.RemoveMonster(stray));
        var first = cs.Monsters[0];
        cs.RemoveMonster(first);
        Assert.Equal(1, cs.Monsters.Count);
        Assert.Null(cs.GetCreatureById(1));
        Assert.False(first.HasAi);
        Assert.False(first.IsStunned);
    }

    public static void Test_State_HittableEnemiesSkipDeadMonsters()
    {
        var cs = BuildManualState();
        cs.Monsters[0].CurrentHp = 0;
        var buffer = new List<MonsterState> { cs.Monsters[0] };
        cs.GetHittableEnemies(buffer);
        Assert.Equal(1, buffer.Count);
        Assert.True(ReferenceEquals(cs.Monsters[1], buffer[0]));
    }

    public static void Test_State_ManualStateCloneRoundTrips()
    {
        var original = BuildManualState();
        var originalBytes = StateHasher.CanonicalBytes(original);
        var clone = original.DeepClone();
        Assert.SequenceEqual(originalBytes, StateHasher.CanonicalBytes(clone));
        Assert.Equal(StateHasher.Hash(original), StateHasher.Hash(clone));

        Assert.False(ReferenceEquals(original.Player, clone.Player));
        Assert.False(ReferenceEquals(original.Monsters, clone.Monsters));
        Assert.False(ReferenceEquals(original.Rng, clone.Rng));
        Assert.False(ReferenceEquals(original.Rng.Shuffle, clone.Rng.Shuffle));
        Assert.True(ReferenceEquals(clone.Rng.Shuffle, clone.Rng.MonsterAi), "aliasing inside the mock RunRngSet must survive the clone");
        for (int p = 0; p < original.Player.AllPiles.Count; p++)
        {
            var source = original.Player.AllPiles[p];
            var copy = clone.Player.AllPiles[p];
            Assert.False(ReferenceEquals(source, copy), $"pile {source.Type} must be copied");
            Assert.Equal(source.Count, copy.Count);
            for (int i = 0; i < source.Count; i++)
            {
                Assert.False(ReferenceEquals(source[i], copy[i]));
                Assert.True(ReferenceEquals(source[i].Def, copy[i].Def));
            }
        }
        for (int m = 0; m < original.Monsters.Count; m++)
        {
            Assert.False(ReferenceEquals(original.Monsters[m], clone.Monsters[m]));
            Assert.True(ReferenceEquals(original.Monsters[m].Def, clone.Monsters[m].Def));
            Assert.False(ReferenceEquals(original.Monsters[m].Extra, clone.Monsters[m].Extra));
        }
        AssertPowerOwners(original, "original");
        AssertPowerOwners(clone, "clone");

        clone.Player.CurrentHp = 1;
        clone.Player.Hand.MutableCards.Reverse();
        clone.Player.Powers[0].SetAmount(9);
        clone.Player.DrawPile[0].ExhaustOnNextPlay = true;
        clone.Monsters[0].Block = 30;
        ((TestExtraState)clone.Monsters[0].Extra!).History.Add(5);
        clone.Monsters[1].Powers[0].SetAmount(1);
        clone.Rng.Shuffle.NextInt(100);
        clone.Monsters.RemoveAt(1);
        clone.RoundNumber = 9;
        Assert.SequenceEqual(originalBytes, StateHasher.CanonicalBytes(original));

        var second = clone.DeepClone();
        Assert.SequenceEqual(StateHasher.CanonicalBytes(clone), StateHasher.CanonicalBytes(second));
        AssertPowerOwners(second, "clone of clone");
    }

    public static void Test_State_EveryFieldChangesTheHash()
    {
        var baseState = BuildManualState();
        ulong baseHash = StateHasher.Hash(baseState);
        var mutations = new (string Name, Action<CombatState> Mutate)[]
        {
            ("player hp", s => s.Player.CurrentHp -= 1),
            ("player max hp", s => s.Player.MaxHp += 1),
            ("player block", s => s.Player.Block += 1),
            ("player side", s => s.Player.Side = CombatSide.Enemy),
            ("player energy", s => s.Player.Energy += 1),
            ("player base energy", s => s.Player.BaseMaxEnergy += 1),
            ("player stars", s => s.Player.Stars += 1),
            ("player turn number", s => s.Player.IncrementTurnNumber()),
            ("player phase", s => s.Player.Phase = PlayerTurnPhase.End),
            ("player hooks", s => s.Player.DeactivateHooks()),
            ("draw pile order", s => SwapFirstTwo(s.Player.DrawPile)),
            ("hand order", s => SwapFirstTwo(s.Player.Hand)),
            ("hand to discard", s => s.Player.DiscardPile.AddInternal(s.Player.Hand.RemoveAtInternal(0))),
            ("discard to exhaust", s => s.Player.ExhaustPile.AddInternal(s.Player.DiscardPile.RemoveAtInternal(0))),
            ("exhaust to play", s => s.Player.PlayPile.AddInternal(s.Player.ExhaustPile.RemoveAtInternal(0))),
            ("draw to hand", s => s.Player.Hand.AddInternal(s.Player.DrawPile.RemoveAtInternal(0))),
            ("card flag", s => s.Player.Hand[0].ExhaustOnNextPlay = true),
            ("card var", s => s.Player.Hand[0].Vars.Damage.BaseValue += 1m),
            ("card keyword", s => s.Player.Hand[0].AddKeyword(CardKeyword.Retain)),
            ("power amount", s => s.Player.Powers[0].SetAmount(s.Player.Powers[0].Amount + 1)),
            ("power order", s => (s.Player.Powers[0], s.Player.Powers[1]) = (s.Player.Powers[1], s.Player.Powers[0])),
            ("power removed", s => s.Player.Powers.RemoveAt(1)),
            ("power turn-start amount", s => s.Player.Powers[0].AmountOnTurnStart = 7),
            ("monster hp", s => s.Monsters[0].CurrentHp -= 1),
            ("monster max hp", s => s.Monsters[0].MaxHp += 1),
            ("monster block", s => s.Monsters[0].Block += 3),
            ("monster power amount", s => s.Monsters[1].Powers[0].SetAmount(9)),
            ("monster spawned flag", s => s.Monsters[0].SpawnedThisTurn = !s.Monsters[0].SpawnedThisTurn),
            ("monster max hp before modification", s => s.Monsters[0].MaxHpBeforeModification = null),
            ("monster extra state", s => ((TestExtraState)s.Monsters[0].Extra!).Counter++),
            ("monster order", s => (s.Monsters[0], s.Monsters[1]) = (s.Monsters[1], s.Monsters[0])),
            ("monster removed", s => s.Monsters.RemoveAt(1)),
            ("round number", s => s.RoundNumber++),
            ("current side", s => s.CurrentSide = CombatSide.Enemy),
            ("outcome", s => s.Outcome = CombatOutcome.Victory),
            ("in-progress flag", s => s.IsInProgress = !s.IsInProgress),
            ("starting flag", s => s.IsStarting = !s.IsStarting),
            ("next card instance id", s => s.AllocateCardInstanceId()),
            ("next creature id", s => s.AllocateCreatureId()),
            ("rng advanced", s => s.Rng.Shuffle.NextInt(10)),
        };
        var distinct = new HashSet<ulong> { baseHash };
        foreach (var (name, mutate) in mutations)
        {
            var copy = baseState.DeepClone();
            Assert.Equal(baseHash, StateHasher.Hash(copy), $"clone before '{name}'");
            mutate(copy);
            ulong mutated = StateHasher.Hash(copy);
            Assert.NotEqual(baseHash, mutated, $"mutation '{name}' did not change the hash");
            Assert.Equal(baseHash, StateHasher.Hash(baseState), $"mutation '{name}' on the clone changed the original");
            Assert.True(distinct.Add(mutated), $"mutation '{name}' collided with another mutation's hash");
        }
    }

    public static void Test_State_ManualCloneSharesNoMutableReferences()
    {
        var original = BuildManualState();
        AssertNoSharedMutable(original, original.DeepClone(), "manual state");
        var clone = original.DeepClone();
        AssertNoSharedMutable(clone, clone.DeepClone(), "manual clone of clone");
    }

    public static void Test_State_GraphInspectorFlagsSharedMutableObjects()
    {
        var def = new TestCardDef("STATE_TEST_STRIKE");
        var shared = new List<int> { 1, 2 };
        var a = new Holder { Items = shared, Def = def };
        var b = new Holder { Items = shared, Def = def };
        var problems = new GraphInspector().FindSharedMutable(a, b);
        Assert.True(problems.Any(p => p.Contains("List", StringComparison.Ordinal)), "a shared List must be reported: " + string.Join("; ", problems));

        var c = new Holder { Items = new List<int> { 1, 2 }, Def = def };
        var clean = new GraphInspector().FindSharedMutable(a, c);
        Assert.Equal(0, clean.Count, "defs, strings, empty arrays and immutable leaves may be shared: " + string.Join("; ", clean));

        int counter = 0;
        Func<int> capturing = () => ++counter;
        var d = new Holder { Items = new List<int>(), Callback = capturing };
        var e = new Holder { Items = new List<int>(), Callback = capturing };
        Assert.True(new GraphInspector().FindSharedMutable(d, e).Count > 0, "a shared closure over mutable state must be reported");

        var boxedA = new Holder { Items = new List<int>(), Pair = new KeyValuePair<string, List<int>>("k", shared) };
        var boxedB = new Holder { Items = new List<int>(), Pair = new KeyValuePair<string, List<int>>("k", shared) };
        Assert.True(new GraphInspector().FindSharedMutable(boxedA, boxedB).Count > 0, "references inside struct fields must be followed");

        var frozen = new FrozenNode(new FrozenNode(null));
        var f = new Holder { Items = new List<int>(), Frozen = frozen };
        var g = new Holder { Items = new List<int>(), Frozen = frozen };
        Assert.Equal(0, new GraphInspector().FindSharedMutable(f, g).Count, "objects with only readonly immutable fields may be shared");

        Assert.True(new GraphInspector().FindSharedMutable(a, a).Count > 0, "an object compared with itself is entirely shared");
    }

    public static void Test_State_CombatSimCloneSharesNoMutableReferences()
    {
        var sim = CreateSim(Seeds[0]);
        AssertNoSharedMutable(sim, sim.Clone(), "combat start");
        AssertCardsUnique(sim.State, "combat start");

        var policy = new ScriptPolicy(3UL);
        var buffer = new List<GameAction>(16);
        int endTurns = 0;
        for (int step = 0; step < MaxSteps && !sim.IsTerminal && endTurns < 2; step++)
        {
            sim.LegalActions(buffer);
            var action = policy.Choose(buffer);
            if (action.Kind == ActionKind.EndTurn)
            {
                endTurns++;
            }
            sim.Step(action);
        }
        var clone = sim.Clone();
        AssertNoSharedMutable(sim, clone, "mid fight");
        AssertCardsUnique(sim.State, "mid fight original");
        AssertCardsUnique(clone.State, "mid fight clone");
        AssertPowerOwners(clone.State, "mid fight clone");
        var cloneOfClone = clone.Clone();
        AssertNoSharedMutable(clone, cloneOfClone, "clone of clone");
        AssertNoSharedMutable(sim, cloneOfClone, "original vs clone of clone");
    }

    public static void Test_State_SameSeedSameScriptSameHashes()
    {
        foreach (var seed in Seeds)
        {
            var first = CreateSim(seed);
            var second = CreateSim(seed);
            Assert.SequenceEqual(StateHasher.CanonicalBytes(first.State), StateHasher.CanonicalBytes(second.State), $"seed {seed}: initial canonical bytes");
            var (script, firstHashes) = RunPolicy(first, new ScriptPolicy(seed ^ 0xA5A5A5A5UL), MaxSteps);
            Assert.True(script.Count > 0, $"seed {seed}: the script must contain at least one action");
            var secondHashes = Replay(second, script, $"seed {seed} replay");
            Assert.SequenceEqual(firstHashes, secondHashes, $"seed {seed}: hash sequences");
            Assert.Equal(first.Outcome, second.Outcome, $"seed {seed}: outcome");
            Assert.SequenceEqual(StateHasher.CanonicalBytes(first.State), StateHasher.CanonicalBytes(second.State), $"seed {seed}: final canonical bytes");

            var third = CreateSim(seed);
            var (thirdScript, thirdHashes) = RunPolicy(third, new ScriptPolicy(seed ^ 0xA5A5A5A5UL), MaxSteps);
            Assert.SequenceEqual(script, thirdScript, $"seed {seed}: identical states must offer identical legal actions");
            Assert.SequenceEqual(firstHashes, thirdHashes, $"seed {seed}: independent rerun");
        }
        Assert.NotEqual(CreateSim(Seeds[0]).StateHash(), CreateSim(Seeds[0] + 1UL).StateHash(), "different seeds must give different states");
    }

    public static void Test_State_CloneBranchesDivergeWithoutTouchingOriginal()
    {
        ulong seed = Seeds[1];
        var sim = CreateSim(seed);
        var prefix = AdvanceToBranchPoint(sim, new ScriptPolicy(11UL), 3);
        var control = CreateSim(seed);
        Replay(control, prefix, "control prefix");
        Assert.Equal(control.StateHash(), sim.StateHash(), "control matches the original at the branch point");

        ulong before = sim.StateHash();
        var beforeBytes = StateHasher.CanonicalBytes(sim.State);
        var branch = sim.Clone();
        Assert.Equal(before, branch.StateHash(), "clone hash equals source hash");

        var legal = Legal(sim);
        var play = legal.First(a => a.Kind == ActionKind.PlayCard);
        var end = legal.First(a => a.Kind == ActionKind.EndTurn);
        branch.Step(play);
        ulong branchFirst = branch.StateHash();
        Assert.NotEqual(before, branchFirst, "playing a card must change the branch");
        Assert.Equal(before, sim.StateHash(), "stepping the clone changed the original");
        RunPolicy(branch, new ScriptPolicy(99UL), 80);
        Assert.Equal(before, sim.StateHash(), "running the clone changed the original");
        Assert.SequenceEqual(beforeBytes, StateHasher.CanonicalBytes(sim.State), "original canonical bytes after branch run");

        ulong branchAfterRun = branch.StateHash();
        var branchBytes = StateHasher.CanonicalBytes(branch.State);
        sim.Step(end);
        control.Step(end);
        Assert.NotEqual(branchFirst, sim.StateHash(), "different actions from one state must give different hashes");
        Assert.Equal(control.StateHash(), sim.StateHash(), "original diverged from a never-cloned control");

        var policy = new ScriptPolicy(1234UL);
        var simBuffer = new List<GameAction>(16);
        var controlBuffer = new List<GameAction>(16);
        for (int step = 0; step < 120 && !sim.IsTerminal; step++)
        {
            sim.LegalActions(simBuffer);
            control.LegalActions(controlBuffer);
            Assert.SequenceEqual(controlBuffer, simBuffer, $"legal actions at lockstep {step}");
            var action = policy.Choose(simBuffer);
            sim.Step(action);
            control.Step(action);
            Assert.Equal(control.StateHash(), sim.StateHash(), $"original vs control at lockstep {step}");
        }
        Assert.Equal(control.Outcome, sim.Outcome);
        Assert.Equal(branchAfterRun, branch.StateHash(), "stepping the original changed the clone");
        Assert.SequenceEqual(branchBytes, StateHasher.CanonicalBytes(branch.State), "clone canonical bytes after original run");
    }

    public static void Test_State_CloneReplaysIdentically()
    {
        var sim = CreateSim(Seeds[2]);
        AdvanceToBranchPoint(sim, new ScriptPolicy(5UL), 2);
        var clone = sim.Clone();
        var cloneOfClone = clone.Clone();
        Assert.Equal(sim.StateHash(), clone.StateHash());
        Assert.Equal(sim.StateHash(), cloneOfClone.StateHash());

        var policy = new ScriptPolicy(77UL);
        var a = new List<GameAction>(16);
        var b = new List<GameAction>(16);
        var c = new List<GameAction>(16);
        int steps = 0;
        for (; steps < MaxSteps && !sim.IsTerminal; steps++)
        {
            sim.LegalActions(a);
            clone.LegalActions(b);
            cloneOfClone.LegalActions(c);
            Assert.SequenceEqual(a, b, $"clone legal actions at step {steps}");
            Assert.SequenceEqual(a, c, $"clone-of-clone legal actions at step {steps}");
            var action = policy.Choose(a);
            sim.Step(action);
            clone.Step(action);
            cloneOfClone.Step(action);
            ulong expected = sim.StateHash();
            Assert.Equal(expected, clone.StateHash(), $"clone hash at step {steps}");
            Assert.Equal(expected, cloneOfClone.StateHash(), $"clone-of-clone hash at step {steps}");
        }
        Assert.True(steps > 0, "the replay must take at least one step");
        Assert.Equal(sim.Outcome, clone.Outcome);
        Assert.Equal(sim.Outcome, cloneOfClone.Outcome);
        Assert.Equal(sim.IsTerminal, clone.IsTerminal);
        Assert.SequenceEqual(StateHasher.CanonicalBytes(sim.State), StateHasher.CanonicalBytes(clone.State));
    }

    public static void Test_State_CloneHashEqualsSourceHashAlongTrajectory()
    {
        var sim = CreateSim(Seeds[0]);
        var policy = new ScriptPolicy(2024UL);
        var buffer = new List<GameAction>(16);
        var scratch = new CanonicalWriter();
        for (int step = 0; step <= MaxSteps; step++)
        {
            ulong hash = sim.StateHash();
            var clone = sim.Clone();
            Assert.Equal(hash, clone.StateHash(), $"clone hash at step {step}");
            Assert.Equal(hash, StateHasher.Hash(sim.State, scratch), $"scratch-writer hash at step {step}");
            var w = new CanonicalWriter();
            sim.WriteCanonical(w);
            Assert.Equal(hash, StateHasher.Hash(w.WrittenSpan), $"CombatSim.WriteCanonical hash at step {step}");
            Assert.Equal(sim.IsTerminal, clone.IsTerminal);
            if (sim.IsTerminal)
            {
                break;
            }
            sim.LegalActions(buffer);
            sim.Step(policy.Choose(buffer));
        }
    }

    public static void Test_State_CloneStaysWithinBudget()
    {
        var sim = CreateSim(Seeds[0]);
        AdvanceToBranchPoint(sim, new ScriptPolicy(8UL), 4);
        for (int i = 0; i < 50; i++)
        {
            _ = sim.Clone();
        }
        const int iterations = 500;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            _ = sim.Clone();
        }
        stopwatch.Stop();
        double perCloneMs = stopwatch.Elapsed.TotalMilliseconds / iterations;
        Assert.True(perCloneMs < 1.0, $"CombatSim.Clone() averaged {perCloneMs:F4} ms; the P0 budget is 1 ms");
    }

    private static string ResolveEncounterId()
    {
        if (EncounterRegistry.Defs.Contains(PreferredEncounterId))
        {
            return PreferredEncounterId;
        }
        var ids = EncounterRegistry.AllIds;
        if (ids.Count == 0)
        {
            Assert.Fail("No EncounterDef is registered; the determinism tests need at least one encounter");
        }
        return ids[0];
    }

    private static CombatSim CreateSim(ulong seed) => CombatSim.Create(seed, StartingDecks.Ironclad, ResolveEncounterId());

    private static List<GameAction> Legal(CombatSim sim)
    {
        var buffer = new List<GameAction>(16);
        sim.LegalActions(buffer);
        return buffer;
    }

    private static (List<GameAction> Script, List<ulong> Hashes) RunPolicy(CombatSim sim, ScriptPolicy policy, int maxSteps)
    {
        var script = new List<GameAction>();
        var hashes = new List<ulong> { sim.StateHash() };
        var buffer = new List<GameAction>(16);
        for (int step = 0; step < maxSteps && !sim.IsTerminal; step++)
        {
            sim.LegalActions(buffer);
            Assert.True(buffer.Count > 0, $"a combat that is not over must offer a legal action (step {step})");
            var action = policy.Choose(buffer);
            script.Add(action);
            sim.Step(action);
            hashes.Add(sim.StateHash());
        }
        return (script, hashes);
    }

    private static List<ulong> Replay(CombatSim sim, IReadOnlyList<GameAction> script, string label)
    {
        var hashes = new List<ulong> { sim.StateHash() };
        var buffer = new List<GameAction>(16);
        for (int i = 0; i < script.Count; i++)
        {
            sim.LegalActions(buffer);
            Assert.True(buffer.Contains(script[i]), $"{label}: scripted action {script[i]} is not legal at step {i}");
            sim.Step(script[i]);
            hashes.Add(sim.StateHash());
        }
        return hashes;
    }

    private static List<GameAction> AdvanceToBranchPoint(CombatSim sim, ScriptPolicy policy, int minSteps)
    {
        var taken = new List<GameAction>();
        var buffer = new List<GameAction>(16);
        for (int step = 0; step < MaxSteps; step++)
        {
            if (sim.IsTerminal)
            {
                break;
            }
            sim.LegalActions(buffer);
            if (step >= minSteps && buffer.Any(a => a.Kind == ActionKind.PlayCard) && buffer.Any(a => a.Kind == ActionKind.EndTurn))
            {
                return taken;
            }
            var action = policy.Choose(buffer);
            taken.Add(action);
            sim.Step(action);
        }
        Assert.Fail("No decision point with both a playable card and EndTurn was reached");
        return taken;
    }

    private static void AssertNoSharedMutable(object original, object clone, string label)
    {
        var inspector = new GraphInspector();
        var problems = inspector.FindSharedMutable(original, clone);
        if (problems.Count > 0)
        {
            Assert.Fail($"{label}: the clone shares mutable objects with the original:{Environment.NewLine}  " + string.Join(Environment.NewLine + "  ", problems));
        }
        int reachable = inspector.Collect(clone, "clone").Count;
        Assert.True(reachable >= 20, $"{label}: the inspector reached only {reachable} objects");
    }

    private static void AssertCardsUnique(CombatState state, string label)
    {
        var cards = new HashSet<CardInstance>(ReferenceEqualityComparer.Instance);
        var ids = new HashSet<int>();
        foreach (var pile in state.Player.AllPiles)
        {
            foreach (var card in pile.Cards)
            {
                Assert.True(cards.Add(card), $"{label}: card {card.Id}#{card.InstanceId} appears in more than one place");
                Assert.True(ids.Add(card.InstanceId), $"{label}: instance id {card.InstanceId} is used twice");
            }
        }
    }

    private static void AssertPowerOwners(CombatState state, string label)
    {
        AssertPowerOwners(state.Player, label);
        foreach (var monster in state.Monsters)
        {
            AssertPowerOwners(monster, label);
        }
    }

    private static void AssertPowerOwners(CreatureState creature, string label)
    {
        foreach (var power in creature.Powers)
        {
            Assert.True(ReferenceEquals(power.Owner, creature), $"{label}: power {power.Id} on creature {creature.CombatId} is owned by another creature object");
        }
    }

    private static void AssertIds(CardPile pile, params int[] ids) => Assert.SequenceEqual(ids, pile.Cards.Select(card => card.InstanceId));

    private static void SwapFirstTwo(CardPile pile)
    {
        var cards = pile.MutableCards;
        (cards[0], cards[1]) = (cards[1], cards[0]);
    }

    private static byte[] BytesOf(Action<CanonicalWriter> write)
    {
        var w = new CanonicalWriter(16);
        write(w);
        return w.ToArray();
    }

    private static ulong HashOf(Action<CanonicalWriter> write)
    {
        var w = new CanonicalWriter(16);
        write(w);
        return StateHasher.Hash(w.WrittenSpan);
    }

    private static void AttachPower(PowerModel power, CreatureState owner, int amount)
    {
        power.AttachTo(owner);
        power.SetAmount(amount);
        owner.Powers.Add(power);
    }

    private static CombatState BuildManualState()
    {
        var player = new PlayerState(0, "IRONCLAD", 70, 80, 3);
        var cs = new CombatState(player, "STATE_TEST_ENCOUNTER", 0, new RunRngSet(new Rng(12345UL)));
        var strike = new TestCardDef("STATE_TEST_STRIKE");
        var guard = new TestCardDef("STATE_TEST_GUARD");
        for (int i = 0; i < 4; i++)
        {
            player.DrawPile.AddInternal(new CardInstance(i % 2 == 0 ? strike : guard, cs.AllocateCardInstanceId()));
        }
        for (int i = 0; i < 3; i++)
        {
            player.Hand.AddInternal(new CardInstance(i == 1 ? guard : strike, cs.AllocateCardInstanceId()));
        }
        player.DiscardPile.AddInternal(new CardInstance(guard, cs.AllocateCardInstanceId()));
        player.ExhaustPile.AddInternal(new CardInstance(strike, cs.AllocateCardInstanceId()));
        player.Energy = 3;
        player.Block = 4;
        player.Phase = PlayerTurnPhase.Play;
        AttachPower(new TestBuffPower(), player, 2);
        AttachPower(new TestDebuffPower(), player, 1);
        var monsterDef = new TestMonsterDef();
        for (int i = 0; i < 2; i++)
        {
            var monster = new MonsterState(cs.AllocateCreatureId(), monsterDef, 11 + i, 12 + i, new TestExtraState { Counter = i }, i == 0 ? "front" : "back");
            monster.MaxHpBeforeModification = 12 + i;
            cs.AddMonster(monster);
            AttachPower(new TestDebuffPower(), monster, 3 + i);
        }
        cs.Monsters[1].Block = 6;
        cs.IsInProgress = true;
        return cs;
    }

    private sealed class ScriptPolicy
    {
        private ulong _state;

        public ScriptPolicy(ulong seed)
        {
            _state = seed;
        }

        public GameAction Choose(IReadOnlyList<GameAction> legal)
        {
            if (legal.Count == 0)
            {
                throw new InvalidOperationException("No legal actions to choose from");
            }
            ulong r = Next();
            int plays = 0;
            for (int i = 0; i < legal.Count; i++)
            {
                if (legal[i].Kind == ActionKind.PlayCard)
                {
                    plays++;
                }
            }
            if (plays > 0 && (r & 7UL) != 0UL)
            {
                int pick = (int)((r >> 3) % (ulong)plays);
                for (int i = 0; i < legal.Count; i++)
                {
                    if (legal[i].Kind != ActionKind.PlayCard)
                    {
                        continue;
                    }
                    if (pick == 0)
                    {
                        return legal[i];
                    }
                    pick--;
                }
            }
            for (int i = 0; i < legal.Count; i++)
            {
                if (legal[i].Kind == ActionKind.EndTurn)
                {
                    return legal[i];
                }
            }
            return legal[(int)((r >> 3) % (ulong)legal.Count)];
        }

        private ulong Next()
        {
            unchecked
            {
                _state += 0x9E3779B97F4A7C15UL;
                ulong z = _state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
    }

    private sealed class BlobCanonical : ICanonical
    {
        private readonly byte[] _payload;

        public BlobCanonical(byte[] payload)
        {
            _payload = payload;
        }

        public void WriteCanonical(CanonicalWriter w)
        {
            w.WriteTag(0x7E57);
            w.WriteCount(_payload.Length);
            w.WriteBytes(_payload);
        }
    }

    private sealed class TestCardDef : CardDef
    {
        public TestCardDef(string id)
            : base(id, 1, CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy)
        {
        }

        protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[] { new DamageVar(6m, ValueProp.Move) };

        public override void OnPlay(CombatState cs, CardPlay play)
        {
        }
    }

    private sealed class TestBuffPower : PowerModel
    {
        public override string Id => "STATE_TEST_BUFF";

        public override PowerType Type => PowerType.Buff;

        public override PowerStackType StackType => PowerStackType.Counter;
    }

    private sealed class TestDebuffPower : PowerModel
    {
        public override string Id => "STATE_TEST_DEBUFF";

        public override PowerType Type => PowerType.Debuff;

        public override PowerStackType StackType => PowerStackType.Counter;
    }

    private sealed class TestMonsterDef : MonsterDef
    {
        public TestMonsterDef()
            : base("STATE_TEST_MONSTER")
        {
        }

        public override int MinInitialHp(CombatState cs) => 11;

        public override int MaxInitialHp(CombatState cs) => 13;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
            => throw new NotSupportedException("Test double has no move state machine");
    }

    private sealed class TestExtraState : MonsterExtraState
    {
        public int Counter { get; set; }

        public List<int> History { get; set; } = new();

        public override MonsterExtraState DeepClone() => new TestExtraState { Counter = Counter, History = new List<int>(History) };

        public override void WriteCanonical(CanonicalWriter w)
        {
            w.WriteTag(0x7E01);
            w.WriteInt32(Counter);
            w.WriteCount(History.Count);
            for (int i = 0; i < History.Count; i++)
            {
                w.WriteInt32(History[i]);
            }
        }
    }

    private sealed class Holder
    {
        public List<int> Items = new();
        public CardDef? Def;
        public int[] Empty = Array.Empty<int>();
        public string Name = "holder";
        public Func<int>? Callback;
        public KeyValuePair<string, List<int>> Pair;
        public FrozenNode? Frozen;
    }

    private sealed class FrozenNode
    {
        private readonly FrozenNode? _next;
        private readonly string _label = "frozen";
        private readonly int[] _none = Array.Empty<int>();

        public FrozenNode(FrozenNode? next)
        {
            _next = next;
        }

        public FrozenNode? Next => _next;

        public int Size => _label.Length + _none.Length;
    }

    private sealed class GraphInspector
    {
        private const BindingFlags DeclaredInstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static readonly Type[] ImmutableRoots =
        {
            typeof(string),
            typeof(Type),
            typeof(MemberInfo),
            typeof(Assembly),
            typeof(Module),
            typeof(CardDef),
            typeof(MonsterDef),
            typeof(EncounterDef),
            typeof(MonsterMoveStateMachineDef),
            typeof(AiStateDef),
            typeof(AbstractIntent),
        };

        private readonly Dictionary<Type, FieldInfo[]> _fields = new();
        private readonly Dictionary<Type, bool> _leaf = new();
        private readonly Dictionary<Type, bool> _immutableType = new();

        public Dictionary<object, string> Collect(object root, string rootName)
        {
            var seen = new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
            var pending = new Stack<KeyValuePair<object, string>>();
            Visit(root, rootName, seen, pending);
            while (pending.Count > 0)
            {
                var item = pending.Pop();
                Expand(item.Key, item.Value, seen, pending);
            }
            return seen;
        }

        public List<string> FindSharedMutable(object original, object clone, int maxReports = 25)
        {
            var originalObjects = Collect(original, "original");
            var cloneObjects = Collect(clone, "clone");
            var verdicts = new Dictionary<object, bool>(ReferenceEqualityComparer.Instance);
            var problems = new List<string>();
            foreach (var entry in cloneObjects)
            {
                if (!originalObjects.TryGetValue(entry.Key, out var originalPath))
                {
                    continue;
                }
                if (IsDeeplyImmutable(entry.Key, verdicts, new HashSet<object>(ReferenceEqualityComparer.Instance)))
                {
                    continue;
                }
                problems.Add($"{entry.Key.GetType().FullName} at {entry.Value} is the same object as {originalPath}");
                if (problems.Count >= maxReports)
                {
                    break;
                }
            }
            return problems;
        }

        private void Visit(object? value, string path, Dictionary<object, string> seen, Stack<KeyValuePair<object, string>> pending)
        {
            if (value is null)
            {
                return;
            }
            var type = value.GetType();
            if (type.IsValueType)
            {
                if (!IsLeafValueType(type))
                {
                    ExpandFields(value, type, path, seen, pending);
                }
                return;
            }
            if (IsImmutableType(type))
            {
                return;
            }
            if (value is Delegate del)
            {
                var invocations = del.GetInvocationList();
                for (int i = 0; i < invocations.Length; i++)
                {
                    Visit(invocations[i].Target, $"{path}.<target{i}>", seen, pending);
                }
                return;
            }
            if (seen.TryAdd(value, path))
            {
                pending.Push(new KeyValuePair<object, string>(value, path));
            }
        }

        private void Expand(object obj, string path, Dictionary<object, string> seen, Stack<KeyValuePair<object, string>> pending)
        {
            var type = obj.GetType();
            if (type.IsArray)
            {
                var elementType = type.GetElementType()!;
                if (elementType.IsPointer || elementType.IsFunctionPointer || IsLeafValueType(elementType))
                {
                    return;
                }
                int index = 0;
                foreach (var item in (Array)obj)
                {
                    Visit(item, $"{path}[{index}]", seen, pending);
                    index++;
                }
                return;
            }
            ExpandFields(obj, type, path, seen, pending);
        }

        private void ExpandFields(object obj, Type type, string path, Dictionary<object, string> seen, Stack<KeyValuePair<object, string>> pending)
        {
            foreach (var field in GetFields(type))
            {
                var fieldType = field.FieldType;
                if (fieldType.IsPointer || fieldType.IsFunctionPointer || fieldType.IsByRef || IsLeafValueType(fieldType))
                {
                    continue;
                }
                Visit(field.GetValue(obj), $"{path}.{FieldLabel(field)}", seen, pending);
            }
        }

        private bool IsDeeplyImmutable(object obj, Dictionary<object, bool> verdicts, HashSet<object> visiting)
        {
            var type = obj.GetType();
            if (IsImmutableType(type))
            {
                return true;
            }
            if (type.IsValueType)
            {
                return ValueContentsImmutable(obj, type, verdicts, visiting);
            }
            if (verdicts.TryGetValue(obj, out var known))
            {
                return known;
            }
            if (!visiting.Add(obj))
            {
                return true;
            }
            bool result = ComputeImmutable(obj, type, verdicts, visiting);
            visiting.Remove(obj);
            verdicts[obj] = result;
            return result;
        }

        private bool ComputeImmutable(object obj, Type type, Dictionary<object, bool> verdicts, HashSet<object> visiting)
        {
            if (type.IsArray)
            {
                return ((Array)obj).Length == 0;
            }
            if (obj is Delegate del)
            {
                foreach (var invocation in del.GetInvocationList())
                {
                    if (invocation.Target is { } target && !IsDeeplyImmutable(target, verdicts, visiting))
                    {
                        return false;
                    }
                }
                return true;
            }
            foreach (var field in GetFields(type))
            {
                if (!field.IsInitOnly)
                {
                    return false;
                }
                if (!FieldValueImmutable(obj, field, verdicts, visiting))
                {
                    return false;
                }
            }
            return true;
        }

        private bool ValueContentsImmutable(object boxed, Type type, Dictionary<object, bool> verdicts, HashSet<object> visiting)
        {
            foreach (var field in GetFields(type))
            {
                if (!FieldValueImmutable(boxed, field, verdicts, visiting))
                {
                    return false;
                }
            }
            return true;
        }

        private bool FieldValueImmutable(object owner, FieldInfo field, Dictionary<object, bool> verdicts, HashSet<object> visiting)
        {
            var fieldType = field.FieldType;
            if (fieldType.IsPointer || fieldType.IsFunctionPointer || fieldType.IsByRef || IsLeafValueType(fieldType))
            {
                return true;
            }
            var value = field.GetValue(owner);
            return value is null || IsDeeplyImmutable(value, verdicts, visiting);
        }

        private bool IsImmutableType(Type type)
        {
            if (_immutableType.TryGetValue(type, out var cached))
            {
                return cached;
            }
            bool result = false;
            foreach (var root in ImmutableRoots)
            {
                if (root.IsAssignableFrom(type))
                {
                    result = true;
                    break;
                }
            }
            if (!result)
            {
                result = IsComparer(type);
            }
            _immutableType[type] = result;
            return result;
        }

        private static bool IsComparer(Type type)
        {
            if (typeof(System.Collections.IComparer).IsAssignableFrom(type) || typeof(System.Collections.IEqualityComparer).IsAssignableFrom(type))
            {
                return true;
            }
            foreach (var implemented in type.GetInterfaces())
            {
                if (!implemented.IsGenericType)
                {
                    continue;
                }
                var definition = implemented.GetGenericTypeDefinition();
                if (definition == typeof(IComparer<>) || definition == typeof(IEqualityComparer<>))
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsLeafValueType(Type type)
        {
            if (!type.IsValueType)
            {
                return false;
            }
            if (type.IsPrimitive || type.IsEnum)
            {
                return true;
            }
            if (_leaf.TryGetValue(type, out var cached))
            {
                return cached;
            }
            _leaf[type] = true;
            bool leaf = true;
            foreach (var field in GetFields(type))
            {
                var fieldType = field.FieldType;
                if (fieldType.IsPointer || fieldType.IsFunctionPointer)
                {
                    continue;
                }
                if (!fieldType.IsValueType || !IsLeafValueType(fieldType))
                {
                    leaf = false;
                    break;
                }
            }
            _leaf[type] = leaf;
            return leaf;
        }

        private FieldInfo[] GetFields(Type type)
        {
            if (_fields.TryGetValue(type, out var cached))
            {
                return cached;
            }
            var list = new List<FieldInfo>();
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                list.AddRange(t.GetFields(DeclaredInstanceFields));
            }
            var result = list.ToArray();
            _fields[type] = result;
            return result;
        }

        private static string FieldLabel(FieldInfo field)
        {
            var name = field.Name;
            if (name.Length > 1 && name[0] == '<')
            {
                int end = name.IndexOf('>');
                if (end > 1)
                {
                    return name.Substring(1, end - 1);
                }
            }
            return name;
        }
    }
}

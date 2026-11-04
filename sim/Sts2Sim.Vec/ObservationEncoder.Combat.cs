using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.State;

namespace Sts2Sim.Vec;

// Combat tables: hand (+ effective numbers), pile counts, player powers, orbs, enemies, intents, pets.
public static partial class ObservationEncoder
{
    private static void EncodeCombat(CombatState cs, in SlotBuffers b, Span<int> gi, ref bool overflow)
    {
        var player = cs.Player;
        EncodeHand(cs, b.Hand, b.HandF, b.HandDmg, b.HandX, ref overflow);
        CountPile(player.DrawPile.Cards, b.DrawCounts);
        CountPile(player.DiscardPile.Cards, b.DiscardCounts);
        CountPile(player.ExhaustPile.Cards, b.ExhaustCounts);
        b.GlobalX[GX.DrawMarkedN] = EncodeDrawMarked(player.DrawPile.Cards, b.DrawMarked, ref overflow);
        EncodePileOrder(player.DiscardPile.Cards, b.DiscardOrder, ref overflow);
        EncodePileOrder(player.ExhaustPile.Cards, b.ExhaustOrder, ref overflow);
        EncodePlayerPowers(cs, player, b.PowersPlayer, ref overflow);
        EncodeOrbs(cs, player, b.Orbs, ref overflow);
        EncodeEnemies(cs, b.Enemies, b.EnemyIntents, b.EnemyPowers, gi, ref overflow);
        EncodeAllies(cs, b.Allies, b.AllyPowers, gi, ref overflow);
    }

    private static void EncodeHand(CombatState cs, Span<int> hand, Span<float> handF, Span<float> handDmg, Span<int> handX, ref bool overflow)
    {
        int hc = Schema.HC;
        int hxw = Schema.HandXFields.Length;
        var dampen = cs.Player.GetPower<DampenPower>();
        var cards = cs.Player.Hand.Cards;
        int n = cards.Count;
        if (n > Caps.Hand)
        {
            overflow = true;
            n = Caps.Hand;
        }
        var monsters = cs.Monsters;
        int nm = Math.Min(monsters.Count, Caps.Enemies);
        for (int i = 0; i < n; i++)
        {
            var card = cards[i];
            var row = hand.Slice(i * hc, hc);
            WriteCard(row, card, cs);
            var hx = handX.Slice(i * hxw, hxw);
            hx[HX.AfflictionAmount] = card.Affliction?.Amount ?? 0;
            hx[HX.Dampened] = dampen != null && CardMarks.IsDampened(dampen, card) ? 1 : 0;
            // Effective numbers: CardPreview (the game's DynamicVar.UpdateCardPreview), truncated to the shown integer.
            var f = handF.Slice(i * 2, 2);
            if (CardPreview.Block(cs, card, null) is { } block)
            {
                f[0] = (int)block;
            }
            if (!HasDamageVar(card))
            {
                continue;
            }
            row[HC.Hits] = PlayerView.HitCount(card) ?? 0;
            if (card.TargetType != TargetType.AnyEnemy && CardPreview.Damage(cs, card, null) is { } untargeted)
            {
                f[1] = (int)untargeted;
            }
            var dmg = handDmg.Slice(i * Caps.Enemies, Caps.Enemies);
            for (int j = 0; j < nm; j++)
            {
                var m = monsters[j];
                if (m.IsEnemy && m.IsAlive && CardPreview.Damage(cs, card, m) is { } d)
                {
                    dmg[j] = (int)d;
                }
            }
        }
    }

    // A combat-pile card with its marks (PileCardFields): identity, enchantment and affliction with their amounts.
    private static void WritePileCard(Span<int> row, CardInstance card)
    {
        row[PileCol.CardId] = Vocab.Card(card.Id);
        row[PileCol.Upgrade] = card.UpgradeLevel;
        if (card.Enchantment is { } ench)
        {
            row[PileCol.EnchantId] = Vocab.Enchantment(ench.Id);
            row[PileCol.EnchantAmount] = ench.Amount;
        }
        if (card.Affliction is { } aff)
        {
            row[PileCol.AfflictionId] = Vocab.Affliction(aff.Id);
            row[PileCol.AfflictionAmount] = aff.Amount;
        }
    }

    // discard_order / exhaust_order: row i = Cards[i], the order the pile screens show (oldest first; the game keeps
    // these piles in order, NCardPileScreen). Neither order is hidden information.
    private static void EncodePileOrder(IReadOnlyList<CardInstance> cards, Span<int> table, ref bool overflow)
    {
        int w = Schema.PileCardFields.Length;
        int n = cards.Count;
        if (n > Caps.PileOrder)
        {
            overflow = true;
            n = Caps.PileOrder;
        }
        for (int i = 0; i < n; i++)
        {
            WritePileCard(table.Slice(i * w, w), cards[i]);
        }
    }

    // draw_marked: the draw-pile cards that carry an enchantment or an affliction, as the (sorted) draw-pile screen
    // shows them. H1: rows are sorted by their whole content, so the table depends on the pile's multiset only, never
    // on its order. Returns the true count (global_x.draw_marked_n).
    private static int EncodeDrawMarked(IReadOnlyList<CardInstance> cards, Span<int> table, ref bool overflow)
    {
        var marked = S.Marked;
        marked.Clear();
        for (int i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            var ench = card.Enchantment;
            var aff = card.Affliction;
            if (ench == null && aff == null)
            {
                continue;
            }
            marked.Add((Vocab.Card(card.Id), card.UpgradeLevel, ench != null ? Vocab.Enchantment(ench.Id) : 0, ench?.Amount ?? 0,
                aff != null ? Vocab.Affliction(aff.Id) : 0, aff?.Amount ?? 0));
        }
        int count = marked.Count;
        if (count == 0)
        {
            return 0;
        }
        marked.Sort();
        int n = count;
        if (n > Caps.DrawMarked)
        {
            overflow = true;
            n = Caps.DrawMarked;
        }
        int w = Schema.PileCardFields.Length;
        for (int i = 0; i < n; i++)
        {
            var (id, up, ench, enchAmount, aff, affAmount) = marked[i];
            var row = table.Slice(i * w, w);
            row[PileCol.CardId] = id;
            row[PileCol.Upgrade] = up;
            row[PileCol.EnchantId] = ench;
            row[PileCol.EnchantAmount] = enchAmount;
            row[PileCol.AfflictionId] = aff;
            row[PileCol.AfflictionAmount] = affAmount;
        }
        marked.Clear();
        return count;
    }

    // H1: multiset counts per (card_id, upgrade); the pile order is never written.
    private static void CountPile(IReadOnlyList<CardInstance> cards, Span<short> counts)
    {
        for (int i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            int k = Vocab.CountIndex(Vocab.Card(card.Id), card.UpgradeLevel);
            if (counts[k] < short.MaxValue)
            {
                counts[k]++;
            }
        }
    }

    // H10: visible powers (PlayerView.IsVisibleToPlayer) with the amount the UI shows (PlayerView.DisplayAmountToPlayer).
    private static int WritePowers(CombatState cs, CreatureState creature, Span<int> table, int cap, int width, bool withDebuff, ref bool overflow)
    {
        var powers = creature.Powers;
        int n = 0;
        for (int i = 0; i < powers.Count; i++)
        {
            var power = powers[i];
            if (!PlayerView.IsVisibleToPlayer(cs, power))
            {
                continue;
            }
            if (n >= cap)
            {
                overflow = true;
                break;
            }
            var row = table.Slice(n * width, width);
            row[0] = Vocab.Power(power.Id);
            row[1] = PlayerView.DisplayAmountToPlayer(cs, power);
            if (withDebuff)
            {
                row[2] = power.TypeForCurrentAmount == PowerType.Debuff ? 1 : 0;
            }
            n++;
        }
        return n;
    }

    private static void EncodePlayerPowers(CombatState cs, PlayerState player, Span<int> table, ref bool overflow)
        => WritePowers(cs, player, table, Caps.PlayerPowers, 3, true, ref overflow);

    private static void EncodeOrbs(CombatState cs, PlayerState player, Span<int> table, ref bool overflow)
    {
        var orbs = player.OrbQueue.Orbs;
        int n = orbs.Count;
        if (n > Caps.Orbs)
        {
            overflow = true;
            n = Caps.Orbs;
        }
        for (int i = 0; i < n; i++)
        {
            var orb = orbs[i];
            var row = table.Slice(i * 3, 3);
            row[0] = Vocab.Orb(orb.Id);
            row[1] = (int)orb.PassiveVal(cs);
            row[2] = (int)orb.EvokeVal(cs);
        }
    }

    // Slot j = CombatState.Monsters[j] (the PlayCard / UsePotion target index); dead or non-enemy slots stay zero.
    // H8: only the intents of the next move are read (type, damage, times, card count) — never the move's identity.
    private static void EncodeEnemies(CombatState cs, Span<int> enemies, Span<int> intents, Span<int> powers, Span<int> gi, ref bool overflow)
    {
        const int ew = 9;
        const int iw = 4;
        const int pw = 2;
        var monsters = cs.Monsters;
        var targets = S.Targets;
        targets[0] = cs.Player;
        var surrounded = cs.Player.GetPower<SurroundedPower>();
        int present = 0;
        int incoming = 0;
        int hits = 0;
        for (int j = 0; j < monsters.Count; j++)
        {
            var m = monsters[j];
            if (!m.IsEnemy || !m.IsAlive)
            {
                continue;
            }
            present++;
            if (j >= Caps.Enemies)
            {
                overflow = true;
                continue;
            }
            var row = enemies.Slice(j * ew, ew);
            row[0] = 1;
            row[1] = m.CombatId;
            row[2] = Vocab.Monster(m.MonsterId);
            row[3] = m.CurrentHp;
            row[4] = m.MaxHp;
            row[5] = m.Block;
            bool left = m.HasPower<BackAttackLeftPower>();
            bool right = !left && m.HasPower<BackAttackRightPower>();
            row[6] = left ? 1 : right ? 2 : 0;
            if (surrounded != null && (left || right))
            {
                row[7] = (surrounded.Facing == SurroundedPower.Direction.Right ? left : m.HasPower<BackAttackRightPower>()) ? 1 : 0;
            }
            int ni = 0;
            var nextMove = m.HasAi ? m.Ai.NextMove : null;
            if (nextMove != null)
            {
                var list = nextMove.Intents;
                for (int k = 0; k < list.Count; k++)
                {
                    var view = PlayerView.Intent(cs, m, list[k], targets);
                    if (view.Damage is { } dmg)
                    {
                        int times = view.Times ?? 1;
                        incoming += dmg * times;
                        hits += times;
                    }
                    if (ni >= Caps.EnemyIntents)
                    {
                        overflow = true;
                        continue;
                    }
                    var it = intents.Slice((j * Caps.EnemyIntents + ni) * iw, iw);
                    it[0] = (int)view.Type;
                    it[1] = view.Damage ?? 0;
                    it[2] = view.Times ?? 0;
                    it[3] = view.CardCount ?? 0;
                    ni++;
                }
            }
            row[8] = ni;
            WritePowers(cs, m, powers.Slice(j * Caps.EnemyPowers * pw, Caps.EnemyPowers * pw), Caps.EnemyPowers, pw, false, ref overflow);
        }
        targets[0] = null!;
        gi[GI.EnemyN] = present;
        gi[GI.IncomingDamage] = incoming;
        gi[GI.IncomingHits] = hits;
    }

    // Row a-1 = CombatState.Allies[a] (pets; Allies[0] is the player): the UsePotion ally target -2 - a.
    private static void EncodeAllies(CombatState cs, Span<int> allies, Span<int> powers, Span<int> gi, ref bool overflow)
    {
        const int aw = 5;
        const int pw = 2;
        var list = cs.Allies;
        int alive = 0;
        for (int a = 1; a < list.Count; a++)
        {
            if (list[a] is not MonsterState pet || !pet.IsAlive)
            {
                continue;
            }
            alive++;
            int r = a - 1;
            if (r >= Caps.Allies)
            {
                overflow = true;
                continue;
            }
            var row = allies.Slice(r * aw, aw);
            row[0] = 1;
            row[1] = Vocab.Monster(pet.MonsterId);
            row[2] = pet.CurrentHp;
            row[3] = pet.MaxHp;
            row[4] = pet.Block;
            WritePowers(cs, pet, powers.Slice(r * Caps.AllyPowers * pw, Caps.AllyPowers * pw), Caps.AllyPowers, pw, false, ref overflow);
        }
        gi[GI.AllyN] = alive;
    }
}

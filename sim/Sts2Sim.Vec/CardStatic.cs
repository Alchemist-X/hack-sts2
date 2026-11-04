using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Registry;

namespace Sts2Sim.Vec;

/// <summary>
/// Static card features (schema append of 2026-11-04): one row per <c>cards</c> vocabulary id, published once in the
/// schema JSON as <c>"card_static": {"fields": [...], "rows": [[...], ...]}</c> — not per observation — so a model can
/// embed a card from its numbers as well as from its id (a card it never saw played still has a cost, a type and a damage
/// value). Rows 0 (none) and 1 (unknown) are zero.
/// <para>
/// A ported card (a def in the registry, <c>ported</c> = 1) is read from a probe instance: the canonical values a fresh card
/// has (its def's constructor and <c>CanonicalVars</c>, as <c>new CardInstance</c> builds it) and the values after one
/// <c>Upgrade()</c> (the def's <c>OnUpgrade</c>; e.g. game Models.Cards/Bash.cs:146-150: Damage +2, Vulnerable +1). A card
/// whose upgrade cannot run outside a run keeps its base values in the upgraded columns with <c>upgrade_known</c> = 0. A card
/// known only from the catalog (not ported, <c>ported</c> = 0: it never appears in a game of the ported content) has its
/// catalog cost, type, rarity and target; its other columns are 0.
/// </para>
/// <para>
/// Conventions as in the <c>hand</c> table: cost -1 = X, -2 = unplayable (negative base cost); star cost -1 = none, -2 = X;
/// <c>keyword_mask</c> bit = 1 &lt;&lt; CardKeyword; damage = the Damage / CalculatedDamage / OstyDamage var, block = the
/// Block / CalculatedBlock var (0 when the card has none); hits = <c>PlayerView.HitCount</c> (-1 for X); var_id_k /
/// var_val_k = the first four dynamic vars in declaration order (<c>card_vars</c> ids) with their base values, and
/// var_val_upgraded_k the same var after the upgrade. Everything the card's text calls a "magic number" is one of those
/// vars, named by <c>card_vars</c>.
/// </para>
/// </summary>
public static class CardStatic
{
    public static readonly string[] Fields =
    {
        "ported", "cost", "cost_upgraded", "star_cost", "star_cost_upgraded", "type", "rarity", "target",
        "keyword_mask", "keyword_mask_upgraded", "max_upgrade", "upgrade_known",
        "damage", "damage_upgraded", "block", "block_upgraded", "hits", "hits_upgraded",
        "var_id_0", "var_id_1", "var_id_2", "var_id_3",
        "var_val_0", "var_val_1", "var_val_2", "var_val_3",
        "var_val_upgraded_0", "var_val_upgraded_1", "var_val_upgraded_2", "var_val_upgraded_3",
    };

    private static readonly Lazy<int[][]> RowsL = new(Build);

    /// <summary>Row i = cards vocab id i, columns in <see cref="Fields"/> order.</summary>
    public static IReadOnlyList<int[]> Rows => RowsL.Value;

    public static int Col(string name)
    {
        int i = Array.IndexOf(Fields, name);
        return i >= 0 ? i : throw new KeyNotFoundException($"card_static has no field '{name}'");
    }

    private static int[][] Build()
    {
        var names = Vocab.Cards.Names;
        var rows = new int[names.Count][];
        var ported = new HashSet<string>(CardRegistry.AllIds, StringComparer.Ordinal);
        var catalog = new Dictionary<string, CardCatalogEntry>(StringComparer.Ordinal);
        foreach (var e in CardCatalog.All)
        {
            catalog[e.Id] = e;
        }
        for (int i = 0; i < names.Count; i++)
        {
            var row = rows[i] = new int[Fields.Length];
            if (i <= VocabList.Unknown)
            {
                continue;
            }
            string id = names[i];
            if (ported.Contains(id) && CardRegistry.TryGet(id, out var def) && def is not UnportedCardDef)
            {
                FillPorted(row, def);
            }
            else if (catalog.TryGetValue(id, out var entry))
            {
                row[Col("cost")] = row[Col("cost_upgraded")] = Cost(entry.CanonicalCost, entry.CostsX);
                row[Col("star_cost")] = row[Col("star_cost_upgraded")] = -1;
                row[Col("type")] = (int)entry.Type;
                row[Col("rarity")] = (int)entry.Rarity;
                row[Col("target")] = (int)entry.TargetType;
            }
        }
        return rows;
    }

    private static int Cost(int baseCost, bool costsX) => costsX ? HC.CostX : baseCost < 0 ? HC.CostUnplayable : baseCost;

    private static int StarCost(CardInstance card) => card.Def.HasStarCostX ? -2 : card.BaseStarCost < 0 ? -1 : card.BaseStarCost;

    private static int VarValue(DynamicVarSet vars, params string[] names)
    {
        foreach (string n in names)
        {
            if (vars.TryGet(n) is { } v)
            {
                return v.IntValue;
            }
        }
        return 0;
    }

    private static int Damage(CardInstance card)
        => VarValue(card.Vars, DynamicVarSet.DamageName, DynamicVarSet.CalculatedDamageName, DynamicVarSet.OstyDamageName);

    private static int Block(CardInstance card) => VarValue(card.Vars, DynamicVarSet.BlockName, DynamicVarSet.CalculatedBlockName);

    private static int Hits(CardInstance card) => PlayerView.HitCount(card) ?? -1;

    private static void FillPorted(int[] row, CardDef def)
    {
        var card = new CardInstance(def, 0);
        row[Col("ported")] = 1;
        row[Col("cost")] = Cost(card.EnergyCost.Base, card.EnergyCost.CostsX);
        row[Col("star_cost")] = StarCost(card);
        // per-instance values, as the hand table reads them (MAD_SCIENCE's type is its Tinker Time choice: None until made)
        row[Col("type")] = (int)card.Type;
        row[Col("rarity")] = (int)card.Rarity;
        row[Col("target")] = (int)card.TargetType;
        row[Col("keyword_mask")] = card.KeywordMask;
        row[Col("max_upgrade")] = def.MaxUpgradeLevel;
        row[Col("damage")] = Damage(card);
        row[Col("block")] = Block(card);
        row[Col("hits")] = Hits(card);
        var vars = card.Vars.All;
        int nv = Math.Min(vars.Count, Caps.HandVars);
        var varNames = new string[nv];
        for (int k = 0; k < nv; k++)
        {
            varNames[k] = vars[k].Name;
            row[Col("var_id_0") + k] = Vocab.CardVar(vars[k].Name);
            row[Col("var_val_0") + k] = vars[k].IntValue;
        }

        // the upgraded columns default to the base values (no upgrade, or an upgrade that cannot run outside a run)
        row[Col("cost_upgraded")] = row[Col("cost")];
        row[Col("star_cost_upgraded")] = row[Col("star_cost")];
        row[Col("keyword_mask_upgraded")] = row[Col("keyword_mask")];
        row[Col("damage_upgraded")] = row[Col("damage")];
        row[Col("block_upgraded")] = row[Col("block")];
        row[Col("hits_upgraded")] = row[Col("hits")];
        for (int k = 0; k < nv; k++)
        {
            row[Col("var_val_upgraded_0") + k] = row[Col("var_val_0") + k];
        }
        if (!card.IsUpgradable)
        {
            row[Col("upgrade_known")] = 1;
            return;
        }
        var up = new CardInstance(def, 0);
        try
        {
            up.Upgrade();
        }
        catch (Exception)
        {
            // a static probe fails soft: the row keeps the base values and says so (upgrade_known = 0)
            return;
        }
        row[Col("upgrade_known")] = 1;
        row[Col("cost_upgraded")] = Cost(up.EnergyCost.Base, up.EnergyCost.CostsX);
        row[Col("star_cost_upgraded")] = StarCost(up);
        row[Col("keyword_mask_upgraded")] = up.KeywordMask;
        row[Col("damage_upgraded")] = Damage(up);
        row[Col("block_upgraded")] = Block(up);
        row[Col("hits_upgraded")] = Hits(up);
        for (int k = 0; k < nv; k++)
        {
            row[Col("var_val_upgraded_0") + k] = up.Vars.TryGet(varNames[k]) is { } v ? v.IntValue : row[Col("var_val_0") + k];
        }
    }
}

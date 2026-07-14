using System;
using System.Text;
using System.Text.Json.Nodes;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

namespace Sts2Recorder.Game;

/// <summary>
/// Tiny primitive-field serializers shared by ActionPipeline / HumanFunnels /
/// EventTap. Everything is null-tolerant and never throws: combat history
/// entries hold live mutable model references, so payloads are snapshotted
/// immediately with best-effort reads.
/// </summary>
internal static class JsonDescribe
{
    /// <summary>PascalCase type/member name to snake_case ("CardPlayStarted" -> "card_play_started").</summary>
    public static string SnakeCase(string name)
    {
        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                {
                    sb.Append('_');
                }
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// <summary>Creature descriptor: model id, name, side, vitals. Null-safe.</summary>
    public static JsonNode? Creature(Creature? creature)
    {
        if (creature == null)
        {
            return null;
        }
        try
        {
            var obj = new JsonObject
            {
                ["is_player"] = creature.IsPlayer,
                ["combat_id"] = creature.CombatId,
            };
            obj["model_id"] = Try(() => creature.ModelId.ToString());
            obj["name"] = Try(() => creature.Name);
            if (creature.IsPlayer)
            {
                obj["player_id"] = Try<long?>(() => (long)creature.Player!.NetId);
            }
            obj["hp"] = Try<int?>(() => creature.CurrentHp);
            obj["max_hp"] = Try<int?>(() => creature.MaxHp);
            obj["block"] = Try<int?>(() => creature.Block);
            return obj;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Card descriptor (id only plus upgrade-agnostic entry name).</summary>
    public static JsonNode? Card(CardModel? card)
    {
        if (card == null)
        {
            return null;
        }
        return new JsonObject
        {
            ["id"] = Try(() => card.Id.ToString()),
            ["entry"] = Try(() => card.Id.Entry),
        };
    }

    /// <summary>Card play descriptor (card + target + play metadata).</summary>
    public static JsonNode? CardPlay(CardPlay? play)
    {
        if (play == null)
        {
            return null;
        }
        return new JsonObject
        {
            ["card"] = Try(() => Card(play.Card)),
            ["target"] = Try(() => Creature(play.Target)),
            ["is_auto_play"] = Try<bool?>(() => play.IsAutoPlay),
            ["play_index"] = Try<int?>(() => play.PlayIndex),
            ["play_count"] = Try<int?>(() => play.PlayCount),
            ["result_pile"] = Try(() => play.ResultPile.ToString()),
        };
    }

    /// <summary>Damage result descriptor.</summary>
    public static JsonNode? Damage(DamageResult? result)
    {
        if (result == null)
        {
            return null;
        }
        return new JsonObject
        {
            ["receiver"] = Try(() => Creature(result.Receiver)),
            ["blocked"] = Try<int?>(() => result.BlockedDamage),
            ["unblocked"] = Try<int?>(() => result.UnblockedDamage),
            ["overkill"] = Try<int?>(() => result.OverkillDamage),
            ["was_block_broken"] = Try<bool?>(() => result.WasBlockBroken),
            ["was_fully_blocked"] = Try<bool?>(() => result.WasFullyBlocked),
            ["was_target_killed"] = Try<bool?>(() => result.WasTargetKilled),
        };
    }

    /// <summary>Model id string for any AbstractModel; null-safe.</summary>
    public static string? Model(AbstractModel? model)
    {
        if (model == null)
        {
            return null;
        }
        try
        {
            return model.Id.ToString();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Runs a getter, converting any exception into null instead of propagating.</summary>
    public static T? Try<T>(Func<T?> getter)
    {
        try
        {
            return getter();
        }
        catch
        {
            return default;
        }
    }

    /// <summary>String-flavored Try (avoids generic inference noise at call sites).</summary>
    public static string? Try(Func<string?> getter)
    {
        try
        {
            return getter();
        }
        catch
        {
            return null;
        }
    }
}

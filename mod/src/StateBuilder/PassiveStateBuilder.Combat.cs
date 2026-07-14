// Ported from STS2MCP (https://github.com/Gennadiyev/STS2MCP) — McpMod.StateBuilder.cs (combat sections).
// Copyright 2026 Yikun Ji (Kunologist). MIT License; this attribution is retained per license.
// Sts2Recorder adaptations (game v0.99.1): CombatState is read passively off the local
// player's Creature.CombatState instead of CombatManager.DebugOnlyGetState() (debug-named,
// version-fragile — see docs/hook-map.md "Known risks" #6).

using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2Recorder.Game;

public static partial class PassiveStateBuilder
{
    private static Dictionary<string, object?> BuildBattleState(RunState runState, CombatRoom combatRoom)
    {
        // Read the CombatState passively via the local player's creature instead of
        // CombatManager.Instance.DebugOnlyGetState(). v0.107.1: the property is now the
        // ICombatState interface; everything read below is on the interface.
        var me = LocalContext.GetMe(runState);
        var combatState = me?.Creature.CombatState;
        var battle = new Dictionary<string, object?>();

        if (combatState == null)
        {
            battle["error"] = "Combat state unavailable";
            return battle;
        }

        battle["round"] = combatState.RoundNumber;
        battle["turn"] = combatState.CurrentSide.ToString().ToLowerInvariant();
        // v0.107.1: CombatManager.IsPlayPhase was removed in the global->per-player
        // refactor; the equivalent is the local player's turn phase.
        battle["is_play_phase"] = me?.PlayerCombatState?.Phase == PlayerTurnPhase.Play;
        battle["player_turn_number"] = me?.PlayerCombatState?.TurnNumber;

        // Enemies
        var enemies = new List<Dictionary<string, object?>>();
        var entityCounts = new Dictionary<string, int>();
        foreach (var creature in combatState.Enemies)
        {
            if (creature.IsAlive)
            {
                enemies.Add(BuildEnemyState(creature, entityCounts));
            }
        }
        battle["enemies"] = enemies;

        return battle;
    }

    private static Dictionary<string, object?> BuildEnemyState(Creature creature, Dictionary<string, int> entityCounts)
    {
        var monster = creature.Monster;
        string baseId = monster?.Id.Entry ?? "unknown";

        // Generate entity_id like "jaw_worm_0"
        if (!entityCounts.TryGetValue(baseId, out int count))
            count = 0;
        entityCounts[baseId] = count + 1;
        string entityId = $"{baseId}_{count}";

        var state = new Dictionary<string, object?>
        {
            ["entity_id"] = entityId,
            ["combat_id"] = creature.CombatId,
            ["name"] = SafeGetText(() => monster?.Title),
            ["hp"] = creature.CurrentHp,
            ["max_hp"] = creature.MaxHp,
            ["block"] = creature.Block,
            ["status"] = BuildPowersState(creature)
        };

        // Intents
        if (monster?.NextMove is MoveState moveState)
        {
            var intents = new List<Dictionary<string, object?>>();
            foreach (var intent in moveState.Intents)
            {
                var intentData = new Dictionary<string, object?>
                {
                    ["type"] = intent.IntentType.ToString()
                };
                try
                {
                    var targets = creature.CombatState?.PlayerCreatures;
                    if (targets != null)
                    {
                        string label = intent.GetIntentLabel(targets, creature).GetFormattedText();
                        intentData["label"] = StripRichTextTags(label);

                        var hoverTip = intent.GetHoverTip(targets, creature);
                        if (hoverTip.Title != null)
                            intentData["title"] = StripRichTextTags(hoverTip.Title);
                        if (hoverTip.Description != null)
                            intentData["description"] = StripRichTextTags(hoverTip.Description);
                    }
                }
                catch { /* intent label may fail for some types */ }
                intents.Add(intentData);
            }
            state["intents"] = intents;
        }

        return state;
    }

    private static Dictionary<string, object?> BuildHandSelectState(NPlayerHand hand, RunState runState)
    {
        var state = new Dictionary<string, object?>();

        // Mode
        state["mode"] = hand.CurrentMode switch
        {
            NPlayerHand.Mode.SimpleSelect => "simple_select",
            NPlayerHand.Mode.UpgradeSelect => "upgrade_select",
            _ => hand.CurrentMode.ToString()
        };

        // Prompt text from %SelectionHeader
        var headerLabel = hand.GetNodeOrNull<Godot.Control>("%SelectionHeader");
        if (headerLabel != null)
        {
            var textVariant = headerLabel.Get("text");
            string? prompt = textVariant.VariantType != Godot.Variant.Type.Nil
                ? StripRichTextTags(textVariant.AsString())
                : null;
            state["prompt"] = prompt;
        }

        // Selectable cards (visible holders in the hand)
        var selectableCards = new List<Dictionary<string, object?>>();
        int index = 0;
        foreach (var holder in hand.ActiveHolders)
        {
            var card = holder.CardModel;
            if (card == null) continue;

            var cardInfo = BuildCardInfo(card);
            cardInfo["index"] = index;
            cardInfo["description"] = SafeGetCardDescription(card); // hand cards use default pile
            selectableCards.Add(cardInfo);
            index++;
        }
        state["cards"] = selectableCards;

        // Already-selected cards (in the SelectedHandCardContainer)
        var selectedContainer = hand.GetNodeOrNull<Godot.Control>("%SelectedHandCardContainer");
        if (selectedContainer != null)
        {
            var selectedCards = new List<Dictionary<string, object?>>();
            var selectedHolders = FindAll<MegaCrit.Sts2.Core.Nodes.Cards.Holders.NSelectedHandCardHolder>(selectedContainer);
            int selIdx = 0;
            foreach (var holder in selectedHolders)
            {
                var card = holder.CardModel;
                if (card == null) continue;
                selectedCards.Add(new Dictionary<string, object?>
                {
                    ["index"] = selIdx,
                    ["name"] = SafeGetText(() => card.Title)
                });
                selIdx++;
            }
            if (selectedCards.Count > 0)
                state["selected_cards"] = selectedCards;
        }

        // Confirm button state (read-only)
        var confirmBtn = hand.GetNodeOrNull<MegaCrit.Sts2.Core.Nodes.CommonUi.NConfirmButton>("%SelectModeConfirmButton");
        state["can_confirm"] = confirmBtn?.IsEnabled ?? false;

        return state;
    }
}

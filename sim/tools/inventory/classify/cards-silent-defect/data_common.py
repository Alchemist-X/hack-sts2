ON_PLAY_CTX = "PlayCardAction GameActionPlayerChoiceContext (inline in the card play; caller context when auto-played)"

AUTO_HAND = ("no decision when eligible options <= MinSelect (all taken, Commands/CardSelectCmd.cs:3693); "
             "0 options -> empty; early-out when combat IsOverOrEnding (:3672)")
AUTO_PILE = ("no decision when eligible options <= MinSelect (all taken, Commands/CardSelectCmd.cs:3397); "
             "0 options -> empty; early-out when combat IsEnding (:3365)")


def prompt(kind, cmd, pile, mn, mx, *, key=None, filt=None, site="OnPlay", ctx=ON_PLAY_CTX, auto=None, cite=None,
           note=None, answer="ordered subset of hand cards"):
    d = {
        "type": kind,
        "cmd": cmd,
        "source_pile": pile,
        "min_select": mn,
        "max_select": mx,
        "filter": filt,
        "prompt_key": key,
        "site": site,
        "choice_context": ctx,
        "auto_resolution": auto if auto is not None else (AUTO_PILE if kind == "PileSelect" else AUTO_HAND),
        "answer_shape": answer,
    }
    if cite:
        d["cite"] = cite
    if note:
        d["note"] = note
    return d


def discard_prompt(n, *, site="OnPlay", ctx=ON_PLAY_CTX, note=None):
    return prompt("HandSelect", "CardSelectCmd.FromHandForDiscard", "Hand", n, n, key="DiscardSelectionPrompt",
                  site=site, ctx=ctx, cite="Commands/CardSelectCmd.cs:3726",
                  note=note or "FromHandForDiscard = FromHand plus a UI gold glow on Sly cards; the picks go to CardCmd.Discard")


ENUM = {
    "CombatTargets": "RunRngType.CombatTargets",
    "CombatOrbGeneration": "RunRngType.CombatOrbs",
    "CombatCardGeneration": "RunRngType.CombatCardGeneration",
    "Shuffle": "RunRngType.Shuffle",
}


def rng(stream, via, *, site="OnPlay", explicit=True, cite=None):
    d = {"stream": "RunRng." + stream, "enum": ENUM[stream], "site": site, "via": via, "explicit": explicit}
    if cite:
        d["cite"] = cite
    return d


K_SLY = "Sly keyword: auto-play after CardCmd discard (Commands/CardCmd.cs:843 collects IsSlyThisTurn, :917 AutoPlay(SlyDiscard))"
K_DISCARD = "CardCmd.Discard / DiscardAndDraw with the Sly auto-play pass (sim CardPileCmd.Discard throws on Sly today)"
K_AUTOPLAY = "CardCmd.AutoPlay (Commands/CardCmd.cs:1779; random AnyEnemy target via RunRng.CombatTargets when target is null)"
K_SHIV = "Shiv.CreateInHand -> CardPileCmd.AddGeneratedCardsToCombat (Models.Cards/Shiv.cs:355, Commands/CardPileCmd.cs:3204)"
K_UPGRADE = "CardCmd.Upgrade in combat (Commands/CardCmd.cs:1882; CardInstance.Upgrade exists, facade missing)"
K_CALC = "CalculatedVar family (CalculationBase + CalculationExtra x multiplier(card, target); Localization.DynamicVars/CalculatedVar.cs)"
K_CALC_DMG = "DamageCmd.Attack(CalculatedDamageVar) overload (Commands/DamageCmd.cs:13)"
K_ORBQ = "OrbQueue (capacity = Character.BaseOrbSlotCount per combat, max 10; Entities.Orbs/OrbQueue.cs)"
K_CHANNEL = "OrbCmd.Channel (Commands/OrbCmd.cs:684: add 1 slot if BaseOrbSlotCount==0 and capacity 0; EvokeNext when full; TryEnqueue; History.OrbChanneled; Hook.AfterOrbChanneled)"
K_EVOKE = "OrbCmd.EvokeNext (Commands/OrbCmd.cs:699; dequeue flag; Hook.AfterOrbEvoked)"
K_PASSIVE = "OrbCmd.Passive (Commands/OrbCmd.cs:745; countAffectedByHooks=false -> one Passive call, no ModifyOrbPassiveTriggerCount)"
K_ORBVAL = "Hook.ModifyOrbValue (FocusPower; Lightning/Frost/Glass values and Dark passive)"
K_FOCUS = "FocusPower (Models.Powers/FocusPower.cs: ModifyOrbValue value+Amount floored at 0, AllowNegative)"
K_TMPFOCUS = "TemporaryFocusPower base (BeforeApplied applies Sign x amount FocusPower silently, stacks re-apply the delta in AfterPowerAmountChanged, AfterSideTurnEnd removes and reverts; PowerModel.BeforeApplied exists in sim)"
K_TMPDEX = "TemporaryDexterityPower base (BeforeApplied applies Sign x amount DexterityPower silently, stacks via AfterPowerAmountChanged, AfterSideTurnEnd removes and reverts)"
K_TMPSTR = "TemporaryStrengthPower base (BeforeApplied applies Sign x amount StrengthPower silently, stacks via AfterPowerAmountChanged, AfterSideTurnEnd removes and reverts)"
K_PART = "side-turn participants on AfterSideTurnStart/AfterSideTurnEnd (sim reconstructs; W1 restores the parameter)"
K_HIST_PLAYS = "CombatHistory.CardPlaysFinished (CardPlayFinishedEntry, HappenedThisTurn, CardPlay.Player)"
K_LISTENER = "cards in combat piles as hook listeners (game listener order: ... orbs, then cards; sim iterates powers only)"
K_X = "X energy cost (HasEnergyCostX; ResolveEnergyXValue / CapturedXValue; Hook.ModifyXValue) - present in sim"
K_ALLCARDS = "PlayerCombatState.AllCards (all combat piles in pile order)"
K_STATUS_GEN = "CombatState.CreateCard<T> + CardPileCmd.AddGeneratedCardToCombat (ported) with History.CardGenerated and Hook.AfterCardGeneratedForCombat"
K_RESULTS = "AttackCommand.Results -> DamageResult.WasTargetKilled"
K_GAINBLOCK_RET = "CreatureCmd.GainBlock returns the block actually gained"
K_MP = "MultiplayerOnly: removed from single-player pools (Models/CardPoolModel.cs:68-81, Factories/CardFactory.cs:25-32)"

"""Per-overload status and scope of every Cmd facade: cmds.json + cmd_status.json -> cmds2.json.

`status` defaults to readiness.py's per-method status; the DamageVar/BlockVar overloads of the
three var-taking facades are missing in the sim while their decimal overloads are ported.
`scope` is the W0 manual reading of where each method is reachable (combat, run, mp, strip, infra, unused).
"""

import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
import common  # noqa: E402

VAR_OVERLOADS = {"CreatureCmd.Damage", "CreatureCmd.GainBlock", "DamageCmd.Attack"}
VAR_TYPES = ("DamageVar", "BlockVar")

SCOPE = {
    "AttackCommand.AddResultsInternal": "combat",
    "AttackCommand.AfterAttackerAnim": "strip",
    "AttackCommand.BeforeDamage": "combat",
    "AttackCommand.Execute": "combat",
    "AttackCommand.FromCard": "combat",
    "AttackCommand.FromMonster": "combat",
    "AttackCommand.FromOsty": "combat",
    "AttackCommand.IncrementHitsInternal": "combat",
    "AttackCommand.OnlyPlayAnimOnce": "strip",
    "AttackCommand.SpawningHitVfxOnEachCreature": "strip",
    "AttackCommand.Targeting": "combat",
    "AttackCommand.TargetingAllOpponents": "combat",
    "AttackCommand.TargetingRandomOpponents": "combat",
    "AttackCommand.Unpowered": "unused",
    "AttackCommand.WithAttackerAnim": "strip",
    "AttackCommand.WithAttackerFx": "strip",
    "AttackCommand.WithHitCount": "combat",
    "AttackCommand.WithHitFx": "strip",
    "AttackCommand.WithHitVfxNode": "strip",
    "AttackCommand.WithHitVfxSpawnedAtBase": "strip",
    "AttackCommand.WithNoAttackerAnim": "strip",
    "AttackCommand.WithWaitBeforeHit": "strip",
    "CardCmd.Afflict": "combat",
    "CardCmd.AfflictAndPreview": "combat",
    "CardCmd.ApplyKeyword": "combat",
    "CardCmd.ApplySingleTurnRetain": "combat",
    "CardCmd.ApplySingleTurnSly": "combat",
    "CardCmd.AutoPlay": "combat",
    "CardCmd.ClearAffliction": "combat",
    "CardCmd.ClearEnchantment": "unused",
    "CardCmd.Discard": "combat",
    "CardCmd.DiscardAndDraw": "combat",
    "CardCmd.Downgrade": "combat",
    "CardCmd.Enchant": "combat",
    "CardCmd.Exhaust": "combat",
    "CardCmd.Preview": "strip",
    "CardCmd.PreviewCardPileAdd": "strip",
    "CardCmd.RemoveKeyword": "combat",
    "CardCmd.Transform": "combat",
    "CardCmd.TransformTo": "combat",
    "CardCmd.TransformToRandom": "combat",
    "CardCmd.Upgrade": "combat",
    "CardPileCmd.Add": "combat",
    "CardPileCmd.AddCurseToDeck": "combat",
    "CardPileCmd.AddCursesToDeck": "run",
    "CardPileCmd.AddDuringManualCardPlay": "combat",
    "CardPileCmd.AddGeneratedCardToCombat": "combat",
    "CardPileCmd.AddGeneratedCardsToCombat": "combat",
    "CardPileCmd.AddToCombatAndPreview": "combat",
    "CardPileCmd.AutoPlayFromDrawPile": "combat",
    "CardPileCmd.Draw": "combat",
    "CardPileCmd.DrawWithoutBlockingOnOtherPlayers": "combat (mp variant)",
    "CardPileCmd.GetTweenForCardsChangingPiles": "strip",
    "CardPileCmd.GiveToAnotherPlayer": "mp",
    "CardPileCmd.RemoveFromCombat": "combat",
    "CardPileCmd.RemoveFromDeck": "combat",
    "CardPileCmd.Shuffle": "combat",
    "CardPileCmd.ShuffleIfNecessary": "combat",
    "CardSelectCmd.FromChooseABundleScreen": "run",
    "CardSelectCmd.FromChooseACardScreen": "combat",
    "CardSelectCmd.FromCombatPile": "combat",
    "CardSelectCmd.FromDeckForEnchantment": "run",
    "CardSelectCmd.FromDeckForRemoval": "run",
    "CardSelectCmd.FromDeckForTransformation": "run",
    "CardSelectCmd.FromDeckForUpgrade": "run",
    "CardSelectCmd.FromDeckGeneric": "run",
    "CardSelectCmd.FromHand": "combat",
    "CardSelectCmd.FromHandForDiscard": "combat",
    "CardSelectCmd.FromHandForUpgrade": "combat",
    "CardSelectCmd.FromSimpleGrid": "combat",
    "CardSelectCmd.FromSimpleGridForRewards": "run",
    "CardSelectCmd.PushSelector": "combat (WhisperingEarring)",
    "CardSelectCmd.Reset": "infra",
    "CardSelectCmd.SuspendSelectorForTest": "infra (test seam)",
    "CardSelectCmd.UseSelector": "infra (test seam)",
    "Cmd.CustomScaledWait": "strip",
    "Cmd.Wait": "strip",
    "CreatureCmd.Add": "combat",
    "CreatureCmd.Damage": "combat",
    "CreatureCmd.Escape": "combat",
    "CreatureCmd.GainBlock": "combat",
    "CreatureCmd.GainMaxHp": "combat",
    "CreatureCmd.Heal": "combat",
    "CreatureCmd.Kill": "combat",
    "CreatureCmd.LoseBlock": "combat",
    "CreatureCmd.LoseMaxHp": "combat",
    "CreatureCmd.SetCurrentHp": "combat",
    "CreatureCmd.SetMaxAndCurrentHp": "combat",
    "CreatureCmd.SetMaxHp": "combat",
    "CreatureCmd.Stun": "combat",
    "CreatureCmd.TriggerAnim": "strip",
    "DamageCmd.Attack": "combat",
    "ForgeCmd.Forge": "combat",
    "ForgeCmd.PlayCombatRoomForgeVfx": "strip",
    "MapCmd.SetBossEncounter": "run",
    "OrbCmd.AddSlots": "combat",
    "OrbCmd.Channel": "combat",
    "OrbCmd.EvokeLast": "combat",
    "OrbCmd.EvokeNext": "combat",
    "OrbCmd.Passive": "combat",
    "OrbCmd.RemoveSlots": "combat",
    "OstyCmd.Summon": "combat",
    "PlayerCmd.AddPet": "combat",
    "PlayerCmd.CompleteQuest": "combat",
    "PlayerCmd.EndTurn": "combat",
    "PlayerCmd.GainEnergy": "combat",
    "PlayerCmd.GainGold": "combat",
    "PlayerCmd.GainMaxPotionCount": "run",
    "PlayerCmd.GainStars": "combat",
    "PlayerCmd.LoseEnergy": "combat",
    "PlayerCmd.LoseGold": "combat",
    "PlayerCmd.LoseMaxPotionCount": "unused",
    "PlayerCmd.LoseStars": "unused",
    "PlayerCmd.MimicRestSiteHeal": "run",
    "PlayerCmd.SetEnergy": "combat",
    "PlayerCmd.SetGold": "unused",
    "PlayerCmd.SetStars": "combat (mp death path)",
    "PotionCmd.Discard": "combat",
    "PotionCmd.TryToProcure": "combat",
    "PowerCmd.Apply": "combat",
    "PowerCmd.Decrement": "combat",
    "PowerCmd.FindExistingInstanceForStacking": "combat",
    "PowerCmd.ModifyAmount": "combat",
    "PowerCmd.Remove": "combat",
    "PowerCmd.TickDownDuration": "combat",
    "RelicCmd.Melt": "combat-capable (verify)",
    "RelicCmd.Obtain": "run",
    "RelicCmd.Remove": "run",
    "RelicCmd.Replace": "run",
    "RelicSelectCmd.FromChooseARelicScreen": "unused",
    "RewardsCmd.GenerateCustom": "run",
    "RewardsCmd.GenerateForRoomEnd": "run",
    "RewardsCmd.OfferCustom": "run",
    "RewardsCmd.OfferForRoomEnd": "run",
    "SfxCmd.Play": "strip",
    "SfxCmd.PlayCardSwooshSfx": "strip",
    "SfxCmd.PlayDamage": "strip",
    "SfxCmd.PlayDeath": "strip",
    "SfxCmd.PlayLoop": "strip",
    "SfxCmd.SetParam": "strip",
    "SfxCmd.StopLoop": "strip",
    "TalkCmd.Play": "strip",
    "ThinkCmd.Play": "strip",
    "VfxCmd.GetSideCenter": "strip",
    "VfxCmd.GetSideCenterFloor": "strip",
    "VfxCmd.PlayFullScreenInCombat": "strip",
    "VfxCmd.PlayNonCombatVfx": "strip",
    "VfxCmd.PlayOnCreature": "strip",
    "VfxCmd.PlayOnCreatureCenter": "strip",
    "VfxCmd.PlayOnCreatureCenters": "strip",
    "VfxCmd.PlayOnCreatures": "strip",
    "VfxCmd.PlayOnSide": "strip",
    "VfxCmd.PlayVfx": "strip",
}


def main():
    cmds = common.load_json(common.work_path("kernel", "cmds.json"))
    status = common.load_json(common.work_path("kernel", "cmd_status.json"))
    out = {}
    for c, rows in cmds.items():
        lst = []
        for r in rows:
            key = c + "." + r["name"]
            st = status[key]
            if key in VAR_OVERLOADS:
                st = "missing" if any(t in r["params"] for t in VAR_TYPES) else "ported"
            row = dict(r)
            row["scope"] = SCOPE[key]
            row["status"] = st
            lst.append(row)
        out[c] = lst
    with open(common.work_path("kernel", "cmds2.json"), "w") as fh:
        json.dump(out, fh, indent=1)


if __name__ == "__main__":
    main()

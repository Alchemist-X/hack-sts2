"""Native replay.mcr statistics for corpus-inventory.json -> native.replay_mcr (aggregate counts only).

`parts`, `by_header_version` and `size_bytes` are recomputed from parts.json. The coverage fields
(`unique_combats_covered*`, `share_of_unique_combats`) and the three descriptive fields are the W0
diff-gate reading (2026-11-01) and are carried as constants: the coverage count came from a manual
decode of the archived replays that is not part of this generator.
"""

import collections
import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
import common  # noqa: E402

W0_COVERAGE = {
    "unique_combats_covered": 708,
    "unique_combats_covered_by_version": {"v0.107.1": 604, "v0.110.1": 41, "v0.111.0": 63},
    "share_of_unique_combats": "708 of 1887 (37.5 %)",
}
WHICH_COMBAT = (
    "the last combat won inside that part: RunLifecycle.WriteReplayPostfix copies replays/latest.mcr with File.Copy(overwrite: true) "
    "after every CombatReplayWriter.WriteReplay while a recorder session is open; the game writes it on the combat-win path "
    "(Combat/CombatManager.cs:1763); its other write, in RunManager.CleanUp (Runs/RunManager.cs:4653-4656), runs after the "
    "recorder's CleanUp prefix has closed the session, so it is never archived")
CONTENTS = (
    "PacketWriter bit stream (Multiplayer.Replay/CombatReplay.cs:33-76): version, gitCommit, modelIdHash, choiceIds, rewardIds, "
    "nextActionId/nextChecksumId/nextHookId, serializableRun = RunManager.ToSave(null) captured by "
    "CombatReplayWriter.RecordInitialState right after the map-point save and before the room roll (Runs/RunManager.cs:615-618 "
    "save, :669-672 RecordInitialState, :681-683 RollRoomTypeFor + CreateRoom), i.e. the same state as that save, events "
    "(GameAction NetActions, hook actions, player-choice results, action resumes), checksumData (NetFullCombatState per "
    "checksum: creatures, piles in order, potions, relics, orbs, player and run RNG sets)")
DECODING = (
    "port PacketReader plus every IPacketSerializable on the path (SerializableRun and nested types, NetAction subtypes, "
    "NetPlayerChoiceResult, NetFullCombatState) per game version, and rebuild ModelIdSerializationCache net ids (ContentSorter "
    "order over ModelDb.All, epochs, saved-property names; Multiplayer.Serialization/ModelIdSerializationCache.cs:95-140) "
    "checked against the header modelIdHash")


def main():
    parts = json.load(open(common.work_path("corpus", "parts.json")))
    withr = [p for p in parts if "replay.mcr" in (p.get("native") or {})]
    sizes = [p["native"]["replay.mcr"] for p in withr]
    by_version = collections.Counter(p["manifest"]["game_version"] for p in withr)
    out = collections.OrderedDict()
    out["parts"] = len(withr)
    out["by_header_version"] = dict(sorted(by_version.items()))
    out["unique_combats_covered"] = W0_COVERAGE["unique_combats_covered"]
    out["unique_combats_covered_by_version"] = W0_COVERAGE["unique_combats_covered_by_version"]
    out["share_of_unique_combats"] = W0_COVERAGE["share_of_unique_combats"]
    out["size_bytes"] = {"mean": int(round(sum(sizes) / len(sizes))) if sizes else 0, "max": max(sizes) if sizes else 0}
    out["which_combat"] = WHICH_COMBAT
    out["contents"] = CONTENTS
    out["decoding_requirements"] = DECODING
    with open(common.work_path("corpus", "replay_stats.json"), "w") as fh:
        json.dump(out, fh, indent=1)


if __name__ == "__main__":
    main()

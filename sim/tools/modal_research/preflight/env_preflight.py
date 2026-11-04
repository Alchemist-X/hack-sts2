"""Bounded native environment preflight with a uniform legal policy."""

import hashlib
import json
import platform
import resource
import time
from collections import Counter
from pathlib import Path

import numpy as np

from sts2train.seedstream import SeedStream, StreamEnv
from sts2vec import STATUS, VecEnv, status_counts

NAMESPACE = "MODAL-SILENT-A10-ENV-PREFLIGHT"
STREAM_PREFIX = "MODA10ENVPREFLIGHT"
EXPECTED_ENGINE_SHA256 = "2d035a9be243fd40b9cef407d31fb46c12b6ca42ff8e39a6f95f15042fa984ce"
EXPECTED_ENCODER = "3b73317739c650bd"
ENVS, STEPS, STEP_CAP, FLOOR_CAP = 8, 64, 8, 80


def require(condition, reason):
    if not condition:
        raise RuntimeError(reason)


def observation_digest(observation, slots):
    digest = hashlib.sha256()
    for key, values in sorted(observation.items()):
        digest.update(key.encode("utf-8"))
        digest.update(np.ascontiguousarray(values[slots]).tobytes())
    return digest.hexdigest()


class CaptureBeforeReset(VecEnv):
    """Observe the native cap rows before StreamEnv has a chance to restore."""

    def step(self, actions, *, copy=False):
        obs, rewards, term, trunc, status, info = super().step(actions, copy=copy)
        self.before_restore_slots = {int(slot): observation_digest(obs, [slot]) for slot in np.flatnonzero(trunc)}
        return obs, rewards, term, trunc, status, info


def check_public_start(obs, schema, slots):
    col = schema.cols("global_i")
    flags = obs["global_i"][slots]
    silent = schema.vocab.characters.index_of("SILENT")
    require(np.all(flags[:, col["character"]] == silent), "character is not SILENT")
    require(np.all(flags[:, col["ascension"]] == 10), "ascension is not A10")


def run(out):
    so = Path(__file__).resolve().parent / "lib/Sts2Sim.Vec.so"
    with so.open("rb") as binary:
        engine_hash = hashlib.file_digest(binary, "sha256").hexdigest()
    require(engine_hash == EXPECTED_ENGINE_SHA256, "native library hash mismatch")
    out["engine_sha256"] = engine_hash
    rng = np.random.default_rng(783104)
    stream = SeedStream(STREAM_PREFIX, ("SILENT",), 10)
    with CaptureBeforeReset(ENVS, threads=1, step_cap=STEP_CAP, floor_step_cap=FLOOR_CAP,
                            auto_reset=False, seed_text=STREAM_PREFIX, content="ported_only",
                            pool=None, pool_cache=16, dll=so) as raw:
        schema = raw.schema
        out.update(abi_version=raw.abi_version, native_version=raw.lib.version(),
                   encoder_version=schema.encoder_version,
                   schema_sha256=hashlib.sha256(raw.lib.schema_json().encode("utf-8")).hexdigest(),
                   candidate_capacity=schema["cand"].shape[0], prompt_capacity=schema["prompt_options"].shape[0])
        require(raw.lib.mode == "native" and raw.abi_version == 2, "expected NativeAOT ABI 2")
        require(raw.supports_restore_many and raw.supports_pool_release, "stream ABI functions unavailable")
        require(schema.encoder_version == EXPECTED_ENCODER and schema.verify_version(), "encoder schema mismatch")
        require((out["candidate_capacity"], out["prompt_capacity"]) == (192, 128), "unexpected candidate capacities")
        env = StreamEnv(raw, stream, lookahead=0, prefetch=False)
        obs = env.reset()
        check_public_start(obs, schema, np.arange(ENVS))
        counts = Counter()
        reasons = Counter()
        out.update(status_counts=counts, ending_reasons=reasons)
        prior_status = raw.status.copy()
        final_digest = hashlib.sha256()
        prompt_col = schema.cols("global_i")["prompt_option_n"]
        for _ in range(STEPS):
            cn = obs["cand_n"].copy()
            require(np.all((cn > 0) & (cn <= 192)), "invalid legal candidate count")
            prompt_n = obs["global_i"][:, prompt_col]
            out["decisions"] += ENVS
            out["candidate_over_64"] += int(np.count_nonzero(cn > 64))
            out["prompt_over_64"] += int(np.count_nonzero(prompt_n > 64))
            out["native_candidate_overflow"] += int(np.count_nonzero(prior_status & STATUS["CANDIDATE_OVERFLOW"]))
            out["native_table_overflow"] += int(np.count_nonzero(prior_status & STATUS["TABLE_OVERFLOW"]))
            actions = rng.integers(cn, dtype=np.int32)
            before_ids = raw.info(copy=True)["episode_id"]
            obs, _, term, trunc, status, info = env.step(actions, capture_final=True)
            counts.update(status_counts(status))
            done = (term | trunc) != 0
            expected_caps = []
            for slot in np.flatnonzero(done):
                record = raw.last_episode(int(slot))
                kind = record["end_kind"]
                reasons[kind] += 1
                if kind == "Truncated" and not status[slot] & (STATUS["SIM_ERROR"] | STATUS["CONTENT_GAP"]):
                    require(trunc[slot] and not term[slot] and record["steps"] == STEP_CAP, "inconsistent step cap")
                    expected_caps.append(int(slot))
                if kind == "Terminal":
                    require(term[slot] and not trunc[slot], "inconsistent game terminal")
                if kind == "FloorStall":
                    require(trunc[slot] and status[slot] & STATUS["FLOOR_STALL"], "inconsistent floor stall")
            slots, final = info["final_observation"]
            require(slots.tolist() == expected_caps, "wrong slots captured as step caps")
            for row, slot in enumerate(slots):
                digest = observation_digest(final, [row])
                require(digest == raw.before_restore_slots[int(slot)], "final observation changed during reset")
                require(info["episode_id"][slot] > before_ids[slot] and info["step_index"][slot] == 0,
                        "ended slot did not reset to an independent episode")
                final_digest.update(bytes.fromhex(digest))
                out["verified_final_observations"] += 1
            check_public_start(obs, schema, np.flatnonzero(done))
            out["errors"].extend(f"slot {i}: {message[:250]}" for i, message in env.slot_errors().items())
            out["start_failures"] += len(env.pop_failed())
            bad = STATUS["SIM_ERROR"] | STATUS["CONTENT_GAP"] | STATUS["ILLEGAL_ACTION"] | STATUS["NOOP"]
            require(not np.any(status & bad) and out["start_failures"] == 0, "unexpected failed or illegal transition")
            prior_status = raw.status.copy()
        out.update(seed_stream=stream.state_dict(), final_observations_sha256=final_digest.hexdigest())
        require(out["verified_final_observations"] > 0, "no external caps were verified")


def main():
    started = time.monotonic()
    report = dict(namespace=NAMESPACE, python=platform.python_version(), numpy=np.__version__,
                  platform=platform.machine(), envs=ENVS, decision_limit_per_slot=STEPS, threads=1,
                  step_cap=STEP_CAP, floor_cap=FLOOR_CAP, decisions=0, candidate_over_64=0, prompt_over_64=0,
                  native_candidate_overflow=0, native_table_overflow=0, verified_final_observations=0,
                  start_failures=0, errors=[])
    code = 0
    try:
        run(report)
        report["passed"] = True
    except Exception as exc:
        report.update(passed=False, failure=f"{type(exc).__name__}: {exc}")
        code = 1
    report.update(wall_s=round(time.monotonic() - started, 3),
                  peak_rss_kib=resource.getrusage(resource.RUSAGE_SELF).ru_maxrss)
    print(json.dumps(report, sort_keys=True))
    return code


if __name__ == "__main__":
    raise SystemExit(main())

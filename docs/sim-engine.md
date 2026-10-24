# Offline `sim` engine (spire-mentor)

`scripts/bench/sim_engine.py` runs the benchmark against
[tfriedel/spire-mentor](https://github.com/tfriedel/spire-mentor) instead of a live game.
spire-mentor is a Rust port of the Slay the Spire 2 run loop, with PyO3 bindings (`sts2_rs`).
It uses the same observation and ref contract as `run_bench.py`, so `text_state` plus
`llm_policy` (or any `policies.load_policy` spec) can play full runs offline. Each run
takes milliseconds of engine time and needs no game instance, pool or port.

* **External dependency, not vendored.** It is pinned at commit
  `386d760b7bffd5d29ba50578d8b1286c9359285b` (`sim_engine.SPIRE_MENTOR_COMMIT`).
  It is MIT-licensed, © Thomas Friedel. None of its code lives in this repo.
* Its default game version is `V0_107_1` (v0.111.0 is a beta flag). That matches our live
  build, v0.107.1.

## Build (once per box)

```bash
curl https://sh.rustup.rs -sSf | sh -s -- -y        # stable Rust; tested with 1.96
git clone https://github.com/tfriedel/spire-mentor /path/to/spire-mentor
cd /path/to/spire-mentor && git checkout 386d760b7bffd5d29ba50578d8b1286c9359285b
python3 -m venv /path/to/simvenv && /path/to/simvenv/bin/pip install maturin
cd sts2-rs/crates/sts2-py && /path/to/simvenv/bin/maturin build --release   # ~6.5 min
/path/to/simvenv/bin/pip install ../../target/wheels/sts2_rs-*.whl
```

If `sts2_rs` lives in another environment, point `STS2_SIM_PATH` at its site-packages,
or run `sim_engine.py` with that venv's python (`STS2_SIM_PYTHON` / `--sim-python`).
The adapter only needs the stdlib plus this repo's `scripts/`.

## Usage

```bash
# one policy, any seeds (no pool/ports involved)
/path/to/simvenv/bin/python scripts/bench/sim_engine.py --policy stub \
    --seeds BENCHA10S00,BENCHA10S01 --ascension 10 --out /tmp/sim-run
python3 scripts/bench/score.py /tmp/sim-run

# LLM comparison (same LLM_* env as the live path)
python3 scripts/bench/compare_models.py --engine sim --sim-python /path/to/simvenv/bin/python \
    --models grok-4.3,grok-4.7 --seeds BENCHA0S01,BENCHA0S02 --out /tmp/sim-compare
```

The output layout matches `run_bench.py`: `<out>/<seed>/trajectory.jsonl`, `result.json`
and `manifest.json`. That means `score.py` and `compare_models` summaries work unchanged.
Results carry `engine: "sim"` and `source: "sim"`. Decision records also store the raw
`sim_action` that was played. `--log-observations` additionally dumps every observation.

## Verification (2026-10-24)

* **Tests:** at the pin, `cargo nextest` over the workspace (excluding the PyO3 crate)
  gave 8179 passed, 19 skipped, 0 failed. The C# oracle parity suite was not run: it needs
  the game, .NET 9 and decompiled source.
* **Seed parity with live v0.107.1:** we compared BENCHA0S01, A0, against
  `bench2-v2-determinism-BENCHA0S01`. Everything matched exactly:
  * the Neow options (Nutritious Oyster / Phial Holster / Cursed Pearl);
  * all 50 act-1 map nodes;
  * the first fight (Nibbit 44/44, BUTT, Attack 12);
  * the opening hand order and the draw-pile multiset;
  * HP 91/91 and gold 99.

  Our seed strings therefore map to the same RNG.
* **Upstream parity claims** (README and `tests/parity/`):
  * Ironclad: 6,104/6,104 fuzzed fights clean.
  * Other characters: 99.4–99.8%.
  * Whole runs: 1,232/1,250 clean.
  * Known divergences are listed in `tests/parity/known_divergences.md`.

  The audit doc the README cites is missing from the public repo. One ignored test notes that
  boss-ancient offers are anchored to a v0.103.2 corpus. Treat late-run ancient offers, and
  non-Ironclad characters, as less certain.
* **Speed:**
  * The Rust random-fight bench runs about 63k fights/s, about 1.4M decisions/s.
  * Through Python, `take_action` runs about 19k/s.
  * This adapter does about 3k decisions/s including text encoding. A stub run finishes in
    about 0.2 s, so LLM latency dominates completely.
* **Full A10 runs:** Ironclad A10 runs complete from a seed without errors. Upstream's
  `HeuristicRunAgent` (1000 sims) won none of BENCHA10S00–09, dying on floors 10–31.
* **Adapter fuzzing:** 60 seeds with the stub/random policies, plus 12 heuristic-driven deep runs
  reaching acts 2–3 (boss relics, shops, events, deck ops, potion swaps). Both gave 0 adapter errors.
  Every legal ref round-trips through `resolve`.

## Caveats

* Sim results are a fast proxy. Headline numbers still come from the live game.
* Reported run-level fields (floor, act, boss kills, HP at boss entry, death cause) are derived
  from the sim's state, not from a game save (`source: "sim"`).
* Some refs differ from the live mod where the sim exposes different choices. Examples are
  `choose_boss_relic`, explicit `potion_swap`, and deck-op `card_select` screens. All of them
  appear in `legal_actions` as usual.

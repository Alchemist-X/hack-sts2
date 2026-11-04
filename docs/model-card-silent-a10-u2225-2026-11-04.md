# Model card: Silent A10 policy `best.pt` (weights `d59f9ea2095799c0`)

sts2train PPO checkpoint at update 2,225 (291,635,200 env steps). Scores are the STS2-Bench protocol v0.2 metric on the simulator (sim: sts2vec simulator, not the real engine). The policy is the checkpoint's greedy argmax with the loop breaker (`sts2train eval`), protocol limits (step cap 1200, floor step cap 80); baselines are `sts2vec eval` rule policies (seed 0) on the same games.

Generated 2026-11-04T14:08:59+0000 by `sts2train report-card` (repo `None`, library `sts2vec/3b73317739c650bd`, ABI 2).

## Results at a glance

| seed set | policy | games | progress [95% CI] | win rate [Wilson 95%] | reached the Act-1 boss | reached Act 2 | reached Act 3 | floor mean | replay |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| silent-a10-v1 | **NN greedy** | 20 | 0.427 [0.351, 0.503] | 0/20 [0.0%, 16.1%] | 17/20 [64.0%, 94.8%] | 12/20 [38.7%, 78.1%] | 1/20 [0.9%, 23.6%] | 22.4 | 20/20 |
| silent-a10-dev-v1 | **NN greedy** | 128 | 0.372 [0.348, 0.396] | 0/128 [0.0%, 2.9%] | 103/128 [72.8%, 86.4%] | 57/128 [36.2%, 53.2%] | 0/128 [0.0%, 2.9%] | 19.8 | 128/128 |
| silent-a10-test-v1 | **NN greedy** | 100 | 0.346 [0.316, 0.377] | 0/100 [0.0%, 3.7%] | 69/100 [59.4%, 77.2%] | 44/100 [34.7%, 53.8%] | 1/100 [0.2%, 5.4%] | 18.5 | 100/100 |

## Checkpoint

| field | value |
| --- | --- |
| file | `best.pt` (33.4 MiB, with optimizer state: resumable) |
| sha256 | `935bb52c659bcb8b4e58e0e90e7b448e039b1fc43c6d00023818b56404b26253` |
| weights_hash | `d59f9ea2095799c0` (recomputed from the stored weights: matches) |
| update / env steps / episodes | 2,225 / 291,635,200 / 1,492,418 (training wins 26,575, 5 sessions, 5.68 h) |
| saved at | 2026-11-04T13:12:07+00:00 |
| encoder_version | checkpoint `3b73317739c650bd`, library `3b73317739c650bd`: model inputs identical |
| architecture | custom preset: d_model 192, state 384, depth 3, heads 6, ff_mult 4; deck_counts on |
| aux heads | combat_hp_pred, win_prob |
| parameters | 2,904,208 |
| training-time best_eval | update 1900, silent-a10-v1: progress 0.413 [0.340, 0.487], wins 0/20, weights `1ae4c556e7ae91ba` |
| training-time last_eval | update 2200, silent-a10-v1: progress 0.344 [0.275, 0.413], wins 0/20, weights `77f5189a17f54d47` |

Training flags (the checkpoint's config; `*` = not recorded, the trainer predates the flag: its default):

| group | flags |
| --- | --- |
| objective | objective protocol, win_bonus 1.0, acq_shaping on |
| starts | characters SILENT, ascension 10, pool 4096, start_saves /content/sts2/sts2train-9f6608701d/saves, save_mix 0.35, save_copies determinized |
| PPO | envs 2048, rollout 64, epochs 1, minibatch 4096, lr 0.0003, gamma 1.0, lam 0.99, clip 0.2, vf_coef 0.5, ent_coef 0.02, max_grad_norm 0.5, max_rows 64 |
| model flags | aux_heads on, aux_coef 0.1, deck_counts on |
| rollouts | rollout_loop_breaker off, step_cap 3000, floor_step_cap 80 |
| runtime | device cuda, amp on, compile default, seed 0, max_hours 1.4 |
| training evals | eval_seedset silent-a10-v1, eval_every 25, loop_breaker on |

## silent-a10-v1: A10, 20 games

Seed set `silent-a10-v1` (`silent-a10-s20`, role target, sha256 `e82165b0a2e86c57`, frozen).

| policy | progress mean [95% CI] | median | win rate [Wilson 95%] | floor mean / median / max | results | steps mean | replay |
| --- | --- | --- | --- | --- | --- | --- | --- |
| NN greedy | 0.427 [0.351, 0.503] | 0.438 | 0.0% [0.0%, 16.1%] | 22.4 / 23.0 / 43 | loss 20 | 362.4 | 20/20 |

- NN greedy: loop breaker on (k 3): 36 loop breaks in 15 games, 0 no-op actions, 36 cycle revisits

### Staged results (count / N [Wilson 95%])

| stage | NN greedy |
| --- | --- |
| reached the Act-1 boss | 17/20 [64.0%, 94.8%] |
| beat the Act-1 boss | 12/20 [38.7%, 78.1%] |
| reached Act 2 | 12/20 [38.7%, 78.1%] |
| reached the Act-2 boss | 3/20 [5.2%, 36.0%] |
| beat the Act-2 boss | 1/20 [0.9%, 23.6%] |
| reached Act 3 | 1/20 [0.9%, 23.6%] |
| cleared (won the run) | 0/20 [0.0%, 16.1%] |

### Floor reached

| act | floor | NN greedy |
| --- | --- | --- |
| 1 | 8 | 1 ###### |
| 1 | 9 | 1 ###### |
| 1 | 14 | 1 ###### |
| 1 | 17 | 5 ############################## |
| 2 | 22 | 1 ###### |
| 2 | 23 | 3 ################## |
| 2 | 24 | 2 ############ |
| 2 | 27 | 2 ############ |
| 2 | 30 | 1 ###### |
| 2 | 33 | 2 ############ |
| 3 | 43 | 1 ###### |

### How the games ended

| ending | NN greedy |
| --- | --- |
| died: boss | 7 (35%) |
| died: elite | 4 (20%) |
| died: normal fight | 9 (45%) |

- NN greedy: died: boss: LAGAVULIN_MATRIARCH_BOSS 2, CEREMONIAL_BEAST_BOSS 1, KNOWLEDGE_DEMON_BOSS 1, THE_INSATIABLE_BOSS 1, SOUL_FYSH_BOSS 1, WATERFALL_GIANT_BOSS 1; died: elite: ENTOMANCER_ELITE 2, KNIGHTS_ELITE 1, INFESTED_PRISMS_ELITE 1; died: normal fight: THE_OBSCURA_NORMAL 2, MYTES_NORMAL 1, LIVING_FOG_NORMAL 1, NIBBITS_NORMAL 1, EXOSKELETONS_NORMAL 1, CORPSE_SLUGS_NORMAL 1 (+2 more)

### Win-probability head (calibration hint)

| policy | games | mean win_prob, first decision | observed win rate | mean win_prob, last decision: lost games (before death) | won games | other |
| --- | --- | --- | --- | --- | --- | --- |
| NN greedy | 20 | 0.000 | 0.0% | 0.001 (20) | - | - |

## silent-a10-dev-v1: A10, 128 games

Seed set `silent-a10-dev-v1` (`silent-a10-dev-s128`, role dev, sha256 `7fada5746894c95e`, frozen).

| policy | progress mean [95% CI] | median | win rate [Wilson 95%] | floor mean / median / max | results | steps mean | replay |
| --- | --- | --- | --- | --- | --- | --- | --- |
| NN greedy | 0.372 [0.348, 0.396] | 0.314 | 0.0% [0.0%, 2.9%] | 19.8 / 17.0 / 33 | loss 128 | 311.5 | 128/128 |

- NN greedy: loop breaker on (k 3): 177 loop breaks in 94 games, 0 no-op actions, 177 cycle revisits

### Staged results (count / N [Wilson 95%])

| stage | NN greedy |
| --- | --- |
| reached the Act-1 boss | 103/128 [72.8%, 86.4%] |
| beat the Act-1 boss | 57/128 [36.2%, 53.2%] |
| reached Act 2 | 57/128 [36.2%, 53.2%] |
| reached the Act-2 boss | 8/128 [3.2%, 11.8%] |
| beat the Act-2 boss | 0/128 [0.0%, 2.9%] |
| reached Act 3 | 0/128 [0.0%, 2.9%] |
| cleared (won the run) | 0/128 [0.0%, 2.9%] |

### Floor reached

| act | floor | NN greedy |
| --- | --- | --- |
| 1 | 7 | 4 ### |
| 1 | 8 | 4 ### |
| 1 | 9 | 6 #### |
| 1 | 12 | 3 ## |
| 1 | 14 | 6 #### |
| 1 | 15 | 2 # |
| 1 | 17 | 46 ############################## |
| 2 | 21 | 6 #### |
| 2 | 22 | 6 #### |
| 2 | 23 | 6 #### |
| 2 | 24 | 9 ###### |
| 2 | 25 | 6 #### |
| 2 | 27 | 8 ##### |
| 2 | 29 | 2 # |
| 2 | 30 | 3 ## |
| 2 | 31 | 3 ## |
| 2 | 33 | 8 ##### |

### How the games ended

| ending | NN greedy |
| --- | --- |
| died: boss | 54 (42%) |
| died: elite | 37 (29%) |
| died: normal fight | 37 (29%) |

- NN greedy: died: boss: LAGAVULIN_MATRIARCH_BOSS 9, WATERFALL_GIANT_BOSS 9, CEREMONIAL_BEAST_BOSS 9, THE_KIN_BOSS 8, SOUL_FYSH_BOSS 7, VANTOM_BOSS 4 (+3 more); died: elite: DECIMILLIPEDE_ELITE 10, BYGONE_EFFIGY_ELITE 7, INFESTED_PRISMS_ELITE 6, TERROR_EEL_ELITE 5, ENTOMANCER_ELITE 3, PHANTASMAL_GARDENERS_ELITE 2 (+3 more); died: normal fight: OVICOPTER_NORMAL 6, THE_OBSCURA_NORMAL 5, SPINY_TOAD_NORMAL 5, SLUMBERING_BEETLE_NORMAL 4, CHOMPERS_NORMAL 3, LOUSE_PROGENITOR_NORMAL 2 (+10 more)

### Win-probability head (calibration hint)

| policy | games | mean win_prob, first decision | observed win rate | mean win_prob, last decision: lost games (before death) | won games | other |
| --- | --- | --- | --- | --- | --- | --- |
| NN greedy | 128 | 0.000 | 0.0% | 0.000 (128) | - | - |

## silent-a10-test-v1: A10, 100 games

Seed set `silent-a10-test-v1` (`silent-a10-test-s100`, role test, sha256 `2ce58e94585257f0`, frozen).

| policy | progress mean [95% CI] | median | win rate [Wilson 95%] | floor mean / median / max | results | steps mean | replay |
| --- | --- | --- | --- | --- | --- | --- | --- |
| NN greedy | 0.346 [0.316, 0.377] | 0.314 | 0.0% [0.0%, 3.7%] | 18.5 / 17.0 / 40 | loss 100 | 299.1 | 100/100 |

- NN greedy: loop breaker on (k 3): 116 loop breaks in 62 games, 0 no-op actions, 116 cycle revisits

### Staged results (count / N [Wilson 95%])

| stage | NN greedy |
| --- | --- |
| reached the Act-1 boss | 69/100 [59.4%, 77.2%] |
| beat the Act-1 boss | 44/100 [34.7%, 53.8%] |
| reached Act 2 | 44/100 [34.7%, 53.8%] |
| reached the Act-2 boss | 5/100 [2.2%, 11.2%] |
| beat the Act-2 boss | 1/100 [0.2%, 5.4%] |
| reached Act 3 | 1/100 [0.2%, 5.4%] |
| cleared (won the run) | 0/100 [0.0%, 3.7%] |

### Floor reached

| act | floor | NN greedy |
| --- | --- | --- |
| 1 | 5 | 1 # |
| 1 | 7 | 9 ########### |
| 1 | 8 | 4 ##### |
| 1 | 9 | 6 ####### |
| 1 | 11 | 4 ##### |
| 1 | 12 | 1 # |
| 1 | 13 | 1 # |
| 1 | 14 | 2 ## |
| 1 | 15 | 3 #### |
| 1 | 17 | 25 ############################## |
| 2 | 21 | 4 ##### |
| 2 | 22 | 4 ##### |
| 2 | 23 | 7 ######## |
| 2 | 24 | 11 ############# |
| 2 | 25 | 5 ###### |
| 2 | 27 | 1 # |
| 2 | 28 | 3 #### |
| 2 | 29 | 2 ## |
| 2 | 31 | 2 ## |
| 2 | 33 | 4 ##### |
| 3 | 40 | 1 # |

### How the games ended

| ending | NN greedy |
| --- | --- |
| died: boss | 29 (29%) |
| died: elite | 41 (41%) |
| died: normal fight | 30 (30%) |

- NN greedy: died: boss: VANTOM_BOSS 8, LAGAVULIN_MATRIARCH_BOSS 7, THE_KIN_BOSS 4, SOUL_FYSH_BOSS 3, CEREMONIAL_BEAST_BOSS 2, THE_INSATIABLE_BOSS 2 (+3 more); died: elite: DECIMILLIPEDE_ELITE 7, INFESTED_PRISMS_ELITE 7, PHROG_PARASITE_ELITE 5, BYGONE_EFFIGY_ELITE 5, TERROR_EEL_ELITE 4, PHANTASMAL_GARDENERS_ELITE 4 (+4 more); died: normal fight: THE_OBSCURA_NORMAL 5, EXOSKELETONS_NORMAL 3, SLUMBERING_BEETLE_NORMAL 3, MYTES_NORMAL 2, LOUSE_PROGENITOR_NORMAL 2, BOWLBUGS_NORMAL 2 (+11 more)

### Win-probability head (calibration hint)

| policy | games | mean win_prob, first decision | observed win rate | mean win_prob, last decision: lost games (before death) | won games | other |
| --- | --- | --- | --- | --- | --- | --- |
| NN greedy | 100 | 0.000 | 0.0% | 0.000 (100) | - | - |

## Definitions

- Progress v0.2 = ((act - 1) + rooms cleared in the act / rooms in the act) / 3, 1.0 for a win; the 95% interval is mean ± 1.96 s/sqrt(n), clipped to [0, 1]. Win rate and every staged row carry a Wilson 95% score interval (0/20 has an upper bound of 16.1%, 0/100 of 3.7%).
- Stages, from each game's final act and floor (k = rooms entered in the act, L = its rooms, the boss room is k = L): reached the Act-N boss = entered act N's boss room or got further; beat the Act-N boss = left act N, or ended `stuck` in its boss room (the floor step cap counts only non-combat decisions, which a boss room offers only after the fight is won; a `capped` game there does not count); reached Act N = final act >= N; cleared = won.
- Endings: deaths by the encounter id of the last combat (`*_BOSS`, `*_ELITE`, `*_EVENT_ENCOUNTER`, else a normal fight); stuck = the floor step cap; capped = the step cap.
- Floor reached: the final total floor (rooms entered) per act; `#` scales the first column.
- Win-probability head: mean of `win_prob` at each game's first and last decision; a calibrated head starts near the observed win rate and ends near 0 in lost games. A hint, not a calibration test.

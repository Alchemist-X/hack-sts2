# 第 2 题：训练种子怎么生成、见过多少局和多少个唯一种子

日期 2026-11-04；仓库工作树 HEAD 8c9455f（只读）。文中数字均由本目录脚本从归档数据、checkpoint 和本地模拟器重新算出。

路径约定：`out/`、`scripts/`、`proposal/`、`ex/` 都在 `/private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/diag/q2-seeds/` 下；`ex/` 是两个归档 tarball 的全新解压。代码路径相对于仓库根 `/Users/Aincrad/dev-proj/hack-sts2/.claude/worktrees/recorder-performance-lag-0f9d6d/`（`sim/tools/train/sts2train/` 简写为 `sts2train/`，`sim/Sts2Sim.Vec/` 简写为 `Vec/`，`sim/Sts2Sim.Core/` 简写为 `Core/`）。

## 结论

1. 正常新开局从**固定列表**取种子，训练中不会生成新种子。每个会话（包括 `--resume`）开始时，都按配置重建同一张表：4096 个 fresh 条目，种子文本为 `TRAIN0-SILENT-0` … `TRAIN0-SILENT-4095`（SILENT，A10），另有 2220 个存档条目（148 个人类 A9 存档，每个 15 份），共 6316 条。
2. 开局顺序：2048 个槽共用这张表。槽 i 的第 k 局使用条目 `(XXH64(seed_text‖i) + k0_i + k) mod 6316`，也就是每个槽从一个伪随机偏移出发，每开一局往后走一条。`seed_text` 新跑时是 `PPO0`，续训时是 `PPO0R<续训起点 update>`。槽的位置不写入 checkpoint；续训会得到一组新的偏移，计数从头开始。所以续训不会接着上次的位置走，也不会按上一个会话的顺序重放。只有从同一个 update 再次续训（seed_text 相同、池相同）时，才会重放完全相同的顺序（事实 15）。
3. 同一条目每次开局的游戏状态逐位相同（StateHash 相同；动作相同，轨迹也相同）。同一种子的两局之所以不同，只是因为策略采样到的动作不同。存档的 14 个额外副本是建池时一次性做的"确定化"克隆，之后固定不变，并非每局重新采样。
4. 产出 u2225 的谱系是 s6b u1–126 → s7b u127–350 → s8a u351–2225，共完成 **1,492,418 局**：fresh 起点 967,401 局，存档起点 525,017 局。fresh 起点一局没赢过；26,575 次胜利全部来自 A9 存档起点。
5. 唯一种子：4096 个 fresh 种子**全部**至少开过一局（严格证明，没有用估计），平均每个种子完成 **236.2 局**。存档起点只覆盖 **4 局人类游戏**里的 148 个存档点，其中第二、三幕的存档全部出自同一局。换句话说，"每局都是新种子"不成立：约 149 万局只对应 4096 个独立的新开局种子。
6. s9a（从 u2225 续训）在 u2226–u2630 完成 205,538 局，依据是 10:49 打印的 status.json。此后运行时被终止，数据全部丢失。
7. 评测种子（C1A10SI01–20、D1A10SI001–128、H1A10SI001–100）与训练用的 TRAIN0-SILENT-0..4095 不是同一个种子族，文本没有交集，64 位运行种子也没有碰撞。
8. 每个种子实际重复了多少次，没有日志记录，只能给出精确均值和覆盖下界。下文给出一个已在本地冒烟测试中验证过的最小记录改动（`proposal/ppo-start-logging.diff`）。

### 对后续实验的直接含义

- `--seed` 同时决定四件事：torch/numpy 随机数、fresh 种子族（`TRAIN{seed}`）、存档副本种子和开局偏移。如果用不同的 `--seed` 做"换训练随机种子复验"，训练用的游戏集合也会跟着换。若只想改变优化过程的随机性，需要先把种子族和 RNG 种子解耦，比如新增一个 `--pool-seed-prefix` 参数。
- 从同一个 checkpoint 续训、配置也相同的两条线（如 s9a 和 s9b），开局顺序相同，相当于共用随机数，适合做配对比较。harvest 会往池里添加条目，从加入第一个条目起，两条线的顺序就会分开。
- 做新种子盲测时，应使用从未用过的前缀（不要用 TRAIN0-*），并在配置里锁定 `--pool 4096`。
- 训练分布并不纯粹是 A10 新开局：约 35% 的开局来自 A9 存档，第二、三幕的起点全部出自同一局人类游戏，而且所有胜利都来自这些存档起点。

## 已确认事实

### A. 起点池：新开局从哪里来

1. 14 个归档 run 都用 `--seed 0`。u2225 谱系的三段（s6b、s7b、s8a）和 s9a 的池配置相同：`pool 4096`、`characters [SILENT]`、`ascension 10`、148 个存档、`save_mix 0.35`、`save_copies determinized`。池为 4096 fresh + 2220 存档条目，共 6316 条，`save_mix_effective 0.351488`，`skipped 0`。（out/configs.json；各 run `config.json` 的 `config.*`、`start_pool.*`；s9a 启动日志第 16 行，见 out/s9a_launchlog_excerpt.txt）
2. fresh 种子文本是一张固定列表：文本 = `f"{seed_prefix}-{ch}-{i}"`，其中 `seed_prefix = f"TRAIN{cfg.seed}"`，i = 0 … cfg.pool−1，ch = SILENT。训练循环里没有任何代码生成新种子或往池里加条目；ppo.py:838 的 `env.pool.warm()` 只是预先建好模板，不改变条目。按训练代码在本地重建池后读回，得到 4096 个互不相同的文本，首尾分别是 TRAIN0-SILENT-0 和 TRAIN0-SILENT-4095，与布局完全一致。（sts2train/startpool.py:338-340, 446-448；sts2train/ppo.py:352-353, 819, 838；在 ppo.py 中 grep add_fresh/add_save/snapshot/branch/release，全部无结果；out/verify_startseq.json `B_*`）
3. 布局公式：每个存档的份数 = max(1, round(4096×0.35/0.65/148)) = 15，所以存档条目共 2220 个。条目 j 是存档，当且仅当 ⌊(j+1)·2220/6316⌋ > ⌊j·2220/6316⌋，即存档在整张表中均匀穿插。存档副本按"轮"排列，每轮 148 个存档按 sha256("sts2train-save-round", seed, 轮次, 存档 sha) 排序。（sts2train/startpool.py:209-243）
4. fresh 条目的游戏完全由种子文本决定。模板是 `RunSim.NewRun(RunConfig(种子文本, SILENT, 10){Act1="random"})`，第一次使用时生成并缓存（`pool_cache = max(4096, pool) = 4096`），之后每局都克隆这个模板。运行种子 = XXH64(UTF-8(种子文本))；12 条运行 RNG 流、幕选择 RNG 和玩家 RNG 都从种子文本派生；各幕的房间（普通/精英遭遇队列、事件、Boss）在 NewRun 时就已用 UpFront 流生成。（Vec/StartPool.cs:272-301, 358-362；sts2train/ppo.py:355；Core/Random/RngSeeds.cs:11-19；Core/Random/DeterministicHash.cs:28-34；Core/Random/RngSets.cs:16-19, 30-38；Core/Run/Loop/RunLoop.cs:58, 89, 115, 117-118, 139-160；sim/VEC.md:904-905）
5. 本地实测（用托管库、按训练代码建池）：同一条目开两次，StateHash 相同。取 4 个 fresh 条目（TRAIN0-SILENT-0/777/2048/4095）和 4 个存档条目（副本 0、5、14），每个都在两个槽里开局并喂入相同的随机动作，直到这局结束（1–71 步），每一步的 StateHash 都相同。由此可知：同一条目加上同样的动作，地图、遭遇、奖励和抽牌都相同。同一种子的不同局之间的差别，只来自策略采样到的动作。（out/verify_startseq.json `C`；out/verify_determinism.json `paired_trajectories`）
6. 存档条目：副本 0 是原样导入，隐藏状态就是人类原局的状态。副本 1–14 的做法是先从副本 0 restore，再调用 `clone_determinized(copy_seed(0, sha256, c))`，其中 copy_seed 取 sha256("sts2train-save-copy:0:<sha>:<c>") 的前 8 字节。148 个存档都没有 pending_prompt，所以这 2072 份全部是确定化副本。（sts2train/startpool.py:73-75, 341-349；out/configs.json `saves_pending_prompt = 0`）
7. "确定化"的含义：玩家能看到的状态不变，隐藏状态重新采样。重采样的部分包括：抽牌堆顺序；12 条运行 RNG 流和哈希运行种子（它是后续幕地图、事件、怪物/遭遇 RNG 的根）；玩家的 3 条 RNG 流；事件、奖励和怪物私有 RNG；遗物袋顺序；尚未访问的事件顺序。保持不变的部分包括：普通/精英遭遇队列、后续幕的 Boss 和古代、未开宝箱里的遗物、未打开的卡牌奖励。重采样只在建池时做一次，之后每局都克隆同一个模板。实测结果：副本 0 和副本 1 的 37 个观测缓冲区逐字节相同，StateHash 不同；抽样 120 个条目（77 个 fresh、2 个副本 0、41 个确定化副本），在 seed_text 不同的两个 env 中 StateHash 全部相同。（sim/VEC.md:862-898, 900-910；out/verify_startseq.json `C`；out/verify_determinism.json `cross_session_sample`）
8. 148 个存档的 sha256，在所有带存档的 run、本地 `tmp/train/saves-silent-a9` 和 26f1 bundle 的 `saves/` 中完全一致。它们来自 4 个人类 run_seed：A43U5UZ5W8BS 101 个、TKQGHES2VYL7 18 个、G7AHVEDBNWGP 15 个、K6DM7HTD7S5B 14 个，分布在 34 个记录器会话分段里，游戏版本 v0.111.0。第二、三幕的 64 个存档全部来自 A43U5UZ5W8BS。（out/saves_compare.json）
9. 从存档开局时使用存档自身的难度 A9；`--ascension 10` 只作用于 fresh 条目。（sts2train/startpool.py:8-9, 451-454；`config.json` 中 `start_pool.histograms_unique_saves.ascension = {9: 148}`；s9a 启动日志第 20 行）

### B. 开局顺序

10. 槽 i 的槽种子 = XXH64(UTF-8(seed_text) ‖ int32 LE i)。每次开局取第 (槽种子 + EpisodeCount) mod pool_count 个条目，然后 EpisodeCount 加 1；一局结束后自动重置，继续沿同一序列往下走。也就是说，每个槽从一个伪随机偏移出发，每局向后走一条。（Vec/VecEnv.cs:245, 331-338, 843-866, 881-891；Vec/StartPool.cs:159-170；sim/VEC.md:52-57）
11. 2048 个槽共用同一个池（每个 VecEnv 只有一个 StartPool），开局序列与线程调度无关。（Vec/VecEnv.cs:241；sim/VEC.md:53-54）
12. `seed_text` 的取值：新跑为 `PPO{seed}`，续训为 `PPO{seed}R{续训起点 update}`。u2225 谱系的 5 个会话依次为：`PPO0`（u1–126）、`PPO0R126`（u127–350）、`PPO0R350`（u351–1553）、`PPO0R1553`（u1554–2050）、`PPO0R2050`（u2051–2225）。s9a 为 `PPO0R2225`。（sts2train/ppo.py:808；out/configs.json `sessions.update_start`；out/sessions_from_metrics.txt）
13. 槽 0 是例外。建池时每做一个确定化副本就 restore 槽 0 一次，所以训练开始前槽 0 的 EpisodeCount 已经是 2072，它的第一局从 (槽种子 + 2072) mod 6316 开始。本地用 64 个槽、分别以 `PPO0R2225` 和 `PPO0R350` 为 seed_text 重建，所有槽的首个开局条目都与公式一致，统计到的 restore 次数正好是 2072。（sts2train/startpool.py:348；out/verify_startseq.json `A_*`、`restores_of_slot0_during_build`）
14. 续训不会延续开局位置。checkpoint 只保存模型、优化器、trainer 计数器（updates/env_steps/episodes/wins/…/sessions）以及 numpy/torch/cuda RNG 状态，不保存 VecEnv 的槽状态。续训时新建 VecEnv，EpisodeCount 归零；seed_text 变为 `PPO0R<upd0>`，偏移随之换成新的一组。中断时正在进行的 2048 局直接放弃。实测：两个 seed_text 的首个开局条目不同，而池的内容相同。（sts2train/ppo.py:21-22, 269-277, 321-330, 798-810, 1029；u2225 checkpoint 实读的 `top_keys/counters/rng_keys`，见 out/ckpt_u2225_counters.json；out/verify_startseq.json `D`）
15. 只要 `--seed`、续训起点 update 和池都相同，开局顺序就完全相同。例如 s6a 与 s6b（都是 `PPO0` 加 6316 条 A10 池）；s3b 与 s3c（都是 `PPO0R100`）；s9a 与任何从 u2225 续训且池不变的 run（`PPO0R2225`）。（sts2train/ppo.py:808；out/configs.json）
16. 每个会话中，2048 个槽只有 1727–1756 个不同的起始偏移，约 15% 的槽与其他槽的偏移完全重合。重合的槽走同一串条目，只是因为局长不同而在时间上错开。（out/coverage.json `distinct_slot_offsets`）
17. 在同一个 run 目录续训时，metrics.jsonl 中 update 大于续训起点的行会被删除。u2225 谱系的三个 metrics.jsonl 都没有缺号或重复，episodes_total 逐行连续，三段之间的两个接缝处也连续（128,599 → s7b，306,873 → s8a）。（sts2train/runlog.py:105-115；sts2train/ppo.py:896；out/metrics_counts.json；out/lineage_u2225.json `junction_matches_previous_segment`）

### C. 局数与唯一种子

18. u2225 谱系依次为：s6b u1–126（随机初始化）→ s7b u127–350（从 s6b ckpt-126 续训）→ s8a u351–2225（从 s7b u350 分叉）。合计 2225 次更新、291,635,200 env steps（每次更新 2048×64 = 131,072 步），完成 1,492,418 局。其中 fresh 起点 967,401 局，存档起点 525,017 局，存档占 0.35179，与布局占比 0.351488 吻合。共胜 26,575 局，全部来自存档起点，fresh 起点 0 胜。以上与 u2225 checkpoint 中的计数器一致：episodes 1,492,418、wins 26,575、illegal 0、sim_error 0、content_gap 0、floor_stall 461、sessions 5。（out/lineage_u2225.json；out/ckpt_u2225_counters.json）
19. 各会话的完成局数见下文"局数表"，各会话之和等于总数。（out/sessions_from_metrics.txt；out/s7b_sessions_to_u350.json；out/s8a_sessions_to_u2225.json）
20. 开局数 = 1,502,658，比完成局数多出 5 个会话窗口末尾各 2048 个未完成的局。之所以能精确算出：sim_error 和 content_gap 都是 0，又有自动重置，所以窗口内每个槽始终恰好有一局在进行。（sts2train/ppo.py:1029；Vec/VecEnv.cs:881-891；out/ckpt_u2225_counters.json）
21. 存档起点的局按起始幕划分：第一幕 298,379 局，第二幕 120,430 局，第三幕 106,208 局。第二、三幕合计 226,638 局，全部来自 A43U5UZ5W8BS 这一局人类游戏。（out/save_by_act_u2225.json；事实 8）
22. 4096 个 fresh 种子全部至少开过一局。这是严格证明，不依赖估计：每次更新所有 2048 个槽都走 64 步合法动作（`env_steps_update` 恒为 131,072，illegal 为 0），而单局不超过 step_cap = 3000 步，所以一个有 U 次更新的会话里，每个槽至少开局 ⌈64U/3000⌉ 次。s8a 会话 3 有 U = 1203，每槽至少 26 局；而这个会话相邻偏移的最大间隔只有 24，因此 6316 个条目全部被覆盖。（Vec/VecEnv.cs:740, 757；metrics `env_steps_update`；out/coverage.json）
23. 精确均值：每个 fresh 种子平均完成 236.18 局（967,401/4096），每个存档条目 236.49 局，每个存档文件 3,547 局，每次更新平均 670.7 局。（out/lineage_u2225.json）
24. 唯一游戏种子 = 4096 个 fresh 种子，加上 4 局人类游戏中的 148 个存档点（对应 2220 个隐藏状态变体）。1,492,418 局只对应 4096 个独立的新开局种子。（事实 2、8、22）
25. s9a 从 ckpt-0002225 续训（日志为"291,635,200 env steps, session 6"），使用同一张 6316 条的池。10:49 EST 的 status.json 显示：update 2630，episodes 1,697,956，wins 32,829。因此 u2226–u2630（405 次更新）完成 205,538 局、胜 6,254 局；最近 20 次更新共 9,781 局，其中 fresh 6,333 局。此后运行时被终止（记忆文件按余额推算约为 11:04 EST），metrics 和 checkpoint 全部丢失。（out/s9a_status_u2630.txt、out/s9a_launchlog_excerpt.txt，均摘自会话记录 jsonl 第 5873 行；记忆文件 training-cycle-2026-11-03.md 第 153、159 行）
26. 本周期所有归档 run 目录（各自的 metrics 行，不含分叉前的前缀）共记录完成 4,044,033 局，其中包括 A0 和其他谱系；另有 s9a 已知的 205,538 局。（out/metrics_counts.json）
27. 早期各 run 的池配置都属于 TRAIN0-* 种子族：t0 为 2048 条、5 个角色轮换、A0、无存档；s1a（A0）、s1b（A10）、s2a（A10）为 4096 条 fresh、无存档；s3b、s3c、s6a、s6b、s7a、s7b、s8a、s8b 为 A10 加同一批 148×15 存档，共 6316 条；s4a、s5b 为 A0 加同一批存档。u2225 谱系从 s6b 的随机初始化开始，三段池配置完全相同，A0 run 的数据没有进入这条谱系。（out/configs.json；out/runs_table.md）
28. 与起点序列有关的代码在所有训练提交（ef70ee6、e877036、9f66087、26f1d80、HEAD）中一致：ppo.py 中的 seed_text、seed_prefix、pool_cache 三行相同，startpool.py 整个文件无差异，VecEnv.cs 中按序列取条目的那一行也相同。ef70ee6 → 9f66087 之间 Core 的改动只涉及药水/商店的合法性判断和 TurnsTaken 记账（7 个文件，+77/−5），没有改动 NewRun、GenerateRooms 或 RNG 相关文件。（`git show <c>:sim/tools/train/sts2train/ppo.py | grep`；`git diff --stat ef70ee6 HEAD -- sim/tools/train/sts2train/startpool.py` 输出为空；`git diff --stat ef70ee6 9f66087 -- sim/Sts2Sim.Core`）

### D. 评测种子与训练种子

29. 评测集如下：silent-a10-v1 为 C1A10SI01–C1A10SI20（20 局），silent-a10-dev-v1 为 D1A10SI001–D1A10SI128（128 局），silent-a10-test-v1 为 H1A10SI001–H1A10SI100（100 局），均为 SILENT、A10、ported_only，并与 26f1 bundle 中同名文件的 sha256 一致。训练用的 fresh 种子是 TRAIN0-SILENT-0..4095，两者文本交集为 0。把全部 8 个已提交的种子集和计划中的 TRAIN0-SILENT-4096..4607（ab-v1）放在一起，共 5016 个不同文本，它们的 64 位运行种子互不相同。（sim/tools/eval/seedsets/*.json；out/seed_families.json；`shasum -a 256`）
30. 评测使用单独的 env（seed_text "EVAL"，pool=None，逐局 add_fresh），不会改动训练池。（sim/tools/vec/sts2vec/evaluate.py:65-68）

### E. 局数表（u2225 谱系与 s9a）

| 会话（seed_text） | update | 更新数 | 每槽至少开局 | 完成局 | fresh | 存档 | 胜 | fresh 种子覆盖（严格下界） |
|---|---|---|---|---|---|---|---|---|
| s6b 会话 1（PPO0） | 1–126 | 126 | 3 | 128,599 | 83,248 | 45,351 | 470 | 2,534 / 4096 |
| s7b 会话 2（PPO0R126，谱系部分） | 127–350 | 224 | 5 | 178,274 | 115,553 | 62,721 | 1,731 | 3,284 / 4096 |
| s8a 会话 3（PPO0R350） | 351–1553 | 1203 | 26 | 802,041 | 520,062 | 281,979 | 14,967 | **4096 / 4096** |
| s8a 会话 4（PPO0R1553） | 1554–2050 | 497 | 11 | 286,312 | 185,579 | 100,733 | 6,984 | 3,977 / 4096 |
| s8a 会话 5（PPO0R2050，至 u2225） | 2051–2225 | 175 | 4 | 97,192 | 62,959 | 34,233 | 2,423 | 2,945 / 4096 |
| **u2225 谱系合计** | 1–2225 | 2225 | — | **1,492,418** | **967,401** | **525,017** | **26,575** | **4096 / 4096** |
| s9a 会话 6（PPO0R2225，已知部分） | 2226–2630 | 405 | 9 | 205,538 | 无记录 | 无记录 | 6,254 | 3,869 / 4096 |

谱系五个会话的 fresh_wins 都是 0，胜利全部来自存档起点；s9a 没有 fresh/存档拆分。"每槽至少开局" = ⌈64·更新数/3000⌉。"覆盖"栏是严格下界，真实覆盖只会更高。会话 3 的平均值为：每槽完成 391.6 局，每个条目开局 127.3 次（(802,041 + 2048)/6316）。各 run 的池配置见 out/runs_table.md。

## 证据位置

| 事实编号 | 文件/命令 | 关键字段或行号 |
|---|---|---|
| 1 | out/configs.json；ex/*/runs/*/config.json；out/s9a_launchlog_excerpt.txt | `config.seed/pool/characters/ascension/save_mix/save_copies`；`start_pool.pool_entries=6316, fresh_entries=4096, save_entries=2220, unique_saves=148, copies_per_save=15, skipped=0`；日志行 16 |
| 2 | sts2train/startpool.py；sts2train/ppo.py；out/verify_startseq.json | startpool.py:338-340, 446-448；ppo.py:352-353, 819, 838；`B_fresh_seed_texts_match_layout=true`、`B_distinct_fresh_seed_texts=4096` |
| 3 | sts2train/startpool.py | 209-243（份数公式在 222，穿插在 227-229，轮次在 236-242） |
| 4 | Vec/StartPool.cs；sts2train/ppo.py；Core/Random/*.cs；Core/Run/Loop/RunLoop.cs；sim/VEC.md | StartPool.cs:272-301, 358-362；ppo.py:355；RngSeeds.cs:11-19；DeterministicHash.cs:28-34；RngSets.cs:16-19, 30-38；RunLoop.cs:58, 89, 115, 117-118, 139-160；VEC.md:904-905 |
| 5 | scripts/verify_startseq.py、scripts/verify_determinism.py → out/verify_startseq.json、out/verify_determinism.json | `C.two_starts_fresh_same_hash`、`identical_actions_identical_states`；`paired_trajectories[*].identical_states_throughout=true` |
| 6 | sts2train/startpool.py；out/configs.json | 73-75、341-349；`saves_pending_prompt=0` |
| 7 | sim/VEC.md；out/verify_startseq.json；out/verify_determinism.json | VEC.md:862-898、900-910；`save_copy0_vs_copy1_same_observation_buffers=true`、`save_copy0_vs_copy1_hash_differs=true`；`cross_session_sample.all_state_hashes_equal=true` |
| 8 | scripts/saves_compare.py → out/saves_compare.json | `identical_to_reference.*.same=true`；`saves_per_run_seed`；`acts_by_run_seed` |
| 9 | sts2train/startpool.py；config.json；out/s9a_launchlog_excerpt.txt | 8-9、451-454；`histograms_unique_saves.ascension={9:148}`；日志行 20 |
| 10 | Vec/VecEnv.cs；Vec/StartPool.cs；sim/VEC.md | VecEnv.cs:245, 331-338, 856, 864-865, 881-891；StartPool.cs:159-170；VEC.md:52-57 |
| 11 | Vec/VecEnv.cs；sim/VEC.md | VecEnv.cs:241；VEC.md:53-54 |
| 12 | sts2train/ppo.py；out/configs.json；out/sessions_from_metrics.txt | ppo.py:808；`sessions[*].update_start`；会话边界（session_s 归零处） |
| 13 | sts2train/startpool.py；out/verify_startseq.json | 348；`A_first_start_entries_match_formula=true`（两个 env 各 64 槽）、`restores_of_slot0_during_build=2072` |
| 14 | sts2train/ppo.py；out/ckpt_u2225_counters.json；out/verify_startseq.json | ppo.py:21-22, 269-277, 321-330, 798-810, 1029；`top_keys`（不含槽状态）、`rng_keys=[cuda,numpy,torch]`；`D.first_start_entries_differ_between_sessions=true` |
| 15 | sts2train/ppo.py；out/configs.json | 808；s6a/s6b 的 `sessions[0].resumed_from=None`，s3b/s3c 的 `update_start=100` |
| 16 | scripts/coverage.py → out/coverage.json | `sessions[*].distinct_slot_offsets`（1727–1756） |
| 17 | sts2train/runlog.py；sts2train/ppo.py；out/metrics_counts.json；out/lineage_u2225.json | runlog.py:105-115；ppo.py:896；`duplicates=0`、`gaps=[]`、`episodes_total_continuity_breaks=[]`；`junction_matches_previous_segment=true` |
| 18 | scripts/lineage_counts.py → out/lineage_u2225.json；scripts/ckpt_counters.py → out/ckpt_u2225_counters.json | `total.episodes/fresh_episodes/save_episodes/wins/fresh_wins/env_steps_update`；`counters.episodes=1492418, wins=26575, illegal=0, sessions=5` |
| 19 | scripts/sessions_from_metrics.py → out/sessions_from_metrics.txt、out/s7b_sessions_to_u350.json、out/s8a_sessions_to_u2225.json | 每个会话的 `episodes/fresh_episodes/save_episodes/wins` |
| 20 | out/ckpt_u2225_counters.json；sts2train/ppo.py；Vec/VecEnv.cs | `sim_error=0, content_gap=0`；ppo.py:1029；VecEnv.cs:881-891 |
| 21 | scripts/save_by_act.py → out/save_by_act_u2225.json | `save_episodes_by_start_act={1:298379, 2:120430, 3:106208}` |
| 22 | Vec/VecEnv.cs；metrics.jsonl；out/coverage.json | VecEnv.cs:740（StepIndex++）、757（step_cap）；`env_steps_update` 最小值 = 最大值 = 131072（out/metrics_counts.json）；`u2225_lineage_union_rigorous.fresh_entries_started_at_least_once=4096`；会话 3 中 `max_circular_gap_between_offsets=24`、`min_episodes_started_per_slot_rigorous=26` |
| 23 | out/lineage_u2225.json | fresh_episodes/4096、save_episodes/2220、save_episodes/148、episodes/2225 |
| 24 | 事实 2、8、22 | — |
| 25 | out/s9a_status_u2630.txt；out/s9a_launchlog_excerpt.txt；scripts/extract_s9a_status.py；记忆文件 | status 中的 `update=2630, episodes=1697956, wins=32829, recent.episodes=9781, recent.fresh_episodes=6333`；日志行 16、26；training-cycle-2026-11-03.md 第 153、159 行 |
| 26 | scripts/metrics_counts.py → out/metrics_counts.json | 各 run 的 `sum_episodes` 之和 |
| 27 | scripts/configs.py → out/configs.json；scripts/runs_table.py → out/runs_table.md | 每个 run 的 `config` 与 `start_pool` |
| 28 | git（仓库根目录执行） | `git show <c>:...ppo.py \| grep 'seed_text = f"PPO\|seed_prefix=f"TRAIN\|pool_cache=max'`；startpool.py 的 diff 为空；`git diff --stat ef70ee6 9f66087 -- sim/Sts2Sim.Core` |
| 29 | sim/tools/eval/seedsets/*.json；scripts/seed_families.py → out/seed_families.json | `text_overlap_with_train_fresh` 全为 0；`hash_collisions=[]`；`distinct_hashed_run_seeds=5016` |
| 30 | sim/tools/vec/sts2vec/evaluate.py | 65-68 |

### 脚本与输出（全部绝对路径）

- 脚本目录 `/private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/diag/q2-seeds/scripts/`，包括：configs.py、metrics_counts.py、lineage_counts.py、sessions_from_metrics.py、save_by_act.py、saves_compare.py、ckpt_counters.py、coverage.py、seed_families.py、verify_startseq.py、verify_determinism.py、extract_s9a_status.py、grep_transcript.py、runs_table.py、make_logging_patch.py、smoke_logging.sh、check_smoke_logging.py。
- 输出目录 `/private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/diag/q2-seeds/out/`，各文件在上表中按名字引用。
- 解压后的归档 `/private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/diag/q2-seeds/ex/`（来自 `scratchpad/runs/archive/runmeta-sts2train.tar.gz` 和 `runmeta-sts2b.tar.gz`）。
- 运行方式：读归档数据的脚本用 `python3 -I`；需要库的脚本用 `STS2VEC_DLL=<仓库>/sim/Sts2Sim.Vec/bin/Release/net9.0/ nice -n 10 uv run --project <仓库>/sim/tools/train python <脚本>`，每次运行都在 10 秒以内，最多 64 个槽、2 个线程。
- 操作说明：有一次冒烟测试，因为 zsh 没有拆分参数，trainer 按默认配置（1024 envs、5 次更新、96 秒、CPU）跑了一遍；输出已删除，仓库未受影响。之后改用 bash 脚本并显式传参，重新运行。

## 尚不确定之处

1. **每个种子（及每个存档条目）实际开局了多少次，即最少、最多和分布；每个槽实际开了多少局。** 没有日志可以支撑。metrics 只记录每次更新的 fresh/存档局数；start_entry 只在内存中用于分类，episode_id 也没有落盘。目前能确定的只有总数、精确均值（236.2 局/种子）和覆盖下界（全覆盖）。要回答这个问题，只能从现在起按下文方案记录；过去的数据无法补算，因为采样过程不可复现。
2. **s6b（ef70ee6）和 s7b（e877036）的库生成的 TRAIN0-SILENT-i，是否与 s8a（9f66087/26f1d80）逐位相同。** git diff 显示生成路径没有改动，但没有实际运行旧的 Linux AOT 构建来比对。动作接口确实变了（奖励宏、FillShop 药水规则），所以同一种子下可选的轨迹并不相同。验证方法：在 raven 上分别加载三个 bundle 的 `.so`，对 TRAIN0-SILENT-0..255 比较首个观测的 StateHash。
3. **s9a 在 u2630 之后（大约到 u2700）的局数，以及 u2226–u2630 的 fresh/存档拆分。** 这些数据已随运行时一起丢失，只剩 10:49 那一份状态快照（其中只有最近 20 次更新的窗口拆分）。
4. **本地验证用的是托管 Release DLL，而 Colab 用的是同源的 NativeAOT `.so`。** 没有直接在 `.so` 上运行验证。不过相关的 C# 源码在各训练提交中一致（事实 28），64 个槽的实测结果也与公式逐一吻合。
5. **每个种子约重复 236 次，是否造成了对训练种子的记忆（即训练种子与留出种子之间的成绩差）。** 本题没有测量。建议：冻结 u2225，用同一协议（greedy + loop-breaker）评测 TRAIN0-SILENT-0..127 这 128 个训练种子，与 dev-128 做对比；如果差距明显，就说明是记忆，而不是泛化。
6. 记忆文件中提到的 `silent-a10-ab-v1`（TRAIN0-SILENT-4096..4607）只存在于 harvest 工作树的计划里，本仓库中没有这个文件，所以我只核对了它的文本范围。它与训练池使用同一前缀，任何 `--pool > 4096` 的 run 都会把它纳入训练池。

## 从现在起的记录方案（已在副本上实现并冒烟测试，仓库未改动）

补丁：`proposal/ppo-start-logging.diff`，在 ppo.py 上新增 47 行、修改 2 行。生成脚本是 `scripts/make_logging_patch.py`，它作用于 `proposal/pkg/sts2train/ppo.py` 这份副本。各改动点（行号指当前 HEAD 的 ppo.py）：

1. **ppo.py:912（sessions.append）**：每个会话记录 `seed_text` 和 `pool_count`。有了这两项，任何会话的开局序列都可以离线重建。
2. **ppo.py:931 之后**：每个会话写一次 `pool_layout.json`，记录条目 id 到名字的映射（fresh 记为种子文本，存档记为 `<存档文件>#copy<c>`）。名字由新增的函数 `start_entry_names(env, starts)` 生成，放在 ppo.py:364 之前。
3. **ppo.py:1029-1030（env.reset 之后）**：新建 `start_counts[pool_count]`。续训时从 checkpoint 恢复，前提是池大小相同；然后把首批开局计入。
4. **ppo.py:1091（done 之后）**：用 `np.add.at(start_counts, info["start_entry"][done], 1)` 统计自动重置时新开的条目，同时把这些条目追加到 `started_this`。
5. **ppo.py:1229（metrics 行）**：新增 `starts_update`、`start_entries_seen`、`fresh_seeds_seen`，以及 `fresh_starts_min/median/max`，即每个种子累计开局次数的分布概况。
6. **ppo.py:1240 之后**：新文件 `episodes.jsonl`，复用 JsonlWriter（keep_upto），续训时会删掉需要重跑的行。每次更新写一行，按列存放：`slot`、`start_entry`、`start_name`、`win`、`end_kind`、`act`、`floor`、`steps`、`progress`，以及本次更新新开的条目 `started`。冒烟测试实测每局约 104 字节，按每次更新 670 局计约 68 KB，每 2225 次更新约 147 MB。其中大头是 `start_name`；如果只存 `start_entry`，再通过 `pool_layout.json` 关联名字，体积大约减半。
7. **ppo.py:947（make_checkpoint）**：checkpoint 额外存入 `start_counts`、`slot_episode_id`（2048 个 int64）和 `seed_text`。由于 EpisodeCount = (episode_id − slot − 1)/N + 1，仅凭 seed_text 和每个 checkpoint 时的 slot_episode_id，就能精确重建每个槽开过的全部条目，不需要逐局行。
8. 冒烟测试：`scripts/smoke_logging.sh` 跑了 16 envs、CPU、3 次更新，再续训到第 5 次。验证结果（out/smoke_logging_check.json）：episodes.jsonl 逐局记录 32 局，与 metrics 中的局数之和 32 相等；计数在续训后继续累计，总计 64 = 32 局完成 + 2×16 局未完成；只用 seed_text 和 slot_episode_id 离线重建出的每条目开局计数，与日志计数**完全相等**。
9. 注意：harvest 等方案会动态增删池条目，那时 pool_count 会变化，上面第 7 条的离线重建公式不再适用，必须依赖第 6 条的逐局 `start_entry` 行。

## 复核（独立核查）

复核时间 2026-11-04。做法：两个归档 tarball 重新解压到 `verify/ex/`（与 `ex/` 无关）；所有计数脚本重写，不复用原脚本；XXH64 按 xxHash 规范自写（`verify/scripts/xxh64_own.py`，3 个标准测试向量通过），不调用 `sts2vec.schema.slot_seed`；库检查用本地托管 DLL（64 槽、2 个 env 线程、`nice -n 10`，单次不超过 15 s）；checkpoint 用 `torch.load(weights_only=True)` 读取，不做任意反序列化。复核目录：`/private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/diag/q2-seeds/verify/`，下文的 `verify/out/*`、`verify/scripts/*` 都指这里。

### 核实无误的事实

- **事实 1–3（池的构成与布局）**：14 个 run 全部 `seed 0`；u2225 谱系三段与 s9a 的池都是 4096 fresh + 2220 存档 = 6316，148×15，determinized，`save_mix_effective 0.351488`，`skipped 0`。用训练代码在本地建池三次（seed_text 为 PPO0、PPO0R350、PPO0R2225），结果都是：`kinds` 与穿插公式逐条一致；fresh 种子文本与布局一致，4096 个互不相同，下标为 0..4095；确定化副本 2072 个。ppo.py 里没有 add_fresh/add_save/snapshot/branch/release 调用。（verify/out/cfg_dump.txt；verify/out/lib_checks.json `A_B_start_order`）
- **事实 4（fresh 条目由种子文本完全决定）**：所引代码行逐一核对无误。另外，`TryCache` 只计 fresh 模板（StartPool.cs:365-374，Release 时也只对 Fresh 减计数），所以 4096 个 fresh 模板正好放进 `pool_cache=4096`。即使缓存满了，也只是每次重新 NewRun，结果相同。
- **事实 5（同条目加同动作得到同轨迹）**：自建 12 对槽，6 对 fresh、6 对存档（含副本 0/3/9/14），各对喂相同随机动作。每一步 StateHash 都相等，直到本局结束（10–53 步）。（verify/out/lib_checks.json `C_paired_rollouts`）
- **事实 6–7（确定化副本）**：148 个存档逐个检查 15 个副本，37 个观测缓冲区逐字节相同，StateHash 两两不同。在 PPO0R2225 和 PPO0R350 两个 env 中比较全部 2220 个存档条目和 256 个 fresh 条目，StateHash 与观测摘要全部相等，比原文抽查的 120 个更全面。`DeterminizeOptions.ResampleRunQueues` 默认为 true（sim/Sts2Sim.Core/Random/Determinizer.cs:19；RunSim.cs:160-164），所以遗物袋和未访问事件的顺序确实会重采样。（verify/out/lib_checks.json `D_copies`、`D_cross_session`）
- **事实 8 中可确认的部分**：10 个带存档的 run 的 config.json、本地目录和 26f1 bundle 中，148 个文件名和 sha256 按路径顺序完全一致，pending_prompt 全为 0。run_seed 的分布为 101/18/15/14；共 34 个会话分段；版本都是 v0.111.0；第二、三幕的 64 个存档全部来自 A43U5UZ5W8BS。（verify/out/saves_vs_runs.json、saves_prov.json）
- **事实 9**：三段 config 的 `histograms_unique_saves.ascension` 都是 {9: 148}。
- **事实 10、11、13（开局顺序）**：用自写 XXH64 计算 `(XXH64(seed_text‖i) + k0_i + k) mod 6316`，其中 k0_0 = 2072（等于建池时做的确定化副本数），k0_i = 0。在三个 seed_text 下，64 个槽连续 5 次开局（k=0..4）全部与库给出的 start_entry 一致。代码路径为 Restore → ResetSlot → StartSlot → EpisodeCount++（VecEnv.cs:382-388, 668-686, 843-866）；ef70ee6 中的同一路径语义相同（该提交 VecEnv.cs:332-338, 369-376, 545-548）。（verify/out/lib_checks.json `resets_k0_to_k4_match_own_formula`）
- **事实 12、14、15**：会话边界与 resumed_from 见 verify/out/cfg_dump.txt。u2225 checkpoint 的顶层键里没有任何槽或 env 状态，RNG 键为 cuda/numpy/torch，计数器与原文一致。（verify/out/ckpt_u2225.json）
- **事实 17**：metrics 没有重复行、没有缺号，episodes_total 逐行连续，两个接缝都对得上。s8a 的 session_s 恰好在 u351、u1554、u2051 归零。（verify/out/count_metrics.json）
- **事实 18–21、23 和局数表**：独立重算的结果与原文逐项相同：1,492,418 / 967,401 / 525,017 / 胜 26,575 / fresh 胜 0；env steps 291,635,200；floor_stall 461；五个会话各行数字；开局数 1,502,658；按幕划分 298,379 / 120,430 / 106,208；均值 236.18 / 236.49 / 3,547.4 / 670.75。（verify/out/count_metrics.json `lineage_u2225`）
- **事实 22（4096 个种子全部覆盖的严格证明）**：自写脚本复算，各会话下界依次为 2,534 / 3,284 / 4,096 / 3,977 / 2,945，s9a 为 3,869；会话 3 中每槽至少开局 26 次，最大间隔 24。证明的前提也逐一核对过：所有 config 的 `env.step_cap` 都是 3000；谱系每行的 `env_steps_update` 都是 131,072，且 illegal、sim_error、content_gap 均为 0；StepIndex 只在动作成功执行时加一（VecEnv.cs:740），StepIndex ≥ StepCap 时截断（757）；所有 run 的 `baseline_steps` 都是 0，训练前没有随机基线预先消耗开局。（verify/out/coverage_own.json）
- **事实 25（s9a）**：会话记录第 5873 行（时间戳 2026-11-04T15:49:09Z）包含：update 2630、episodes 1,697,956、wins 32,829、session 6，最近 20 次更新 9,781 局，其中 fresh 6,333 局。另外 env_steps 344,719,360 = 291,635,200 + 405×131,072，说明 s9a 的每个槽每次更新也都走满 64 步。（verify/out/s9a_line5873.txt）
- **事实 26、27**：所有 run 合计 4,044,033 局；各 run 的池配置与原文一致。（verify/out/count_metrics.json `all_runs_episodes_sum`；verify/out/cfg_dump.txt）
- **事实 28 的其余部分**：startpool.py 在 5 个提交中 sha 相同（cf1d611f73dd）；ppo.py 中 seed_text、seed_prefix、pool_cache 和 env.reset 这几行内容相同；VecEnv.cs 和 StartPool.cs 从 e877036 到 HEAD 逐字节相同；Core 在 ef70ee6→9f66087 之间改了 7 个文件（+77/−5），内容是药水奖励和商店的合法性判断，以及 TurnsTaken 记账；9f66087→HEAD 之间 Core 和 Vec 都没有改动。
- **事实 29–30（评测种子）**：共 5016 个不同文本，对应 5016 个不同的 64 位运行种子，没有碰撞；v1、dev、test 三者两两无交集；文件 sha 与 bundle 一致；评测使用单独的 env（sim/tools/vec/sts2vec/evaluate.py:62-68）。（verify/out/seedsets_own.json）
- **记录方案**：diff 为 +47/−2。用自写 XXH64 和安全加载方式重查冒烟产物：逐局记录 32 行，与 metrics 的 32 局相等；start_counts 合计 64；离线重建结果与日志相等；每一局结束后，该槽的下一局都从"上一局条目 + 1"开始。（verify/out/smoke_recheck.json）

### 需更正的事实

1. **事实 16**：原文"约 15% 的槽与其他槽的偏移完全重合"应更正。15% 对应的是"多余槽"：2048 减去不同偏移数，为 292–321 个（14–16%）。真正"与至少一个其他槽共享偏移"的槽有 551–606 个，占 27–30%。原 out/coverage.json 中名为 `slots_sharing_an_offset` 的字段，实际计算的是 2048 − 不同偏移数。（verify/out/coverage_own.json `redundant_slots`、`slots_sharing_offset_with_another_slot`）
2. **结论 5、事实 8、事实 24**：原文说"4 局人类游戏中的 148 个存档点（对应 2220 个隐藏状态变体）"，应更正为以下几点：
   - 148 个文件的 sha256 互不相同，但只有 **129 个不同的游戏状态**：15 组共 34 个文件之间只有 `num_reloads` 字段不同，是同一个存档点重新载入后再次保存的结果。导入库后，StateHash 也恰好是 129 个，分组与按内容分组完全一致。
   - 玩家视角（37 个观测缓冲区）只有 **123 种**。
   - 2220 个存档条目中实际只有 **2201 个不同状态**：19 个副本 0 互相重复；2072 个确定化副本彼此都不相同，也不与任何副本 0 相同。
   - 按 (run_seed, floor, entry_point, room_type) 分组，只有 118 个存档点。
   - 4 个 run_seed 对应 4 局原始人类对局，但 A43U5UZ5W8BS 并不是一条线性轨迹。同一存档点出现了可见状态不同的分支：第 17 层 Boss 入口在 part6–8 时 hp 为 23，在 part9–10 时 hp 为 50，而金币 178、牌组 24 张、遗物 5 个都相同；第 23 层是 hp16/金币 287 对 hp50/金币 487；第 24 层遗物数是 6 对 7。可见该局有回档重玩。
   
   （verify/out/saves_dedup.json、saves_branches.json；verify/out/lib_checks.json `E_saves_distinct_states`；verify/out/lib_save_distinct.json）
3. **事实 28**：原文"VecEnv.cs 中按序列取条目的那一行也相同"，实际两者文本不同。ef70ee6（s6b）中是 `entry = (int)((s.SlotSeed + (ulong)s.EpisodeCount) % (ulong)count);`（该提交 VecEnv.cs:545）；e877036 起改为 `Pool.SequenceEntry(s.SlotSeed + (ulong)s.EpisodeCount)`，内部是 `_order[(int)(k % (ulong)n)]`（VecEnv.cs:856、StartPool.cs:168）。训练中从未 release 过条目，所以两种写法结果相同。另外，ef70ee6→e877036 之间 VecEnv.cs 增加 451 行、StartPool.cs 增加 239 行，加入了 release、restore_many、branch 等功能。（verify/git/VecEnv.cs.*、StartPool.cs.*；`git diff --stat ef70ee6 e877036 -- sim/Sts2Sim.Vec`）
4. **尚不确定之处第 6 条**：原文"silent-a10-ab-v1 只存在于 harvest 工作树的计划里"，实际它是 train/harvest 分支上已提交的文件（commit a570373），路径为 `/Users/Aincrad/dev-proj/hack-sts2/.claude/worktrees/train-harvest/sim/tools/eval/seedsets/silent-a10-ab-s512.json`。逐条核对，内容正好是 TRAIN0-SILENT-4096..4607（512 个），与训练池 0..4095 无交集，XXH64 也无碰撞。记忆文件 training-cycle-2026-11-03.md 第 161 行记载，u2225 已在这个集合上评测过（progress 0.3676）。本工作树 HEAD 中确实没有这个文件。（verify/out/seedsets_own.json `silent-a10-ab-v1`）

### 无法核实的事实

- **Colab 上的 NativeAOT `.so` 是否表现一致。** 本地只有托管 DLL。本地 DLL 构建于 2026-11-04 03:36 EST，晚于最后一次 Core/Vec 提交（9f66087，02:39 EST）；`lib.version()` 为 sts2vec/3b73317739c650bd，与 s7b、s8a 的 config 相同。但这个字符串只是 encoder_version，不能证明二进制一致。
- **s6b 所用的库（ef70ee6，encoder 5dbfa09dd9dcb5ed）与之后的库，对同一种子文本生成的初始状态是否逐位相同。** NewRun 路径（RunLoop CreateForNewRun/GenerateRooms 和 RNG 相关文件）在代码上没有改动，但没有实际运行旧库验证。
- **s8a 续训用的 `s7b-fork.pt` 是否就是 s7b 的 u350 checkpoint。** 只有间接证据：s8a 首行的 episodes_total − episodes = 306,873，正好等于 s7b 在 u350 时的 episodes_total；sessions 计数为 3。这个文件本身不在归档中。
- **s9a 在 u2630 之后的数据、u2226–2630 的 fresh/存档拆分，以及各条目实际开局次数的分布。** 这些都没有日志。
- **过程说明中"误按默认配置跑了 1024 envs 冒烟测试"一事。** 无法核实，原文已自述，对数据没有影响。

### 补充发现

1. 会话 3 中每个条目不只"开过"，而且至少**完成**过一局：每槽至少完成 25 局（26 次开局减去末尾在途的 1 局），而最大间隔为 24，因此 6316 个条目全部覆盖。（verify/out/coverage_own.json `session3_completed_at_least_once_lb`）
2. 归档数据中更晚的两条谱系：u2536（s8a 跑到 u2536）完成 1,665,311 局，其中 fresh 1,079,528 局，fresh 胜 0，总胜 31,139；s8b 的 u2625 谱系完成 1,712,470 局，其中 fresh 1,110,117 局，fresh 胜 0，总胜 33,300。（verify/out/other_lineages.json）
3. 这里的"完成局"指已结束的局，包括 floor stall 截断（谱系中 461 次）和 step_cap 截断。存档起点的 525,017 局是从对局中途开始的残局，不是完整对局。从第 1 层开始的完整尝试只有 fresh 的 967,401 局，对应 4096 个种子。
4. 续训只改变每个槽的起始偏移。每个会话都在同一张表上按同一个循环顺序（j, j+1, …）推进，所以重复是全局性的：每个会话都会把整张池走好几遍（会话 3 平均每个条目开局 127 次）。
5. 记录方案的冒烟测试用的池是 64 个 fresh 加 148×1 个存档（copies=1），没有生成确定化副本，因此没有测到槽 0 的 k0=2072 偏移。重建代码已经按 unique_saves×(copies−1) 处理了这一点，但没有在生产配置下实测。日志体积按每局约 104 B 计，1,492,418 局约 155 MB（即 147 MiB）。
6. 每个存档文件平均 3,547 局，这是按文件算的。由于有 19 个副本 0 重复，有重复文件的状态实际被开局的次数是单个文件的 2–4 倍。

### 复核脚本与输出（绝对路径前缀 `/private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/diag/q2-seeds/verify/`）

- `scripts/`：xxh64_own.py、cfg_dump.py、count_metrics.py、ckpt_read.py、coverage_own.py、lib_checks.py、lib_save_distinct.py、seedsets_own.py、saves_prov.py、saves_dedup.py、saves_branches.py、saves_vs_runs.py、s9a_extract.py、smoke_recheck.py、other_lineages.py。
- `out/`：cfg_dump.txt、count_metrics.json、ckpt_u2225.json、coverage_own.json、lib_checks.json、lib_save_distinct.json、seedsets_own.json、saves_prov.json、saves_dedup.json、saves_branches.json、saves_vs_runs.json、s9a_line5873.txt、smoke_recheck.json、other_lineages.json。
- `git/`：各训练提交中 VecEnv.cs、StartPool.cs、ppo.py、startpool.py 的原文；`ex/a`、`ex/b` 是两个 tarball 的全新解压；追加复核前的原 memo 备份在 `memo.before-review.md`。

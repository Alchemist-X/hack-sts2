# Q3 隔离审计：训练集、20-seed、dev-128、test-100（及 ab-512）是否严格隔离

日期 2026-11-04。只读审计：没有训练，没有改仓库，没有启动游戏；录制会话目录只读。

路径约定（下文证据指针用这些前缀）：

- `REPO` = `/Users/Aincrad/dev-proj/hack-sts2/.claude/worktrees/recorder-performance-lag-0f9d6d`（集成工作树，HEAD 8c9455f）
- `HARV` = `/Users/Aincrad/dev-proj/hack-sts2/.claude/worktrees/train-harvest`（分支 train/harvest，HEAD 8d99874）
- `SCR` = `/private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/diag/q3-isolation`（本审计的脚本 `SCR/scripts/` 和输出 `SCR/out/`）
- `S` = 上一级 scratchpad；`ARCH` = `S/next/review/extract`（前序代理解开的两个归档，只读使用）
- `T` = 会话记录 `/Users/Aincrad/.claude/projects/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17.jsonl`（只 grep，`L####` 为行号，时间为记录的 UTC 时间戳）
- `MEM` = `/Users/Aincrad/.claude/projects/-Users-Aincrad-dev-proj-hack-sts2/memory/training-cycle-2026-11-03.md`
- `SESS` = `~/Library/Application Support/Sts2Recorder/sessions`（只读）

时间说明：MEM 里早期条目的时间标签不可靠（例如标"~02:10"的条目实际写于 04:44:39Z，即 23:44 EST）。本文所有时间以 T 的时间戳和 git 提交时间为准。EST = UTC−5。

## 结论（先读）

1. **种子层面严格隔离，未发现训练数据泄漏。** 20-seed、dev-128、test-100、ab-512 与训练用的 4,096 个 fresh 种子以及 148 个人类存档所属的 4 个对局，两两之间在种子文本、(角色, 文本, 难度) 和 64 位 run seed 三个层级上交集都为 0。种子到 RNG 的映射是 64 位 XxHash64，sim 不做大小写或 O/0、I/1 规范化。实测中，仅差一个字符的种子生成的是不同地图。训练代码里没有任何路径读取评测集的快照、搜索结果或动作标签。
2. **选择层面并不干净。** 20-seed 是训练中选 best.pt 的集合（u2225 谱系共 91 次评测）。dev-128 是跨运行挑选的集合。12 个候选的 test-100 都和 dev-128 同时算出、同时被看到。现有记录里，每一次挑选都等于当时 dev-128 的最大值，test-100 没有改变任何挑选、停止或超参数决定。但 test-100 已被用于三件事：给 u2536 贴"runner-up"标签、周期后的平台期分析（与 dev 合并，并做了逐决策探测）、10:50 EST 预注册的 s9b A/B 采纳规则（要求 test-100 Δ ≥ 0）。因此 test-100 已是"暴露集"，不再是一次性保留集。
3. **"被编辑的那一局"已逐边界核实。** 148 个存档只来自 4 局人类 A9 对局。其中唯一的胜局 A43U5UZ5W8BS 有 5 次游戏外修改存档：回血到 50/51 三次、+200 金币、28 张牌中升级到 27 张、凭空多出 2 个遗物。它还有 15 次战斗中退出重来，以及 2 次已记录的死亡后用修改过的存档继续。全部 64 个 Act 2/3 存档都在编辑之后。"训练胜利全部来自存档开局"已确认（fresh 开局 0 胜）。"全部来自这一局"只是推断，日志没有按存档或按对局归因。
4. **对"继续单卡 / 双卡对照 / 先修实现"的含义：** 这一项不需要修复训练实现。但启动新的长训练前，有三件低成本的事应先做：
   - (a) 冻结一个新的 ≥256 局盲测集，把 test-100 降级为第二 dev 集；
   - (b) 训练启动时加种子集重叠守卫。ab-512 就是训练生成器序列 i=4096..4607，`--seed 0 --pool >4096` 会把它拉进训练，而且现在没有防护；
   - (c) 决定编辑后的存档是否保留，或者至少在指标里单独分组。

## 已确认事实

1. 训练 fresh 开局用的是**固定种子池**，不是不断生成新种子。种子文本为 `TRAIN{seed}-{characters[i % n]}-{i}`，i = 0..pool−1，前缀为 `TRAIN{cfg.seed}`。所有归档运行的 Silent 配置都是 seed=0、pool=4096、characters=[SILENT]，所以 Silent A10 的训练 fresh 种子是 `TRAIN0-SILENT-0` … `TRAIN0-SILENT-4095`，涉及运行 s1b、s2a、s3b、s3c、s6a、s6b、s7a、s7b、s8a、s8b。A0 运行 s1a、s4a、s5b 用同一批文本，但在 A0；t0 用 `TRAIN0-<5 角色>-0..2047`（A0）。（`REPO/sim/tools/train/sts2train/startpool.py:336-340`，`ppo.py:349-355`，`REPO/sim/tools/vec/sts2vec/pool.py:134-137`；`SCR/out/run_configs.json` 字段 seed/pool/characters/ascension）
2. 每个 fresh 条目在 sim 里是一个模板，每次开局都**精确克隆**同一个 `RunSim.NewRun`。因此同一个种子文本，每次开局都是同一局（地图和 RNG 初态相同）。不同 worker、不同运行、断点恢复后，用的仍是这 4,096 局；恢复只改变 slot 的开局顺序（seed_text `PPO0R{upd}`），不改变池。（`REPO/sim/Sts2Sim.Vec/StartPool.cs:272-300` `return template.Clone()`；`REPO/sim/VEC.md:52-57`；`ppo.py:808`）
3. 种子文本到游戏 RNG 的映射如下，没有截断，也没有取模：
   - run seed = 种子文本 UTF-8 的 XxHash64（64 位）；只有以小写 `old` 开头的文本才走 32 位旧哈希，本轮没有这类种子。
   - 每条 run RNG 流的种子 = run seed + 流名哈希。
   - MegaRandom 用完整 64 位种子经 splitmix64 初始化。
   - 地图、玩家流和选幕都由同一文本派生。
   - `CanonicalizeSeed`（转大写、O→0、I→1）在 sim 中没有调用者，只有测试用到。

   （`REPO/sim/Sts2Sim.Core/Random/RngSeeds.cs:11-19,93-100`；`DeterministicHash.cs:28-34,55-116`；`RngSets.cs:16-19,29-38`；`MegaRandom.cs:21-24,55-61`；`REPO/sim/Sts2Sim.Core/Run/Loop/RunLoop.cs:58,89,115`；grep 结果 `Sts2Sim.Tests/RngTests.cs:644-645` 为唯一调用处）
4. 实测（预编译库、CPU、单线程）：
   - 同一文本两次开局，状态哈希和全部观测缓冲都相同。
   - 以下变体的 map_nodes/map_routes 都不同：`D1A10SI001` vs `D1A10S1001`、vs `d1a10si001`，`H1A10SI001` vs `H1A10S1001`，`C1A10SI01` vs `C1A10S101`，同一文本的 A10 vs A0。
   - 本地 XXH64 与 `sts2vec.schema.xxh64` 在 10 个文本上结果一致。

   （`SCR/scripts/sim_seed_identity.py` → `SCR/out/sim_seed_identity.txt`）
5. 两两交集全部为 0。范围如下：
   - silent-a10-v1：20 局，`C1A10SI01`–`20`
   - silent-a10-dev-v1：128 局，`D1A10SI001`–`128`
   - silent-a10-test-v1：100 局，`H1A10SI001`–`100`
   - silent-a10-ab-v1：512 局，`TRAIN0-SILENT-4096`–`4607`
   - 训练 fresh 池（3 组）和人类存档的 4 个 run seed

   比较了三个层级：种子文本、(角色, 文本, 难度)、64 位 run seed。9 个冻结种子集的 sha256 字段与按 `sts2vec.protocol.checksum` 重算的值全部一致。唯一的非零交集在训练池内部：A0 与 A10 共用 `TRAIN0-SILENT-*` 文本，t0 与其他池共用其中 410 个 Silent 文本，三者都只用于训练。（`SCR/scripts/seed_intersections.py` → `SCR/out/seed_intersections.json` 字段 intersections、populations.*.sha256_ok）
6. 并集中 6,658 个不同种子文本的 XxHash64 没有碰撞。任意两个 run seed 的最小环形距离为 728,673,311,463，约 2^39.41，评测集与训练/存档之间的最小距离也是这个值。Monster、Encounter、Event 流是"run seed + 小整数偏移"派生的（偏移为坐标、楼层、战斗 id），距离远大于这些偏移，所以不存在"名字不同、实际同局或同用途流重合"的情况。（`SCR/out/seed_intersections.json` 字段 hash_collisions_between_distinct_texts=[]、min_circular_gap_log2=39.41；`RngSeeds.cs:50-64,74-80`）
7. 格式层面也有隔离：集成 HEAD 的种子集正则 `^[A-Z0-9]{1,10}$` 不接受带 `-` 的训练文本；人类游戏种子是 12 位，也不可能和 ≤10 位的评测种子重名。train-harvest 分支为了 ab-512，把正则放宽到也接受 `TRAIN<n>-<角色>-<i>`。（`REPO/sim/tools/vec/sts2vec/protocol.py:87,123-125`；`git -C HARV diff 6e971aa -- sim/tools/vec/sts2vec/protocol.py`）
8. ab-512 位置与风险：
   - 只存在于 train-harvest 分支（提交 a570373，2026-11-04 11:11:24 EST），不在集成 HEAD。
   - 它恰好是训练生成器序列的延续（i=4096..4607）。只要将来有运行用 seed=0 且 `--pool` >4096，ab-512 就会进入训练。
   - ppo.py、cli.py、startpool.py（两条分支都查了）都没有"训练池与冻结种子集求交"的检查。

   （`HARV/sim/tools/eval/seedsets/silent-a10-ab-s512.json`；`git -C HARV show --stat a570373`；`SCR/out/leakage_greps.txt` G7）
9. 分析代理建了诊断集 `silent-a10-trainunseen-128`（`TRAIN0-SILENT-4096..8160`，步长 32），其中 16 个种子落在 ab-512 内，并已用 u2225 和 u925 做过本地探测。这些只用于评测，没有进训练，但它们已不算"未触碰"。（`S/next/analysis/seedsets/trainunseen128.json`；`S/next/analysis/scripts/run_trainseed_probes.sh`；`SCR/out/diag_sets_overlap.txt`）
10. 评测 env 与训练 env 分离：
    - 评测：`pool=None`、`seed_text="EVAL"`，每局 `add_fresh(评测种子)`。
    - 训练：池只有 4,096 个 fresh 条目和 2,220 个存档条目（s7b、s8a、s8b 均为 pool_entries 6316）。
    - 训练代码中只有 `startpool.py:150,340,344,349` 会往训练池加条目。lookahead 和 report-card 的 `add_fresh`/`branch` 只作用于它们自己的评测/搜索 env。

    （`REPO/sim/tools/vec/sts2vec/evaluate.py:62-68`；`ARCH/gpu1/runs/s8a-silent-a10-mid-upg3/config.json` start_pool；`SCR/out/leakage_greps.txt` G2/G5）
11. 148 个存档只来自 **4 局独立人类对局**：

    | run seed | 存档数 | session part 数 | 范围 | 结局 |
    | --- | --- | --- | --- | --- |
    | A43U5UZ5W8BS | 101 | 22 | Act 1–3，楼层 1–48 | 胜 |
    | TKQGHES2VYL7 | 18 | 6 | 仅 Act 1，楼层 1–8 | 放弃 |
    | G7AHVEDBNWGP | 15 | 3 | 仅 Act 1，楼层 1–9 | 第 9 层精英战死亡 |
    | K6DM7HTD7S5B | 14 | 3 | 仅 Act 1，楼层 1–7 | 放弃 |

    - 共 34 个录制 session part，全部 Silent A9、游戏 v0.111.0、录制器 0.3.0/0.3.1。
    - 录制于 2026-11-02 19:06–23:02 EST。
    - 148 个 sha256 与 manifest 一致且互不重复。
    - 64 个 Act 2/3 存档全部来自 A43U5UZ5W8BS。

    （`SCR/scripts/saves_inventory.py` → `SCR/out/saves_inventory.json` 字段 by_run、act2plus_saves_by_run、sha256_all_match；`SCR/scripts/part_boundaries.py` → `SCR/out/part_boundaries.json` 字段 final_result；`REPO/tmp/train/saves-silent-a9/manifest.json`）
12. 存档池构建器 `collect_start_saves.py` 只读取录制目录下的 `<session>/native/saves/*.save`，按角色和难度过滤（SILENT、A9）。manifest 中 `sessions_root` 指向录制目录，`filters` 为 `{SILENT, 9, min_floor 0}`。（`REPO/scripts/train/collect_start_saves.py:7-24`；`REPO/tmp/train/saves-silent-a9/manifest.json`）
13. 所有带存档的运行（s3b、s3c、s4a、s5b、s6a、s6b、s7a、s7b、s8a、s8b）用的是**同一组 148 个存档**：
    - sha256 集合摘要都是 `4b993c14567704db`，与仓库目录、bundle 26f1d805a0、bundle 8d9987467a 一致。
    - 每个存档 15 份：第 0 份精确导入，还原真实对局保存的 RNG 流状态，等于重放人类面对过的隐藏状态；第 1–14 份是 determinized 克隆，所有 RNG 流和 run seed、抽牌堆顺序、遗物袋、未访问事件都重采样，玩家可见状态不变，克隆种子由 sha256(seed, 存档 sha, 副本号) 派生。
    - pending_prompt 为 0，所以没有存档退回为精确副本。
    - 合计 148 个真实 RNG 条目，加 2,072 个重采样条目。

    （`startpool.py:73-75,222,336-349`；`REPO/sim/Sts2Sim.Core/Run/Save/RunImporter.cs:151,155`；`Determinizer.cs:105-169`；`ARCH/*/runs/*/config.json` start_pool.copies_per_save=15）
14. 人类存档的种子不可能与评测种子重合：
    - 文本和 64 位哈希交集都为 0（事实 5、6）。
    - 导入时用的是存档自带的 RNG 快照，不重新派生。
    - 重采样副本的种子来自 sha256，与任何评测种子的 XxHash64 无关。

    （`RunImporter.cs:151`；`startpool.py:73-75`；`SCR/out/seed_intersections.json`）
15. **"被编辑的那一局"的准确含义。** A43U5UZ5W8BS 有 22 个 part、21 个 Continue 边界；存档的 num_reloads 计数从 0 到 21，正好对上，说明没有缺 part。对每个边界，比较"上一 part 游戏最后写出的存档"与"下一 part 开头游戏读入的存档"（录制器 trigger=run_started 的副本），结果如下：
    - **15 个边界**：上一 part 在战斗中结束，读入的存档与上次写出的完全相同，即从房间入口存档重来这场战斗。
    - **5 个边界**：两份存档的 RNG 计数和地图历史完全相同，但玩家字段不同，即存档在游戏外被改过：
      - part 9：HP 23→50，在 Act 1 Boss 处；
      - part 14：HP 16→50，金币 287→487；
      - part 16：已升级牌 4→27（共 28 张）；
      - part 18：HP 27→51，并多出 TWISTED_FUNNEL；
      - part 20：多出 KUNAI。
      - 这 5 次的上一 part 都是在战斗中结束的。
    - **1 个边界**：在地图界面的普通继续。
    - part 15 和 part 19 的 manifest 记录了死亡（`win:false, abandoned:false`，最后状态 HP 0）。之后 part 16 和 part 20 从改过的存档继续，part 22 记录胜利。
    - 其余 3 局没有编辑，只有战斗中退出重来，分别为 2、2、4 次。
    - 逐叶子节点全量比对复核（忽略 save_time/num_reloads/run_time）：
      - 5 个编辑边界只在 `players/current_hp`、`players/gold`、`players/deck`（23 个升级等级）、`players/relics` 上不同；
      - "重来"边界在所有玩法字段上都相同，唯一的差异是在每幕第一次重载时，地图点上多了 `can_modify=True` 标记（序列化细节）。

    （`SCR/out/part_boundaries.json` 字段 boundaries[].kind/diff_fields/last_save_k/first_save_k1、parts[].result；`SCR/out/edit_epochs.json`；`SCR/scripts/boundary_fulldiff.py` → `SCR/out/boundary_fulldiff.txt`）
16. A43U 的 101 个训练存档中：
    - 67 个在第一次编辑之后（Act 1 3 个、Act 2 34 个、Act 3 30 个），也就是全部 64 个 Act 2/3 存档都继承了至少 1 次编辑；
    - 47 个在第一次被撤销的死亡之后（part ≥16）。
    - Act 2 存档平均 27.3 张牌、5–13 个遗物；Act 3 存档平均 31.8 张牌、13–19 个遗物。
    - 按池条目算（结构份额，不是实测局数）：A43U 占全部开局条目的 24.0%（1515/6316），其中编辑后存档占 15.9%。

    （`SCR/out/a43u_parts.txt`；`SCR/out/edit_epochs.json` 字段 saves_after_first_edit_by_act、act2_mean_deck、act3_mean_deck）
17. 训练胜利全部来自存档开局：已核对的 10 个 A10 运行中，fresh 开局胜利都是 0。其中 s1b、s2a 没有存档，总胜利为 0；其余 8 个运行每一行的 fresh_wins 都是 0。u2225 谱系（s6b u1–126 → s7b u127–350 → s8a u351–2225）：
    - fresh 开局 967,401 个 episode，0 胜；
    - 存档开局 525,017 个 episode，26,575 胜。
    - 这里统计的是 episode 数，不是独立对局数：fresh episode 只来自 4,096 个种子，存档 episode 只来自 148 个存档。

    （`SCR/scripts/win_sources.py`、`lineage_wins.py` → `SCR/out/win_sources.txt`；`ARCH/*/runs/*/metrics.jsonl` 字段 fresh_wins/save_wins）
18. "每一个训练胜利都来自这一局被编辑的对局"**没有日志支持**。metrics 只记录 fresh/save 的汇总以及按开始幕统计的局数，不按存档或按 run seed 归因胜利；另外 3 局的 47 个 Act 1 存档原则上也可能赢。能确认的只有两点：胜利全部来自存档开局；Act 2/3 开局全部来自 A43U。（`startpool.py:363-393` split_stats；metrics.jsonl 键列表）
19. 泄漏审计结论：**全部未发现**。具体如下：
    - dev/test 的快照、搜索轨迹、动作标签用于训练：未发现。
    - 训练损失只有 PPO（裁剪策略 + 价值 − 熵）和 aux 头（胜率 BCE、战斗末血量 MSE），都基于自己的 rollout；没有 BC、模仿或蒸馏代码。
    - lookahead 的 `--record` 能写 `searches.jsonl`（ExIt 目标），但代码中没有读取者；本地 scratchpad 和归档里 `searches.jsonl` 数量为 0。lookahead 只出过评测行：s1b 的 dev/test/v1/A0、s7b u300 的 v1、u2050 的 dev。
    - 存档池只来自录制目录（事实 12）。scratchpad 里所有 `.save` 文件都属于那 4 局人类对局，或者是真机孪生 TW1NA10S01 与测试夹具；没有评测种子的存档。
    - harvest 只克隆 fresh 训练 episode 的状态（`start_entry < base` 且不是存档）。

    （`SCR/out/leakage_greps.txt` G1–G8；`ppo.py:553-556`；`lookahead.py:645,847`；`HARV/sim/tools/train/sts2train/harvest.py:14,43,225`）
20. 评测对训练的唯一反馈是标量选择：
    - 训练中定期在 silent-a10-v1 上评测，progress 最高的检查点标为 best.pt。
    - 所有运行的 eval_seedset 都是 silent-a10-v1；u2225 谱系共做了 91 次 20-seed 评测（s6b 6 次、s7b 9 次、s8a 76 次）。
    - 计划文档原定"训练中用 dev-128 每 50 次更新评测并选 best.pt"，实际执行偏离了计划。

    （`ppo.py:953-999`，选择逻辑在 984-993；`ARCH/*/runs/*/evals.jsonl`；`REPO/docs/train-plan-silent-a10-2026-11-03.md:269,285-286`）
21. 选择偏差可以直接量出来：
    - 12 个候选中，被 20-seed 选为 best.pt 的 5 个，(v1-20 − dev-128) 平均 +0.077；其余 7 个平均 +0.028。
    - dev-128 选中的 u2225，(dev − test) = +0.026，是 12 个候选中最大的；12 个的平均只有 −0.0007。
    - 也就是说，u2225 的 dev 分数本身带有挑选偏差。

    （`SCR/scripts/selection_bias.py` → `SCR/out/selection_bias.txt`；数据来自 `REPO/docs/train-results-silent-a10-2026-11-04.md:24-37`）
22. test-100 的计算记录：
    - **最早的一次**是 Owner C 对旧检查点 s1b（u100，旧库）的测量：greedy 0.1620，lookahead 0.1676，Δ +0.0057 不显著。该报告 08:07:56Z（03:07 EST）进入主会话。文档写明 test 上"只运行了在 dev 集上选定的配置"；ExIt 是否启动按 dev 判定（+0.0152 < +0.02，未启动）。
    - 之后 s8a/s8b 谱系的 **12 个检查点**算过 test-100，时间在 10:35:00Z 到 14:20:32Z 之间。每次都由 `final_eval_remote.sh` 与 v1-20、dev-128 一起计算，并同时打印。

    （`REPO/docs/lookahead-2026-11-04.md:9-10,44-53`；T L3965；`S/colab/final_eval_remote.sh`；`SCR/scripts/test100_events.py` → `SCR/out/test100_events.tsv`、`dev128_events.tsv`）
23. 关于**预注册**：
    - 计划文档提交于 e80ea22（2026-11-03 23:46:55 EST = 04:46:55Z），写明：dev-128 用于 best.pt 选择和所有 A/B、lookahead 调参；test-100 "**Never** used for selection or tuning"；"Select on dev-128 only; test-100 at milestones only"。
    - MEM 在 08:30:11Z 和 10:32:01Z 两次重申"按 dev-128 report-card 最终挑选"。
    - 以上都早于第一个 test-100 数字（≤08:07:56Z）和谱系的第一个 test-100（10:35:00Z）。
    - 但计划里的 M3 关口本身用 test-100 阈值决定继续或停止。这一关从未执行，T 中 04:46Z 之后没有 M3 决策记录。

    （`REPO/docs/train-plan-silent-a10-2026-11-03.md:55,269-270,367,1183`；`git log e80ea22`；T L4111、L4433；`SCR/out/decision_timeline.txt`）
24. 每一次实际挑选都等于当时 dev-128 的最大值，依次为 u1300 → u1525 → u2050 → u2536（14:07Z 短暂领先，被打包）→ u2225。若按 test-100 挑，12:31Z 时会选 u1900（0.365），最终会选 u2536（0.373）。没有记录显示 test-100 改变过挑选、停训或超参数。s9a 的 lr 1.5e-4、ent 0.015 在 14:28:59Z 的说明中也没有引用 test 数字。（`SCR/out/test100_events.tsv` 与 `dev128_events.tsv` 对照；MEM 07:35、08:05、09:10、09:25 条目；T L5632）
25. test-100 已被实际**使用**，不是纯保留：
    - (a) 14:09:52Z 起，u2536 被标为 "runner-up (test-100 0.373)"并打包。打包发生在 14:07:26Z，当时它是 dev 第一；最终按 dev 排第二的是 u2625（0.365），结果文档仍写 u2536 为 runner-up。
    - (b) 周期后的平台期分析把 dev-128 与 test-100 合并（每个检查点 228 局）来统计死亡位置和校准头部，并对 u925、u2225 在 test-100 上做了逐决策本地探测。
    - (c) MEM 10:55 条目（写于 15:50:12Z）预注册的 s9b vs s9a A/B 规则，以及 scout 备忘录的成功判据，都要求 "test-100 Δ ≥ 0" 才采纳 harvest。

    （T L5389、L5433；`REPO/docs/train-results-silent-a10-2026-11-04.md:5,35,40`；`S/next/analysis/plateau-analysis.md:68,274,405`；`S/next/analysis/scripts/run_probes.sh`；`S/next/scout/next-training-design.md:524`；T L5894）
26. 种子集文件版本一致：
    - 所有工作树里 20/dev/test 的种子集文件逐字节相同，ab-512 只在 train-harvest。
    - bundle 26f1d805a0 与 8d9987467a 中的种子集与仓库一致。
    - 归档中 234 个 silent-a10-v1、10 个 dev-128、8 个 test-100 评测目录的种子集内容哈希都等于冻结值。

    （`cmp` 循环，见证据表；`SCR/scripts/eval_seedset_ids.py` → `SCR/out/eval_seedset_ids.txt`）

## 种子集与交集（脚本与输出）

| 集合 | id / 角色 / 冻结日期 | 局数 | 种子范围 | 难度 | 内容 sha256（前 16） |
| --- | --- | --- | --- | --- | --- |
| 20-seed | silent-a10-v1 / target / 2026-11-03 | 20 | C1A10SI01–C1A10SI20 | A10 | e82165b0a2e86c57 |
| dev-128 | silent-a10-dev-v1 / dev / 2026-11-04 | 128 | D1A10SI001–D1A10SI128 | A10 | 7fada5746894c95e |
| test-100 | silent-a10-test-v1 / test / 2026-11-04 | 100 | H1A10SI001–H1A10SI100 | A10 | 2ce58e94585257f0 |
| ab-512 | silent-a10-ab-v1 / ab / 2026-11-04（仅 HARV） | 512 | TRAIN0-SILENT-4096–4607 | A10 | 9b80110f347272b9 |
| 训练 fresh（A10） | 10 个 A10 运行 | 4096 | TRAIN0-SILENT-0–4095 | A10 | — |
| 训练 fresh（A0） | s1a、s4a、s5b | 4096 | TRAIN0-SILENT-0–4095 | A0 | — |
| 训练 fresh（t0） | t0 | 2048 | TRAIN0-{IRONCLAD,SILENT,DEFECT,REGENT,NECROBINDER}-0–2047 | A0 | — |
| 人类存档 | 148 文件 | 4 局 | A43U5UZ5W8BS、TKQGHES2VYL7、G7AHVEDBNWGP、K6DM7HTD7S5B | A9 | 集合摘要 4b993c14567704db |

种子文本交集矩阵（`SCR/out/seed_intersections.json`；在 (角色, 文本, 难度) 和 64 位 run seed 两个层级上，跨"评测 × 训练/存档"同样全部为 0）：

```
                         dev128  v1-20  test100  ab512  t0(A0)  pool(A0)  pool(A10)  human
silent-a10-dev-v1          128      0       0      0       0        0         0        0
silent-a10-v1                0     20       0      0       0        0         0        0
silent-a10-test-v1           0      0     100      0       0        0         0        0
silent-a10-ab-v1             0      0       0    512       0        0         0        0
train t0 (5 chars, A0)       0      0       0      0    2048      410       410        0
train pool A0                0      0       0      0     410     4096      4096        0
train pool A10               0      0       0      0     410     4096      4096        0
human saves (run seeds)      0      0       0      0       0        0         0        4
```

- 训练池内部的 4096/410 是同一批文本用在不同难度（A0/A10）或角色交错上。这只发生在训练集合之间，(角色, 文本, 难度) 层级上 A0×A10 为 0。
- 6,658 个不同文本没有哈希碰撞，最小环形间距 2^39.41。
- 复现：`python3 -I SCR/scripts/seed_intersections.py REPO/sim/tools/eval/seedsets HARV/sim/tools/eval/seedsets/silent-a10-ab-s512.json SCR/out/run_configs.json SCR/out/saves_inventory.json SCR/out/seed_intersections.json`

## 人类存档溯源（148 个）

| run seed | 存档 | part 数 | 幕 / 楼层 | 战斗中退出重来 | 游戏外编辑 | 结局（最后 part 的 manifest） |
| --- | --- | --- | --- | --- | --- | --- |
| A43U5UZ5W8BS | 101 | 22 | Act1 37 / Act2 34 / Act3 30；1–48 | 15 | 5 | win:true（part 22）；part 15、19 记录死亡后继续 |
| TKQGHES2VYL7 | 18 | 6 | Act1；1–8 | 4（另有 1 次普通继续） | 0 | abandoned |
| G7AHVEDBNWGP | 15 | 3 | Act1；1–9 | 2 | 0 | 死亡（第 9 层精英战，HP 0） |
| K6DM7HTD7S5B | 14 | 3 | Act1；1–7 | 2 | 0 | abandoned |

A43U5UZ5W8BS 的 5 次编辑。判定条件：下一 part 读入的存档与上一 part 最后写出的存档相比，RNG 计数和地图历史相同，玩家字段不同。

| 进入 part | 幕 / 历史楼层 | 改动 |
| --- | --- | --- |
| 9 | Act 1 / 16（Boss 战中退出后） | HP 23 → 50 |
| 14 | Act 2 / 22 | HP 16 → 50，金币 287 → 487 |
| 16 | Act 2 / 23（part 15 记录了死亡） | 已升级 4 → 27（28 张） |
| 18 | Act 2 / 23 | HP 27 → 51，+TWISTED_FUNNEL |
| 20 | Act 3 / 41（part 19 记录了死亡） | +KUNAI |

sim 中的处理：每个存档 1 份精确导入（重放真实 RNG），另外 14 份 determinized（隐藏状态重采样）。所有带存档的运行都用这同一组 148 个文件。复现命令：`python3 -I SCR/scripts/saves_inventory.py REPO/tmp/train/saves-silent-a9 SCR/out/saves_inventory.json`；`python3 -I SCR/scripts/part_boundaries.py "SESS" SCR/out/part_boundaries.json A43U5UZ5W8BS G7AHVEDBNWGP K6DM7HTD7S5B TKQGHES2VYL7`（只读）；`python3 -I SCR/scripts/edit_epochs.py SCR/out/saves_inventory.json SCR/out/part_boundaries.json`。

## 泄漏审计（found / not found）

检查范围：
- 集成 HEAD 8c9455f：`sim/tools/train/sts2train/*.py`、`sim/tools/vec/sts2vec/*.py`、`scripts/train/*`；
- train-harvest HEAD 8d99874：`sts2train/*.py`；
- scratchpad 与两个归档。

完整命令和输出见 `SCR/out/leakage_greps.txt`。

| 检查项 | 命令（摘要） | 结果 |
| --- | --- | --- |
| 训练代码引用评测集名或评测种子前缀 | `grep -rn -E "silent-a10-dev\|silent-a10-test\|silent-a10-ab\|dev-v1\|test-v1\|D1A10SI\|H1A10SI\|C1A10SI" sim/tools/train/sts2train sim/tools/vec/sts2vec scripts/train` | **not found**（训练路径中没有）。只有 reportcard.py:3-4,67（评测报告默认集合）和 cli.py:329（probe 帮助文本） |
| 训练池的来源 | `grep -n -E "add_save\|add_fresh\|\.snapshot\(\|clone_determinized\(\|\.branch\(\|restore_many\(\|pool\.release" sts2train/*.py` | 训练池只由 startpool.py:150,340,344,349 构建。lookahead.py:256,456-467 与 reportcard.py:1146 作用于它们自己的 env。**未发现评测快照入池** |
| 模仿 / BC / 蒸馏 / ExIt 目标读取 | `grep -rn -i -E "imitation\|behaviou?r.?clon\|distill\|teacher\|searches\.jsonl\|expert\|bc_loss\|kl_anchor"` | **not found**。仅 lookahead.py:43,637,645,847 写 searches.jsonl，没有读取者 |
| 训练损失组成 | `grep -n "loss =" ppo.py` | ppo.py:553-556：`pg + vf_coef*vl − ent_coef*ent + aux_coef*(wl+cl)`，全部来自自己的 rollout |
| 评测 env 是否独立 | `grep -n "VecEnv(" ...` | evaluate.py:64（pool=None、"EVAL"）、lookahead.py:251、reportcard.py:1142 与训练 env（ppo.py:810）各自独立 |
| 评测结果回流方式 | `grep -n eval_seedset ppo.py` | 只用于 run_eval 和 best.pt 标记（ppo.py:963-993），是标量选择，不是数据 |
| harvest 分支 | 同上 grep，加 harvest.py 捕获规则 | 捕获限 `start_entry < base` 且非存档（harvest.py:14,43,225）。**未发现评测数据** |
| 产物 | `find S -name searches.jsonl`；`find S -name "*.save"` 按 run seed 归类 | searches.jsonl 共 0 个。.save 文件都属于 4 局人类对局、TW1NA10S01 真机孪生、测试夹具（C0VER27、OTHER0SEED、G1YFC196B2H2），**没有评测种子** |
| 存档池是否由评测种子构建 | 比对 sha 集合摘要、manifest 的 sessions_root | **not found**。所有运行都用 148 个录制存档，摘要 4b993c14567704db |

lookahead 用途确认：只作为评测策略。用过的地方有：s1b 在 dev-128、test-100、v1-20、A0-dev 上的行（`docs/lookahead-2026-11-04.md`），s7b u300 的 v1-20 行（MEM 04:05 条目），u2050 的 dev-128 行（`ARCH/gpu1/la-u2050-dev`，目录中没有 searches.jsonl）。ExIt 的启动信号未触发，代码里也没有 ExIt 训练路径。

## 决策审计（test-100 何时计算、何时被看到、是否影响决策）

| # | 首次出现（UTC / EST） | 检查点 | dev-128 | test-100 | 当时的决策 | 是否引用 test |
| --- | --- | --- | --- | --- | --- | --- |
| 0 | 04:46:55Z / 23:46 EST（11-03） | — | — | — | 计划文档预注册：dev 用于选择和调参，test 永不用于选择 | — |
| 1 | ≤08:07:56Z / 03:07 | s1b greedy；s1b + lookahead | 0.1556；0.1708 | 0.1620；0.1676 | 只跑了 dev 上选定的配置；ExIt 按 dev 判定为不启动 | 仅报告 |
| — | 08:30:11Z / 03:30 | — | — | — | MEM："按 dev-128 report-card 最终挑选" | — |
| — | 10:32:01Z / 05:32 | — | — | — | MEM：候选列表，按 dev-128 挑 | — |
| 2 | 10:35:00Z / 05:35 | u1300 | 0.329 | 0.327 | 继续评测其他候选 | 否 |
| 3 | 10:38:36Z / 05:38 | u925（v1 上的 best.pt）、u1275 | 0.283、0.325 | 0.295、0.313 | "Selection rule: dev-128; report test-100"，u1300 领先；备注"20-seed best 过拟合"（dev 和 test 都能看出） | 描述性 |
| 4 | 11:04:19Z / 06:04 | u1525 | 0.335 | 0.326 | 领先候选（按 dev） | 否 |
| 5 | 12:31:46Z / 07:31 | u1900（best.pt）、u2050 | 0.342、0.351 | **0.365**、0.353 | 按预注册规则选 u2050，test 更高的 u1900 被忽略；12:33Z 打包 u2050 | 否 |
| 6 | 12:59:15Z / 07:59 | s8b u2100、u2181 | 0.335、0.343 | 0.350、0.341 | u2050 仍为首选 | 否 |
| 7 | 14:07:01Z / 09:07 | u2536 | 0.363 | **0.373** | 14:07:26Z 打包（当时是 dev 第一） | 否 |
| 8 | 14:09:00Z / 09:09 | u2225（best.pt） | **0.372** | 0.346 | u2225 领先；u2536 记为 "runner-up (better test)" | **是**（标签） |
| 9 | 14:20:32Z / 09:20 | s8b u2625、u2475 | 0.365、0.350 | 0.369、0.344 | 最终挑选 u2225；文档写"Runner-up u2536 (test-100 0.373)" | **是**（标签） |
| 10 | 周期后（plateau-analysis，约 10:00–10:50 EST） | u925、u2225 的 test 探测 | — | 228 局合并 | 死亡位置、头部校准、"决定性检查用 dev+test 配对" | **是**（作为分析输入） |
| 11 | 15:50:12Z / 10:50 | s9b vs s9a A/B 规则 | — | — | 采纳 harvest 需要 test-100 Δ ≥ 0 | **是**（计划中的使用） |

判定：
- **预注册**：dev-128 作为最终挑选规则，test-100 只报告。这在任何 test 数字出现之前就已写下（第 0 行），并在谱系第一个 test 数字之前两次重申。
- **暴露**：12 个谱系检查点加 s1b 的 2 行；第 8、9 行的标签；第 10 行的分析；第 11 行的计划用途。
- **偏离**：best.pt 实际在 20-seed 上选，不是计划说的 dev-128。
- **没有**任何记录显示 test-100 改变了挑选、停训、超参数或谱系选择。

复现：`python3 -I SCR/scripts/test100_events.py T > SCR/out/test100_events.tsv`（dev 版为 `dev128_events.py`）；`SCR/out/decision_timeline.txt`。

## 证据位置

| 事实编号 | 文件/命令 | 关键字段或行号 |
| --- | --- | --- |
| 1 | `REPO/sim/tools/train/sts2train/startpool.py`；`ppo.py`；`REPO/sim/tools/vec/sts2vec/pool.py`；`SCR/out/run_configs.json`（`SCR/scripts/run_configs.py ARCH/gpu1 ARCH/gpu2`） | startpool 336-340、436-448；ppo 349-355、102、148；pool 134-137；seed=0、pool=4096、characters |
| 2 | `REPO/sim/Sts2Sim.Vec/StartPool.cs`；`REPO/sim/VEC.md`；`ppo.py` | 272-300（`template.Clone()`）；VEC.md 52-57；ppo 808 |
| 3 | `REPO/sim/Sts2Sim.Core/Random/RngSeeds.cs`、`DeterministicHash.cs`、`RngSets.cs`、`MegaRandom.cs`、`Run/Loop/RunLoop.cs`；`grep -rn CanonicalizeSeed REPO/sim` | RngSeeds 11-19、93-100；DH 28-34；RngSets 16-19、29-38、243-246；MegaRandom 21-24、55-61；RunLoop 58、89、115；Tests/RngTests.cs 644-645 |
| 4 | `SCR/scripts/sim_seed_identity.py` → `SCR/out/sim_seed_identity.txt` | equal=True/False、map buffers equal |
| 5 | `SCR/scripts/seed_intersections.py` → `SCR/out/seed_intersections.json` | intersections[]、populations.*.sha256_ok |
| 6 | 同上；`RngSeeds.cs` | hash_collisions_between_distinct_texts、min_circular_gap_log2=39.41；50-64、74-80 |
| 7 | `REPO/sim/tools/vec/sts2vec/protocol.py`；`git -C HARV diff 6e971aa -- sim/tools/vec/sts2vec/protocol.py` | 87、123-125；`_POOL_SEED_RE` |
| 8 | `HARV/sim/tools/eval/seedsets/silent-a10-ab-s512.json`；`git -C HARV show --stat a570373`；`SCR/out/leakage_greps.txt` | games TRAIN0-SILENT-4096..4607；G7（没有重叠守卫） |
| 9 | `S/next/analysis/seedsets/trainunseen128.json`；`S/next/analysis/scripts/run_trainseed_probes.sh`；`SCR/out/diag_sets_overlap.txt` | 16 个落在 ab-512 内 |
| 10 | `REPO/sim/tools/vec/sts2vec/evaluate.py`；`ARCH/gpu1/runs/s8a-silent-a10-mid-upg3/config.json`；`SCR/out/leakage_greps.txt` | 62-68；start_pool.pool_entries=6316、fresh_entries=4096、save_entries=2220；G2、G5 |
| 11 | `SCR/out/saves_inventory.json`；`SCR/out/part_boundaries.json`；`REPO/tmp/train/saves-silent-a9/manifest.json` | by_run、distinct_sessions=34、sha256_all_match=true、act2plus_saves_by_run；final_result |
| 12 | `REPO/scripts/train/collect_start_saves.py`；manifest.json | 7-24；sessions_root、filters |
| 13 | `startpool.py`；`RunImporter.cs`；`Determinizer.cs`；`ARCH/*/runs/*/config.json`；bundle 目录 | 73-75、222、336-349；151、155；106-169；copies_per_save=15、pending_prompt；sha 摘要 4b993c14567704db |
| 14 | `RunImporter.cs`；`startpool.py`；`SCR/out/seed_intersections.json` | 151；73-75 |
| 15 | `SCR/scripts/part_boundaries.py` → `SCR/out/part_boundaries.json`；`SCR/out/edit_epochs.json`；`SCR/scripts/boundary_fulldiff.py` → `SCR/out/boundary_fulldiff.txt` | boundaries[].kind、diff_fields；parts[].result（part 15/19/22）；逐叶子差异的路径 |
| 16 | `SCR/out/a43u_parts.txt`；`SCR/out/edit_epochs.json` | saves_after_first_edit_by_act；pool entry shares |
| 17 | `SCR/scripts/win_sources.py`、`lineage_wins.py` → `SCR/out/win_sources.txt` | fresh 967,401 / 0；save 525,017 / 26,575 |
| 18 | `startpool.py`；`ARCH/gpu1/runs/s8a-silent-a10-mid-upg3/metrics.jsonl` 键 | 363-393；没有 per-run 或 per-save 的胜利字段 |
| 19 | `SCR/out/leakage_greps.txt`；`ppo.py`；`lookahead.py`；`HARV/.../harvest.py` | G1-G8；553-556；645、847；14、43、225 |
| 20 | `ppo.py`；`ARCH/*/runs/*/evals.jsonl`；`REPO/docs/train-plan-silent-a10-2026-11-03.md` | 953-999、984-993；seedset=silent-a10-v1（91 次）；269、285-286 |
| 21 | `SCR/scripts/selection_bias.py` → `SCR/out/selection_bias.txt`；`REPO/docs/train-results-silent-a10-2026-11-04.md` | 24-37 |
| 22 | `REPO/docs/lookahead-2026-11-04.md`；T L3965；`S/colab/final_eval_remote.sh`；`SCR/out/test100_events.tsv` | 9-10、44-53；--seedsets v1,dev,test |
| 23 | `REPO/docs/train-plan-silent-a10-2026-11-03.md`；`git -C REPO log -1 --format=%cd e80ea22`；T L4111、L4433 | 55、269-270、367、1183 |
| 24 | `SCR/out/test100_events.tsv`、`SCR/out/dev128_events.tsv`；MEM 07:35/08:05/09:10/09:25 条目；T L5632 | 各检查点 dev/test |
| 25 | T L5389、L5433、L5894；`REPO/docs/train-results-silent-a10-2026-11-04.md`；`S/next/analysis/plateau-analysis.md`；`S/next/scout/next-training-design.md` | 结果文档 5、35、40；plateau 68、274、405；scout 524 |
| 26 | 对各工作树执行 `cmp`（`for w in worktrees/*/ ... cmp`），以及 bundle 的 `seedsets/`；`SCR/out/eval_seedset_ids.txt` | 234 / 10 / 8 个评测目录的哈希 = 冻结值 |

## 尚不确定之处

1. **训练胜利按存档或按对局的归因**：日志里没有，只有 fresh/save 汇总。要确定，需要在 `startpool.split_stats` 中按 `SaveStart.provenance.run_seed` 输出 `save_wins_by_run`、`save_episodes_by_run`、`save_wins_by_start_act`，见下一节。
2. **是否有没回传主会话的 test-100 计算**：本审计只覆盖主会话记录、MEM、docs、本地 scratchpad 和两个归档。子代理各自的会话记录以及 raven 上 Owner C 的输出目录没有检查。要确定，需要检查 raven 上的 lookahead 输出目录和子代理记录；之后改用"test 访问账本"。
3. **s9a 的种子池和存档**：它的 config.json 和 metrics 已随运行时丢失，只能从 u2225 检查点的 config（seed 0、pool 4096、Silent A10、saves 0.35）和启动参数（T L5654，没有 `--pool` 或 `--seed`）按 resume 语义推断。
4. **编辑是怎么做的、出于什么目的**：数据只能证明"读入的存档不等于游戏最后写出的存档，且只有玩家字段不同"。死亡后如何恢复 current_run.save 不可知。之前"19 次战斗中退出重来"的说法与本审计不一致：本审计数到 20 个边界在战斗中结束（15 次原样重来 + 5 次伴随编辑）。
5. **harvest 分支的捕获规则**：只读了代码和文档字符串，没有运行 `test_harvest.py`。
6. **编辑后存档对策略的影响**：没有测量。最直接的方法是同起点、同步数、`--exclude-edited` 与不排除各跑一条做对照。

## 从现在起的记录与防护方法

1. **隔离守卫（训练启动时）**：
   - 把训练池的 (角色, 文本, 难度) 和 XxHash64 run seed，与 `seedset_dirs()` 中所有冻结集合（含 dev/test/ab）求交；
   - 交集非空就拒绝启动；
   - 结果写入 config.json，例如 `isolation: {checked: [...ids], overlaps: 0}`。
   - 这样可以直接防住 `--seed 0 --pool >4096` 吞掉 ab-512 的情况。
2. **胜利与种子归因（每次更新写进 metrics）**：
   - `save_wins_by_run`、`save_episodes_by_run`、`save_wins_by_start_act`；
   - `fresh_unique_entries_started`，以及累计的 `fresh_unique_entries_seen`。
3. **test 访问账本**：
   - 每次在 role=test 的集合上评测，就往一个只追加的 jsonl 写一行：UTC 时间、权重哈希、集合 id、目的（final/report）；
   - `final_eval_remote.sh` 默认不再跑 test，只对最终入选的 1 个检查点跑一次。
4. **新的保留集（Q8 前提）**：
   - 下一轮训练开始前，冻结一个新的 ≥256 局 Silent A10 盲测集。用新前缀（如 `B1A10SI0001–0256`），不得落在 `TRAIN*` 序列上，先提交 sha256；
   - 只在 checkpoint 冻结后跑一次；
   - 现在的 test-100 改称"暴露集"，按第二 dev 集使用。
5. **存档清单加编辑标记**：
   - `collect_start_saves.py` 在 manifest 中写入每个 run 的 Continue 边界类型（retry/edit/continue），以及每个存档继承的编辑数；
   - 提供 `--exclude-edited`；
   - 训练指标把"编辑后存档开局"单独分组。

## 复核（独立核查）

复核人：独立核查代理，2026-11-04。做法：逐条回到源码、归档、录制目录和会话记录重算，不复用原脚本；脚本另写在 `V/scripts/`，输出在 `V/out/`（`V` = `SCR/verify`）。全部只读：没有改仓库，没有训练，没有启动游戏；录制目录只读。本地重算均为 CPU、`nice -n 10`、单线程，sim 实测 12 个 env、一次 reset。

主要工具：
- 独立实现的 XXH64：`V/scripts/vxxh64.py`，与 C# 测试向量 `REPO/sim/Sts2Sim.Tests/RngTests.cs:514-524` 的 7 条全部一致；
- 种子集交集：`v_seedsets.py`；
- 流种子碰撞：`v_stream_collisions.py`；
- sim 实测：`v_sim_identity.py`；
- 运行配置：`v_runs.py`；
- 胜利来源：`v_wins.py`；
- 存档摘要：`v_savedigest.py`、`v_bundle_saves.py`；
- Continue 边界：`v_boundaries.py`、`v_fulldiff_check.py`、`v_saves.py`；
- test-100 事件：`v_test_events.py`、`v_tgrep.py`、`v_subagent_test_runs.py`；
- 评测目录：`v_eval_dirs.py`；
- 全部 `.save`：`v_all_saves.py`。

总体判定：**结论成立，需小幅更正**。下面四点都已独立复现：种子层面严格隔离；训练没有读取任何评测数据；test-100 已暴露，但没有驱动任何挑选；A43U 有 5 次存档改动。需更正的地方是时间先后的表述、个别范围或行号、两处把推断写成事实的措辞，以及一个不合法的新前缀示例。补充发现中最重要的一条：ab-512 已整套评测过 u2225，而且计划文档自己的 S1 配方 `--pool 32768 --seed 0` 会把它纳入训练池。

### 核实无误的事实

1. 事实 1：
   - 所有归档运行都是 seed=0。10 个 A10 运行和 3 个 A0 运行的 pool=4096，角色为 SILENT；t0 的 pool=2048，5 个角色，A0。
   - fresh 种子文本为 `TRAIN0-<角色>-i`，由 `startpool.py:336-340`、`sts2vec/pool.py:134-137`、`ppo.py:349-355` 生成。
   - u2225 检查点自带的 config 同为 seed 0、pool 4096。

   （`V/out/v_runs.json`；`S/deliverable/u2225/checkpoint/config.json`）
2. 事实 2：
   - fresh 条目按模板克隆：`StartPool.cs:272-301` `return template.Clone()`，模板来自 `NewTemplate` → `RunSim.NewRun`（`:358-362`）。
   - act1="random" 时选幕也由文本派生（`RunLoop.cs:58-69`）。
   - 断点恢复只换 seed_text `PPO{seed}R{upd}`（`ppo.py:808`）；slot 序列为 `(slot_seed + k) mod n`（`VEC.md:52-57`，`sts2vec/schema.py:398-400`）。
3. 事实 3：
   - run seed = 完整 64 位 XXH64（`RngSeeds.cs:11-19`，`DeterministicHash.cs:28-34`）。
   - 流种子 = seed + 流名哈希（`RngSets.cs:29-38,227-234`）。
   - MegaRandom 经 splitmix64 初始化（`MegaRandom.cs:21-24,44-61`），`Rng(ulong)` 直接传入（`Rng.cs:16-19`）。
   - 战斗 RNG 默认 Xxh64 方案（`CombatRunContext.cs:9`，`CombatRoom.cs:112`）。LegacyV18 的 32 位路径只出现在 `Sts2Sim.Diff` 和测试中。
   - `CanonicalizeSeed` 在测试以外没有调用者。

   （`grep -rn "LegacyV18|GetDeterministicHashCodeOld|CanonicalizeSeed" --include='*.cs'`）
4. 事实 4（独立实测，另加 0→O 变体）：
   - 同一文本两次开局：state_hash 与全部 37 个观测缓冲都相同。
   - I→1、0→O、小写、test/v1 的 I→1、A10 vs A0：map 与观测都不同。

   （`V/out/v_sim_identity.txt`）
5. 事实 5：
   - 9 个冻结集的 sha256 全部重算一致。
   - 评测集 × 训练池/人类存档在三个层级上交集都为 0。
   - 4 个人类 run seed 读自每个 `.save` 的 `rng.seed`，不是读 manifest。

   （`V/out/v_seedsets.json` intersections、frozen.*.sha256_ok）
6. 事实 6：
   - 不含诊断集共 6,658 个文本，含诊断集共 6,770 个，没有 64 位碰撞。
   - 最小环形间距 728,673,311,463（2^39.41），评测 vs 训练/人类的最小间距也是这个值。

   （`V/out/v_seedsets.json`）
7. 事实 7（正则与 HARV diff）、事实 8（ab-512 所在提交 a570373 为 11:11:24 EST；两条分支的 ppo/cli/startpool 都没有重叠守卫）：已核实。（`git -C HARV diff 6e971aa -- sim/tools/vec/sts2vec/protocol.py`；`V/out/v_plan_and_guard.txt`）
8. 事实 9：
   - trainunseen-128 为 `TRAIN0-SILENT-4096..8160`，步长 32，其中 16 个落在 ab-512 内。
   - u2225 与 u925 都在其上做过 probe，meta.json 记录 updates 2225/925。

   （`S/next/analysis/probe/u2225-trainunseen/meta.json`、`u0925-trainunseen/meta.json`）
9. 事实 10：
   - 评测 env 为 `pool=None`、`seed_text="EVAL"`，每局 `add_fresh`（`sts2vec/evaluate.py:62-72`）。
   - 10 个带存档运行的 start_pool 均为 6316 = 4096 + 2220。

   （`V/out/v_runs.json`）
10. 事实 11、12：
    - 148 个文件来自 4 局：101/18/15/14 个存档，22/6/3/3 = 34 个 part。
    - 楼层：A43U 1–48，TKQG 1–8，G7AH 1–9，K6DM 1–7。
    - 结局：A43U 胜；TKQG、K6DM 放弃；G7AH 在精英战死亡。
    - 全部 A9、v0.111.0，录制器 0.3.0/0.3.1，录制于 19:06:16–23:02:58 EST。
    - 148 个文件的 sha256 与 manifest 一致，并与 manifest `source` 指向的录制副本逐字节相同（0 个不一致）。
    - 构建器只读 `*/native/saves/*.save`。

    （`V/out/v_saves.txt`、`v_boundaries.json`；`collect_start_saves.py:7-24,185`）
11. 事实 13：
    - 10 个带存档运行 config 里 `start_pool.saves` 的 sha256 集合完全相同。
    - 仓库目录、bundle 26f1d805a0 和 8d9987467a 中的同一组文件，按复核自己的摘要方法都是 `b006a9cb2c9ed36f`。原文的 `4b993c14567704db` 是另一种摘要方法，结论相同。
    - 7 个带存档的 bundle（35424a862a、bf005c7355、ef70ee6273、e877036880、9f6608701d、a8d91cf636、26f1d805a0）也都是这一组，覆盖了所有 session 用过的 bundle。
    - 每个存档 15 份，pending_prompt=0。
    - 第 0 份为精确导入，RNG 来自存档快照（`RunImporter.cs:151`）；其余为 determinized 克隆（`Determinizer.cs:105-171`，`startpool.py:73-75,343-349`）。

    （`V/out/v_runs.json`、`v_savedigest.txt`、`v_bundle_saves.txt`）
12. 事实 15（独立全量叶子比对，忽略 save_time/num_reloads/run_time）：
    - A43U 的 21 个边界分为三类：
      - 5 个只差 `/players`：part 9 HP 23→50；part 14 HP 16→50、金币 287→487；part 16 升级 4→27（23 个叶子）；part 18 HP 27→51、+TWISTED_FUNNEL；part 20 +KUNAI。
      - 15 个重来：12 个完全相同；3 个只差 `can_modify` 从缺失变为 True，分别在 part 2、11、19，即每幕第一次重载。
      - 1 个普通继续：part 21。
    - num_reloads 每个边界恰好 +1，从 0 到 21。
    - `run_ended` 的死亡在 part 15、19，胜利在 part 22。
    - 其余 3 局：TKQG 4 次重来，外加 1 次奖励界面继续（part 4 的最后一份存档是 `combat_exit`）；G7AH、K6DM 各 2 次重来。没有编辑。

    （`V/out/v_boundaries.json`、`v_fulldiff_check.txt`）
13. 事实 16：
    - A43U 编辑后存档 67 个：Act1 3、Act2 34、Act3 30。
    - 第一次死亡（part 15）之后的存档 47 个。
    - Act2 存档平均 27.3 张牌、5–13 个遗物；Act3 存档平均 31.8 张牌、13–19 个遗物。
    - 池份额：24.0%（1515/6316），编辑后 15.9%（1005/6316）。

    （`V/out/v_saves.txt`）
14. 事实 17、18：
    - u2225 谱系 u1–2225 共 2,225 行 metrics：fresh 967,401 个 episode，0 胜；save 525,017 个 episode，26,575 胜。
    - 10 个 A10 运行的 fresh 胜利都是 0。
    - metrics 没有按存档或按 run 统计胜利的字段，只有 `save_episodes_by_start_act` 等。

    （`V/out/v_wins.txt`；`startpool.py:363-393`）
15. 事实 19：以下复现结果均为"未发现"。
    - G1–G6 的 grep 已在两条分支上复现。
    - `searches.jsonl` 在 scratchpad、`REPO/tmp` 和两个归档中都是 0 个。
    - 主会话和 64 个子代理记录中，没有任何一次实际执行带 `--record` 的 lookahead，命中的都是提交信息或用法文档。
    - 损失为 `ppo.py:553-556`。
    - scratchpad 中所有 `.save` 的种子都属于原文列出的 4 局、TW1NA10S01 或测试夹具。

    （`V/out/v_all_saves.txt`）
16. 事实 20、21 的数字：
    - 谱系周期评测 91 次（6+9+76），0 次报错，全部在 silent-a10-v1 上。
    - 计划原文 `:269,285-286` 规定训练中用 dev-128 选 best.pt。
    - 按结果文档 `:26-37` 重算：+0.077 / +0.028；u2225 的 dev−test = +0.026，为 12 个中最大；12 个平均 −0.00075。
17. 事实 22：
    - 主会话中 test-100 结果行恰好 12 个检查点，首次出现在 10:35:00Z（L4497），最后在 14:20:32Z（L5472），每次都与 dev-128 同行打印。
    - s1b 的 test 数字来自提交 5c6ba5d（03:07:27 EST）的 lookahead 文档，主会话 L3965（08:07:56Z）只转述了 Δ +0.0057。
    - 子代理记录印证："held-out confirmation of the dev-selected config"，raven queue3 于 07:10:34Z 排队，见 agent-a023ce674c874a6be。

    （`V/out/v_test_events.txt`、`v_subagent_test_runs.txt`）
18. 事实 23–25：
    - 计划 e80ea22（04:46:55Z）的行 55、269-270、367、1183 与原文所述一致。
    - 04:46Z 之后没有 M3 决策记录；`\bM3\b` 只命中一个 base64 片段。
    - 每次挑选都等于当时 dev-128 的最大值。若按 test 挑，会选 u1900 或 u2536。
    - L5632 没有引用 test。
    - 结果文档第 5 行的 runner-up 标签、plateau 第 68、274、405 行、MEM 10:55 条目和 scout 第 524 行的 test-100 用法都已核实。
    - test probe 的 meta.json 生成于 09:41:23 和 09:41:57 EST。
19. 事实 26：
    - 种子集文件在含有它们的全部 17 个工作树中逐字节相同。
    - 各 bundle 中的同名文件与仓库一致。
    - 归档中 234、10、8 个评测目录的内容哈希都等于冻结值。

    （`V/out/v_eval_dirs.txt`、`v_bundle_saves.txt`）

### 需更正的事实（原文 → 更正，证据）

1. 事实 20："所有运行的 eval_seedset 都是 silent-a10-v1" → 只有 10 个 A10 运行是 silent-a10-v1。A0 运行 s1a、s4a、s5b 是 silent-a0-v1，t0 是 std-a0-v1。（`V/out/v_runs.json` 字段 eval_seedset）
2. 事实 23："以上都早于第一个 test-100 数字（≤08:07:56Z）和谱系的第一个 test-100（10:35:00Z）" → 只有计划提交（04:46:55Z）早于 s1b 的 test 数字。
   - MEM 的两次重申晚于 s1b 的 test 数字：分别在 08:30:11Z（L4111）和 10:32:01Z（L4433）。s1b 的 test 数字在主会话为 08:07:56Z，文档提交为 08:07:27Z。
   - 这两次重申只早于谱系的第一个 test-100（10:35:00Z）。
   - 结论不变：规则在任何 test 数字出现之前就已写下。s1b 的 test 行属于另一个检查点，并且只跑了按 dev 选定的配置。

   （`V/out/v_decision_grep.txt`；`git log 5c6ba5d`）
3. 事实 10 与泄漏表："只有 `startpool.py:150,340,344,349` 会往训练池加条目" → 第 150 行写入的是 `probe_saves` 自建的 1-slot 探测 env，即 `VecEnv(1, pool=None, seed_text="SAVEPROBE")`（`startpool.py:129`），不是训练池。训练池条目只来自 340（fresh）、344（存档导入）、349（determinized 克隆）。
4. 事实 19 与 lookahead 一节："s1b 在 dev-128、test-100、v1-20、A0-dev 上的行" → A0-dev 这一行用的是 A0 检查点 s1a best（u75），不是 s1b。（`REPO/docs/lookahead-2026-11-04.md:16-17,71-75`）
5. 事实 20："评测对训练的唯一反馈是标量选择" → 代码层面的自动反馈确实只有 best.pt。但流程层面还有两处用到了评测数字：
   - 依据 v1-20 停掉 s7a（large）、改用 mid 配方（MEM 02:58、03:20 EST 条目）；
   - s9a 从 dev-128 选出的 u2225 续训（T L5654）。

   这不是数据泄漏。但它意味着今后 s9 系列相对 u2225 在 dev-128 上的比较本身带有挑选条件，dev-128 同样不是中立的。
6. 事实 21："也就是说，u2225 的 dev 分数本身带有挑选偏差" → 数字无误，因果说法过强。
   - u2225 的 dev−test = +0.026，按两侧 CI 半宽 0.024/0.031 估算差值的 SE ≈ 0.020，z ≈ 1.3。这符合"赢家诅咒"，但不能算已证实。
   - v1-20 一组（+0.077 vs +0.028，约 2σ，未计相关性）的证据更强。
7. 事实 14："不可能重合" → 第 0 份精确导入沿用人类种子，与评测种子在 64 位上已确认不同，这部分成立。determinized 副本的新流种子是伪随机 64 位值，应表述为"概率可忽略（约 1e-9 量级）"，而不是逻辑上不可能。复核另查了跨用途的流种子，也没有碰撞（见补充 G）。
8. 事实 5 / 结论 1："唯一的非零交集在训练池内部" → 原文自己的 `SCR/out/seed_intersections.json` 中还有 smoke-a0-v1 ⊂ std-a0-v1（10 个，都是 A0 多角色评测集，与 Silent A10 无关）。若算上诊断集，还有 trainseen-128 ⊂ 训练池（设计如此）和 trainunseen-128 ∩ ab-512 = 16。都不影响"评测 × 训练 = 0"。
9. 事实 8 的证据指针："`leakage_greps.txt` G7（没有重叠守卫）" → G7 只列了评测集引用和捕获规则。没有守卫的直接证据是 `V/out/v_plan_and_guard.txt`：在两条分支的 ppo/cli/startpool 上 grep `overlap|isolat|intersect|disjoint`，0 命中。
10. 决策表第 10 行"约 10:00–10:50 EST" → test-100 probe 生成于 09:41 EST（meta.json created），`plateau-analysis.md` 的 mtime 为 10:43 EST。事实 25(a) 中 runner-up 标签最早出现在 14:09:44.937Z（T L5417，写入 MEM 的命令），比 L5433 早 8 秒，可以忽略。
11. "从现在起" 第 4 条的示例前缀 `B1A10SI0001–0256` 为 11 个字符，不符合 `^[A-Z0-9]{1,10}$`（`protocol.py:87,124-125`），`validate_seedset` 会拒绝。可以改用 10 个字符以内的写法，例如 `B1A10SI001`–`B1A10SI256`；若需要 1,000 局以上，可用 `B1A10S0001`–`B1A10S1024`。

### 无法核实的事实

1. raven 上 Owner C 的 lookahead 输出目录：没有访问服务器。子代理记录只显示 test-100 上跑了 dev 选定配置和 greedy，没有其他排队。原文"尚不确定"第 2 条的子代理部分已由复核补上（64 个子代理记录，见 `V/out/v_subagent_test_runs.txt`），raven 部分仍未核实。
2. s9a 实际使用的池：它的 config 和 metrics 已随运行时丢失（MEM 11:20 EST 条目）。以下三点支持原文的推断，但仍不是 s9a 自身的记录：
   - 启动参数（T L5654）显式给出 `--start-saves <26f1 bundle>/saves`、`--save-mix 0.35`、`--eval-seedset silent-a10-v1`，没有 `--pool` 或 `--seed`；
   - `run_train.sh` 写明"flags given here override the checkpoint's"；
   - u2225 的 config 为 seed 0、pool 4096。
3. 编辑是怎么做的、出于什么目的；编辑后存档对策略的影响；`test_harvest.py` 是否通过（复核也没有运行）；按 run 或按存档的胜利归因（日志不存在）。

### 补充发现

A. **ab-512 已经整套评测过，不再"未触碰"。**
   - 11:15–11:17 EST，用 report-card（greedy + loop breaker）在 512 局上评测了 u2225：progress 0.368 [0.355, 0.380]，0 胜，230 局到达 Act 2。
   - 这是 A/B 基线，属于 ab 角色的本来用途。但不能再拿它当"新种子泛化"的盲测集。
   - 原文说"16 个种子已被触碰"，低估了暴露程度。

   （`S/next/ab-baseline/u2225-ab512/`；MEM 11:48 EST 条目）
B. **计划自己的 S1 配方会吞掉 ab-512。**
   - 计划写的是 `--pool 32768 --seed 0`（`train-plan…:195,230,701`），对应 `TRAIN0-SILENT-0..32767`，完整包含 ab-512（4096..4607）和 trainunseen-128（4096..8160）。
   - 计划中"训练种子含 '-'，不可能与评测种子相同"这一前提（`:251-253`）已被 train/harvest 放宽正则打破。
   - 因此事实 8 的风险不是假设：只要有人回到计划配方，就会发生重叠。
C. **训练种子方面还有一处偏离计划。**
   - 计划规定每个 session 用 32,768 个 fresh 种子，并用 `--seed k` 轮换前缀（`TRAIN{k}`，phase 1 约 20 万个不同种子；`:196,251-253,1179` 风险 #9）。
   - 实际所有运行、所有 session 都是 seed 0、pool 4096，所以针对"种子记忆"的缓解措施没有执行。
   - 原文只记录了 best.pt 选集的偏离。

   （`V/out/v_runs.json`、`v_plan_and_guard.txt`）
D. **只有 bundle 26f1d805a0 和后来的 8d9987467a 带 dev/test 种子集。** 此前 6 个带存档的 bundle 只带 v1-20、std 和 A0 集。这与那些 session 训练中只评测 v1-20、没有评测 dev-128 的情况一致。（`V/out/v_bundle_saves.txt`）
E. **test 暴露有结构性来源。**
   - `reportcard.py:67` 的 `DEFAULT_SEEDSETS` 默认包含 silent-a10-test-v1；
   - `S/colab/final_eval_remote.sh:16` 显式传入 v1、dev、test；
   - 所以每个候选都会顺带算出 test-100。

   低成本修法：把 test 从默认集合中去掉，改为显式开关，并配合 test 访问账本。这一条属于实现层面的小修，原文结论 4 中"不需要修复训练实现"应限定为"不需要修复训练算法"。
F. **训练见过的种子与没见过的种子（各 128 局，不配对）：** u2225 为 0.363 vs 0.354，u925 为 0.298 vs 0.299。在约 ±0.027 的分辨率下，看不出对 4,096 个训练种子的记忆差。（`S/next/analysis/probe/*-trainseen|*-trainunseen/probe.txt`）
G. **跨局流种子：** 6,770 个文本 × 16 条命名流（12 条 run 流、3 条 player 流和 act_selection），共 108,320 个流种子，全部互不相同。跨用途的流重合也不存在。（`V/out/v_stream_collisions.txt`）
H. **录制目录现在有 236 份存档副本。**
   - 除这 148 份外，另有第 5 局人类对局 WWVGBDEYNG19 的 88 份：Silent A10，录制器 0.4.0，15 个 part，胜利，开局于 2026-11-04 04:43:37Z。
   - 这局比存档池 manifest（04:34:51Z）晚 9 分钟开始，不在任何训练池或评测集中。
   - 将来可以用作 A10 人类参照，或者作为隔离的人类存档集。
I. **来源对局都只装了录制器。** 4 局来源对局的全部 34 个 part，manifest 的 `mods` 都只有 `Sts2Recorder@0.3.x`，没有 STS2_MCP，符合 CLAUDE.md 的人类数据来源规则。（`V/out/v_boundaries.json` parts[].mods）
J. **A0 运行 s5b 有 1 次 fresh 开局胜利**（111,035 个 fresh episode 中），是全部归档里唯一的 fresh 胜利；所有 A10 运行都是 0。所以"训练胜利全部来自存档开局"只对 A10 成立。（`V/out/v_wins.txt`）
K. **cycle 之后 test-100 仍在被用于分析。**
   - plateau 的 "decisive check" 建议用 dev+test 配对比较（`plateau-analysis.md:274`），同文 C12（`:405`）也承认这会消耗保留集；
   - 本轮诊断工作流（wf_1b457b53-15a，agent-a844b0bab3a1b8c9c，17:06Z）也读取了 dev+test 的评测对局文件。

   这进一步说明 test-100 只能当暴露集使用。
L. **`REPO/tmp/fid-int/in` 下有另外 39 个种子的存档**（fidelity 输入，A0/A10），都不在任何冻结评测集中，也不在训练存档集中。原文限定在 scratchpad 范围内的枚举是正确的。（`V/out/v_all_saves.txt`）

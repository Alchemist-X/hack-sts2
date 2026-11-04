# Q9b 断点恢复完整性核查（u2225、s8a/s8b 分段续训、s9a）

范围：恢复时是否完整还原优化器、学习率调度、归一化统计和 RNG；哪些状态没有还原；没还原的部分会不会被误读成训练效果。
代码基准：HEAD 8c9455f。它和 Colab 包 26f1d805a0 的训练器源码逐字节相同（事实 15）。
本文路径前缀约定：`$Q` = `/private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/diag/q9-resume`，`$R` = `/Users/Aincrad/dev-proj/hack-sts2/.claude/worktrees/recorder-performance-lag-0f9d6d`，`$E` = `$Q/extract`（我自己从 `runs/archive/*.tar.gz` 解出的 run 级文件）。

## 结论

- **完整恢复**：模型权重；Adam 的一阶矩、二阶矩和 step（u2225 的 step = 71,200 = 2225 次更新 × 32 个 minibatch）；全部计数器；best_eval / last_eval；numpy、torch CPU 和 CUDA 的 RNG 状态。lr 会被 `cfg.lr` 覆盖：没传 `--lr` 时就是检查点里的值。
- **无需恢复，代码里本来就没有**：学习率调度或 anneal（lr 恒定，s9a 名字里的 "anneal" 其实是一次性把 lr 改成常数 1.5e-4）；归一化运行统计（优势按本批标准化，没有观测或回报归一化）；AMP GradScaler（bf16 不需要）；Python `random`（没有使用）。
- **不恢复，每次续训都重建**：环境。2048 个槽位上正在进行的对局全部丢弃，所有槽同时开新局；起始序列用 `seed_text = "PPO{seed}R{upd0}"` 重新播种，每槽一个新的伪随机偏移。另外还有每槽的奖励、塑形和辅助目标状态，以及 torch.compile 图。
- **CPU 实证**：同一检查点、同一组 flag 恢复两次，2 次更新和 60 次更新都逐位一致。去掉 RNG、去掉 Adam 状态、改 `--seed` 都会改变结果。但"恢复 1 次更新再恢复 1 次"和"连续跑 2 次"在第 2 次更新就有 46 个字段不同，说明**恢复 ≠ 不中断续跑**，差异全部来自环境重启。
- **对解读的影响**：每次恢复后前 4–5 次更新的训练指标会明显失真。第 1 次更新只有存档起点的局能结束；第 2–3 次 fresh progress 只有 0.12–0.16；按局加权的 progress 比恢复前 25 次低 0.048–0.064；含这 5 次的 25 次更新窗口会低约 0.008–0.010。s9a 第一行（184 局、fresh 0、progress 0.341、熵 0.876）是这种瞬态，不是 lr/ent 改动的效果。20-seed eval 看不出恢复点的跳变，噪声太大。

## 已确认事实

1. 训练检查点由 `make_checkpoint` 写出，内容是：format、spec、config、arch、model（state_dict，含持久 buffer）、weights_hash、optimizer（Adam state_dict）、updates、env_steps、counters、rng、params、encoder_version、saved_at、heads、best_eval、last_eval。u2225 实际加载出来正好是这 17 个键（`ppo.py:269-277`；`$Q/out/ckpt-u2225-summary.json` 字段 `keys`）。
2. u2225 的计数器：updates 2225，env_steps 291,635,200，episodes 1,492,418，wins 26,575，wall_s 20,459.5，sessions 5。另有 saved_at 2026-11-04T13:12:07Z，params 2,904,208，encoder_version 3b73317739c650bd，后者与本地库一致（`$Q/out/ckpt-u2225-summary.json` 字段 `counters`/`saved_at`/`params`）。
3. 优化器按 `Adam(model.parameters(), lr=cfg.lr, eps=1e-5)` 创建。u2225 中 171 个参数里有 170 个带状态（exp_avg、exp_avg_sq、step），step 全部为 71,200 = 2225 × 32（每次更新 2048×64/4096 = 32 个 minibatch，epochs 1）。也就是说，s6b→s7b→s8a 这条谱系上的每次 fork 和续训都没有重置过 Adam。唯一没有状态的参数是 `kind_emb.weight`：它在 `model.py:177` 定义，但前向里没有用到，所以从来没有梯度。这与恢复无关（`ppo.py:877`；`$Q/scripts/adam_coverage.py` 输出 `missing_state: ['kind_emb.weight'], steps_per_update: 32.0`）。
4. 恢复时 `restore_checkpoint` 先严格加载权重，再整体加载 Adam state_dict（含 betas 和 eps），然后把每个 param_group 的 lr 改写为 `cfg.lr`（`ppo.py:280-295`，覆盖 lr 在 `291-293`，调用在 `879`）。
5. 训练器里没有任何 lr scheduler 或 anneal。lr 只在 `ppo.py:877`（创建）和 `ppo.py:291-293`（恢复后覆盖）写入，每行 metrics 都记录 lr（`ppo.py:1237`）。s8a 的 2186 行 metrics 中 lr 全是 0.0003。ent_coef 同样是常数（`ppo.py:610`，损失在 `ppo.py:553`），并且不写入 metrics。s9a 的 run 目录叫 `s9a-silent-a10-u2225-anneal`，实际做法是续训时传 `--lr 0.00015 --ent-coef 0.015`，此后保持常数（`grep -n "param_groups|lr=|scheduler|anneal" sts2train/*.py` 只命中 292/877/879/1237；`$E/sts2train/runs/s8a-silent-a10-mid-upg3/metrics.jsonl` 字段 `lr`；会话记录中的 s9a 启动命令）。
6. 没有需要恢复的归一化运行统计。优势在每次更新内用本批的均值和标准差标准化（`ppo.py:1151-1154`）；回报和价值目标不做归一化（`ppo.py:1135-1143`）；没有观测归一化。模型只有 LayerNorm（可学习参数，在 state_dict 里）和由 spec 生成的常量索引 buffer（`model.py:135,138,238,202-205,282-284`），没有 BatchNorm 或 running buffer（grep `register_buffer|BatchNorm|running_`）。
7. RNG 部分：检查点保存 numpy PCG64 状态、torch CPU 状态，以及 CUDA 各卡状态（MPS 设备时也保存 MPS 状态）。u2225 的 rng 键为 numpy、torch、cuda（1 卡）。恢复时这些状态全部写回，但 CUDA/MPS 的写回包在 `contextlib.suppress(Exception)` 里，失败时不报错。训练器和 sts2vec 都没有使用 Python `random`（`ppo.py:321-343`，抑制异常在 `338-343`；`$Q/out/ckpt-u2225-summary.json` 字段 `rng`；grep `import random` 无命中）。
8. 计数器、best_eval、last_eval 会恢复，sessions 加 1（`ppo.py:797-806`）。best_eval 会带进新目录：本地 A 运行（新目录）的 `status.json` 中 best_eval = u1900 的 0.41348（s8a 在 silent-a10-v1 上的最佳）。按 `is_new_best` 规则，s9a 这类新目录只有在某次 eval 严格高于 0.41348 时才会写出 best.pt（`ppo.py:499-505,991-993`；`$Q/runs/A/status.json` 字段 `best_eval`）。
9. 以下状态不在检查点里，恢复时重建：新建 VecEnv（`ppo.py:810`）；`env.reset()` 让全部槽位从新局开始（`ppo.py:1029`）；起始序列 `seed_text = f"PPO{cfg.seed}R{upd0}"`（`ppo.py:808`）；每槽的 EpisodeRewards、AcqShaping 势函数、AuxTargets、LoopBreaker 状态（`ppo.py:843-866,1030-1033`）；torch.compile 图在每个会话首次更新时重新编译（`ppo.py:578-587,882-883`）。AMP 只用 bf16 autocast，没有 GradScaler（grep 无命中）。模块 docstring 也写明 "the env restarts from fresh episodes"（`ppo.py:20-22`）。
10. 起始序列：槽 i 的第 k 局从池条目 `(XXH64(seed_text‖i) + k) mod pool_count` 开始（`VEC.md:52-57`，`sts2vec/schema.py:398-400`）。续训后 k 从 0 重新计数，seed_text 换成 `PPO0R{upd0}`，所以每槽得到一个新的伪随机偏移：既不回到位置 0，也不接着原来的位置。我用本地 16 槽 VecEnv 实测，16/16 个槽与公式一致；其中槽 0 多出 2072 的偏移，正好是建池时在槽 0 上做 restore 的次数。对 2048 槽：PPO0R1553、PPO0R2050、PPO0R2225 三个恢复点首局落在存档条目的比例为 0.356、0.343、0.345；2050 与 2225 两个恢复点首局条目相同的槽数为 0（`$Q/out/start_sequence.json`）。
11. 起始池在各次恢复之间不变：4096 个 fresh 条目（种子 `TRAIN0-SILENT-i`）加 148 个存档各 15 份，共 6316 个条目，有效存档比例 0.351488。s8a 第 5 会话的池（saves_dir 是 9f66 包路径）、26f1 包的存档、本地 `saves-silent-a9`，这三处的 148 个文件名和 sha256 完全相同；本地 A 运行的池布局与 s8a 一致。池的构造由 seed 和存档内容确定性决定（`startpool.py:11-21,73-75,308-353`；`$E/.../s8a.../config.json` 字段 `start_pool`；`$Q/runs/A/config.json` 字段 `start_pool`）。
12. 配置覆盖规则：
    - train 子命令设了 `argument_default=argparse.SUPPRESS`，只有命令行显式给出的 flag 才进入 `given`。
    - `resolve_config` 的优先级为：默认值 < 检查点 config（`allow_big_local` 永不继承）< preset < given。
    - 架构参数与检查点不一致时直接拒绝恢复。
    - `--start-saves none` 可以在续训时关掉存档起点。
    - 没给出的其余参数（lr、ent_coef、seed、threads、max_hours、start_saves 路径、eval 设置等）全部沿用检查点的值。
    - Colab 的 `run_train.sh` 总会追加 `--out RUN_DIR --resume auto --threads N`（以及 `--envs`）。

    依据：`cli.py:16,132-133,435-447`；`ppo.py:171-186,743-772`；`bundle-26f1/.../run_train.sh` 的 `ARGS=(...)`。
13. 沿用检查点值的一个例子是 start_saves 路径：u2225 的 config 中为 `/content/sts2/sts2train-9f6608701d/saves`。本地不传 `--start-saves` 续训时，1 秒内报 `FileNotFoundError: --start-saves /content/sts2/sts2train-9f6608701d/saves: not a directory`，会明确失败，不会悄悄丢掉存档起点（`$Q/runs/H_stale_saves.stdout`；`startpool.py:53-58`）。
14. 运行目录记录：
    - 同目录续训时，metrics.jsonl 和 evals.jsonl 中 update > upd0 的行会被删掉并重做（`runlog.py:105-115`；`ppo.py:896-897`）。
    - config.json 的 `sessions[]` 只记录 host、device、bundle、resumed_from 等，不记录本会话传入的 flag；顶层 config 只保留最近一次会话的配置（`ppo.py:905-931`）。
    - 新目录里的 sessions 从本会话开始：A 运行只有 `[(6, 2225, ckpt-0002225.pt)]`（`$Q/runs/A/config.json`）。
    - metrics 行里没有 session 字段。
15. 代码一致性：
    - 26f1d805a0 包 wheel 中的 ppo、cli、runlog、startpool、objective、model、heads、loopbreak、features、evaluate.py 与 HEAD 逐字节相同（`cmp`）。
    - `git diff 9f6608701d 26f1d805a0 -- sim/tools/train/sts2train/ppo.py` 为空。
    - `git diff e877036880 26f1d805a0 -- .../ppo.py` 只有新增的 loop breaker，恢复逻辑没变。
    - sim 源码在 9f66→26f1 和 26f1→HEAD 之间都没有差异（`git diff --stat ... -- sim/Sts2Sim.Core sim/Sts2Sim.Vec sim/tools/vec`）。
16. CPU 实证（确定性）：从 u2225 用相同 flags 恢复两次（A、B，各 2 次更新，参数 `--envs 16 --rollout 8 --minibatch 64 --compile off --eval-every 0`、存档 + mix 0.35、3+3 线程、nice 10，每次墙钟 27–31 s）。metrics 2/2 行、66 个非计时字段逐位相同。终点检查点 weights_hash 都是 bf5a0091a3d6dcb2，Adam step 都是 71,204，numpy/torch RNG 哈希相同。60 次更新的版本（A60/B60）也是 60/60 行逐位相同，其间完成 31 局（18 局 fresh、13 局存档起点）（`$Q/out/cmp-A-B.json`、`$Q/out/cmp-A60-B60.json`、`$Q/out/ckpts-A-B.json`）。
17. CPU 实证（RNG 确实来自检查点）：把检查点的 rng 置空（G）后，首行有 19 个字段不同，终点 numpy 和 torch RNG 哈希都与 A 不同。所以 A/B 一致靠的是恢复的 RNG 状态，而不只是用 cfg.seed 重新播种（`$Q/out/cmp-A-G_norng.json`；`$Q/out/ckpts-all.json`）。
18. CPU 实证（`--seed`）：CLI 有 `--seed`（`cli.py:146`）。续训时加 `--seed 1`（C），首行有 20 个字段不同，例如 reward_mean 0.002145 对 0.001685。终点 numpy RNG 与 A 相同，torch RNG 不同。原因是续训时 `--seed` 不改变恢复的 RNG（`ppo.py:794-795` 先播种，`879` 再覆盖），改变的是环境：seed_text、fresh 池种子前缀 `TRAIN{seed}` 和存档副本种子都变了，等于换了一套训练对局（`ppo.py:353,808`；`startpool.py:73-75`；`$Q/out/cmp-A-C_seed1.json`）。
19. CPU 实证（优化器状态有实际影响）：CLI 没有丢弃优化器状态的选项，我在 scratch 里做了一个 `optimizer={}` 的临时检查点（D）。`plan_run` 只检查键存在，`restore_checkpoint` 遇到空状态会跳过，于是得到一个全新的 Adam。对比 A：
    - 首行 reward_mean 相同（同一次 rollout），有 9 个字段不同：approx_kl 0.001513 对 0.000281，policy_loss 0.001056 对 −0.000030，clipfrac 0.0156 对 0。
    - 两次更新后，权重变化的 L2 为 0.6046 对 0.4795（1.26 倍）；单参数最大变化 0.00119（约等于 4 步 × lr，新 Adam 每步接近按符号走 lr）对 0.00449；两条更新方向的余弦为 0.689。
    - 终点 Adam step 为 4 对 71,204。

    依据：`$Q/out/cmp-A-D_noopt.json`、`$Q/out/update_geometry.json`、`$Q/out/ckpts-all.json`。
20. CPU 实证（s9a 的两项覆盖）：`--lr 0.00015 --ent-coef 0.015`（E）后，metrics 中 lr = 0.00015；首行 loss 比 A 高 0.0054（约 0.005 × 熵 1.07）；权重变化是 A 的 0.510 倍，方向余弦 0.903；Adam step 继续到 71,204，状态保留（`$Q/out/cmp-A-E_s9aflags.json`、`$Q/out/update_geometry.json`）。
21. CPU 实证（恢复 ≠ 不中断续跑）：先从 u2225 恢复跑 1 次更新，存下 u2226；再用 `--resume auto` 跑 u2227（S）。与连续跑 2 次的 A 相比：u2226 完全相同；u2227 有 46 个字段不同。例如 S 有 1 局存档起点结束而 A 为 0，value_loss 0.001135 对 0.0000782，EV 0.935 对 0.994。两次更新的权重变化余弦为 0.801。权重、Adam、RNG 都已恢复，所以差异只能来自环境重启（`$Q/out/cmp-A-S_split.json`、`$Q/out/update_geometry.json`）。
22. GPU 归档中的会话边界：
    - 纯续训点（只改了预算或检查点类 flag）：s8a 的 u1553 和 u2050（`--resume auto --max-hours 1.4 --keep-ckpts 8 ...`），s8b 的 u2181（`--max-hours 1.3`）。
    - fork 点：s8a 的 u350（自 s7b，acq_upgrade_coef 0.004→0.012，包 e877→9f66）；s8b 的 u1300（自 s8a，acq_relic_coef 0.02）；s7b 的 u126（自 s6b）。

    依据：`$E/*/runs/*/config.json` 字段 `sessions`；会话记录中的 `s8a-cont`、`s8a-cont2`、`s8b-cont`、`s7b-fork.pt`、`s8a-fork.pt` 启动命令。
23. 纯续训后的第 1 次更新里，结束的几乎都是存档起点的局：
    - s8a u1554：217 局，fresh 0，平均 30 步/局。
    - s8a u2051：213 局，fresh 1。
    - s8b u2182：155 局，fresh 0。
    - s9a u2226：184 局，fresh 0。

    对照：s8a 中离会话起点超过 10 次更新的 2156 行里，每次更新结束的局数最少 477（p01 为 511），fresh 局数最少 302（`$Q/out/window_bias.jsonl` 字段 `first_rows`；`$Q/out/resume_transient.json` 字段 `controls`；会话记录中的 `update 2226 t=4.6 min ...` 一行）。
24. 瞬态幅度：恢复后第 1–5 次更新与恢复前 25 次相比（三组数分别是 u1553 / u2050 / u2181）：

    | 指标 | 恢复前 25 次 | 恢复后第 1–5 次 | 恢复后第 6–30 次 |
    | --- | --- | --- | --- |
    | 每次更新结束局数 | 590 / 560 / 550 | 430 / 405 / 390 | — |
    | 按局加权 progress | 0.354 / 0.371 / 0.363 | 0.306 / 0.307 / 0.315 | 0.355 / 0.373 / 0.369 |
    | fresh progress | 0.276 / 0.294 / 0.282 | 0.231 / 0.237 / 0.231 | — |

    恢复后第 2–3 次更新的 fresh progress 只有 0.121–0.163（`$Q/out/resume_transient.json`；`$Q/out/window_bias.jsonl`）。
25. 同权重配对对照：s8b 在 u1300 从 s8a fork，环境重启；s8a 自己在 u1300 没有中断。

    | 更新 | s8a（不中断）局数 | s8a progress | s8b（重启）局数 | s8b progress | s8b fresh 局数 | s8b fresh progress |
    | --- | --- | --- | --- | --- | --- | --- |
    | u1301 | 592 | 0.371 | 217 | 0.350 | 0 | – |
    | u1302 | 622 | 0.365 | 196 | 0.304 | 74 | 0.127 |
    | u1303 | 562 | 0.352 | 443 | 0.243 | 313 | 0.158 |
    | u1306 | 624 | 0.363 | 591 | 0.372 | — | — |

    到 u1306 两者已经接近。s7b→s8a 在 u350 的 fork 也是同样的模式（`$Q/out/fork_side_by_side.txt`；`$Q/out/fork_transient.jsonl`）。
26. 窗口偏差：从恢复点起算的 25 次更新窗口（b+1..b+25），其按局加权 progress 比 b+6..b+30 低 0.0076（u1553）、0.0099（u2050）、0.0077（u2181）（`$Q/out/window_bias.jsonl` 字段 `post1_25`/`post6_30`）。
27. 熵、价值损失和 s9a 首行：
    - 恢复后首行的熵高于恢复前 25 次的均值：0.802 对 0.793，0.820 对 0.774，0.833 对 0.767。第 2–3 行反而偏低（0.719–0.753），之后恢复。
    - 首行 value_loss 约减半：0.00069 对 0.00136，0.00071 对 0.00148，0.00064 对 0.00150。
    - s9a u2226：熵 0.876、KL 0.0012、184 局（0/184）、progress 0.3406。同样从 u2225 权重出发、没有中断的 s8a u2226 是：熵 0.792、KL 0.0021、568 局（370/198）、progress 0.3779。
    - 两边 u2226 的 rollout 都是用 u2225 权重采集的，所以首行的局数、progress 和熵差异来自环境重启，不是 lr/ent 改动的训练效果。

    依据：inline 计算脚本；`$E/sts2train/runs/s8a-silent-a10-mid-upg3/metrics.jsonl` 第 2226 行；会话记录。
28. 编译开销：同一 runtime 上续训时，首次更新的 t_update 为 8.8 / 7.9 / 7.9 s（平常约 3.2 s）。s9a 在新 runtime 上，首次更新 infer 117.9 s、update 157.2 s，开始训练 4.6 min 后才完成第一次更新。这只占用会话预算，不改变训练数据（`$Q/out/window_bias.jsonl` 字段 `first_rows` 末列；会话记录）。
29. 20-seed eval 不会被恢复机制直接改变（固定种子、贪心策略，只取决于权重），也分辨不出恢复造成的跳变。跨恢复点的相邻 eval 差为 +0.0106（1553→1575）和 −0.0453（2050→2075）；不跨边界的相邻差共 86 个，标准差 0.0355，范围 −0.094 到 +0.100（`$E/sts2train/runs/s8a-silent-a10-mid-upg3/evals.jsonl` 字段 `progress_mean`）。
30. 本地拿不到 s9a 在 u2226 之后的任何数据：`scratchpad/s9a/meta/` 为空，`runmeta.tgz` 为 0 字节，`fetch.err` 为 Colab 的 "Endpoint not found (HTTP 404)"。能用的只有会话记录里的启动命令、"resumed from .../s9a-silent-a10-u2225-anneal/checkpoints/ckpt-0002225.pt at update 2,225 (291,635,200 env steps, session 6)" 和 u2226 这一行（`scratchpad/s9a/`；会话记录 grep）。

## 证据位置

| 事实编号 | 文件/命令 | 关键字段或行号 |
| --- | --- | --- |
| 1 | `$R/sim/tools/train/sts2train/ppo.py`；`$Q/out/ckpt-u2225-summary.json`（`scripts/inspect_ckpt.py`） | 269-277；`keys` |
| 2 | `$Q/out/ckpt-u2225-summary.json` | `counters`、`saved_at`、`params`、`encoder_version` |
| 3 | `ppo.py`；`model.py`；`scripts/adam_coverage.py` | 877、274；model.py:177；`steps_per_update: 32.0`、`missing_state` |
| 4 | `ppo.py` | 280-295（291-293 lr 覆盖）、879 |
| 5 | `grep -n "param_groups\|lr=\|scheduler\|anneal" sts2train/*.py`；`$E/sts2train/runs/s8a-silent-a10-mid-upg3/metrics.jsonl` | 292/877/879/1237；`lr`（2186 行全为 0.0003）；`ppo.py:553,610` |
| 6 | `ppo.py`；`model.py` | 1135-1143、1151-1154；model.py:135,138,202-205,238,282-284 |
| 7 | `ppo.py`；`$Q/out/ckpt-u2225-summary.json` | 321-343（338-343 suppress）、294-295；`rng.keys` |
| 8 | `ppo.py`；`$Q/runs/A/status.json` | 797-806、499-505、991-993；`best_eval.update=1900, progress_mean=0.41348` |
| 9 | `ppo.py` | 20-22、578-587、808、810、843-866、882-883、1029-1033 |
| 10 | `$R/sim/VEC.md`；`$R/sim/tools/vec/sts2vec/schema.py`；`$Q/out/start_sequence.json`（`scripts/start_sequence.py`） | VEC.md:52-57；schema.py:398-400；`formula_matches_env`、`slot0_restores_during_pool_build=2072`、`first_start_save_fraction` |
| 11 | `startpool.py`；`$E/sts2train/runs/s8a-silent-a10-mid-upg3/config.json`；`$Q/runs/A/config.json` | 11-21、73-75、308-353；`start_pool.saves[].sha256`、`pool_entries=6316`、`save_mix_effective=0.351488` |
| 12 | `cli.py`；`ppo.py`；`bundle-26f1/sts2train-26f1d805a0/run_train.sh` | cli.py:16,132-133,435-447；ppo.py:171-186,743-772；`ARGS=(--out ... --resume auto --threads ...)` |
| 13 | `$Q/runs/H_stale_saves.stdout`；`startpool.py` | `FileNotFoundError`；53-58 |
| 14 | `runlog.py`；`ppo.py`；`$Q/runs/A/config.json` | runlog.py:105-115；ppo.py:896-897,905-931；`sessions` |
| 15 | `cmp $Q/extract/wheel-26f1/sts2train/*.py $R/sim/tools/train/sts2train/*.py`；`git diff`（三组提交） | 全部 identical；ppo.py 差异为空或仅 loop breaker |
| 16 | `$Q/out/cmp-A-B.json`、`cmp-A60-B60.json`、`ckpts-A-B.json`（`scripts/run_resume.sh`、`compare_metrics.py`、`compare_ckpts.py`） | `all_identical: true`；`weights_hash`、`adam_steps`、`rng_*` |
| 17 | `$Q/out/cmp-A-G_norng.json`、`ckpts-all.json` | `n_diff_fields=19`；`rng_numpy`/`rng_torch` |
| 18 | `$Q/out/cmp-A-C_seed1.json`；`cli.py:146`；`ppo.py:353,794-795,808,879` | `n_diff_fields=20`、`reward_mean` |
| 19 | `$Q/out/cmp-A-D_noopt.json`、`update_geometry.json`、`ckpts-all.json`（`scripts/make_variants.py`） | `approx_kl`、`policy_loss`、`clipfrac`；`norm`、`max_abs`、`cos_to_ref`；`adam_steps` |
| 20 | `$Q/out/cmp-A-E_s9aflags.json`、`update_geometry.json` | `lr`、`loss`；`norm_ratio_to_ref=0.510`、`cos_to_ref=0.903` |
| 21 | `$Q/out/cmp-A-S_split.json`、`update_geometry.json`（`scripts/run_resume_auto.sh`） | u2226 `identical`；u2227 `n_diff_fields=46`；`cos_to_ref=0.801` |
| 22 | `$E/*/runs/{s7b,s8a,s8b}*/config.json`；会话记录（`scripts/grep_transcript.py`） | `sessions[].update_start/resumed_from/bundle`；启动命令 |
| 23 | `$Q/out/window_bias.jsonl`、`resume_transient.json`；会话记录 `update 2226 t=4.6 min` | `first_rows`；`controls.episodes_min=477`、`episodes_p01=511`、`fresh_episodes_min=302` |
| 24 | `$Q/out/resume_transient.json`（`scripts/resume_transient.py`） | `pre_25`、`post_1_5`、`post_6_30` |
| 25 | `$Q/out/fork_side_by_side.txt`、`fork_transient.jsonl`（`scripts/fork_transient.py`） | u1301–u1308 / u351–u358 两列 |
| 26 | `$Q/out/window_bias.jsonl`（`scripts/window_bias.py`） | `post1_25`、`post6_30` |
| 27 | s8a/s8b `metrics.jsonl`（inline 计算）；s8a metrics 第 2226 行；会话记录 | `entropy`、`value_loss`、`approx_kl`、`episodes` |
| 28 | `$Q/out/window_bias.jsonl`；会话记录 | `first_rows` 末列 `t_update_s`；`infer=117.91s upd=157.21s` |
| 29 | `$E/sts2train/runs/s8a-silent-a10-mid-upg3/evals.jsonl`（inline 计算） | 相邻 `progress_mean` 差 |
| 30 | `scratchpad/s9a/{meta,runmeta.tgz,fetch.err}`；会话记录 | 空、0 字节、HTTP 404；`resumed from ... session 6` |

## 尚不确定之处

1. **GPU 上恢复是否逐位可复现**：没测过，本机不能用 GPU。GPU 路径上有 torch.compile（inductor）、bf16 autocast、TF32，嵌入反向等算子里还有 atomics，两次相同 flag 的恢复很可能不会逐位一致，所以 CPU 上的结论（事实 16）不能直接推到 GPU。
   - 判定方法：在同一 A100 runtime 上从 u2225 用相同 flag 恢复两次，各跑 3 次更新（`--max-updates 2228 --eval-every 0`），逐行比较 metrics，并比较终点权重 L2，把它当作双卡对照的噪声底。
2. **s9a 的 CUDA RNG 是否恢复成功**：写回失败会被 `suppress(Exception)` 吞掉，日志里没有任何记录。
   - 记录方法：每个会话在 config.json 的 `sessions[]` 里写 `rng_restored: {numpy, torch, cuda: ok|failed|absent}`。
3. **s9a 在 u2227 之后的训练指标和 eval**：本地没有日志（事实 30）。
   - 取回方法：从 runtime 或 Drive 拿回 `s9a-silent-a10-u2225-anneal/{metrics.jsonl,evals.jsonl,config.json,train.log}`，检查 u2226–u2231 是否也有事实 23–27 的瞬态特征；比较时从 u2231 起算。
4. **瞬态对学习本身的影响**：训练指标的瞬态已经量化（事实 23–27），但这 4–5 次更新用的是偏向开局状态的批次，它们对权重和策略的影响没有测过；20-seed eval 也分辨不出来（事实 29）。
   - 判定方法：从同一检查点做两条分支，一条每 K 次更新恢复一次，另一条不中断，更新数相同，再用 dev-128 做配对评测。也可以离线比较两条分支 b+5 时的策略 KL。
5. **每个会话实际用了哪些 flag**：config.json 只保存最后一次会话的 config。本文的会话级 flag 来自会话记录里的启动命令（事实 22），无法从运行目录单独核实；ent_coef 也不写入 metrics。
   - 记录方法：把 `given`（命令行覆盖项）和它与检查点 config 的差异写进 `sessions[]`，并在 metrics 每行写 ent_coef / vf_coef / clip。
6. **s8a 第 3 会话（u350–u1553）用的存档是否与第 5 会话相同**：第 5 会话的池是从同一个 9f66 存档目录建的，148 个 sha256 已确认（事实 11）。第 3 会话在同一 host、同一目录，推断相同，但没有单独日志证明。
7. **本地实验的规模**：本地 16 env × rollout 8，与 GPU 上 2048 × 64 差别很大，只能验证代码路径和确定性。权重变化、余弦这些数值受小批次噪声主导，不能当作 GPU 上的量级。

## 检查点内容与 `--resume` 行为对照

| 组件 | 在检查点中 | 恢复行为 | 证据 |
| --- | --- | --- | --- |
| 模型权重（含持久索引 buffer） | 是 | 严格加载 | ppo.py:273,287 |
| Adam exp_avg / exp_avg_sq / step | 是（step 71,200） | 全量加载 | ppo.py:274,288-290 |
| Adam lr | param_groups 中有 | 改写为 cfg.lr（检查点值或 `--lr`） | ppo.py:291-293 |
| betas / eps / weight_decay | 是 | 沿用检查点值 | opt.load_state_dict |
| LR 调度 / anneal | 代码中不存在 | — | grep 仅命中 4 处 |
| ent_coef 等损失系数 | config 中有 | 检查点值或命令行覆盖；不写入 metrics | ppo.py:610 |
| 归一化运行统计 | 不存在 | — | ppo.py:1151-1154 |
| numpy / torch CPU / CUDA RNG | 是 | 写回；CUDA 失败时静默 | ppo.py:321-343 |
| Python random | 未使用 | — | grep |
| updates / env_steps / episodes / wins / wall_s | 是 | 恢复；sessions+1 | ppo.py:797-806 |
| best_eval / last_eval | 是 | 恢复，跨目录继承 | ppo.py:804 |
| 环境在途对局 | 否 | 全部丢弃，2048 槽开新局 | ppo.py:810,1029 |
| 起始序列位置（每槽局数计数） | 否 | `PPO{seed}R{upd0}` 重新播种，k=0 | ppo.py:808；VEC.md:52-57 |
| 起始池（fresh 种子、存档副本） | 否，可重建 | 由 seed + 存档确定，内容不变 | startpool.py:308-353 |
| 每槽奖励 / 塑形 / 辅助目标 / loop breaker | 否 | 重置 | ppo.py:1030-1033 |
| torch.compile 图、inductor 缓存 | 否 | 每会话重编；新 runtime 冷编译约 4.6 min | ppo.py:882-883 |
| AMP GradScaler | 不存在（bf16） | — | grep |
| metrics/evals 中 > upd0 的行 | — | 同目录时删除并重做 | runlog.py:105-115 |

## CPU 复现实验（u2225，16 env，每次 2 次更新）

| 运行 | 改动 | 与 A 不同的字段（u2226 / u2227） | 终点权重变化 L2 | 与 A 更新方向的余弦 | Adam step |
| --- | --- | --- | --- | --- | --- |
| A | 基准 | — | 0.4795 | 1 | 71,204 |
| B | 同 A | 0 / 0 | 0.4795 | 1.000 | 71,204 |
| A60 vs B60 | 60 次更新 | 60 行全部 0 | — | — | — |
| C | `--seed 1` | 20 / 20 | 0.5593 | 0.138 | 71,204 |
| D | 去掉 Adam 状态 | 9 / 13 | 0.6046 | 0.689 | 4 |
| G | 去掉 RNG | 19 / 17 | 0.4829 | 0.141 | 71,204 |
| E | `--lr 1.5e-4 --ent-coef 0.015` | 9 / 14 | 0.2447 | 0.903 | 71,204 |
| S | u2226 处断开再 `--resume auto` | 0 / 46 | 0.5385 | 0.801 | 71,204 |

复现：`$Q/scripts/run_all.sh`（每次运行不到 1 分钟，其中池预热约 24 s）。

## 对 s9a 与过夜分段续训的影响

除了有意改动的 flag，每次恢复还带来了以下变化：

- **s8a u1553 / u2050、s8b u2181（只改预算类 flag）**
  - 环境重启，2048 局在途对局丢弃，seed_text 换成 PPO0R{upd0}。
  - 在同一 runtime 上重新编译（首次更新 7.9–8.8 s）。
  - u1553 和 u2181 同时把包从 9f66 换到 26f1：训练器 ppo.py 和 sim 源码相同，NativeAOT 是重新构建的。
  - Adam 和 RNG 都恢复了，lr 保持 3e-4。
- **s9a u2225（新目录、新 runtime）**
  - 有意改动：lr 3e-4 → 1.5e-4，ent_coef 0.02 → 0.015，之后都是常数，没有 anneal 曲线。
  - 其他 flag：`--max-hours 6`；`--start-saves` 改用 26f1 包路径，148 个文件与原来相同，池不变。
  - 随之发生的变化：
    - 环境重启（PPO0R2225）；
    - 冷编译，首次更新在开训 4.6 min 后才完成；
    - Adam 矩估计保留，但 lr 减半，实际步长立即减半（事实 20）；
    - best_eval（0.41348）被带进新目录，新目录要超过它才会写出 best.pt；
    - 新目录的 sessions 只从 6 开始，谱系只能靠 `resumed_from` 追溯。
- **解读规则**
  - 读训练指标时，排除每个会话的前 5 次更新（保守些用 6 次），也就是 s9a 从 u2231 起算；或者只用固定种子的 eval。
  - 跨会话比较时，不要用包含恢复点的 25 次更新窗口，否则 progress 会偏低约 0.008–0.010（事实 26）。
  - 恢复后首行熵偏高、第 2–3 行偏低、value_loss 减半，这些都是状态构成变化造成的，不能当作 ent_coef 生效的证据。

## 从现在起的记录方法与对照实验建议

1. **metrics 每行增加字段**：`session`、`session_update`（本会话第几次更新），以及 `ent_coef`、`vf_coef`、`clip`。这样分析时可以按规则自动剔除瞬态行，也能审计系数何时变化。
2. **config.json 的 `sessions[]` 增加字段**：`given`（本会话的命令行覆盖项）、`config_diff`（相对检查点 config）、`rng_restored`（各组件 ok / failed / absent）、`adam_step`、`seed_text`、首局存档比例。
3. **双卡对照**：两条线从同一个检查点、同一个 `--seed`、同样的 `--envs` 恢复。这样起点的环境序列和 RNG 完全相同，是共同随机数配对，早期差异可以归因到唯一的改动和 GPU 数值噪声。两条线的分段节奏要相同（相同的 max-hours / 相同的 update 边界），或者都不分段。先做"不确定之处"第 1 条的同卡确定性测试，测出噪声底。
4. **`--seed` 的语义**：续训时 `--seed` 不会改变已恢复的 RNG，只会换掉 4096 个 fresh 训练对局和存档副本（事实 18）；全新训练时则两者都会变。设计"不同训练随机种子复验"时要明确选哪一种，不要在谱系中途用 `--seed` 当复验开关。
5. **可选缓解（需要改代码，本次未做）**：续训后先跑约 5 次只采样、不更新的 burn-in，把各槽的对局错开；或者干脆在分析中剔除这些行。当前的 snapshot 只存在内存里，无法把环境状态存进检查点，所以做不到真正无缝的续训。

## 执行说明

- **资源**：全部在 CPU 上运行，`nice -n 10`，env 3 线程 + torch 3 线程，.NET 服务器 GC 限制为 3 个堆。共 10 次训练进程，单次墙钟 27–51 s，另有 1 次预期会失败的启动（H，1 s）。没有用 GPU/MPS，没有启动游戏，没有碰存档原件（只读 `tmp/train/saves-silent-a9`）。
- **仓库**：`git status` 为空，没有改动受版本控制的文件。副作用是首次 import 时，Python 往 gitignore 的 `$R/sim/tools/train/.venv/.../site-packages/` 写了约 970 个 `.pyc` 字节码缓存（torch、sympy、matplotlib 等，时间 11:27:24–11:33:18）。它们不影响行为；之后的脚本已改用 `python -I -B`。
- **产物**：全部在 `$Q` 下，包括 `runs/`（各运行目录和终点检查点，约 0.47 GB）、`variants/`（两个去掉组件的临时检查点）、`out/`（对比结果）、`scripts/`（可复现脚本，入口是 `run_all.sh`）。

## 复核（独立核查）

复核方式：对抗式独立核查，目标是推翻原文。逐行重读 HEAD 代码。从 `runs/archive/*.tar.gz` 重新解出 s6b/s7b/s8a/s8b 的 run 级文件，放到 `$Q/verify/extract/`；s8a 的 metrics/evals/config 与 `$E` 逐字节相同。所有归档数字都用自己写的脚本重算。CPU 上按原文同一组 flag 重跑了 9 个恢复进程（VA、VB、VS 两段、VE、VD、VG、VC、VA60），每个 26–46 s，另跑了 1 次预期失败的启动（VH，0.8 s）。资源设置为 nice 10、env 3 线程 + torch 3 线程、.NET 3 个 GC 堆，没有用 GPU/MPS。所有 Python 都用 `-I -B` 运行，没有写仓库（`git status` 为空）。

- 脚本：`$Q/verify/scripts/`（`v_run.sh`、`v_cmp.py`、`v_geom.py`、`v_inspect_ckpt.py`、`v_ckpt_evals.py`、`v_transient.py`、`v_evals.py`、`v_best_stale.py`、`v_kl_entropy.py`、`v_startseq.py`、`v_sessions.py`、`v_variants.py`）
- 输出：`$Q/verify/out/`，汇总在 `v_summary.json`
- 复核前的原文备份：`$Q/verify/memo.before-review.md`

### 核实无误的事实

1. **事实 2、3、4**
   - u2225 的 17 个键与计数器全部复现（`verify/out/v_ckpt_u2225.json`）。
   - encoder_version 与本地库一致，`resume_compat.model_inputs = identical`（`verify/runs/VA/config.json`）。
   - Adam：170/171 个参数带状态，step 全部为 71,200。另外两个检查点：u2050 为 65,600，u2536 为 81,152，三者都等于 32 × updates（`v_ckpt_evals.txt`）。
   - param_groups 为 betas (0.9, 0.999)、eps 1e-5、weight_decay 0。
   - 唯一没有状态的是 `kind_emb.weight`：model.py 里没有任何 `self.kind_emb(` 调用。
2. **事实 5、6、7**
   - lr 只在 ppo.py:292/877/879/1237 出现。s8a 的 2186 行和 s8b 的 1325 行 metrics 中，lr 全为 0.0003。
   - 没有 scheduler 或 anneal；ent_coef 是常数（610/553），metrics 里没有这个字段。
   - 没有运行归一化，没有 GradScaler，没有 Python `random`。
   - 另外确认模型没有 Dropout（model.py:125 "no dropout"），所以 `model.train()` 不消耗 RNG。
   - RNG 的保存与恢复在 321-343，CUDA/MPS 的写回包在 suppress 里。
3. **事实 9、10**
   - 本地 16 槽 VecEnv 按训练器方式建池：15 个槽与公式直接一致；槽 0 偏移 2072 = 148 × 14，正好是建池时 restore 的次数。第二次 reset 时每个槽的 k 都加 1。
   - 2048 槽首局落在存档条目的比例为 0.35596 / 0.34277 / 0.34521。
   - 2050 与 2225 两个恢复点首局条目相同的槽数为 0。
   - 依据：`verify/out/v_startseq.json`。
4. **事实 11**
   - s6b、s7b、s8a、s8b 的 `config.start_pool.saves`、本地 `saves-silent-a9`、26f1 包的 `saves/`，这六处的 (相对文件名, sha256) 集合完全相同，都是 148 个。
   - 池 6316 = 4096 + 148 × 15，有效存档比例 0.351488。
5. **事实 12、13、14**
   - 覆盖规则见 cli.py:132、435-447，ppo.py:171-186、743-772，run_train.sh:60-61。
   - VH（不传 `--start-saves`）0.8 s 内报 `FileNotFoundError: --start-saves /content/sts2/sts2train-9f6608701d/saves: not a directory`。
   - JsonlWriter 的 `keep_upto` 见 runlog.py:105-115，原文行号无误。
   - 新目录的 sessions 只有本会话。
6. **事实 15**
   - 26f1 wheel 里 sts2train 的全部 16 个 .py、sts2vec 的全部 11 个 .py 都与 HEAD 逐字节相同，比原文列出的 10 个更全。
   - ppo.py 在 9f66→26f1 之间没有差异；sim 源码在 9f66→26f1 和 26f1→HEAD 之间都没有差异。
   - e877→26f1 的 ppo.py 差异只涉及 loop breaker（rollout 选项，以及 eval 传入 `loop_breaker`），恢复逻辑没有变。
7. **事实 16–21（CPU 实证）**
   - 我的 VA 与原 A、VB 与 VA、VS 与原 S、VE/VD/VG/VC 与原 E/D/G/C、VA60 与原 A60，逐行逐位相同（66 个非计时字段）。原 A60 与 B60 的 60 行也完全相同，其间完成 31 局（18 fresh、13 存档）。
   - 与 VA 相比不同的字段数（u2226 / u2227）：C 20/20、D 9/13、G 19/17、E 9/14、S 0/46。S 的 u2227：value_loss 0.001135 对 0.0000782，EV 0.935 对 0.994，S 结束 1 局存档起点的对局。
   - 终点：A 与 B 的 weights_hash 都是 bf5a0091a3d6dcb2，Adam step 都是 71,204；D 的 step 为 4。
   - 权重变化 L2：A 0.4795；D 0.6046（单参数最大变化 0.00119 对 A 的 0.00449，余弦 0.689）；E 0.2447（比值 0.510，余弦 0.903）；S 0.5385（余弦 0.801）；C 0.5593（余弦 0.138）；G 0.4829（余弦 0.141）。
   - C 的终点 numpy RNG 与 A 相同，torch RNG 不同；G 的两者都与 A 不同。
   - E 的 u2226：loss 高 0.0054，approx_kl 7.05e-5 对 A 的 2.81e-4。
   - 依据：`v_cmp_*.json`、`v_geom.txt`。
8. **事实 22、23、24、25、26、27（数值部分）、28、29、30**
   - 会话边界与 bundle 来自 config.json 的 `sessions`。会话记录中 s8a-cont、s8a-cont2、s8b-cont 的启动命令只有 `--resume auto --max-hours 1.4|1.3 --eval-every 25 --eval-seedset silent-a10-v1 --ckpt-every 25 --ckpt-every-min 15 --keep-ckpts 8 --log-every 1`。s9a 的启动命令含 `--max-hours 6 --lr 0.00015 --ent-coef 0.015 --start-saves /content/sts2/sts2train-26f1d805a0/saves`。s9a 首行原文为 `update 2226 t=4.6 min ... infer=117.91s upd=157.21s eps=184 prog=0.3406 ... ent=0.876 kl=0.0012 | fresh eps=0`。
   - 首行局数：217/0、213/1、155/0；对照 n=2156、最少 477、p01 511、fresh 最少 302。
   - 瞬态表：恢复前 25 次每次更新结束 589.7 / 560.5 / 550.1 局，恢复后第 1–5 次 430.4 / 405.2 / 390.2 局。按局加权 progress 分别为 0.3537 / 0.3714 / 0.3628 → 0.3056 / 0.3070 / 0.3152 → 0.3555 / 0.3729 / 0.3686。fresh progress 0.2762 / 0.2936 / 0.2824 → 0.2305 / 0.2369 / 0.2315。第 2–3 次更新的 fresh progress 为 0.121–0.163。
   - fork 并排表与原文一致。
   - 熵与价值损失：首行熵 0.802/0.793、0.820/0.774、0.833/0.767；value_loss 0.00069/0.00136、0.00071/0.00148、0.00064/0.00150。
   - 首次更新 t_update 为 8.816 / 7.91 / 7.861 s，其余约 3.16 s。
   - eval 差：+0.0106、−0.0453；非边界 86 个，SD 0.0355（ddof=0，ddof=1 时为 0.0357），范围 −0.0937 到 +0.1004。
   - s9a 本地元数据为空，`fetch.err` 为 HTTP 404。
   - 原文标"inline 计算"的数字，现在可以用 `v_transient.py`、`v_kl_entropy.py`、`v_evals.py` 复现。
9. **执行说明**：venv 里 11:27:24–11:33:18 写入的 `.pyc` 实测 965 个，与"约 970"相符。

### 需更正的事实（原文 → 更正，证据）

1. **事实 1 与对照表第 1 行**
   - 原文："model（state_dict，含持久 buffer）"、"模型权重（含持久索引 buffer）"。
   - 更正：state_dict 只有 171 个参数张量，共 2,904,208 个数。模型的 15 个 buffer（`_ix_*`、`gi_num_idx`、`kind_off`、`kind_size`、`kind_prompt`）都是 `persistent=False`，不在检查点里，每次恢复时由 spec 重建。
   - 依据：model.py:199-206、282-284；`verify/out/v_ckpt_u2225.json` 中 `buffers_in_state_dict = []`、`state_dict_keys_not_params = []`。
   - 影响：不影响恢复的正确性。
2. **事实 8、结论第 1 条、对照表 best_eval 行**
   - 原文："best_eval / last_eval 完整恢复"；"u1900 的 0.41348（s8a 在 silent-a10-v1 上的最佳）"。
   - 更正：s8a 在 silent-a10-v1 上最佳的 clean eval 是 **u2225 的 0.42672**（`evals.jsonl`；`status.json` 中 best_eval 也是 2225），正好是 s9a 恢复用的那个检查点。
   - 原因：检查点里的 best_eval/last_eval 总会漏掉同一 update 的 eval。
     - 周期检查点在 ppo.py:1208-1211 写出，同一 update 的 eval 在 1214-1215 才跑。
     - `run_eval` 发现 `last_ckpt_upd == upd`，不会再存一次（957-958）。
     - 会话末尾同样是先存（1263-1264）后 eval（1265-1267）。
   - 实测（`v_ckpt_evals.txt`）：
     - ckpt-0002225：best_eval = u1900 0.41348，last_eval = u2200。
     - ckpt-0002050：last_eval = u2025，而 evals.jsonl 里有 u2050 = 0.38934。
     - ckpt-0002536：last_eval = u2525，漏掉了 u2536 的期末 eval。
   - 四个续训点里只有 u2225 受影响：u1553 的 0.37004、u2050 的 0.38934、s8b u2181 的 0.36562 当时都不是新高（`v_best_stale.txt`）。
   - 后果：
     - s9a 新目录以 0.41348 为基准，只要某次 eval 高于 0.41348 就会写 best.pt，即使低于父检查点自己的 0.42672。
     - s9a 的 status.json 显示的 best_eval 也是这个旧值。
   - 应改为："best_eval/last_eval 恢复的是写检查点那一刻的值，比同一 update 的 eval 落后一次。"
3. **"对 s9a 与过夜分段续训的影响"第 1 项**
   - 原文："NativeAOT 是重新构建的"。
   - 更正：没有重新构建。9f66 包和 26f1 包的 manifest 中，`aot_key` 都是 5c80f4e6ba70，`lib/Sts2Sim.Vec.so` 的 sha256 都是 2d035a9be243fd40…，与本机缓存 `$R/tmp/colab/aot/5c80f4e6ba70/Sts2Sim.Vec.so` 相同。`make_colab_bundle.sh:40-44` 按 sim/Sts2Sim.Core、sim/Sts2Sim.Vec、Dockerfile、vec_publish_aot.sh 的树哈希取缓存键，这两个提交的这些树完全相同。
   - 依据：`$Q/verify/bundle-manifests/`。
   - 影响：9f66→26f1 的换包对环境是逐字节零变化，这几个续训点比原文说的更"纯"。
4. **结构化报告里 s9a u2226 的归因**
   - 原文："entropy 0.876, KL 0.0012 … this gap is a resume artifact, not an lr/ent effect"。
   - KL 部分更正：KL 差不是恢复瞬态。
     - 三个纯续训点首行 KL 为 0.00213 / 0.00205 / 0.00213，与各自恢复前 25 次的均值 0.00194 / 0.00199 / 0.00173 持平或略高。
     - s9a 的 0.0012 低于 s8a u2201–2225 的最小值 0.00162（均值 0.00216，SD 0.00025）。
     - 这与 lr 减半一致：CPU 上同一批次的 E，u2226 的 KL 是 A 的 0.25 倍。
     - 依据：`v_kl_entropy.txt`。
   - 正文事实 27 只把局数、progress 和熵归因于重启，没有包括 KL，这一点正文无误；需要更正的是结构化结论。
   - 熵的归因只是推断。metrics 里的熵是 32 个顺序 minibatch 的均值，其中后 31 个是在新 lr/ent 的优化步之后计算的。s9a 首行熵比 s8a 恢复前 25 次均值高 0.0745，三个纯续训点为 +0.009 / +0.047 / +0.066，s9a 处在瞬态范围的上沿。结论应写成"与瞬态一致，不能排除 lr/ent 的贡献"。
   - 局数和 progress 的归因不受影响：rollout 只取决于权重，两边权重相同。
5. **事实 26 与结论的数值**
   - 原文：u2181 的窗口偏差为 0.0077。
   - 更正：0.0076。未取整的值是 −0.00764；原值是用两个已取整到 4 位的窗口值相减得到的。
   - 结论里的"约 0.008–0.010"实际为 0.0076–0.0099。

### 无法核实的事实

1. GPU 上的逐位确定性、s9a 的 CUDA RNG 是否恢复成功、s9a 在 u2226 之后的数据：与原文相同，本机没有 GPU，s9a 的元数据为空，fetch.err 为 HTTP 404。原文"不确定之处"第 3 条写"u2227 之后"，与事实 30 的"u2226 之后"不一致；应为"u2226 之后（从 u2227 起）缺失"。
2. s8a 第 3 会话（u350–1553）的存档集合仍然没有直接日志。不过 s6b（ef70 包）、s7b（e877 包）、s8b（另一台 host）的 start_pool 记录都是同一组 148 个 sha256，原文的推断得到更强的支持。
3. "两次相同 flag 的 GPU 恢复很可能不逐位一致"是合理判断，没有验证。
4. 事实 27 中熵差的成因，见更正 4。

### 补充发现

1. **status.json 的心跳窗口也受瞬态影响**：`recent` 是 maxlen 20 的 deque（ppo.py:1001、1017-1021），每个会话从空开始。所以恢复后约 20 次更新内，心跳看到的 `recent.progress_mean` 正好是事实 26 那种含瞬态的窗口，会偏低，监控时不应把它当成退步。
2. **会话边界已经可以从 metrics 识别**：虽然没有 session 字段，但 `session_s` 在每个会话的首行归零。s8a 在 u1554 重置为 60.33 s，在 u2051 重置为 60.19 s（`v_best_stale.txt`）。原文建议增加的字段仍然有用，但不是识别边界的唯一办法。另外，`sessions[]` 其实也记录了 compile、amp、static_rows 等运行参数（ppo.py:912-921），只是不记录 lr、ent_coef、max_hours 这类本会话的覆盖项。
3. **训练时没有 LoopBreaker 状态**：训练用的 LoopBreaker 只在 `rollout_loop_breaker` 为真时构建（ppo.py:861-866）。u2225 的 config 中这一项为 false，所以 s8a/s8b/s9a 的训练里没有这个状态需要重置；事实 9 中的 LoopBreaker 对这些运行不适用。
4. **事实 25 两组 fork 对照的干净程度不同**
   - s7b→s8a 的 u350 fork 同时换了原生库和 sim：e877 包的 aot_key 为 8f89eba48d17、.so sha256 为 254efb10…，9f66 包为 5c80f4e6ba70 / 2d035a9b…；e877→9f66 之间 sim 有 3 个文件改动（git diff --stat）。所以这一组不是同一环境下的对照。
   - u1300 那组更干净：s8a 和 s8b 都在 9f66 包、用同一个 .so。
   - 即便如此，严格的"同权重"只对 u1301 这一行成立（两边 rollout 都用 u1300 权重）。之后 s8b 因 acq_relic_coef 0.02 和不同的批次，权重已经分开。
5. **eval 不消耗训练 RNG**：eval 走 argmax，loop breaker 用自己的 `np.random.default_rng([seed, j, m])`（loopbreak.py:63）。所以"检查点写在 eval 之前"不会让恢复后的 RNG 与连续运行错位，只影响 best_eval/last_eval 的记账（更正 2）。
6. **当前 API 无法序列化环境状态**：VecEnv 只有内存中的 snapshot/restore/clone/branch（env.py:226-280，VEC.md:294）。C# 里有 `RunExporter.ToSave`，但 VEC.md:932 写明它对 determinized 克隆不保真，也没有暴露给 sts2vec。所以原文"做不到真正无缝的续训"在当前 API 下成立。
7. **建议的修复（需改代码，本次未做）**
   - 让 `run_eval` 在更新 best_eval 后重写同一 update 的检查点；或者在 restore 时从同目录的 evals.jsonl 重算 best_eval。
   - 跨目录 fork 时，以父目录 evals.jsonl 的最高值为准。

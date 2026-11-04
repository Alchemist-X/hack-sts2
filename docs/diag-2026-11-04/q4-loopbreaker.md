# 第 4 项：loop-breaker 实际修复了什么（u2225，2026-11-04）

## 结论（先给结论）

- loop-breaker 只在评测侧生效（`sts2train eval`、训练中的周期评测和 best.pt 选择、report-card、live twin），**这条谱系的训练 rollout 从未启用它**。被替换的动作和它的概率从未进入 PPO 批次。
- u2225 在 dev-128 上共介入 177 次，涉及 94/128 局。其中 176 次发生在休息点，循环为 SMITH → 选卡 → 在确认页 PromptCancel → 回到同一个休息点；另有 1 次在商店，循环为删卡 → 选卡 → 取消。所有介入都发生在第一次回访，breaker 屏蔽 SMITH 后改走 HEAL。
- 回访时，模型读取的 27 个缓冲**逐字节相同**；全部 37 个观测缓冲相同，模拟器 StateHash（含 RNG）也相同。策略没有记忆，因此贪心策略只能无限重复，直到层停滞上限触发。这里的“重复”本身不是学到的偏好；真正学到的是一对不一致的选择：休息点上偏好 SMITH，到确认页又偏好取消，而取消在训练里不花任何代价。
- 关闭 breaker 后，同一权重在 dev-128 上的结果是 94/128 局 stuck，progress 从 0.372 降到 0.252。配对差为 **+0.120 [0.097, 0.143]**（95% CI 为 t 区间，下同），94 局变好，0 局变差。u2050 的配对差是 +0.069。breaker 开着时 u2225 比 u2050 高 0.021 [−0.003, +0.045]，差异不显著；关掉后 u2225 显著更差，差 −0.030 [−0.050, −0.009]。**排名随 breaker 开关翻转。**
- 训练中同样存在循环。用 u2225 按训练方式采样时，1.4–1.5% 的决策是回到已决策过的观测，约 46% 的局至少出现一次，2.5% 的决策（占非战斗决策的 8.7%）落在零进展循环里。这些循环几乎都很短：u1–u2225 谱系中 FLOOR_STALL 截断只有 461 次，对应 1,492,418 个结束局（0.031%）。
- 升级奖励只在牌组升级等级之和变化时发放，不在选择 SMITH 或预览选卡时发放。但它统计的是“牌组里的升级等级”，所以拿到一张本身已升级的卡也会得奖励。177 个循环的进度奖励与 shaping 合计都是 0。在 γ=1、λ=0.99 下，经过这个循环的 SMITH 能继承回访后所选动作约 97% 的优势，训练里没有力量把它压下去。

## 已确认事实

1. **机制**：`LoopBreaker` 为每个 env 槽保存最近 64 次决策的（摘要, 动作）。摘要是 27 个模型读取缓冲的 64 位哈希。再次遇到同一摘要时，过去在这里走过的行被置为 −1e9。若此前已经来过 ≥k=3 次，就只保留合法且未被禁的离开类动作（ProceedRewards/LeaveShop/LeaveRestSite/LeaveEvent/SkipTreasureRelic/EndTurn/PromptConfirm/PromptCancel）。若所有合法行都被禁，则不做屏蔽（`loopbreak.py:41-45, 48-79, 125-174, 176-195`）。
2. **生效位置**：(a) 贪心评测 `greedy_policy` → `breaker.greedy`，`sts2train eval` 默认开启（`evaluate.py:46-64, 166`；`cli.py:411-415`）；(b) 训练中的周期评测，并由它选出 best.pt（`ppo.py:161, 953-993`）；(c) report-card 的协议评测，固定开启（`reportcard.py:577-585`）；(d) live twin 真游戏 runner，默认开启（`scripts/live/nn_twin/twin.py:119-125`，`nn_twin_play.py:62`）；(e) lookahead 的贪心基线**关闭**（`lookahead.py:686-698`）；(f) 训练 rollout 仅在 `--rollout-loop-breaker` 时启用，默认关闭（`ppo.py:163, 862-866, 1074-1077`）。
3. **本谱系的训练从未用它**：s8a 的 config 中 `rollout_loop_breaker=false`；s6b/s7b 的 config 里没有这个键（训练早于该功能）。s6b/s7b/s8a/s8b 的 metrics.jsonl 都没有 `loop_breaks/no_op_actions/cycle_revisits` 字段，这些字段只在启用该开关时写入（`ppo.py:1198-1200`）。rollout 走 `runner.sample(x)` 的无 adjust 分支，存入的 logp 是未屏蔽分布的（`ppo.py:1075, 1085`）（`out/metrics_stall.json`、s8a `config.json`）。
4. **假如开启 `--rollout-loop-breaker`（代码推断，未运行）**：存入的 logp 来自屏蔽后的分布（`ppo.py:730-737, 1077, 1085`）。更新时 newlogp 用未屏蔽的 logits 计算（`ppo.py:546-549`），RolloutStore 也不保存掩码（`features.py:314-319`）。于是在被屏蔽的步上，θ=θ_old 时比率就等于 1−Σπ_old(被禁)，例如 SI001 回访时为 0.362，已在 [0.8, 1.2] 之外。A>0 时走未裁剪项，梯度照常存在；A<0 时走裁剪项，梯度为 0，形成单侧偏差（`ppo.py:546-550`）。
5. **层停滞上限的训练语义**：VecEnv 统计同一层的非战斗决策，合法和非法索引都计，action<0 不计；层号变化时清零。达到 ≥80 时截断：truncated=1，env 奖励 0，status 256。训练器把它当作终局：`done=term|trunc`，GAE 中 `nonterm=1−done`，不做 bootstrap。这一步的进度奖励等于终局进度减前一状态进度，同层停滞时为 0；shaping 为 −φ(s)（`VecEnv.cs:698-770, 943-948`；`ppo.py:1091, 1139-1141`；`objective.py:109-112, 175`）。实测关闭 breaker 后的 stuck 局：SI001 在 F16 共 80 个决策、3 个不同观测，截断步 r_progress=0、F=−0.096；SI044 为 F=−0.116。**在奖励上，停滞等同于在该层死亡**（`out/lboff-stuck-2games.jsonl`）。
6. **训练期停滞计数**（u2225 谱系 s6b u1–126 → s7b u127–350 → s8a u351–2225）：FLOOR_STALL 共 461 次，结束局 1,492,418 个，占 0.031%，非法动作 0。三段分别为 55/128,599、52/178,274、354/1,185,545。env 步合计 291,635,200，与模型卡一致（`out/lineage_stall.json`）。s8a 在 u401–900 为 0 次，在 u1001–1100 最高，为 158/62,898（`out/metrics_stall.json`，以及按每 100 次 update 的分段汇总）。
7. **重放忠实性**：在本地库 3b73317739c650bd 上，用 u2225（weights d59f9ea2095799c0）重放 dev-128 的 128 局 history_actions，共 39,869 个决策。影子 breaker 的选择与记录动作全部一致（39,869/39,869）；128/128 局终局哈希一致；177/177 次介入全部复现（`out/dev128-u2225-c{0,1}.report.json`）。
8. **dev-128 介入统计**（u2225，贪心 + breaker）：177 次，94/128 局，平均每局 1.38 次。每局次数分布为 {0:34, 1:43, 2:32, 3:10, 4:6, 5:2, 6:1}。按位置分：休息点 176，商店 1，事件、战斗及其他均为 0。循环形态：SMITH→PromptSelect→PromptCancel 173 次，SMITH→PromptCancel 3 次，商店 BuyEntry(删卡)→PromptSelect→PromptCancel 1 次。替换结果：SMITH→HEAL 176 次，删卡→购买 THE_BOMB $90 1 次。所有介入都在第 1 次回访，从未触发 k=3 的离开优先规则；循环长度为 3 的 174 次，为 2 的 3 次（`out/dev128-u2225-loops.json`）。
9. **观测完全相同**：177 次回访中，64 位摘要、27 个模型缓冲的 sha256、37 个观测缓冲（含 aux 等非模型缓冲），以及模拟器 `RunSim.StateHash`（包含 run RNG，见 `RunState.cs:354-356`）都与首次访问相同。`Policy.forward` 只接收观测（`model.py:574`）。所以模型看不出差异，贪心策略必然重复（`out/dev128-u2225-loops.json` 的 identity、sim_state_hash_equal_at_revisit 字段）。
10. **关闭 breaker 的差距（u2225，本地 CPU，2×64 局）**：94/128 局 stuck，恰好就是开 breaker 时有介入的那 94 局；其余 34 局动作序列完全相同。每个 stuck 局在首次介入之前的动作前缀都相同，并且停在同一层（休息点 93、商店 1）。progress 0.2519 对 0.3722，配对差 +0.1203，t 区间 [0.0975, 0.1431]，bootstrap 区间 [0.0985, 0.1432]；94 局变好、0 局变差。平均层数 13.82 对 19.76；到达第 2 幕 9 局对 57 局（`out/dev128-on-vs-off.json`）。
11. **u2050 与排名翻转**：u2050 关闭 breaker 为 0.2818（52 局 stuck，来自存档的 la-u2050-dev-greedy），开启为 0.3511（52 局里 88 次介入），配对差 +0.069 [0.050, 0.089]。与文档中的 0.282/0.351 一致。u2225−u2050 的配对差：开 breaker 为 +0.021 [−0.003, +0.045]，46 局胜、33 局负；关 breaker 为 −0.030 [−0.050, −0.009]，33 局胜、64 局负（`out/rank_on_off.json`；`docs/train-results-silent-a10-2026-11-04.md:57-62`）。
12. **循环本身零奖励**：177 个循环里，训练器的进度奖励和 shaping 加总都是 0.0，每一步的 r_progress 和 F 都是 0。F 按训练器自身的 `EpisodeRewards`/`AcqShaping` 计算；在所有非终局步上，F 与三项势能之和一致，0 行不符（`out/dev128-u2225-loops.json` 的 progress_reward_on_cycles、shaping_on_cycles、F_sum_check_bad_rows 字段）。
13. **升级项何时发放**：φ 中包含 c_up × 牌组（master deck）升级等级之和，s8a 的 c_up=0.012、γ=1，F=φ(s')−φ(s)（`objective.py:162-166, 175`）。dev-128 上共有 246 行该项非零，0 行与牌组升级数变化不符。来源如下：TakeRewardCard 拿到已升级的卡 72 行；SMITH 后的 PromptConfirm 51 行；事件选项 47 行；战斗步 37 行，其中 36 行是结束战斗的那一步；非 SMITH 的 DeckUpgrade 选择 23 行；ClaimReward 7 行；宝箱遗物 4 行；变形 2 行；删除已升级卡 2 行，为负值；商店 1 行。选择 SMITH、进入升级页、在预览中选卡都是 0。确认那一步为 +0.012，例如 SI003 的 t=96，升级数由 0 变为 1（`out/dev128-u2225-loops.json` 的 upgrade_term 字段，`out/loop-cases.md`）。
14. **休息点行为不一致**：dev-128 上有 393 个不同的休息点决策状态，其中 227 个以 SMITH 为 argmax。SMITH 的结局：176 次取消（173 次在选卡后取消，3 次直接取消），51 次确认。HEAL 共 342 次：主动 166 次，被 breaker 强制 176 次。HP 占比：确认时中位 1.00（最低 0.80），取消时中位 0.63，直接 HEAL 时中位 0.51。确认页上 p(confirm) 的中位：取消的情形为 0.004，确认的情形为 0.635（最低 0.503）。回访时 p(SMITH) 中位 0.717（0.491–0.976）。也就是说，确认页会看 HP 决定是否升级，休息点选项却不跟着 HP 变（`out/rest_stats.json`）。
15. **训练式采样下的循环**（u2225，复用训练器的 env 和起点池：4096 个新开局 + 148 个人类存档 ×15 份确定化副本，存档开局占 0.3515；64 env，2 次各 120 秒）。seed 4：237,184 个决策，991 个结束局（新开局 666、存档开局 325），回访 3,358 次（1.42%），464 局（46.8%）至少一次，停滞 1 次（发生在 Event）。seed 5：201,728 个决策，867 局（581/286），回访 3,014 次（1.49%），394 局（45.4%），停滞 0 次；4,956 个决策（2.46%）落在循环内，占 56,994 个非战斗决策的 8.70%。回访按相位：休息点 1,820/1,652，DeckUpgrade 提示页 1,435/1,355，商店 50/5，DeckRemove 53/0。同一休息点状态最多被返回 20/21 次。seed 5 的 DeckUpgrade 提示页采样：Cancel 1,652 次，Confirm 215 次（`out/sample-u2225-s4.json`、`out/sample-u2225-s5.json`）。
16. **GAE 推导（用最终 critic 的数值，并非训练时测得的梯度）**：γ=1、λ=0.99，循环上 r=F=0，且两次访问的 V 相同。于是 A(SMITH) = 循环部分 + λ³·A(回访后所选动作)。SI001 的循环部分为 −0.0002，λ³=0.970。确认页取消那一步的 TD 误差为 +0.0095，因为 V(休息点)=0.024 高于 V(确认页)=0.014。在训练里，SMITH 绕一圈再 HEAL，与直接 HEAL 得到的信用几乎相同（`ppo.py:1139-1141`，`out/gae_cycle_SI001.txt`）。
17. **随训练的趋势**：s8a 周期评测（silent-a10-v1，每次 20 局，breaker 开）每局介入次数为：u251–500 0.07，u501–750 0.13，u751–1000 0.86，u1001–1250 1.09，u1251–1500 0.99，u1501–1750 0.59，u1751–2000 0.47，u2001–2250 0.93，u2251–2500 0.71。dev-128 上：u1900 25 局 28 次，u2050 52 局 88 次，u2225 94 局 177 次，u2536 75 局 124 次（`out/evals_lb_trend.txt`）。
18. **该循环早于 s8a 把升级系数调到 3 倍**：s7b（acq_upgrade_coef 0.004）的周期评测没有 breaker，1020 局中 385 局 stuck（Prompt 276、RestSite 93、Shop 16），主要出现在 u800 之后。解码 s7b 的 u1150 评测，15 个 stuck 局全部是 SMITH→选卡→取消；u1375 的 16 个 stuck 局中，15 个是 SMITH→取消，1 个是 SMITH→选卡→取消。s6b（旧库 5dbfa09dd9dcb5ed）的 120 局中 40 局 stuck，相位为 Rewards/CardReward/Shop/Prompt。s8a 开 breaker 后 1780 局 0 stuck（`out/prebreaker_stuck_phases.txt`，`out/s7b_stuck_cycles.txt`）。
19. **Live twin**：两局真游戏各发生 1 次介入，均为休息点上用 HEAL 代替重复的 SMITH（`docs/train-results-silent-a10-2026-11-04.md:78`）。
20. **loopbreak.py 的说明已过时**：模块 docstring 只说剩余的循环是非法索引和“商店删卡/取消”，没有提休息点 SMITH 取消循环；而后者现在占 u2225 介入的 176/177（`loopbreak.py:4-11` 与事实 8 对照）。

## 证据位置

| 事实编号 | 文件/命令 | 关键字段或行号 |
| --- | --- | --- |
| 1 | /Users/Aincrad/dev-proj/hack-sts2/.claude/worktrees/recorder-performance-lag-0f9d6d/sim/tools/train/sts2train/loopbreak.py | 41-45 LEAVE_KINDS/K/WINDOW；48-79 ObsDigest；125-174 apply（160 banned，162 全禁不屏蔽，164-166 k 次后只留离开类）；176-195 commit/greedy |
| 2 | 同目录 evaluate.py；cli.py；ppo.py；reportcard.py；lookahead.py；/Users/Aincrad/dev-proj/hack-sts2/.claude/worktrees/recorder-performance-lag-0f9d6d/scripts/live/nn_twin/twin.py；/Users/Aincrad/dev-proj/hack-sts2/.claude/worktrees/recorder-performance-lag-0f9d6d/scripts/live/nn_twin_play.py | evaluate.py 46-64、166、188；cli.py 162-165、204-208、411-415；ppo.py 161-163、953-993；reportcard.py 577-585；lookahead.py 686-698；twin.py 119-125；nn_twin_play.py 62-64 |
| 3 | /private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/next/review/extract/gpu1/runs/s8a-silent-a10-mid-upg3/config.json（与 runmeta-sts2train.tar.gz 的条目大小一致）；/private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/diag/q4-loopbreaker/out/metrics_stall.json | config.rollout_loop_breaker=false；loop_breaker_metric_keys=[]；ppo.py 1075、1085、1198-1200 |
| 4 | ppo.py；features.py | ppo.py 546-550（ratio）、730-737（sample adjust）、1077、1085；features.py 314-319（无掩码字段） |
| 5 | /Users/Aincrad/dev-proj/hack-sts2/.claude/worktrees/recorder-performance-lag-0f9d6d/sim/Sts2Sim.Vec/VecEnv.cs；Status.cs；objective.py；/private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/diag/q4-loopbreaker/out/lboff-stuck-2games.jsonl | VecEnv.cs 698-770、943-948；Status.cs 25-28；ppo.py 1091、1139-1141；objective.py 109-112、175；截断步 status=256、F_acq=−0.096/−0.116 |
| 6 | /private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/diag/q4-loopbreaker/out/lineage_stall.json；/private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/diag/q4-loopbreaker/out/metrics_stall.json | metrics.jsonl 的 floor_stall、episodes、illegal 按 update 求和 |
| 7 | /private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/diag/q4-loopbreaker/out/dev128-u2225-c0.report.json、dev128-u2225-c1.report.json | replay_mismatch=0；final_hash_mismatch=[]；loop_breaks 99+78 |
| 8、9、12、13 | /private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/diag/q4-loopbreaker/out/dev128-u2225-loops.json；/private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/diag/q4-loopbreaker/out/loop-cases.md | by_location；cycle_shapes；identity；sim_state_hash_equal_at_revisit；upgrade_term；F_sum_check_bad_rows；RunSim.cs 172-177；RunState.cs 354-356；model.py 574 |
| 10 | /private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/diag/q4-loopbreaker/out/dev128-on-vs-off.json；out/dev128-lboff-c0/、out/dev128-lboff-c1/ | paired_delta_*；off_stuck_set_equals_on_loop_break_set=true；replay audit 64/64 ×2 |
| 11 | /private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/diag/q4-loopbreaker/out/rank_on_off.json；存档 la-u2050-dev-greedy/stats.json | on/off 两种情况下 u2225−u2050；la-u2050 stats.progress_mean=0.28176 |
| 14 | /private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/diag/q4-loopbreaker/out/rest_stats.json | hp_frac_*；p_confirm_on_confirm_screen |
| 15 | /private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/diag/q4-loopbreaker/out/sample-u2225-s4.json、sample-u2225-s5.json | revisit_rate_per_decision；decisions_inside_cycles_frac；revisits_by_phase；prompt_choices |
| 16 | /private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/diag/q4-loopbreaker/out/gae_cycle_SI001.txt | cycle_part −0.000196；weight 0.9703 |
| 17 | /private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/diag/q4-loopbreaker/out/evals_lb_trend.txt | s8a evals.jsonl 的 loop_breaks；各 final-eval 的 report.json |
| 18 | /private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/diag/q4-loopbreaker/out/prebreaker_stuck_phases.txt、s7b_stuck_cycles.txt | s7b evals/u*/games 的 stuck.phase；u0001150、u0001375 解码 |
| 19 | /Users/Aincrad/dev-proj/hack-sts2/.claude/worktrees/recorder-performance-lag-0f9d6d/docs/train-results-silent-a10-2026-11-04.md | 第 78 行 |
| 20 | loopbreak.py | 4-11 |

## 尚不确定之处

- **训练中 461 次停滞发生在哪里**：没有日志支持。metrics 只记录每个 update 的计数，不记录相位和层。本地采样 1,858 局里只出现 1 次停滞，在 Event，样本太小。解决办法：rollout 中每次 FLOOR_STALL 截断都记录 (update, slot, phase, prompt_kind, floor, 本层回访次数)。
- **训练各阶段的真实循环率**：没有日志支持，因为 rollout 没有计数器。事实 15 只用了最终的 u2225，起点序列（seed_text）也与训练不同。解决办法：加一个“只计数”的 rollout 模式（`LoopBreaker(enabled=False)`，现有 `--rollout-loop-breaker` 会屏蔽，不能只计数），每个 update 按相位记录 cycle_revisits、no_op 和循环内决策数。
- **`--rollout-loop-breaker` 对学习的实际影响**：从未运行过。事实 4 是读代码得出的推断。
- **从源头消除循环能否带来超过 +0.120 的真实提升**：尚未测试。breaker 只是事后挽救；不确定把训练改成看得到“已取消过”或惩罚空转后，休息点选择会不会变好。
- **test-100 关闭 breaker 的差距**：有意没有跑，以免多次使用测试集。存档中开 breaker 时为 62/100 局、116 次介入。
- **u2050 的 off 结果**来自 CUDA 存档，u2225 的 off 结果来自本地 CPU。CPU 重放已经逐个决策复现了 u2225 在 CUDA 上的决策，但 u2050 没有在 CPU 上复核。
- 37 个战斗步上 master deck 的升级变化，具体是哪张卡或哪个遗物造成的，没有解码。只确认了其中 36 个发生在结束战斗的那一步。
- 事实 16 用的是最终 critic 的 V，训练时的 critic 不同。它说明信用怎么分配，不是测得的梯度。
- 3 倍升级系数是否加重了 SMITH 取消习惯：无法归因。s7b 用 0.004 也出现了同样的循环（事实 18），但两条谱系不是对照实验。

## 典型轨迹（逐步表，节选；全文见 out/loop-cases.md）

所有数字都由训练器自己的奖励代码计算。“相同”指 64 位摘要、27 个模型缓冲的 sha256 和 StateHash 都与首次访问一致。

**例 1：休息点 SMITH→选卡→确认页取消（SILENT-D1A10SI001，F16，hp 47/70，第 1 次介入）**

| t | 相位 | 合法动作 / top 概率 | argmax | 实际执行 | breaker | r_progress | F(牌组/遗物/升级) | V |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 217 | RestSite | SMITH .638；HEAL .362 | SMITH | SMITH | - | 0 | 0/0/0 | .024 |
| 218 | Prompt/DeckUpgrade（27 行，未选） | PERFECTED_STRIKE .055，STRIKE .046… | PERFECTED_STRIKE | 同 | - | 0 | 0/0/0 | .014 |
| 219 | Prompt/DeckUpgrade（已选，待确认） | Cancel .993；Confirm .007 | Cancel | Cancel | - | 0 | 0/0/0 | .014 |
| 220 | RestSite，**与 t=217 相同** | SMITH .638；HEAL .362 | SMITH | **HEAL** | 屏蔽行 1 | 0 | 0/0/0 | .024 |
| 221 | RestSite（hp 68/70） | LeaveRestSite 1.0 | 同 | 同 | - | 0 | 0/0/0 | .036 |

**例 2：商店删卡→选卡→取消（SILENT-D1A10SI044，F15，hp 19/70，唯一一次商店介入）**

| t | 相位 | top 概率 | argmax | 实际执行 | breaker | r_progress | F |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 217 | Shop（金币 113） | 删卡 $100 .333；THE_BOMB $90 .202；MEMENTO_MORI .176；POUNCE .120；Leave .106 | 删卡 | 删卡 | - | 0 | 0 |
| 218 | Prompt/DeckRemove（26 行） | FLICK_FLACK .065… | FLICK_FLACK | 同 | - | 0 | 0 |
| 219 | Prompt/DeckRemove（待确认） | Cancel .999；Confirm .001 | Cancel | Cancel | - | 0 | 0 |
| 220 | Shop，**与 t=217 相同** | 同 t=217 | 删卡 | **THE_BOMB $90** | 屏蔽行 5 | 0 | 0 |
| 221 | Shop（金币 23） | Leave .894 | Leave | Leave | - | 0 | 0 |

breaker 的替代动作是“剩下的最优行”，这里是花 90 金币买牌，而不是离开。只有到第 k=3 次回访时才会优先选择离开。

**例 3：SMITH 后直接在选卡页取消（SILENT-D1A10SI049，F13，hp 52/70）**：t=125 SMITH .513 / HEAL .487 → t=126 选卡页 argmax 是 PromptCancel .070（共 18 行）→ t=127 与 t=125 相同，SMITH 被屏蔽，执行 HEAL（.487）→ hp 70/70。r 与 F 全部为 0。

**对照：真实升级（SILENT-D1A10SI003，F11，hp 70/70）**：t=94 SMITH .951 → t=95 选 DEADLY_POISON → t=96 PromptConfirm .635，**F=+0.012（升级项），升级数 0→1** → t=97 离开。

**介入最多的局：SILENT-D1A10SI082，共 6 次**，分别在 F11/16/24/27/29/32，全部是 SMITH(p .65–.84)→选卡→取消，再被替换为 HEAL；hp 在 31/70 到 60/70 之间。

**关闭 breaker 时的同一局**：SI001 在 F16 用完 80 个非战斗决策，只在 3 个观测之间循环，最常见的那个出现 27 次。截断步 status=256，r_progress=0，F=−0.096，与在该层死亡等价。

## 直接回答第 4 项各问

- **loop-breaker 修复了什么**：它让评测里的贪心策略不再卡死在“回到逐字节相同状态”的零进展循环中。对 u2225，这样的循环几乎全部是休息点升级取消，另有一次商店删卡取消。它没有修复训练，也没有修复策略本身。dev-128 上 0.372 中有 +0.120 来自 breaker，0.252 才是策略自身的贪心水平。
- **模型能否看出状态差异**：不能。所有回访的输入完全相同，模型也没有记忆。重复不是学到的“喜欢重复”。学到的是休息点（SMITH）和确认页（取消）这一对互相矛盾的偏好。确认页会看 HP：确认时 HP 中位 1.00，取消时 0.63。休息点选项却不跟着 HP 变。
- **升级奖励是否只在真实升级时发放**：只在牌组升级等级之和变化时发放，选择 SMITH 或预览时不发放。这个量也包括拿到已升级的卡等情况，不只是“执行一次升级”。
- **训练中是否有循环**：有，且频繁但很短。按训练方式采样，约 1.4–1.5% 的决策、约 46% 的局有回访，2.5% 的决策落在循环内。它们几乎都在停滞上限之前退出（谱系中 0.031% 的局被截断）。循环零奖励、零代价，GAE 让 SMITH 继承约 97% 的后续优势，训练没有压低它。从 s7b 到 s8a，评测中的循环倾向一直在增长。
- **PPO 行为概率**：本谱系不替换动作，不存在这个问题。若开启 `--rollout-loop-breaker`，会出现事实 4 所说的单侧比率偏差，需要把掩码存下来、在更新时重新施加，之后才可以使用。

## 对下一步决策的含义（供汇总第 1–4 项）

- **先修复实现与度量，成本低，建议在任何长训练之前完成**：(1) 所有 checkpoint 比较同时报告 breaker 开和关两组配对结果，因为排名会翻转（事实 11）。(2) 训练增加只计数的循环日志和 FLOOR_STALL 位置日志。(3) 更新 loopbreak.py 的说明。
- **可作为实验线的“唯一主要改动”**，二选一，并与匹配预算的续训对照比较：(a) 让状态可区分，例如在观测里加入“本处已取消过/本观测回访次数”；(b) 对零进展回访加小额惩罚，或改为 γ<1。判据：关闭 breaker 时的 dev-128 progress，以及 breaker 介入率。两者都**未测试**。注意 (a) 会改变编码器，需要按 widening 流程续训。
- 按 dev-128 关闭 breaker 的结果，u2050 好于 u2225；因此续训的共同起点若只看开 breaker 的指标来选，可能选错。

## 从现在起应记录的内容

1. rollout 每个 update 记录 `cycle_revisits`、`no_op_actions`、循环内决策数，并按相位拆分（只计数，不屏蔽）。
2. 每次 FLOOR_STALL 截断记录 phase、prompt_kind、floor、该层回访次数，以及新开局/存档开局。
3. 评测 summary 中记录每次介入的 (t, phase, floor, argmax, 替代动作)。目前只有次数，位置要靠重放才能得到。
4. 周期评测同时跑 breaker 关闭的版本（20 局开销很小），并把 off 的 progress 记入 evals.jsonl。

## 复现命令

```bash
Q4=/private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/diag/q4-loopbreaker
EX=/private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/next/review/extract
CK=/private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/deliverable/u2225/checkpoint/ckpt-0002225.pt
RUN=$EX/gpu1/final-eval-gpu1-seg3/s8a-silent-a10-mid-upg3-best/evals/silent-a10-dev-v1
export STS2VEC_DLL=/Users/Aincrad/dev-proj/hack-sts2/.claude/worktrees/recorder-performance-lag-0f9d6d/sim/Sts2Sim.Vec/bin/Release/net9.0/ OMP_NUM_THREADS=3 MKL_NUM_THREADS=3
PY="nice -n 10 uv run --project /Users/Aincrad/dev-proj/hack-sts2/.claude/worktrees/recorder-performance-lag-0f9d6d/sim/tools/train python"
cd $Q4
python3 -I scripts/lineage_stall.py $EX/gpu2/runs/s6b-silent-a10-mid-signal $EX/gpu2/runs/s7b-silent-a10-mid-newenv $EX/gpu1/runs/s8a-silent-a10-mid-upg3
for c in 0 1; do $PY scripts/replay_lb.py --checkpoint $CK --run-dir $RUN --out out/dev128-u2225-c$c.jsonl --chunk $c/2; done   # 约 25 s/块
python3 -I scripts/analyze_loops.py out/dev128-u2225-loops out/dev128-u2225-c0.jsonl out/dev128-u2225-c1.jsonl
python3 -I scripts/make_tables.py out/dev128-u2225-loops.json out/loop-cases.md out/dev128-u2225-c0.jsonl out/dev128-u2225-c1.jsonl
python3 -I scripts/rest_stats.py out/dev128-u2225-c0.jsonl out/dev128-u2225-c1.jsonl
for c in 0 1; do $PY scripts/eval_lb_off.py --checkpoint $CK --run-dir $RUN --out out/dev128-lboff-c$c --chunk $c/2 --breaker off; done   # 约 20 s/块
python3 -I scripts/compare_on_off.py $RUN out/dev128-u2225-loops.json out/dev128-on-vs-off.json out/dev128-lboff-c0 out/dev128-lboff-c1
python3 -I scripts/rank_on_off.py $RUN out/dev128-lboff-c0,out/dev128-lboff-c1 $EX/gpu1/final-eval-gpu1/s8a-silent-a10-mid-upg3-ckpt-0002050/evals/silent-a10-dev-v1 $EX/gpu1/la-u2050-dev-greedy
$PY scripts/sample_loops.py --checkpoint $CK --saves /private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/bundle-26f1/sts2train-26f1d805a0/saves --out out/sample-u2225-s5.json --seconds 120 --seed 5
python3 -I scripts/gae_cycle.py out/dev128-u2225-c0.jsonl SILENT-D1A10SI001 217
python3 -I scripts/evals_lb_trend.py $EX/gpu1/runs/s8a-silent-a10-mid-upg3
```

## 复核（独立核查）

复核日期 2026-11-04。复核没有复用备忘录的脚本，而是另写一套脚本重新推导。目录为 `/private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/diag/q4-loopbreaker/verify/`，脚本在 `scripts/`，输出在 `out/`，汇总见 `out/verify_summary.json`。所有运行都在本机 CPU 上完成：`nice -n 10`、3 线程、≤64 env，单次不超过 110 s。

独立复现手段：
- `v_replay.py`：不调用 `sts2train.loopbreak`。用 u2225（d59f9ea2095799c0）在本地库 3b73317739c650bd 上按存档 history_actions 重放 dev-128（2×64 局），逐决策记录以下内容：模型 argmax 与实际执行行、27 个模型缓冲和全部 37 个缓冲的 sha256、决策前的 StateHash、训练器 `EpisodeRewards`/`AcqShaping` 算出的 r 与 F。输出在 `out/v-dev128-c{0,1}.jsonl`，分析结果在 `out/v-dev128-analysis.json`。
- `v_eval_off.py`：走 `sts2train eval` 的同一路径（`sts2train.evaluate.evaluate`，`loop_breaker=False`），重跑 dev-128 的两个 64 局切片，输出在 `out/v-off-c0`、`out/v-off-c1`。配对差与 t 区间由 `v_pair.py` 计算。
- `v_lineage.py`、`v_stuck.py`、`v_decode.py`、`v_locate.py`：重算 metrics.jsonl 的合计；重新统计 s6b、s7b、s8a 周期评测中的 stuck 局。随后不加载权重，只按记录动作重放全部 385 个 s7b stuck 局和 s8a 全部 89 次周期评测，逐次定位循环。
- `v_sample.py`：训练式采样，使用训练器的 `env_kwargs` 和 `start_pool_from_config`，回访用自写的 sha1 计数，不经过 LoopBreaker。用第三个随机种子 6，跑 100 s。

### 核实无误的事实

- **事实 1**：`loopbreak.py:41-45、48-79、125-174、176-195` 与描述一致。离开优先规则只在 `len(tried) >= self.k` 时启用（164 行）；全部行都被禁时不做屏蔽（162 行）。
- **事实 2**：`evaluate.py:46-64、166`，`cli.py:411-415`（默认 on），`ppo.py:161、953-993`，`reportcard.py:577-585`，`twin.py:119-125`，`nn_twin_play.py:62`，`lookahead.py:686-698`，均与描述一致。另有补充，见补充发现 1。
- **事实 3**：s8a 与 s8b 的 config.json 中 `rollout_loop_breaker=false`；s6b 与 s7b 没有这个键。四个 run 的 metrics.jsonl（s6b 126 行、s7b 1274 行、s8a 2186 行、s8b 1325 行）都没有 loop_breaks、no_op_actions、cycle_revisits 键，而这些键只经 `ppo.py:1197-1200、1238` 写入。`ppo.py:1074-1085` 的无 adjust 分支存入的是未屏蔽的 logp。git 记录显示，loopbreak 与 `--rollout-loop-breaker` 同在 059604a（2026-11-04 02:06 EST）引入，早于 s8a 会话 3（03:04 EST）。
- **事实 4（代码推断，未运行）**：`ppo_loss` 用 `model(xb)` 重算 logits（`ppo.py:541-549`）；模型只按 j<cand_n 屏蔽（`model.py:612-613`）；RolloutStore 没有掩码字段（`features.py:314-321`）；s8a 的 clip 为 0.2。比率与单侧裁剪的推导复核无误。SI001 回访时比率为 1−0.63775=0.36225。
- **事实 5**：`VecEnv.cs:698-767、947-948`，`Status.cs:25-28`，`ppo.py:1091、1139-1141`，`objective.py:109-112、175`。补充一点：`progress.py:58` 把所有非胜利结束都按 "loss" 计分，所以停滞与在同层死亡的进度奖励相同。我重放了关闭 breaker 的 SI001：t=217–296 共 80 个非战斗决策，只有 3 个不同观测，同一观测最多出现 27 次；末步 status=256、r=0、F=−0.096。SI044 末步 F=−0.116（`out/v-off-c0-replay.jsonl`）。
- **事实 6**：重算结果为 461/1,492,418（0.0309%），非法动作 0。三段分别为 55/128,599、52/178,274、354/1,185,545。env 步 291,635,200，与模型卡第 3、22 行一致，模型卡的 1,492,418 局也一致。各 run 的 update 行没有重复，也没有缺失。s8a u401–900 为 0 次，u1001–1100 为 158/62,898。
- **事实 7**：独立重放共 39,869 个决策。模型 argmax 与记录动作只在 177 个回访点不同，其余全部一致；128/128 局终局哈希一致。
- **事实 8**：177 次、94/128 局，每局分布为 {0:34, 1:43, 2:32, 3:10, 4:6, 5:2, 6:1}。休息点 176 次、商店 1 次；循环长度 3 的 174 次、长度 2 的 3 次；全部发生在第 1 次回访。替换结果为 SMITH→HEAL 176 次、删卡→无色卡 1 次（金币 113→23，即 $90）。存档 report.json 中 cycle_revisits=177、no_op=0，与 loop_breaks 相同，说明每次回访都被介入。
- **事实 9**：177/177 次回访中，27 个模型缓冲的 sha256、全部 37 个缓冲的 sha256、StateHash 都与首次访问相同，V 与 p(argmax) 也相同。`RunSim.cs:172-188` 调用 `RunState.cs:354-356`，把 RNG 写进哈希。`model.py:574` 只接收观测，模型中没有循环结构。
- **事实 10**：我自己重跑了关闭 breaker 的评测，两块分别为 0.25270 和 0.25111，均值 0.25191；逐局动作序列与备忘录的 off 运行 128/128 相同。配对差 +0.12027，t 区间 [0.0975, 0.1431]，bootstrap（20,000 次）[0.0982, 0.1431]。94 局变好、0 局变差，34 局动作完全相同；stuck 集合与介入集合相同。94 局在首次介入前的动作前缀都相同，并停在首次介入所在的楼层（休息点 93、商店 1）。平均层数 13.82 对 19.76；到达第 2 幕 9 局对 57 局。
- **事实 11**：u2050 关闭为 0.28176、开启为 0.35110，配对差 +0.06935 [0.0497, 0.0890]；52 局 stuck，正好是有介入的 52 局，共 88 次介入。开 breaker 时 u2225−u2050 为 +0.02107 [−0.0028, 0.0449]（46 胜 33 负）；关 breaker 时为 −0.02985 [−0.0505, −0.0092]（33 胜 64 负）。与文档第 57–62 行的 0.282/0.351 一致。
- **事实 12**：177 个循环上 r 与 F 的和都是 0；所有非终局步上，F 与三项势能之和一致（0 行不符）。
- **事实 13**：共 246 行，来源复核如下：
  - TakeRewardCard 72 行，这 72 行的牌组数都 +1，升级和也 +1；
  - SMITH 后 PromptConfirm 51 行；
  - 事件 47 行；
  - 战斗 37 行，其中 36 行是结束战斗的那一步，另有 1 行 EndTurn 为 −1，且战斗继续；
  - 非 SMITH 的 DeckUpgrade 选择 23 行；
  - ClaimReward 7 行，宝箱遗物 4 行，变形 2 行，DeckGeneric −1 两行，商店 1 行。

  SMITH 227 行、SMITH 路径上的 PromptSelect、PromptCancel 176 行，升级和都不变；PromptConfirm 51 行均 +1，F=+0.012。SI003 t=96 由 0 变为 1。
- **事实 14 的数字**全部复现：393、227/166、176（173+3）/51、HEAL 342=166+176；HP 中位 0.631/1.00（最低 0.80）/0.514；p(confirm) 中位 0.004/0.635（最低 0.503）；回访时 p(SMITH) 中位 0.717（0.491–0.976）。末句的解释有误，见更正 2。
- **事实 15 的数字**与备忘录的 JSON 一致，但 seed 4 和 seed 5 没有用相同种子重跑。第三个种子的结果见补充发现 5。
- **事实 16**：δ(217)=−0.01021，δ(218)=+0.00070，δ(219)=+0.00951；循环部分为 −0.000196，λ³=0.970299。
- **事实 17**：evals.jsonl 的 89 行都带 loop_breaks，分段数字全部复现。dev-128 上 u1900（权重 1ae4c556e7ae91ba）28 次/25 局、u2050 88/52、u2225 177/94、u2536 124/75 也全部复现，权重哈希与 evals.jsonl 对应。
- **事实 18 的计数**：s7b 1020 局中 385 局 stuck（Prompt 276、RestSite 93、Shop 16），其中 328 局在 u800 之后；s6b 120 局中 40 局；s8a 1780 局中 0 局。u1150 有 15/20 局 stuck，u1375 有 16/20 局，解码出的形态与备忘录一致。结论句有误，见更正 1。
- **事实 19**：文档第 78 行原文一致，但所用模型不是 u2225，见更正 3。
- **事实 20**：`loopbreak.py:4-11` 确实只提到非法索引和商店删卡/取消循环。

### 需更正的事实

1. **事实 18 的标题“该循环早于 s8a 把升级系数调到 3 倍”**（报告中为 "The SMITH-cancel cycle predates s8a's 3x upgrade coefficient"）**不成立**。
   - s7b：按记录动作重放全部 385 个 stuck 局。u750 之前全部是商店删卡→选卡→取消，其中包括 u350 分叉之前、处在 u2225 谱系上的 10 局（u175–u325）。SMITH 循环在 s7b 上首次出现于 u750，共 339 局。
   - s8a：定位了全部 1,222 次周期评测介入，每次评测的计数都与 evals.jsonl 一致。u251–500 的 9 次全部是商店删卡；SMITH 循环首次出现在 u525；合计 SMITH 1,170 次、商店 52 次。
   - 被解码的 s7b u1150（09:46 UTC）与 u1375（10:21 UTC）都晚于 s8a 的开始时间（08:04 UTC）。
   - 正确表述：SMITH 取消循环不是 0.012 系数独有的，系数 0.004 的 s7b 分支后来也出现了。但两条分支都是在 u350 分叉之后才出现（s8a 在 u525，s7b 在 u750）；分叉前的谱系上只见到商店删卡循环。
   - 证据：`out/v-cycle-timeline.json`、`out/v-s7b-decode-all.txt`、`out/v-s8a-loc.jsonl`。
2. **事实 14 末句和“直接回答”中的“休息点选项却不跟着 HP 变”不成立。**
   - 在 393 个首访休息点决策中，Spearman(HP 占比, p(SMITH)) = 0.57。
   - SMITH 为 argmax 的比例：HP<0.4 时 0.44，0.4–0.6 时 0.42，0.6–0.8 时 0.48，0.8–0.95 时 0.93，≥0.95 时 0.96。
   - 正确表述：两处都随 HP 变化，但阈值不一致。确认页只在 HP≥0.80 时确认；休息点在 HP<0.8 的状态里仍有 42–48% 选 SMITH，而这些状态正是确认页取消的地方。
   - 证据：`out/v-rest-hp.json`，脚本为 `scripts/v_rest_hp.py`。
3. **事实 19 的 live twin 两局用的是 u2050，不是本备忘录讨论的 u2225。** 依据是 `docs/train-results-silent-a10-2026-11-04.md:67`：“u2050 (the 07:30 fallback; u2225 was not replayed before the 10:00 cut)”。应改写为“u2050 的两局真游戏各发生 1 次介入”。
4. **事实 15 中“占 56,994 个非战斗决策的 8.70%”的分母有误。**
   - 56,994 实际是 decision_kind≠Combat 的决策数，其中包含战斗内的提示页。seed 5 中有 HandSelect 8,773、ChooseCard 870、PileSelect 163、HandUpgrade 4。
   - dev-128 重放的 is_combat 标志显示，HandSelect 与 PileSelect 全部在战斗中，ChooseCard 有 153/155 在战斗中。HandUpgrade 在 dev-128 中没有出现，按名称计入，影响可忽略。
   - 去掉这些之后，真正的非战斗决策约 47,184 个，循环内决策占比约 10.5%（4,956/47,184）。
5. **结论和“直接回答”中的“从 s7b 到 s8a，评测中的循环倾向一直在增长”**（报告中为 "Loop propensity grew during training"）**说得过头了。**
   - s8a 每次 20 局的周期评测中，每局介入次数从 u251–500 的 0.07 升到 u751–1500 的 0.86–1.09，之后在 0.47–0.93 之间波动。
   - dev-128 上 u1900 为 28 次、u2050 为 88 次、u2225 为 177 次、u2536 为 124 次，并不单调。
   - s7b 的数字是未开 breaker 时的 stuck 局，而且属于另一条分支，与上面不是同一个量。
   - 正确表述：u251–1000 期间上升，之后波动。
6. **事实 13 中“删除已升级卡 2 行，为负值”只对了一半**，两行里只有 1 行是删除：
   - SI055 t=331：DeckGeneric，牌组 20→19，F=−0.015，是删除；
   - SI103 t=104：同为 DeckGeneric，但牌组数不变（18→18），F=−0.012。这是一张已升级的 Strike 被改变，不是删除。
   - 证据：`out/v-dev128-c1.jsonl`。

### 无法核实的事实

- **事实 15 中 seed 4 和 seed 5 的具体数字**：没有用相同种子重跑，只核对了 JSON 内部的一致性，例如 56,994 = 201,728 − 144,734、464/991 = 46.8%。第三个种子给出的量级一致，见补充发现 5。
- **事实 3 对 s8a 会话 3（bundle 9f6608701d）的判断**：config.json 是最后一次会话解析后的配置。会话 3 的 rollout 没有 breaker，是根据 metrics 全程没有循环计数键、再加上 git 的时间顺序推断的，没有读到那个 bundle 的源码。
- **例 2 的卡名**（THE_BOMB、MEMENTO_MORI、POUNCE）：只解码到了类别（colorless_card $90，与金币 113→23 吻合），卡名本身没有核对。
- **TakeRewardCard 的 72 行**：确认了牌组数 +1、升级和 +1，但没有解码升级的是不是所拿的那张卡本身。
- **事实 19**：只有文档依据，没有读 live twin 的原始日志。
- **事实 4 对学习的影响**：与备忘录相同，只有代码推断，没有实际运行。

### 补充发现

1. **lookahead 不只是贪心基线不开 breaker。** lookahead 策略本身在搜索之外也是纯 argmax（`lookahead.py:334-338`，docstring 第 689–691 行），所以 0.274 对 0.282 的比较两边都没有 breaker。report-card 的 greedy-ref 行同样是 breaker off（`reportcard.py:11-12、402-421`）。
2. **启用 `--rollout-loop-breaker` 前需先修实现。** `ppo.py:13-16` 与 `cli.py:204-208` 的说明称，存入屏蔽后的 logp 之后，“PPO 的比率相对于实际采样的策略”。但更新时并没有重新施加掩码（见事实 4）。`tests/test_loop_breaker.py:203-240` 只测了采样一侧，没有覆盖更新一侧。启用之前，必须先把掩码存进 RolloutStore，并在 `ppo_loss` 中重新施加。
3. **loopbreak.py 的说明在写成时（02:06 EST）是准确的。** 那时分叉前的谱系上只有商店删卡循环。SMITH 循环到 s8a 的 u525 才出现，到 u751–1000 已占介入的 165/173，所以这份说明对 u2225 已经过时。
4. **SMITH 循环的首次出现**：s8a（系数 0.012）在 u525，s7b（系数 0.004）在 u750。这提示 3 倍系数可能让它更早出现，但两者属于不同分支，周期评测每次只有 20 局，不能据此归因。
5. **第三个采样种子**（seed 6，100 s，自写 sha1 计数，不用 LoopBreaker）：190,016 个决策中回访 3,151 次（1.66%）；786 个结束局中 378 局（48.1%）至少回访一次；停滞 0 次。按相位分：RestSite 1,742、DeckUpgrade 1,399、Shop 7、DeckRemove 2、Rewards 1。合并三个种子，回访率为 1.42–1.66%，至少回访一次的局占 45–48%（`out/v-sample-s6.json`）。
6. **GAE 结论的措辞。** A(SMITH) 与在同一状态直接做选择的差为 −0.0002 − 0.03·A(回访后动作)，符号取决于 A：A>0 时绕路少拿 3% 的信用，A<0 时绕路少受 3% 的惩罚。所以应当说“没有系统性的反向梯度”，而不是“完全没有梯度”。
7. **1,492,418 个训练结束局是 episode，不是独立游戏。** 其中新开局 967,401 个、存档开局 525,017 个（见 metrics 的 fresh_episodes 与 save_episodes）。新开局来自固定的起点池：采样日志为“4096 fresh + 2220 save entries”，对应 `ppo.py:352-353` 中的 seed_prefix `TRAIN{seed}`。这一点与第 2 项（种子）有关，此处只作提示。
8. **两种 stuck 位置的说法并不矛盾。** 关闭 breaker 时，u2225 的 stuck 局在截断那一刻的相位为 Prompt 91、RestSite 2、Shop 1；备忘录说首次介入位置是休息点 93、商店 1。两者描述的是同一批循环，只是取的时刻不同。

**结论**：备忘录的核心结论全部成立，包括机制、生效位置、训练从未启用、回访时观测逐字节相同、循环零奖励、+0.120 的差距、排名翻转、升级项的发放时机，以及训练期的停滞率。需要更正的是 6 处表述：
- 事实 18 的时间与因果说法；
- 事实 14 对 HP 的解释；
- 事实 19 所用的模型；
- 事实 15 的分母；
- “一直在增长”这一趋势描述；
- 事实 13 中一处来源。

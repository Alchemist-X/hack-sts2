# Q5 诊断：模型实际在优化哪些奖励（s8a 谱系，最终选型 u2225）

路径约定：`<Q>` = `/private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/diag/q5-rewards`，`<T>` = `/Users/Aincrad/dev-proj/hack-sts2/.claude/worktrees/recorder-performance-lag-0f9d6d/sim/tools/train/sts2train`，`<X>` = `/private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/next/review/extract`（归档运行元数据，只读）。

## 结论摘要

1. PPO 训练用的奖励是三项之和：协议进度增量、胜利奖励 1.0、获取势函数塑形。参数为 γ=1.0、GAE λ=0.99、rollout 64、1 epoch、minibatch 4096、2048 envs。因为 γ=1，且回合结束时 φ=0，每回合的塑形总和恒等于 −φ(起点)，所以塑形不改变最优策略，只改变学习过程中的信用分配。评论家学到的是含塑形的价值：价值头对含塑形回报的 EV 是 0.53/0.55，对不含塑形回报只有 0.32/−0.15。
2. 战斗步占全部步数的 75–80%。在这些步上，目标奖励在样本中恰好为 0，唯一的即时奖励是死亡那一步的塑形"欠账" −φ(s)：新开局平均每局 −0.099，存档开局每局 −0.209。进度奖励的 98.0% 落在地图移动步，2.0% 落在 Boss 战后的 ProceedRewards 步。击杀第一幕 Boss 只记 1/51，和走一个房间完全相同。
3. 胜利奖励只出现在 A9 人类存档开局（第 2/3 幕）。谱系 u1–u2225 共 1,492,418 局，胜 26,575 局，全部是存档开局；新开局 967,401 局，胜 0 局。所以胜率头的正样本全部来自 A9 存档。精确正目标估计约 0.79M，占全部胜率目标的 0.27%；84% 的胜率目标是胜率头在窗口末端对自己的自举预测。
4. 胜率头在存档开局上对真实结局有区分力：AUC 0.976，BCE 0.118，常数基线为 0.308。但在 A10 新开局上它从未见过正样本，输出约 1e-4，无法验证。在动作排序测试中，它在精英战上与随机无异（ρ=−0.01）。
5. 动作排序测试覆盖 48 个第一幕 Boss/精英进场状态，每个状态做 K=16 次确定化，共 8,064 次 rollout。按 Spearman ρ：战斗血量头 0.49，价值头 0.38，胜率头 0.11，策略 logits 自身 0.59。三个头都不如策略先验，也都远低于约 0.87 的噪声上限。
6. GAE 对截断和终止的处理完全相同，截断处不自举。但截断极少：谱系中 floor-stall 只有 461 次（0.031%），SIM_ERROR/CONTENT_GAP 为 0，所以这对当前训练的影响可以忽略。

## 已确认事实

1. s8a（含 u2225）的训练配置如下：objective=protocol，win_bonus=1.0，gamma=1.0，lam=0.99，rollout=64，epochs=1，minibatch=4096，envs=2048，lr=3e-4（整个谱系 metrics 的 lr 只有 0.0003 一个值，无调度），ent_coef=0.02，vf_coef=0.5，clip=0.2，max_grad_norm=0.5。其余设置：aux_heads on（aux_coef 0.1，aux_bootstrap on），acq_shaping on（deck 0.003 / relic 0.01 / upgrade 0.012），floor_step_cap=80，step_cap=3000，max_rows=64，rollout_loop_breaker=False。起始池为 4096 个 A10 新开局，加 148 个 A9 存档各 15 份确定化副本，实际存档比例 0.351488。u2225 checkpoint 自带的 config 与此一致（`<X>/gpu1/runs/s8a-silent-a10-mid-upg3/config.json` 的 config/start_pool；`<Q>/out/rollA.log` 首部 config 行）。
2. 目标奖励的定义：存活步 r = progress(s') − progress(s)；回合结束步 r = 最终协议进度 − progress(s)，胜利再加 1.0（`<T>/objective.py:108-112`）。进度公式为 ((幕−1) + min(c, L−1)/L)/3，胜利时进度为 1.0（`sts2vec/protocol.py:59-64, 80`）。因此每进一个房间加 1/(3L)，对应第一幕 0.0196、第二幕 0.0208、A9 第三幕 0.0222。死亡步的目标奖励为 0，没有显式的死亡惩罚。样本中 2,360 次死亡全部发生在战斗步，这些步的目标奖励均为 0（`<Q>/out/rollAB_analysis.json` per_step_groups）。
3. 塑形的定义：φ = 0.003·min(deck_n−10, 12) + 0.01·relic_n + 0.012·Σ升级等级（`<T>/objective.py:162-166`）。每步塑形 F = γ·φ(s')·(1−done) − φ(s)（`objective.py:175`）。F 加到 PPO 的 reward 上（`<T>/ppo.py:1097-1104`），但 metrics 里的 reward_mean 只记录不含塑形的部分（`ppo.py:1230-1231`）。样本中 1,734 个回合的分项之和与训练器自身记账（e["shaping"]、e["return"]）的最大误差为 0；新开局每局塑形恒为 −0.019 = −φ(起点)（`<Q>/out/rollAB_analysis.json` per_episode.max_chk_*、shaping_mean）。
4. GAE 的实现：done = term | trunc（`ppo.py:1091`）。nonterm = 1−done，δ = r + γ·V(s')·nonterm − V(s)，gae = δ + γλ·nonterm·gae（`ppo.py:1137-1142`），ret = adv + V（`ppo.py:1143`）。只在 64 步窗口末端用窗口后的观测 V(s_T) 自举（`ppo.py:1121-1124, 1138`）。截断（floor-stall 80、step_cap 3000、SIM_ERROR/CONTENT_GAP）与死亡/胜利按同一方式处理，都不自举。优势按更新做标准化（`ppo.py:1151-1154`）。1 个 epoch 意味着每个样本只用一次，每次更新的 131,072 个样本分成 32 个 4096 的 minibatch（`ppo.py:1156`）。
5. 截断很少。谱系 u1–u2225（s6b 1–126 → s7b 127–350 → s8a 351–2225，共 2,225 行，291,635,200 env-steps，与 ckpt 的 env_steps 一致）中：floor_stall 461 次，占 1,492,418 局的 0.031%；sim_error、content_gap、illegal 都是 0。本地样本 2,428 个结束回合中截断为 0（`<Q>/scripts/q5_metrics.py` → `<Q>/out/metrics_composition.json` lineage_u1_2225；`rollAB_analysis.json` per_step_groups.trunc）。
6. 辅助头的目标：回合在窗口内以终止结束时，该回合各步的胜率目标取真实结局；截断回合被屏蔽；窗口末端仍在进行的回合，用胜率头自身在窗口后观测上的预测作为软目标（`<T>/heads.py:100-133`，其中 111-118 行）。损失为 0.1 × (BCE + 血量平方误差)，与 PPO 损失相加并共享主干（`heads.py:46-53`；`ppo.py:554-556`）。
7. 本地样本：用 u2225 的采样策略、训练起始池、64 envs，跑两次各 165 秒（种子文本 Q5A/Q5B），共 575,872 env-steps。按起始类型和是否战斗分组时：战斗步占新开局步数的 75.2%（311,695/414,656），占存档开局步数的 80.0%（129,042/161,216）。440,737 个战斗步上的 r_prog 与 r_win 恰好为 0。进度奖励的 98.0% 落在 Map/Travel 步（30,817 次），2.0% 落在 Rewards/ProceedRewards 步（634 次）。后者都是幕间过渡：第一幕 Boss 之后 +1/51，共 480 次；第二幕 Boss 之后 +1/48，共 86 次；另有 68 次 A9 第三幕 48 层胜利，每次 +1/45 再加胜利奖励 1.0（`<Q>/out/rollAB_analysis.json` progress_reward_landing；本备忘"四组奖励组成表"）。
8. 每局累计量（四组，样本均值）见"四组奖励组成表"。新开局每局目标回报 0.2901，全部来自非战斗步。塑形中，非战斗步的获取奖励为 +0.0780（deck 0.0225 / relic 0.0347 / upgrade 0.0209），战斗步为 −0.0970（其中死亡欠账 −0.0986）。存档开局每局目标回报 0.2655 = 进度 0.1799 + 胜利 0.0857；塑形中战斗步 −0.2094，非战斗步 −0.0035（获取 +0.0435，胜利时的欠账 −0.0470）（`rollAB_analysis.json` per_episode）。
9. 塑形占绝对奖励总量（Σ|F| / (Σ|r_obj| + Σ|F|)）的 43.8%。分组看：新开局非战斗步 21.5%，存档非战斗步 25.1%，战斗步 100%（战斗步只有欠账）。胜利奖励占目标奖励绝对总量的 9.8%（68.0/694.5），且全部来自存档开局（`rollAB_analysis.json` per_step_groups.*.shaping_share_of_abs_reward）。
10. 评论家学到的是含塑形回报。价值头对含塑形蒙特卡洛回报的 EV：新开局 0.529、存档 0.549；对不含塑形回报：0.318、−0.154。存档开局的平均价值为 −0.087，接近含塑形回报的 −0.061，而不含塑形回报为 +0.223（`<Q>/scripts/q5_value_fit.py` → `<Q>/out/value_fit.json`）。
11. 样本与训练末段的统计一致。训练 s8a u2176–2225（50 次更新）每步：reward_mean 0.001174，reward_mean_shaped 0.000819，shaping_mean −0.000355，acq_potential_mean 0.130；每局：fresh_return 0.2913，save_return 0.2449，shaped_return 0.1917，shaping_return −0.0833。样本每步：r_obj 0.001206，F −0.000355；新开局每局回报 0.2901，存档 0.2655，存档胜率 8.6%（训练中为 7.0% = 691/9,861）。谱系平均：reward_mean 0.001159，shaping_mean −0.000402，return 0.2254（fresh 0.2450 / save 0.1894），shaping_return −0.0788（`metrics_composition.json` s8a_u2176_2225、lineage_u1_2225）。
12. 胜率头的正样本只来自存档开局。谱系 2,225 次更新中，每次更新的 fresh_wins 都是 0（新开局共 967,401 局）；save_wins 合计 26,575，占 525,017 个存档局的 5.06%。本地样本：新开局 0/1,127；存档开局按起始幕分为第一幕 0/339、第二幕 4/140、第三幕 48/128（`metrics_composition.json` lineage_updates_with_fresh_wins=[]；`rollAB_analysis.json` per_episode.save.by_start_act）。
13. 胜率目标的构成。谱系共 291,619,834 个目标，其中精确目标 46,566,812 个（16.0%），其余为自举软目标；目标总质量 Σy = 5.05M（1.73%）。精确正目标的严格界为 [26,575, 1,700,800]，即每次胜利贡献 1–64 步。样本中实测每次胜利贡献 29.7 个（2,017/68），据此估计谱系约 0.79M 个精确正目标（0.27%），s8a 末段约 410 个/次更新（共 131,072 个目标）（`<Q>/scripts/q5_winpos.py` → `<Q>/out/winpos.json`）。
14. 用训练日志比较胜率头 BCE 与常数基线：谱系 aux_win_loss 平均 0.0304，以每次更新的平均目标算出的常数 BCE H(ȳ) 平均为 0.0856；2,225 次更新中有 2,224 次低于常数。u2225 单次为 0.0359 vs 0.0995。注意这一比较包含 84% 的自举软目标，带有自我参照，不能当作对真实结局的检验（`metrics_composition.json` aux_win_loss_mean、const_baseline_bce_mean、updates_where_loss_below_const）。
15. 用样本中的真实结局检验胜率头。存档开局（607 局、113,700 步、52 胜）：逐步 BCE 0.118，常数基线 0.308；逐步 AUC 0.976，首步 AUC 0.971；第二幕 AUC 0.942，第三幕 AUC 0.940。高概率段校准偏低：预测 0.169 时实际 0.403，预测 0.599 时实际 0.751。新开局（1,127 局、287,911 步）：正样本为 0；预测中位数 1.6e-4，p99 1.2e-3，最大 0.157；常数 0 基线的 BCE 为 0，低于胜率头的 0.00025；AUC 无定义（`rollAB_analysis.json` win_head）。
16. 新开局贪心评测中的胜率头值域：u2225 在 dev-128 与 test-100 归档 Colab 终评共 228 局，0 胜。首步均值 1.58e-4，每局最大值的中位数 1.12e-3、p95 0.0313、最大 0.108，与 q4_results.json 的 u2225.win_prob 数值完全一致（`<Q>/scripts/q5_winprob_eval.py` → `<Q>/out/winprob_eval_u2225.json`）。
17. 回放保真度：本地库重放 dev-128 的 128 局，history_actions 与 final_state_hash 和 Colab 终评 128/128 一致；排序测试的 48 个根状态，其观测哈希逐一等于 probe 记录的 "h"（`<Q>/scripts/q5_replay_check.py` → `<Q>/out/replay_check_dev128.json`：same 128、diff []；`<Q>/out/rank_*_k16.log` 中的 "observation hashes match the probe"）。
18. 动作排序测试（设计与结果见下节）。在 41 个真值不恒定的状态上，Spearman ρ（95% 自助区间；置换零分布 ±0.114）为：战斗血量头 0.492 [0.378, 0.593]，价值头 0.376 [0.257, 0.489]，胜率头 0.111 [−0.006, 0.226]，策略 logits 0.594 [0.475, 0.704]。tie-aware top-1 分别为 0.561 / 0.439 / 0.293 / 0.561，随机为 0.328。平均遗憾（本场结束血量比例）分别为 0.0146 / 0.0294 / 0.0807 / 0.0142，随机为 0.0559。胜率头在精英战上 ρ=−0.014，top-1 为 0.167，低于随机的 0.304。真值的 split-half 信度为 0.763（Spearman-Brown 校正后 K=16 约 0.866）（`<Q>/out/rank_k16_analysis.json`、`<Q>/out/rank_k16_nonconst.json`）。

## 证据位置

| 事实编号 | 文件/命令 | 关键字段或行号 |
| --- | --- | --- |
| 1 | `<X>/gpu1/runs/s8a-silent-a10-mid-upg3/config.json`；`<Q>/out/rollA.log` | config.*；start_pool.save_entries=2220、copies_per_save=15、save_mix_effective=0.351488；"config: objective=protocol … lam=0.99 rollout=64 …" |
| 2 | `<T>/objective.py`；`sim/tools/vec/sts2vec/protocol.py`；`<Q>/out/rollAB_analysis.json` | 108-112；59-64、80；per_step_groups.*/combat.r_prog=0 |
| 3 | `<T>/objective.py`；`<T>/ppo.py`；`rollAB_analysis.json` | 162-166、175；1097-1104、1230-1231、1316-1328；per_episode.*.max_chk_return/shaping=0 |
| 4 | `<T>/ppo.py` | 1091、1121-1124、1135-1143、1151-1156、546-556（损失） |
| 5 | `<Q>/out/metrics_composition.json`（`python3 -I <Q>/scripts/q5_metrics.py <X> <out>`） | lineage_u1_2225.floor_stall_sum=461、env_steps_update_sum=291635200；sim_error/content_gap/illegal 求和=0 |
| 6 | `<T>/heads.py`；`<T>/ppo.py` | 13-24、46-53、86-98、100-133；1126-1133、554-556 |
| 7 | `<Q>/out/rollAB_analysis.json`（`q5_rollout.py` ×2 + `q5_analyze_rollout.py`） | per_step_groups、progress_reward_landing、progress_reward_on_combat_steps_share=0.0 |
| 8 | 同上 | per_episode.fresh/save.*（r_prog/r_win/f_deck/f_rel/f_up/f_debt × combat/noncombat） |
| 9 | 同上 | per_step_groups.*.shaping_share_of_abs_reward；ALL=0.4383 |
| 10 | `<Q>/out/value_fit.json` | fresh/save.ev_vs_shaped_return、ev_vs_objective_return |
| 11 | `<Q>/out/metrics_composition.json` | s8a_u2176_2225.*、lineage_u1_2225.* |
| 12 | `<Q>/out/metrics_composition.json`；`rollAB_analysis.json` | fresh_wins_sum=0、lineage_updates_with_fresh_wins=[]；per_episode.save.by_start_act |
| 13 | `<Q>/out/winpos.json` | exact_positives_per_win_mean=29.66、lineage_u1_2225.exact_positive_estimate=788261 |
| 14 | `<Q>/out/metrics_composition.json` | aux_win_loss_mean=0.0304、const_baseline_bce_mean=0.0856、updates_where_loss_below_const=2224 |
| 15 | `<Q>/out/rollAB_analysis.json` | win_head.save/fresh（bce_head、bce_const_base_rate、auc_steps、calibration、fresh_range） |
| 16 | `<Q>/out/winprob_eval_u2225.json`；`…/next/analysis/q4_results.json` | all.first_mean/max_median/max_p95/max_max；u2225.win_prob |
| 17 | `<Q>/out/replay_check_dev128.json`（比较 `<X>/gpu1/final-eval-gpu1-seg3/s8a-silent-a10-mid-upg3-best/evals/silent-a10-dev-v1/games` 与 `…/next/analysis/probe/u2225-dev/eval/games`）；`<Q>/out/rank_boss_k16.log`、`rank_elite_k16.log` | history_actions、final_state_hash；"observation hashes match the probe" |
| 18 | `<Q>/out/rank_k16_analysis.json`、`rank_k16_nonconst.json` | all.*.rho_hp_mean/ci95/null95；all.random_top1；elite.win |

## 奖励定义与优化参数（u2225 实际生效值）

| 项 | 公式 / 数值 | 落在哪一步 | 代码 |
| --- | --- | --- | --- |
| 进度增量 | Δ[((幕−1)+min(c,L−1)/L)/3]：每房间 +1/(3L)，即 0.0196 / 0.0208 / 0.0222（A9 第三幕） | Map/Travel 98.0%；Boss 后 ProceedRewards 2.0% | objective.py:108-112；protocol.py:80 |
| 胜利奖励 | +1.0（另加 1 − progress = 1/45） | A9 存档第三幕 48 层 ProceedRewards | objective.py:112 |
| 死亡惩罚 | 目标中没有（死亡步 r=0）；唯一的死亡即时信号是塑形欠账 −φ(s) | 战斗步 | objective.py:112、175 |
| 获取塑形 | F = φ(s')·(1−done) − φ(s)，φ = 0.003·min(deck−10,12) + 0.01·relics + 0.012·upgrades | 获取发生的步（多为非战斗）；回合结束步还上 −φ | objective.py:162-175 |
| 截断 | floor-stall（同层 80 个非战斗决策）、step_cap 3000、SIM/CONTENT 错误：done=1，不自举，进度冻结，欠账 −φ | — | ppo.py:1091、1139；env.py:40-42 |
| 窗口边界 | 64 步窗口末端自举 V(s_T)；λ=0.99 时窗口第一步对自举项的权重为 0.99^63 ≈ 0.53 | — | ppo.py:1121-1124、1138 |
| 其余损失项 | 熵 0.02；价值 0.5（值裁剪 0.2）；辅助头 0.1×(BCE + SE)；Adam eps 1e-5，lr 恒为 3e-4 | — | ppo.py:546-556、877 |

## 四组奖励组成表（本地样本，u2225，采样策略，训练起始池）

样本规模：2 次 × 165 秒 × 64 envs，共 8,998 步/槽位 × 64 = 575,872 env-steps，结束回合 2,428 个。为去除"长回合在固定窗口内完成不了"的偏差，每局统计只保留结束前至少 1,500 步就开始的回合（保留 1,734 个：新开局 1,127、存档 607；剔除 821 个开始太晚的回合，保留集中没有未完成者）。逐步占比用窗口内全部步。它只代表 u2225 时刻的训练分布，不是整段训练的平均。

| 组 | 每局步数 | 目标奖励/局 | 其中进度 / 胜利 | 塑形/局（净） | 获取 deck / relic / upgrade | 欠账 | 塑形占绝对奖励 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 新开局·战斗 | 192.0 | 0 | 0 / 0 | −0.0970 | −0.00001 / 0 / +0.0016 | −0.0986（窗口内 1,574 次死亡） | 100% |
| 新开局·非战斗 | 63.4 | +0.2901 | 0.2901 / 0 | +0.0780 | +0.0225 / +0.0347 / +0.0209 | 0 | 21.5% |
| 存档·战斗 | 149.8 | 0 | 0 / 0 | −0.2094 | −0.0001 / 0 / −0.0003 | −0.2089（窗口内 786 次死亡） | 100% |
| 存档·非战斗 | 37.5 | +0.2655 | 0.1799 / 0.0857 | −0.0035 | +0.0067 / +0.0191 / +0.0177 | −0.0470（窗口内 68 次胜利） | 25.1% |

整体塑形占绝对奖励的 43.8%。存档开局按起始幕看每局塑形（= −φ(起点)）：第一幕 −0.055，第二幕 −0.315，第三幕 −0.519。

## 胜率头核查

- 正样本：训练中只来自 A9 存档开局，主要是第 2/3 幕起始（事实 12、13）。A10 新开局约 97 万局，没有出现过一个正样本。
- 能否胜过常数基线：用训练日志比较时 BCE 低于常数（事实 14），但这一比较有自我参照。用真实结局比较时，存档开局明显优于常数（BCE 0.118 vs 0.308，AUC 0.976）；新开局无法检验，常数 0 的 BCE 为 0，胜率头不可能胜过它（事实 15、16）。
- 同一状态下候选动作的排序：此前没有日志支持。lookahead 文档只记录了"value-only 叶评分在 turn horizon 上 +0.0018"（`docs/lookahead-2026-11-04.md:184`）。本次测试中胜率头的排序接近随机（事实 18）。
- 结论：胜率头实际上是"A9 存档第 2/3 幕能否通关"的检测器，对目标分布（A10 新开局）没有经结局验证的信息，不宜用作叶评估或停止判据。

## 动作排序测试（问题 4 的最小可行版本）

- 状态：u2225 在 dev-128 上贪心加 loop-breaker 的对局中，第一幕的战斗进场决策。按 key 顺序取前 24 局的 Boss 进场，再取前 24 局各自的首个精英进场，共 N=48。其中 Boss 根状态 11/24、精英根状态 3/24 在原评测里死在该战。根状态由 history_actions[:t] 重放得到，并通过 probe 的观测哈希校验。
- 公平性设计：
  - 每个根状态生成 K=16 个诚实确定化克隆（`env.branch(determinized=True, seed=inv_branch_seed(det_seed(0, key, t, k)))`：视图相同，重抽隐藏状态）。所有候选共用这 16 个克隆（公共随机数）。
  - 每个候选在克隆的精确副本上执行第一步，之后由同一个贪心策略打到本场结束。结束判定沿用 heads.AuxTargets：is_combat 归零、combats_won 增加或回合终止。上限 400 步，实际没有触顶或报错。
  - 真值为 16 次结束血量比例的均值，死亡记 0；生存率作为次要真值。
  - 头的分数在第一步之后的后继状态上读取，同样取 16 次均值。
  - 后继观测在全部 16 个确定化中都相同的候选（如手里同名的两张牌）合并为一类。
  - 基线有三个：随机排序（期望 top-1 = 真值最优类占比）；每状态打乱分数 4,000 次得到的 ρ 置换零分布；真值 split-half 信度（偶数 k 对奇数 k）。
- 规模：Boss 运行 3,728 次 rollout，用时 116.9 秒；精英运行 4,336 次，用时 88.6 秒；合计 8,064 次。另有 K=8、12 个 Boss 状态的试跑（808 次），结论同向。

| 打分器 | ρ（41 个非恒定状态） | 95% CI | top-1 | 遗憾 | Boss ρ | 精英 ρ | ρ（生存真值） |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 战斗血量头 | 0.492 | [0.378, 0.593] | 0.561 | 0.0146 | 0.414 | 0.546 | 0.430 |
| 价值头 | 0.376 | [0.257, 0.489] | 0.439 | 0.0294 | 0.386 | 0.369 | 0.283 |
| 胜率头 | 0.111 | [−0.006, 0.226] | 0.293 | 0.0807 | 0.287 | −0.014 | 0.250 |
| 策略 logits（贪心） | 0.594 | [0.475, 0.704] | 0.561 | 0.0142 | 0.517 | 0.648 | 0.471 |
| 随机 | 0（零分布 ±0.114） | — | 0.328 | 0.0559 | — | — | — |

7 个 Boss 状态的所有候选结局相同（全部死亡或完全一致），不计入 ρ 与上表的 top-1。噪声上限为 split-half 0.763，K=16 时约 0.866。

## 尚不确定之处

1. 整段训练的战斗/非战斗奖励拆分：归档 metrics 没有这些字段，没有日志支持。四组表只代表 u2225 时刻。要确定早期（例如 s7b、s8a 前段）的组成，需要按下文的方法从现在起记录，或者用旧 checkpoint 重跑 `q5_rollout.py`（每个约 6 分钟 CPU）。
2. 训练中精确正目标的确切数量：没有记录。0.79M 是用样本中每胜 29.7 个外推的估计，严格界为 [26,575, 1,700,800]。
3. 胜率头在 A10 新开局上的判别力：没有任何正样本，无法检验。要检验，需要 A10 新开局的胜局，或者 A10 第 2/3 幕的存档起点。
4. 排序测试的真值是本场战斗结局，并且用贪心策略续玩。价值头和胜率头的训练目标视野更长，它们在长视野上的排序能力没有测，那需要每个候选打完整局，成本约 100 倍。策略 logits 占优，可能部分来自续玩用的就是同一策略（自洽偏差）。48 个状态来自 dev-128，Boss 与精英的状态集来自部分重叠的对局，不是独立样本。
5. step_cap 截断次数：metrics 只记录 floor_stall，没有单独记录 step_cap 截断。aux_win_frac 最小为 0.997，间接说明被屏蔽的截断步极少，但这不是计数。
6. 本地 CPU fp32 与训练时 CUDA bf16 autocast 的数值差异没有量化。它只影响采样细节，不影响奖励定义。样本的起始序列（seed_text Q5A/Q5B）与训练（PPO0R2050）不同，起始池本身完全相同（148/148 sha256 一致）。
7. s9a（u2225 续训，lr 1.5e-4，ent 0.015）：运行时已丢失，没有可用数据。

## 从现在起的记录方法（建议，未实施；仓库只读）

1. 在 ppo.py 的 rollout 循环里，按"起始类型 × s_t 是否战斗"在每次更新累计以下量，写入 metrics.jsonl：Σr_obj、Σ胜利奖励、Σ获取塑形（非终止步的 F）、Σ欠账（终止步的 F）、步数。另加按起始幕统计的胜局数，以及按原因统计的截断数（floor_stall / step_cap / error）。
2. 在 heads.AuxTargets.targets 的统计里加 aux_win_exact_pos（精确正目标数），并按起始类型给出精确目标数。让 ppo_loss 额外返回只在精确目标上的 BCE，与同批常数基线并列记录。
3. 每个参评 checkpoint 跑一次 `q5_rank.py`：固定这 48 个 dev 状态，K=16，CPU 约 3.5 分钟。记录各头的 ρ、top-1 和遗憾，作为 lookahead/ExIt 叶评估器的回归指标。

## 脚本与输出

脚本均位于 `<Q>/scripts/`，用训练 venv 的 python 以 `-I` 运行（等价于 `uv run --project <train> python`），并设 `STS2VEC_DLL=…/sim/Sts2Sim.Vec/bin/Release/net9.0`。运行条件为 `nice -n 10`、3 线程、≤64 env 槽位、每次运行 ≤3 分钟。

- `q5_metrics.py <X> <Q>/out/metrics_composition.json`：从归档 metrics 计算组成、胜利和胜率目标统计。
- `q5_rollout.py --checkpoint …/u2225/checkpoint/ckpt-0002225.pt --saves …/tmp/train/saves-silent-a9 --out <Q>/out/rollA --envs 64 --threads 3 --seconds 165 --seed-text Q5A --torch-seed 1`：第二次运行把参数换成 rollB、Q5B、seed 2。
- `q5_analyze_rollout.py <Q>/out/rollAB_analysis.json <Q>/out/rollA <Q>/out/rollB`；`q5_winpos.py`；`q5_value_fit.py`；`q5_winprob_eval.py`；`q5_replay_check.py`。
- `q5_rank.py --kinds boss|elite --n-per-kind 24 --K 16`：输出 `<Q>/out/rank_boss_k16.json` 与 `<Q>/out/rank_elite_k16.json`。随后用 `q5_rank_analyze.py` 生成 `<Q>/out/rank_k16_analysis.json`，再用 `q5_rank_nonconst.py` 生成 `<Q>/out/rank_k16_nonconst.json`。

## 复核（独立核查）

复核方式：逐条回到一手来源（代码行、归档 metrics.jsonl / config.json、u2225 checkpoint、rollout 原始 npz / episodes、排序测试原始 JSON），用自写脚本独立重算，并重跑了两个本地实验的一小段以检验可复现性。脚本在 `<Q>/verify/scripts/`（v_metrics.py、v_roll.py、v_value.py、v_rank.py、v_winhead_withinact.py、v_misc.py、v_ckpt.py、v_compare_reruns.py），输出在 `<Q>/verify/out/`。运行条件与原文相同：CPU、`nice -n 10`、3 线程、64 env、单次 ≤ 30 秒。正文未改动（改前副本：`<Q>/verify/out/memo.before-review.md`）。

### 核实无误的事实

- 事实 1：s8a config.json 与 u2225 checkpoint 自带 config 逐项一致（objective protocol、win_bonus 1.0、γ 1.0、λ 0.99、rollout 64、epochs 1、minibatch 4096、envs 2048、lr 3e-4、ent 0.02、vf 0.5、clip 0.2、max_grad_norm 0.5、acq 0.003/0.01/0.012、floor_step_cap 80、step_cap 3000、max_rows 64、rollout_loop_breaker False、aux 0.1 + bootstrap）。起始池 4096 + 2220 = 6316，0.351488。谱系 2,225 行 metrics 的 lr 只有 0.0003。代码中没有学习率调度：`ppo.py:291-293` 在恢复时直接把 lr 设为 cfg.lr，ckpt 里也没有 scheduler 键（`verify/out/v_ckpt.json`、`v_metrics.json` lr_unique）。
- 事实 2：`objective.py:108-112` 与 `protocol.py:59-64、80` 的代码与原文描述一致。样本中 440,737 个战斗步的 r_prog 与 r_win 全为 0；2,360 次死亡（新开局 1,574 + 存档 786）全部发生在战斗步。ProceedRewards 步的取值：0.019608 共 480 次（新开局 352 + 存档 128），0.020834 共 86 次（2 + 84），0.022222 共 68 次（全部是存档开局，全部是第三幕 48 层胜利）（`verify/out/v_roll.json` agg、proceed_rewards_values）。
- 事实 3：代码行 `objective.py:162-166、175`，`ppo.py:1097-1104、1230-1231、1316-1328` 核对无误。全部 1,574 个结束的新开局回合，塑形和的最小值与最大值都是 −0.019（`v_roll.json` fresh_shaping_all_finished）。
- 事实 4：`ppo.py:1091、1121-1124、1135-1143、1149-1157` 核对无误。2,225 次更新的 env_steps_update 全部等于 131,072，即所有步都有效，每次更新恰好 32 个 minibatch（`v_metrics.json` env_steps_update_unique）。
- 事实 5：独立重算结果：2,225 行连续；Σenv_steps_update = 291,635,200；floor_stall 461；sim_error、content_gap、illegal 均为 0；461/1,492,418 = 0.031%。u2225 ckpt 的 counters 也逐项相同：episodes 1,492,418、wins 26,575、floor_stall 461（`v_metrics.json`、`v_ckpt.json`）。
- 事实 6：`heads.py:46-53、100-133` 与 `ppo.py:554-556、1126-1130` 核对无误。
- 事实 7–9：用独立实现重算 npz，步数、比例、四组每局均值、43.8% / 21.5% / 25.1% / 100%，以及胜利奖励占 9.79%，与原文完全一致（`v_roll.json`）。
- 事实 10：EV 0.5287 / 0.5491（含塑形）对 0.3181 / −0.1544（不含塑形）；存档开局平均 V −0.0869，含塑形 RTG −0.0614，不含塑形 RTG +0.2231（`verify/out/v_value.txt`）。
- 事实 11：u2176–2225 的 reward_mean 0.001174、shaped 0.000819、shaping −0.000355、acq_potential 0.1297、fresh 0.2913、save 0.2449、shaped_return 0.1917、shaping_return −0.0833；存档胜率 691/9,861 = 7.0%。谱系值也一致（回报类按回合加权）（`v_metrics.json`）。
- 事实 12–14：fresh_wins 在每次更新中都是 0；save 26,575/525,017 = 5.06%；胜率目标 291,619,834 个，精确目标 46,566,812 个（15.97%），Σy = 5,054,093（1.733%）；aux_win_loss 0.03038 对常数基线 0.08563，2,224/2,225 次低于基线；u2225 为 0.03586 对 0.09948（`v_metrics.json`）。
- 事实 15：BCE 0.1183 对 0.3081，逐步 AUC 0.9758，首步 AUC 0.9711；新开局 287,911 步、0 个正样本，中位数 1.58e-4，p99 1.19e-3，最大 0.157（`v_roll.json` win_head）。
- 事实 16：从归档终评原始 summary 重算：228 局，weights_hash 全为 d59f9ea2095799c0，0 胜；首步均值 1.584e-4，每局最大值的中位数 1.1155e-3，p95 0.0313，最大 0.1076（`verify/out/v_misc.json` fresh_eval_win_prob）。
- 事实 17：Colab 与 probe 的 dev-128 逐局比对，history_actions、final_state_hash、result、floor 128/128 相同（`v_misc.json` replay_dev128）。
- 事实 18：用自写代码从原始 rank JSON 重算，ρ、CI、top-1、遗憾、随机基线与原文一致。原始数据：8,064 次 rollout，结局只有 won 和 defeat（没有 horizon 或 error）；7 个真值恒定的状态全部是 Boss；died_in_eval 为 Boss 11/24、精英 3/24（`verify/out/v_rank.json`）。
- 第 6 条不确定项中"起始池 148/148 sha256 一致"属实（`v_misc.json` start_pool_sha）。第 7 条"s9a 运行时丢失"属实（`~/.claude/projects/-Users-Aincrad-dev-proj-hack-sts2/memory/training-cycle-2026-11-03.md:159`）。

### 需更正的事实（原文 → 更正，证据）

1. 事实 11 的"谱系平均：……shaping_mean −0.000402……shaping_return −0.0788"（原文没有注明系数）→ 谱系 u1–350（s6b、s7b）的 acq_upgrade_coef 是 0.004，只有 s8a（u351–2225）是 0.012。所以谱系平均混合了两种塑形系数。分段值：shaping_return_mean 为 s6b −0.058、s7b −0.058、s8a −0.084；acq_potential_mean 为 0.072、0.077、0.124。事实 1（s8a 配置）本身无误（`<X>/gpu2/runs/s6b-silent-a10-mid-signal/config.json` 与 `<X>/gpu2/runs/s7b-silent-a10-mid-newenv/config.json` 中 config.acq_upgrade_coef=0.004；`verify/out/v_metrics.json` seg_s6b/seg_s7b/seg_s8a）。
2. 结论 2"在这些步上……唯一的即时奖励是死亡那一步的塑形欠账"，以及事实 9"战斗步只有欠账" → 战斗步上还有少量获取塑形。新开局有 353 个战斗步非零，净 +2.79（主要是升级）；存档有 33 个，净 −0.29。欠账分别为 −154.77 和 −178.87，获取塑形约占战斗步 |F| 的 2.5% 和 0.2%。结论方向不变（`v_roll.json` agg.*:nz_nondebt_f、nondebt_f_sum、debt_sum）。
3. 事实 3"最大误差为 0" → 分项之和与 e["return"] 的最大误差为 4.2e-8，来自逐步奖励的 float32 存储；与 e["shaping"] 的最大误差为 1.1e-16（`<Q>/out/rollAB_analysis.json` per_episode.*.max_chk_return = 1.73e-8 / 4.21e-8）。
4. 结论 4"在 A10 新开局上……无法验证"，以及胜率头核查"对目标分布（A10 新开局）没有经结局验证的信息" → 对整局胜负确实无法验证（0 个正样本），但对中间结局有经过验证的弱排序信息。在 228 局新开局贪心评测中：第一幕 Boss 进场时，win_prob 对"是否击败第一幕 Boss"的 AUC 为 0.730（172 次进场，101 次击败）；每局最大值与进度的 Spearman 为 0.71。在同一位置，价值头 AUC 0.837、战斗血量头 0.857，都强于胜率头。"不宜用作叶评估或停止判据"的结论仍成立（`…/next/analysis/q4_results.json` u2225.win_prob.boss_entry_auc_for_boss_win、spearman_max_vs_progress，u2225.value_at_a1_boss_entry_auc_for_win、cp0_at_a1_boss_entry_auc_for_win；本人从 probe steps.jsonl 与终评 summary 重算得 AUC 0.7304：`v_misc.json` a1_boss_entry_win_prob_auc）。
5. 结论 5 与事实 18"噪声上限约 0.87（0.866）" → 0.866 是两份独立 K=16 真值之间的重测信度：split-half 0.763 经 Spearman-Brown 校正，而且只在 37 个可计算的状态上算出。完美打分器与一份 K=16 真值之间的期望相关上限约为 √0.866 ≈ 0.93。各头离上限比原文说的更远，结论不变（`v_rank.json` split_half_mean、split_half_n、ceiling_perfect_scorer_vs_K16_gt_approx）。
6. 结论 5"三个头都不如策略先验" → 这只在平均 ρ 上成立。策略减战斗血量头的逐状态配对差为 +0.102，自助 95% CI [0.020, 0.188]（n=41），但策略只在 49% 的状态上更高。两者 top-1 相同（0.561），遗憾 0.0142 对 0.0146，基本相同（`v_rank.json` paired_policy_minus_combat_hp、summary_nonconst）。
7. 事实 13 把"约 0.79M 个精确正目标（0.27%）""s8a 末段约 410 个/次更新"放在"已确认事实"里 → 这些数字是用 u2225 样本的 68 次胜利（每胜 29.66 个）外推到整个谱系得到的估计，不是确认事实。能确认的只有严格界 [26,575, 1,700,800] 和样本内的比率（`<Q>/out/winpos.json`；metrics 没有精确正目标的字段）。
8. 事实 5、12 和结论 3 中的"1,492,418 局""新开局 967,401 局" → 这些是训练回合（episode），不是独立对局。新开局来自固定的 4,096 个种子文本 TRAIN0-SILENT-0…4095，平均每个种子约 236 个回合；存档开局来自 148 个存档 × 15 份确定化副本。"新开局 0 胜"应读作"在 4,096 个固定种子上的 967,401 个回合中 0 胜"（`<T>/startpool.py:338-340`；`<X>/gpu1/runs/s8a-silent-a10-mid-upg3/config.json` config.pool=4096、start_pool）。

### 无法核实的事实

- 训练全程（u1–2225）的战斗/非战斗奖励拆分：没有日志支持，与原文一致。
- 训练中精确正目标的确切数量：没有日志，只能给出严格界。
- 训练中胜局按存档起始幕的分布（原文"主要是第 2/3 幕起始"）：metrics 只有 save_episodes_by_start_act 和 save_gain_by_start_act，没有按起始幕统计的胜局数。只有样本支持这一说法（第一幕 0/339、第二幕 4/140、第三幕 48/128）。从现在起可在 split_stats 中加 save_wins_by_start_act 来记录。
- 胜率头在 A10 新开局整局胜负上的判别力：没有正样本，无法检验。
- step_cap 截断的确切次数：没有日志，只能从日志推出上界（见补充发现 1）。
- 训练时 CUDA bf16 与本地 CPU fp32 的数值差异：未量化。本人的重跑只证明本地 CPU 结果可复现。
- s9a 的奖励组成：运行时丢失，没有数据。

### 补充发现

1. 截断的严格上界。谱系中被屏蔽的胜率目标步数为 291,635,200 − 291,619,834 = 15,366。每个截断回合至少屏蔽自身那一步，所以各类截断合计 ≤ 15,366。扣掉 461 次 floor-stall，step_cap 等其他截断 ≤ 14,905，不超过回合数的 1.0%。如果截断全是 floor-stall，平均每次屏蔽 33.3 步，与截断在窗口内均匀分布时的期望（约 32.5 步）相符。这只说明数据与"step_cap 截断约为 0"相容，并不是计数（`verify/out/v_trunc_bound.json`；屏蔽规则见 `heads.py:115-120`）。
2. 可复现性。用原脚本重跑 q5_rollout.py（Q5A、torch seed 1、30 秒），前 859 步 × 64 槽位的全部 24 个数组和前 216 个回合记录与 rollA 逐位一致。重跑 q5_rank.py 的 4 个根状态（2 个 Boss、2 个精英，skip 3，分块方式与原运行不同），688/688 次 rollout 的结局、结束血量、步数和后继哈希完全相同，头输出差 ≤ 6e-8（`verify/out/v_compare_reruns.txt`）。
3. u2225 ckpt 的 counters 中 overflow_cand 为 65，谱系 metrics 的 cand_over_max_rows 合计也是 65，即有 65 个决策的候选超过 64 行被截断。原文未提，对奖励组成的影响可以忽略（`v_ckpt.json`、`v_metrics.json`）。
4. 胜率头在存档开局上的区分力不只是起始幕的差异。按起始幕分层后，第三幕起始的 128 局（48 胜）首步 AUC 为 0.867，回合中点 AUC 为 0.912；第二幕起始的 140 局只有 4 胜（AUC 0.884，样本太小）。逐步 AUC 0.976 混合了起始幕差异，而且 113,700 步高度相关，有效样本量是 607 局 / 52 胜（`verify/out/v_winhead_withinact.txt`）。
5. 新开局起点 φ = 0.019 对应首个观测的 deck 13、relic 1（probe steps.jsonl 中 t=0 的 "deck":13、"rel":1）：0.003×(13−10) + 0.01×1 = 0.019（`…/next/analysis/probe/u2225-dev/steps.jsonl`）。
6. 结论判定：数值层面没有发现与代码或归档数据矛盾的地方，18 条事实都能从一手来源复现。需要更正的是限定与归类：谱系系数混合、"只有欠账"、新开局胜率头的中间结局信息、噪声上限的定义、估计值被放进已确认事实、回合与独立对局的区分。

# Wave 16：forced-choice what-if 评估（A u7664 / C u9164，dev-128）

日期 2026-11-06。工具：`sts2train eval --force SELECTORS [--force-mode greedy|uniform]`（新模块
`sim/tools/train/sts2train/force.py`，仅评估用，训练不构造）。

## 结论先行

- 两个 checkpoint、四种"总是强制"的选择全部显著变差（paired 95% CI 全部在 0 以下）：总是 SMITH −0.10 / −0.14，
  总是删牌 −0.05 / −0.03，总是进精英 −0.09 / −0.08，总是跳过卡牌奖励 −0.23 / −0.25，SMITH+精英 −0.18 / −0.20
  （A / C）。
- 因此"从不升级、少进精英、从不删牌"在"全有 vs 全无"的比较下是**相对理性**的：以当前战斗强度，全面改成另一极端
  明显更差，**不是**"强制即变好 / 中性"的塌缩信号。
- 但这只否定了"无条件总是做 X 更好"。策略的问题更可能是**缺少条件化**（例如高 HP 时 SMITH、HP 充足时进精英、
  金币有余时删牌），这一点本轮的 always-force 设计**测不出来**（见"未验证"）。
- 精英并非被完全回避：基线中 Elite 目的地可选时 A 有 198/473（42%）、C 有 220/512（43%）主动选择精英，每局
  1.55 / 1.72 场精英战。真正"概率 ≈0"的只有 SMITH（A 6/468、C 0/505）和删牌（0/194、0/163）。

## 方法

- 选择器语法 = 训练 explorer 的 `parse_kinds` / `KindExplorer.item_rows`（直接复用其行掩码，不改 explore.py）：
  `ChooseRestOption:SMITH`、`BuyEntry:remove`（= `card_removal`）、`Travel:Elite`、`ProceedRewards:skipcard`。
- 语义：在某个存活对局的决策点（`cand_n > 0`），只要任一选择器有 ≥1 个存在的行（合法、在 logits 宽度内、未被模型
  mask），动作就限制在这些行的并集里；`greedy`（默认）取其中 policy logit 最高的行，`uniform` 用每局独立的 RNG
  （sha256("sts2train-force", seed, game key)）均匀抽取。实现上把该 slot 其余行置为 `NEG`，于是 evaluator 的
  argmax / 采样 / loop breaker 只看到一行；其他决策点用同一个 logits tensor，完全不变。
- 计数（report.json 与每局 summary 的 `force`）：每个选择器 `available`（出现次数）、`forced_decisions`、
  `changed`（强制行 ≠ policy 自己的 argmax）、`policy_took`（policy 自己的 argmax 就在该选择器行上），以及
  `forced_total` / `changed_total` / `games_forced`；平铺别名 `forced_decisions.<selector>`、`available.<selector>`。
  Python API 另有 `observe` 模式（只计数、不限制），本轮基线即用它跑出 policy 自身的可选次数与选择率。
- 读数：raven CPU（自己的 testbox 容器，5 线程），`sts2train.evaluate.evaluate`，`silent-a10-dev-s128`，greedy，
  loop breaker off；宏取 checkpoint 训练配置（A = rest,shop；C = `--macros all` → rest,shop,prompt）。
  每臂 128 局全部 replayable。
- 基线一致性：A 的 observe 基线 progress = 0.4442209296875，与 wave-15 审计（diag14 greedy-off）逐位一致。
- paired：同一 seed 的 progress 差（臂 − 基线），均值、t 95% CI（df 127）、percentile bootstrap 95%（10,000 次
  重采样，seed 0），以及 better / worse / same 局数、动作序列完全相同的局数。
- 注意：本轮所有臂都是 greedy force-mode，即"每次可选都强制"。`ProceedRewards:skipcard` 在每个有卡牌行的奖励屏
  都直接离开，等于"永不拿牌"且会放弃屏上未领的金币 / 药水。全部对局均以 loss 结束（无胜局），所以 mean death floor
  = mean floor。

## 结果：A pinned-0007664（宏 rest,shop）

| 臂 | progress [95% t] | Δ vs 基线 [t] / [bootstrap] | +/−/= | 到达 Act2 / Act3 | 平均层数（=死亡层） | Act1 boss 入场：局数 / HP | 最终牌组 / 升级 / 遗物 | 精英战/局 | 强制（改变） |
|---|---|---|---|---|---|---|---|---|---|
| base（observe） | 0.444 [0.416, 0.473] | — | — | 0.664 / 0.016 | 23.24 | 107 / 59.1 | 25.5 / 2.26 / 6.66 | 1.55 | 0 |
| SMITH | 0.343 [0.318, 0.369] | −0.101 [−0.127, −0.075] / [−0.128, −0.075] | 12/76/40 | 0.273 / 0.008 | 18.33 | 89 / 27.6 | 23.0 / 4.41 / 5.40 | 1.22 | 370（370） |
| 删牌 | 0.391 [0.362, 0.420] | −0.053 [−0.078, −0.028] / [−0.079, −0.029] | 19/47/62 | 0.500 / 0.016 | 20.65 | 97 / 56.3 | 22.7 / 1.64 / 5.88 | 1.44 | 109（109） |
| Elite | 0.351 [0.320, 0.383] | −0.093 [−0.120, −0.065] / [−0.120, −0.066] | 22/72/34 | 0.414 / 0.016 | 18.66 | 71 / 58.2 | 23.8 / 2.02 / 6.66 | 2.67 | 342（204） |
| skipcard | 0.213 [0.198, 0.229] | −0.231 [−0.259, −0.204] / [−0.259, −0.204] | 0/116/12 | 0.023 / 0 | 11.86 | 33 / 59.2 | 15.0 / 0.70 / 3.47 | 0.75 | 614（614） |
| SMITH+Elite | 0.266 [0.246, 0.286] | −0.178 [−0.204, −0.153] / [−0.204, −0.154] | 4/105/19 | 0.117 / 0 | 14.49 | 52 / 21.8 | 21.2 / 3.05 / 5.38 | 2.09 | 494（387） |

基线 observe 计数（可选次数 / policy 自己选）：SMITH 468 / 6，删牌 194 / 0，Elite 473 / 198，skipcard 2182 / 170。
死因房间（基线）：Elite 38、Boss 47、Monster 43；Elite 臂：Elite 78、Boss 31；SMITH 臂：Boss 64。

## 结果：C pinned-0009164（`--macros all` → rest,shop,prompt）

| 臂 | progress [95% t] | Δ vs 基线 [t] / [bootstrap] | +/−/= | 到达 Act2 / Act3 | 平均层数（=死亡层） | Act1 boss 入场：局数 / HP | 最终牌组 / 升级 / 遗物 | 精英战/局 | 强制（改变） |
|---|---|---|---|---|---|---|---|---|---|
| base（observe） | 0.480 [0.453, 0.507] | — | — | 0.734 / 0.039 | 24.98 | 120 / 59.5 | 27.3 / 2.49 / 7.22 | 1.72 | 0 |
| SMITH | 0.342 [0.319, 0.366] | −0.138 [−0.165, −0.110] / [−0.164, −0.111] | 9/87/32 | 0.250 / 0 | 18.30 | 93 / 27.3 | 23.4 / 4.49 / 5.52 | 1.22 | 374（374） |
| 删牌 | 0.448 [0.420, 0.477] | −0.031 [−0.051, −0.011] / [−0.051, −0.012] | 18/43/67 | 0.633 / 0.031 | 23.45 | 112 / 58.7 | 24.7 / 2.33 / 6.74 | 1.65 | 107（107） |
| Elite | 0.397 [0.362, 0.432] | −0.083 [−0.113, −0.053] / [−0.112, −0.053] | 17/71/40 | 0.484 / 0.055 | 20.91 | 87 / 53.1 | 25.4 / 2.07 / 7.56 | 2.98 | 381（229） |
| skipcard | 0.230 [0.213, 0.247] | −0.250 [−0.279, −0.220] / [−0.279, −0.221] | 1/117/10 | 0.047 / 0 | 12.71 | 41 / 49.8 | 15.2 / 0.70 / 3.69 | 0.84 | 670（667） |
| SMITH+Elite | 0.280 [0.258, 0.301] | −0.200 [−0.226, −0.174] / [−0.226, −0.174] | 3/108/17 | 0.117 / 0 | 15.20 | 61 / 17.5 | 22.0 / 3.46 / 5.64 | 2.19 | 538（425） |

基线 observe 计数：SMITH 505 / 0，删牌 163 / 0，Elite 512 / 220，skipcard 2311 / 106。
死因房间（基线）：Monster 44、Boss 52、Elite 32；Elite 臂：Elite 71、Boss 39。

boss_entry 只记录 HP（act / floor / hp / max_hp / boss）；牌组、升级、遗物是**终局**值，不是 boss 入场时的值。

## 解读（按 checkpoint 分开）

**A u7664**
- SMITH：每个篝火都升级 = 从不回血，Act1 boss 入场 HP 59 → 28，到达 Act2 0.66 → 0.27，死在 Boss 的局 47 → 64。
  升级数翻倍（2.3 → 4.4）换不回 HP。说明当前战斗强度下 HP 是瓶颈，"总是 HEAL" 比"总是 SMITH"理性；A 偶尔
  SMITH（6/468）。
- Elite：强制进精英时多打 1.1 场精英，死在精英的局 38 → 78；遗物数不增（6.66 → 6.66，因为死得早）。当前战斗力
  吃不下额外精英，所以"42% 选精英"的现状好于"100% 选精英"。
- 删牌：−0.053，最小但显著。删牌花掉商店金币（终局遗物 6.66 → 5.88、升级 2.26 → 1.64），而被删的卡由 SMITH 同款
  "max-logit 宏行"决定，选卡本身未训练过。27 局动作序列完全相同（未遇到可负担的删牌），62 局 progress 相同，差异集中在其余局。
- skipcard：永不拿牌 −0.23，牌组 25.5 → 15，几乎全死在 Act1。policy 拿牌是对的（基线只在 170/2182 处跳过）。

**C u9164**（基线更强，0.480）
- 方向与 A 完全一致。SMITH 伤害更大（−0.138）：C 的基线更依赖 HP 走得更远，强制 SMITH 后两者都落到 ≈0.34，即
  "总是 SMITH"抹掉了 C 相对 A 的全部优势。
- 删牌伤害更小（−0.031，CI 上界 −0.011）：更接近中性，是 C 的四个选择里最接近"可能有条件收益"的一个。
- Elite −0.083 与 A 相近；C 强制精英后仍有 7 局到 Act3（基线 5），遗物 7.22 → 7.56，说明精英奖励在能活下来的局里
  有收益，但被死亡局压倒。

**整体判断**：两个 checkpoint 都不是"塌缩到明显更差的确定性选择"——强制另一极端一致地伤害 progress。现有回避
在 always/never 两极之间是理性的一侧；PPO 在这些行上"没有看到优势"并不等于优势存在。若要提升，方向是**条件化
决策**（何时 SMITH / 何时进精英 / 何时删牌），而不是把行概率整体推高（对 wave-14 explore-push 的含义：强推
SMITH / Elite 的整体概率很可能有害，除非 push 只作用在优势为正的状态上）。

## 未验证 / 局限

- 只测了"每次可选都强制"。没测条件化强制（如仅 HP ≥ 70% 时 SMITH、仅第一次篝火、仅 Act1 后半段的精英），这才是
  "策略缺少条件化"假设的直接检验；需要给 force.py 加状态谓词，或用 `--force-mode uniform` + 子集抽样。
- SMITH / 删牌强制时，具体升级 / 删除哪张卡由 policy 在宏行上的 logit 决定，而这些行训练中概率 ≈0，选卡质量未知；
  负效应可能部分来自"选错卡"而非"升级 / 删牌本身"。
- skipcard 会放弃屏上未领取的全部奖励（不只卡牌），所以它的 −0.23 上界性地夸大了"跳过卡牌"本身的代价。
- 只有 greedy、loop breaker off、dev-128 单一 seed 集；未跑 sample 模式、ab-512 或其他 checkpoint（B / D）。
- 牌组 / 升级 / 遗物在 boss 入场时的值未记录（summary 只有终局值），表中用终局值。
- `sts2train compare` 未运行（paired 统计用自写脚本，同为 10,000 次 seed-0 percentile bootstrap + t）。

## 复现

```bash
# raven testbox 容器内（overlay 未提交文件），A/C checkpoint 位于 tmp/w16/
cd sim/tools/train && uv run python <scratchpad>/w16/scripts/whatif.py /work/tmp/w16 5
python3 -I <scratchpad>/w16/scripts/analyze.py <runs dir> <out.json>
# 单臂 CLI 等价：
sts2train eval --seedset silent-a10-dev-s128 --checkpoint A-0007664.pt --loop-breaker off \
  --force ChooseRestOption:SMITH,Travel:Elite --force-mode greedy --out OUT
```

# Wave 13 诊断：explored 行的 log-prob 为何每个 update 动几百 nat（2026-11-06）

## 结论（先看这里）

- **H1（表示不一致：bf16 / 归一化路径）基本排除。** 在 fork 点 A u7664 上，bf16 autocast 与 fp32 的 log-prob 差：SMITH 行 |Δ| ≤ 1.08 nat（p50 −0.24），removal 行 ≤ 3.5 nat（p5..p95 = −1.8..+1.3；removal 决策的全部合法行含 LeaveShop / 买卡行 ≤ 4.3）；rollout 的归一化路径（`Categorical(logits).logits`）与 update 的 `F.log_softmax` 最大差 1.5e-5；bf16 下 batch=1 与 batch=N 的 logit 差 = 0（SMITH / removal）。这些量级远小于 E3 的 push_dlogp（−100..−1500）。注意：用的是 CPU bf16 autocast，CUDA + compiled 的 kernel 没测。
- **H3（logit 极端）部分成立，但比想象的小。** u7664 上 SMITH 行相对最大行的 gap 中位数 −92，最小 −148，**没有 < −1000 的行**；removal 行中位数 −73，最小 −890。scorer 本身尺度不大（rest 决策 max |logit| 139，combat 69，removal 874，大头在 `c_bias` 项）。关键在 **log π(SMITH) 对参数的梯度范数 ‖∇θ log π‖ ≈ 238（p50）**，removal 754（p50，最大 1580），而 combat 采样行只有 1.1（p50，p95 16.5）：两三百倍的灵敏度。
- **H2（一步 Adam 把这些行推几十 nat）成立，根因是 Adam 的逐参数归一化 + 全局 grad clip，而不是 checkpoint 的动量或 dormant 状态本身。** 只用 push 项（64 个 SMITH explored 行，A=+1，δ=1）一步：Adam（checkpoint 状态）+12.2 nat，Adam（新状态）+8.9 nat，SGD 3e-4（clip 0.5）+0.034，SGD 3e-4（不 clip）+15.3。push 梯度范数 236，会把整个 minibatch 的梯度裁到 0.5，Adam 再按 1/√v 放大回 ~lr 量级的逐参数步长，于是 push 梯度缩放 0.1、0.01 完全无效（结果逐位相同），缩到 1e-3 仍有 +10 nat。仅动量（零梯度）一步只有 +0.07 nat、32 步 +0.7 nat，**排除了“正常 PPO 漂移带动”**。
- **推动跨决策泛化**：held-out 393 个 rest 决策的 SMITH 行与被推的 64 行同幅移动（+12.3 vs +12.2），所以每个含未 clamp explored 样本的 minibatch 都把**所有** SMITH 行整体平移 ±10 nat 级，后面 minibatch 的 explored 样本（logp_old 是 rollout 时的值）立刻落到 ±1 nat 信赖域外 → clamp → 零梯度，剩下 ~5–10% 未 clamp 的再推一次：随机游走。
- **复现**：32 个 minibatch（epochs 1，每个 16 个 explored SMITH 样本，logp_old = update 开始时的值）用现在的路径（共享 loss，clip 0.5，Adam ckpt 状态）：push_clamped 均值 0.961，|push_dlogp| 均值 33，一个 update 末 SMITH 行漂移中位数 +47（A 正负各半）/ −48（A 全负）。与 E3 的 push_clamped 0.89–0.93、dlogp 逐 update 增长的形态一致（E3 的几百 nat 是多个 update 累积 + gap 发散后灵敏度进一步上升；E3 之后的 checkpoint 没测）。
- **推荐：push 项从共享 loss / Adam / 全局 clip 中拆出来，用独立的普通 SGD 步施加（方案 i），步长按行梯度尺度归一化：lr_push = η / ‖∇θ mean_explored log π‖²，η = 0.5 nat。** 同样的 32-minibatch 复现：push_clamped 0.0（A 混合）/ 0.91（A 全负，此时信赖域正常起作用），一个 update 末 SMITH 漂移中位数 −0.45 / −1.23 nat。固定 SGD lr 1e-5 在 u7664 上效果相同（−0.50 / −1.22），但灵敏度随训练变化，自适应版更稳。

## 方法

- 环境：raven 上自起容器 `diag13`（镜像 `sts2-testbox:latest`，源码 = testbox 现有 archive，ppo.py / explore.py / model.py 与 HEAD ca4ce37 md5 一致），CPU 6 线程。checkpoint：`gs://…/runs/w3m-a/checkpoints/pinned-0007664.pt`（只读下载，md5 b91f6709…，分块上传到 raven）。arch d192 s384 depth3 heads6，aux_heads + deck_counts，Adam step 245248，`rollout_loop_breaker` False，macros rest,shop。
- 决策采集（`collect.py`）：silent-a10-dev-s128，fp32 采样策略（float64 逆 CDF，无 loop breaker），progress 0.431（与 A 的 dev-128 量级一致），87 s。得到 rest 决策 457（含 SMITH 行 8569 行，每决策 9–34 个 SMITH:<card>），removal 决策 187（removal 行 3695，全部是 macro 行），combat 抽样 385。
- 分析（`analyze.py`、`multistep.py`）：所有灵敏度实验都在 fp32、单步、从同一 checkpoint 状态重新开始；Adam 用 `lr 3e-4, eps 1e-5`，checkpoint 状态每次 deepcopy 后载入（注意：CPU 上 `Adam.load_state_dict` 与源 dict 共享张量，不 deepcopy 会被前一次实验污染——本诊断第一轮就踩了这个坑，已修正重跑）。push 项 = `ppo.explore_push_term`（δ=1，logp_old = 当前 logp，所以第一步全部在信赖域内），coef 1，除特别说明外过 `clip_grad_norm_(0.5)`。

## 1. logit gap 与 bf16（H1 / H3）

| 类别（u7664） | 行数 | gap p5 | p50 | p95 | min | < −30 | < −100 | < −1000 | bf16−fp32 log-prob p5 / p50 / p95（max\|Δ\|） |
|---|---|---|---|---|---|---|---|---|---|
| SMITH:<card>（rest） | 8569 | −126.7 | −92.1 | −67.5 | −148.3 | 8475 | 3138 | 0 | −0.64 / −0.24 / +0.14（1.08） |
| HEAL | 451 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 / 0 / 0 |
| LeaveRestSite | 6 | −5.0 | −3.1 | −1.7 | −5.1 | 0 | 0 | 0 | ≤0.04 |
| Remove:<card>（shop） | 3695 | −653.6 | −73.4 | −8.5 | −889.8 | 3155 | 1590 | 0 | −1.77 / −0.02 / +1.30（3.5） |
| LeaveShop | 187 | −639.7 | −63.2 | −4.9 | −877.2 | 133 | 76 | 0 | −1.71 / 0 / +1.29（3.4） |
| combat 全部合法行 | 2356 | −41.7 | −9.9 | 0 | −85.8 | 284 | 0 | 0 | −0.08 / 0 / +0.11（0.69） |

- P(SMITH 合计) 每决策：p50 1.4e-38，p95 3.5e-27（457 个里有 1 个 0.996）；P(HEAL) p5 = 1。P(removal 合计) p50 5e-31，p95 2.3e-6，max 0.56。
- removal 决策里 LeaveShop 自己也常在 −60..−880（这些 shop 决策的最大行是买东西），所以“removal 行极端”部分是整个 shop 决策的 logit 分布极宽。
- 尺度：rest 决策 max |logit| 139（|bilinear| p95 9.2 / max 48，|c_bias| p95 28 / max 103）；removal max 874（c_bias 最大 602，h2 范数最大 603）；combat max 69。
- 归一化路径差（Categorical vs log_softmax）：rest 1.9e-6，removal 1.5e-5，combat 7.6e-6。bf16 batch-1 vs batch-N logit：SMITH / removal 0，combat max 0.06。
- 梯度灵敏度 ‖∇θ log π(a)‖₂（16 个样本）：SMITH p50 238（69..282），removal p50 754（130..1580），combat 采样行 p50 1.1（p95 16.5）。随机扰动（每参数 1e-3 × 参数均值幅度的高斯）：SMITH Δ ≤ 0.05，removal ≤ 0.38，combat ≤ 0.04——各向同性的小扰动下并不敏感，敏感的是**沿 push 梯度方向**的步。

## 2. 单步灵敏度（H2）：push 项单独一步，被推 SMITH 行的 Δlog π

64 个 rest 决策，每个随机一行 SMITH 作为 explored 样本；held-out = 另 393 个 rest 决策的全部 SMITH 行。

| 优化器（一步） | A=+1 p5 / p50 / p95 | A=−1 p50 | held-out SMITH p50 | removal 行 p50 / p95 | combat KL p95 / max | combat Δlog π（p>1e-3 行）max\|·\| |
|---|---|---|---|---|---|---|
| Adam 3e-4，ckpt 状态，clip 0.5 | 10.1 / **12.2** / 14.8 | **−12.9** | 12.3 | 0.96 / 13.8 | 1.4e-3 / 3.4e-3 | 0.23 |
| Adam 3e-4，新状态，clip 0.5 | 6.0 / 8.9 / 11.2 | −9.2 | 8.9 | 1.25 / 17.6 | 1.3e-2 / 2.2e-2 | 0.49 |
| SGD 3e-4，clip 0.5 | 0.029 / 0.034 / 0.047 | −0.034 | 0.034 | 0.009 / 0.087 | 1.8e-7 | 0.002 |
| SGD 1e-5，clip 0.5 | 0.0011 | −0.0012 | 0.0011 | ~0 | ~0 | ~0 |
| SGD 3e-4，不 clip | 12.5 / 15.3 / 20.3 | −17.9 | 15.3 | 3.7 / 39 | 7e-3 / 5.6e-2 | 0.66 |
| Adam ckpt，**零梯度（仅动量）1 步** | −0.03 / 0.07 / 0.16 | — | 0.06 | −0.02 | 3.4e-4 / 3.0e-3 | 0.14 |
| Adam ckpt，零梯度 32 步 | −0.27 / 0.68 / 1.58 | — | 0.59 | −0.23（p5 −7.3） | 3.3e-2 / 0.21 | 1.5 |

- push 梯度范数（clip 前）236；按参数组：option_bank 161，trunk 137，scorer 104，card_emb 16.7，kind_emb 5.2（不 clip 时的 L2）。
- 对照：同样的 Adam ckpt 一步，push 改为 HEAL 行 A=−1（p≈1，梯度范数 1.84）：HEAL Δ = 0（p≈1 时 ∇log π≈0），SMITH 跟着 +1.1；push 改为 64 个 combat 采样行 A=+1（梯度范数 1.36）：被推 combat 行 KL p95 2.5e-3，SMITH −0.28。也就是说**同一 Adam 状态下，“正常”行的 push 只动零点几 nat；SMITH 行动 12 nat 是因为它们的梯度大 ~200 倍且彼此平行**。
- removal 行 push（187 个 removal 决策，A=+1）：Adam ckpt 一步 removal 行 p50 +3.0，p95 +42.6，max +51；Adam 新状态 p95 +35。并带动 SMITH 行 +2.9（共享参数串扰）。
- Adam 二阶矩（dormancy）：option_bank 组 49.8% 的参数 √v < 1e-6（kind_emb 45%，scorer 47%，card_emb 22%，trunk 22%）。但“新状态 Adam”同样 +8.9 nat，说明 dormant 只解释 ~30%，主因是 push 梯度一旦占主导，Adam 的步长 ≈ lr·(1−β1)/√(1−β2)·sign 与梯度大小无关。

### 参数组归因（Adam ckpt，A=+1，被推行 Δlog π p50；全量 = 12.2）

| 组 | 置零该组梯度后 | 只保留该组梯度 |
|---|---|---|
| option_bank（rest_emb / rest_mlp / shop_cat_emb / shop_mlp / item_emb / c_opt） | 4.25 | **9.36** |
| scorer（c_num / c_state / c_l2 / q_state / c_bias / c_hand / c_tgt / c_pot / c_copies） | 10.9 | 2.55 |
| trunk（其余编码器 + transformer） | 11.0 | 2.60 |
| card_emb（card_emb / c_card / card32） | 12.2 | 0.30 |
| kind_emb（c_kind / kind_emb） | 12.2 | 0.11 |
| value / aux heads | 12.2 | 0.07（= 仅动量） |

option bank 一组贡献约 3/4；它在 rest / shop 之外几乎不被训练（√v 极小），所以 push 一来就是满 lr 步长。

## 3. 修复原型（同样的单步测量；目标：被推行 ~0.5 nat/步，副作用小）

| 方案 | 被推 SMITH p50（p5..p95） | held-out SMITH p50 | removal 行 p95 | HEAL | combat KL p95 / max |
|---|---|---|---|---|---|
| (i) push 单独 SGD，lr 1e-6 / 3e-6 / **1e-5** / 3e-5 / 1e-4（不进 clip） | 0.054 / 0.163 / **0.547（0.46..0.74）** / 1.63 / 5.32 | 0.54（lr 1e-5） | 1.37（lr 1e-5） | 0 | 9.5e-6 / 6.5e-5（lr 1e-5） |
| (ii-a) Adam ckpt，push 不进 option_bank / card / kind | 4.17 | 3.99 | 13.9 | 0 | 2.0e-3 / 5.1e-3 |
| (ii-b) Adam ckpt，warm v（v ← max(v, g²)） | 0.90（0.63..1.17） | 0.89 | 1.17 | 0 | 5.4e-4 / 9.5e-4 |
| (iii) Adam ckpt，push 梯度 ×0.1 / ×0.01 / ×1e-3 / ×1e-4 | 12.2 / 12.2 / 10.0 / 3.8 | — | 13.8 / 13.8 / 8.4 / 0.82 | 0 | ~1e-3 |
| (iv) logit floor（max − 30，straight-through），Adam ckpt | 前向 Δ = 0（被 floor 盖住） | 0 | 0.02 | 0 | 1.4e-3 / 3.4e-3 |
| (v) rest_emb[SMITH] ← rest_emb[HEAL]，shop_cat_emb[card_removal] ← [card] | +43.8（5.8..86）；P(SMITH 合计) p50 = 1.0 | +51.6 | removal 行 +10（P(removal) p50 仍 9e-27） | −15.7（p5 −34.5） | 0（combat 不读这些表） |

- (i) 的一阶预测：Δ ≈ lr·‖g‖²（所有 SMITH 行梯度近乎平行）：1e-5 × 236² = 0.56，实测 0.55。所以“使每步 ~0.5 nat”的因子在 u7664 上是 lr 1e-5，**相对现在的 Adam 路径约缩小 22 倍；相对 Adam 去缩放梯度根本做不到**（(iii)：在 clip 0.5 + Adam 下要 ×1e-4 才降到 3.8 nat，且依赖其余梯度大小，不可控）。
- (ii-b) warm v 单步可行（0.9 nat），但 v 只在这一步被抬高，后续 push 步 v 按 β2 衰减后又失效，而且仍走全局 clip，不推荐。
- (iv) 行为影响：rest 决策 9589 行中 9032 行（94%）低于 floor，removal 5747 行中 4567 行（79%），combat 2356 行中 284 行（12%，130/385 个决策）；TV 距离 ≤ 3.3e-12，argmax 0 变化。但 floor 只是把前向 log π 钉在 ≈ −30 − log n，底层 score 仍被同样 +12 nat/步地推（梯度直通），而信赖域看到的 dlogp ≈ 0 → 永不 clamp → score 无界游走，越过 floor 时突然跳变。**它隐藏问题，不解决问题**，不推荐。
- (v) 过冲：SMITH:<card> 行还有 card 项和 A/B 数值项，复制 HEAL 的 bank 行后 SMITH 合计概率中位数变成 1（HEAL 降 16–22 nat），不是 ~1/n；removal 的厌恶不在 shop_cat_emb 里（P(removal) 几乎不变）。不推荐。

### 多步复现（`multistep.py`：32 个 minibatch，每个 16 个 explored SMITH，logp_old = update 起点，δ=1）

| 路径 | A | 平均 push_clamped | 平均 \|push_dlogp\| | 每 4 个 mb 的 push_dlogp | update 末 SMITH 漂移 p5 / p50 / p95 |
|---|---|---|---|---|---|
| 现状（共享 loss，clip 0.5，Adam ckpt） | ±1 各半 | **0.961** | 33.2 | 0, 11.8, 23.2, 35.1, 34.7, 41.6, 41.9, 45.3 | +30 / **+47** / +56 |
| 现状 | 全 −1 | 0.961 | 34.3 | 0, −24, −23, −33, −41, −42, −37, −47 | −53 / −48 / −43 |
| g/‖g‖² 归一化（η 0.5） | ±1 | 0.947 | 18.3 | 0, −6.1, −5.5, −11.9, −10.5, −29.7, … | −53 / −30 / −12 |
| 固定 SGD lr 1e-5 | ±1 | **0.0** | 0.22 | 0, 0.2, 0.5, 0.1, 0, −0.3, −0.3, −0.4 | −0.65 / **−0.50** / −0.41 |
| 固定 SGD lr 1e-5 | 全 −1 | 0.914 | 1.18 | 0, −1.3, −1.2, −1.3, … | −1.64 / −1.22 / −1.04 |
| 行尺度归一化 η/‖∇ mean log π‖²（η 0.5） | ±1 | **0.0** | 0.21 | 同上 | −0.59 / **−0.45** / −0.37 |
| 行尺度归一化 | 全 −1 | 0.908 | 1.18 | 0, −1.3, −1.2, … | −1.65 / −1.23 / −1.05 |

- “g/‖g‖²”是错的归一化：A 正负相抵时 ‖g‖ 很小，步长被除爆。正确的尺度是**单行梯度**的范数（用 mean_explored log π 的梯度估计，A 无关）。
- 固定 lr 与行尺度归一化在 u7664 上等价（因为 ‖∇ log π‖ ≈ 238 ≈ 常数）；但 removal 行的 ‖∇‖ 是 130..1580，且 gap 会随训练变化，所以推荐自适应版。

## 推荐（一个方案）

**`--explore-push` 的 push 项改为独立的“行尺度归一化 SGD 步”，不进 PPO loss、不进 Adam、不进全局 grad clip：**

1. 每个 minibatch：PPO loss（不含 push）照常 backward → clip 0.5 → Adam step（与 push off 的 wave-11 路径逐位相同）。
2. 若该 minibatch 有未 clamp 的 explored 样本：对 explored 子集（~16 行）单独前向，`g = ∇ L_push`、`h = ∇ mean_explored log π`（两次小 backward，或一次 forward + `autograd.grad` 两次），`θ ← θ − (η / ‖h‖²)·g`，η = 新 flag（建议 0.5 nat）。可选再加 per-update 累计上限。
3. SMITH 与 removal 分别归一化更稳（removal 行梯度尺度是 SMITH 的 3 倍，且两者共享参数串扰：一次 SMITH push 让 removal 行 p95 动 1.4 nat）——可以按 explore item 分组各做一次。

代价：新 flag（例如 `--explore-push-step ETA`，替代现在 push 进共享 loss 的语义；`--explore-push-coef` 失去意义）；checkpoint 完全兼容（无新状态，Adam 状态不变）；速度：每个 minibatch 多一次 ~16 行的前向 + 两次小 backward（explored 占 0.4%，相对 4096 行 minibatch 可忽略，但 compiled 路径需要单独的 eager / 编译图）；E3 的 compiled-vs-eager gate 也需要覆盖这条路径。另一个附带好处（复核时实测，见文末“复核”）：共享 clip 0.5 会把有 explored 样本的 minibatch 里的 PPO 梯度整体缩小——4096 行 minibatch、16 个 explored SMITH（A = ±1 各半）、其余为合成优势的 combat 行时，总梯度范数 23.6 vs PPO 单独 0.175，即 PPO 梯度被缩到 ~1/47；A 同号时 push 梯度范数 ~245，缩小 ~490 倍。拆开后这一副作用消失。

退路（若不想改结构）：固定 `lr_push = 1e-5` 的独立 SGD 步（不进 clip），在 u7664 上效果与自适应版相同，但需要随 gap 变化重新标定。

## 未验证 / 局限

- 只测了 fork 点 u7664；E3 在 7682..7695 的 checkpoint（gap 可能已发散到 −1000 级）没测，几百 nat 的绝对值是推断（多 update 累积 + 灵敏度随 gap 增长），没有直接复现到 −1000。
- bf16 用的是 CPU autocast；CUDA bf16 + `torch.compile` 的数值（E2 的 +20 clamp、E3 的 compiled-vs-eager push loss 0.0218 vs 0.0311）没在 GPU 上复现。
- 单步 / 32 步复现只用 push 项，没把 PPO 主项、value、entropy、aux 一起放进同一个 minibatch；“共享 clip 压缩 PPO 梯度”复核时用一个合成 minibatch 测过（~1/47，见文末），但 PPO 部分的优势 / value 目标是合成的，真实训练 minibatch 的比例未测。
- 推荐方案只有原型（脚本内实现），没有改 `ppo.py`，没有跑训练 smoke，没有测速度。
- 采集用的是 fp32 采样策略、无 loop breaker、dev-128 一次，与训练 rollout 的 seed stream 分布不同（rest 457 / removal 187 个决策）。

脚本与原始数据（不在 repo）：`/private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/diag13/scripts/{collect,analyze,multistep,make_fake}.py`，结果 `…/diag13/res.json`、`…/diag13/multistep.json`；raven 上 `/data/raven/hack-sts2/testbox/diag13/tmp/`（含 dec.pkl）。

## 复核（2026-11-06，独立 agent，raven 自起容器 `diag13v`）

- 来源核对：GCS 对象 `pinned-0007664.pt` 的 md5（`gcloud storage objects describe`）= b91f6709…，与本地 / raven 副本一致；文件内 `updates` = 7664，Adam step 245248 = 7664 × 32，`config`：lr 3e-4 常数（无 lr anneal），Adam eps 1e-5、betas (0.9, 0.999)，max_grad_norm 0.5，minibatch 4096，envs 2048 × rollout 64，epochs 1——诊断用的优化器超参与训练一致。testbox src 的全部 232 个 `sim/tools/**/*.py` 与 1966 个 C# / csproj / json 文件与 HEAD 47e9b6f 逐字节一致（47e9b6f 相对 ca4ce37 只改了 docs 和 colab ops 脚本）。`explore_push_term` 来自 `/work/sim/tools/train/sts2train/ppo.py`（即 committed 版本）。
- 决策是真实 env 决策：重跑 `collect.py` 得到逐字节相同的 dec.pkl（md5 b0264bf7…，rest 457 / removal 187 / combat 385，progress 0.431）；解码 rest 决策的候选行 kind：ChooseRestOption 9025、DiscardPotion 517、UsePotion 41、LeaveRestSite 6；removal 决策：BuyEntry 5343、LeaveShop 187、DiscardPotion 198、UsePotion 19。`make_fake.py` 只用于随机初始化 checkpoint 的代码路径 smoke（fake_dec.pkl），没有进入任何报告数字。
- 重跑 `analyze.py` / `multistep.py`：res.json 与 multistep.json 与原结果逐字节相同（CPU 确定性）。文中表格数字逐项对过 res.json，只有 H1 一句的 removal 上界写错（4.3 是 removal 决策全部合法行，removal 行本身是 3.5），已改。
- 独立脚本（`verify.py`，自写，只调用 committed 的 `explore_push_term` / `ppo_loss`；另取一组 64 个 rest 决策，seed 2024）：
  - gap / bf16：SMITH gap p5 / p50 / p95 / min = −126.7 / −92.1 / −67.5 / −148.3，< −100 3138 行，< −1000 0；bf16−fp32 SMITH p5 / p50 / p95 = −0.64 / −0.24 / +0.14（max |Δ| 1.08），removal 行 max |Δ| 3.51。与原文一致。
  - 单步 push（δ 1，clip 0.5）：Adam ckpt 状态 +12.25（p5..p95 10.0..16.1）/ −13.03；同一实验重复一次结果逐位相同（deepcopy 的 Adam 状态没有被污染）；Adam 新状态 +9.12 / −9.23；SGD 1e-5 不 clip +0.57 / −0.57；push 梯度范数 244.8（原文 236，子集不同）。held-out SMITH 行与被推行同幅（+12.2）。
  - 推荐的行尺度归一化步（η 0.5，单步，A = +1）：被推行 +0.48（p5..p95 0.40..0.64），held-out SMITH +0.46，HEAL 0，removal 行 p95 1.17，combat KL max 1e-4。一阶预测正好是 η = 0.5（A 同号时 g = −h）。原文 / 报告里“一步 +0.55”是固定 SGD lr 1e-5 的数，不是行尺度归一化版本。
  - 带 PPO 项的完整 minibatch（committed `ppo_loss`，explored 16 行 A = ±1 各半 + 4080 行 combat，优势 N(0,1)、vret = v + N(0, 0.5²) 为合成，aux 关）：PPO 单独梯度范数 0.175（不触发 clip），加 push 后 23.6，PPO 梯度被缩到 ~1/47；一个 minibatch 后被推行的 A 符号方向位移 p5 / p50 / p95 = −3.6 / +2.0 / +5.0，即同一 minibatch 里已经有行越过 δ = 1，且有 A = −1 的行被抬高 3.6 nat；held-out SMITH 整体 +4.3。补充含义：SMITH 行的 log π 梯度近乎平行，push 实际上是按 ΣA 的“共模平移”，区分不出哪张 SMITH 牌更好；行尺度归一化把速度压到 ~η·mean(A)，但不改变这一点（推断，未测逐牌区分度）。
- 未复核：E3 后期 checkpoint、CUDA bf16 / compile、真实训练 minibatch（真实优势 / value / aux）下的梯度比例、推荐方案在 `ppo.py` 中的实现。

复核脚本与输出：`/private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/diag13v/{verify.py,ck.py}`；raven `/data/raven/hack-sts2/testbox/diag13v/tmp/`（verify.json、res_v.json、multistep_v.json、dec_v.pkl）。

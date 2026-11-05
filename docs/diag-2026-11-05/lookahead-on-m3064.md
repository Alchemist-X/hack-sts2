# 决策时战斗前瞻在 M u3064 上的配对测量（2026-11-05）

**结论：在当前主线检查点 M u3064 上，前瞻搜索是负收益，不是小正收益。** 按 2026-11-04 选定的配置（turn 视界，`value+hp`，
K = 8，R = 1），dev-128 progress 从 greedy 的 0.4042 降到 0.3255，**Δ = −0.0787**（t 95 % CI [−0.1077, −0.0496]，
bootstrap [−0.1082, −0.0505]），18 局变好 / 71 局变差 / 39 局不变；combat 视界更差（Δ −0.0948）。两边都是 0 胜，没有任何
一局打过 Act 2 boss。ExIt 门槛（Δ ≥ +0.02 且 CI 不含 0）没有达到，而且方向是反的。

harness 本身是好的：greedy 复现 0.40419（与 GPU 上 M 的 dev-128 greedy-off 一致），K = 1 与 greedy 128/128 局历史一致，
有记录的重跑与原跑 128/128 一致，所有行都 128/128 可回放、0 desync、0 rollout 错误、0 rebuild。trim_padding 对 M 无影响
（9.6 万行 0 个 argmax 翻转）。问题出在叶子打分用的 M 的价值头上（§3）。

## 1. 环境与输入

| 项 | 值 |
| --- | --- |
| 机器 | raven（8 核，测前 load 0.25），`python:3.13` 容器，`nice -n 10`，`--cpus 8`，8 env + 8 torch 线程 |
| torch | 2.15.0+cpu（复用 `/data/raven/hack-sts2/lookahead/venv`，只读挂载） |
| 库 / 代码 | Colab bundle `sts2train-bd6d6f6863`（`lib/Sts2Sim.Vec.so` NativeAOT，sha256 `997bb8a0…`，ABI 2，encoder `3b73317739c650bd`；两个 wheel 解包到 `m3064/src/`）；`sts2vec check`: OK |
| 检查点 | `gs://…/runs/sp3-main/checkpoints/pinned-0003064.pt`，sha256 `11710869…`，weights `3bd9933b400b8fb7`；训练配置无 macros，但 `acq_shaping: true`（deck 0.003 / relic 0.01 / upgrade 0.012），`cycle_cost 0.01`，`gamma 1.0`，`objective protocol`，aux heads |
| seed set | `silent-a10-dev-v1`（128 局，sha256 `7fada574…`） |
| 运行目录 | raven `/data/raven/hack-sts2/lookahead/m3064/runs/m3064-dev128/`（每行一个子目录 + `paired-*.json`） |

## 2. 结果（dev-128，配对对照 greedy）

Greedy：**0.4042** [0.3779, 0.4305]，128 loss，平均层数 21.31，64 局过 Act 1 boss，0 局过 Act 2 boss，128/128 可回放，96 s。

| 配置（K = 8，R = 1） | progress | Δ | t 95 % CI | bootstrap 95 % CI | 好 / 差 / 同 | 改选比例 | ms / 搜索决策 | wall |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| **turn，value+hp（主问题）** | **0.3255** | **−0.0787** | **[−0.1077, −0.0496]** | **[−0.1082, −0.0505]** | 18 / 71 / 39 | 21.1 % | 13.2 | 349 s |
| combat（H 40），value+hp | 0.3094 | −0.0948 | [−0.1172, −0.0724] | [−0.1173, −0.0727] | 12 / 80 / 36 | 14.3 % | 59.5 | 1,299 s |
| turn，value（诊断） | 0.2887 | −0.1155 | [−0.1394, −0.0916] | [−0.1395, −0.0922] | 6 / 88 / 34 | 24.2 % | 12.7 | 316 s |
| turn，hp（诊断，不用价值头） | 0.3793 | −0.0249 | [−0.0583, +0.0085] | [−0.0581, +0.0081] | 36 / 54 / 38 | 13.8 % | 11.3 | 379 s |
| turn，value+hp，**去 shaping**（诊断补丁，§3） | 0.3522 | −0.0520 | [−0.0836, −0.0204] | [−0.0842, −0.0208] | 30 / 59 / 39 | 20.8 % | 13.2 | 389 s |
| turn，value，去 shaping（诊断补丁） | 0.3210 | −0.0832 | [−0.1099, −0.0564] | [−0.1095, −0.0568] | 18 / 72 / 38 | 23.5 % | 12.8 | 364 s |
| combat，value+hp，去 shaping（诊断补丁） | 0.3522 | −0.0520 | [−0.0811, −0.0230] | [−0.0810, −0.0231] | 27 / 65 / 36 | 14.7 % | 46.7 | 1,189 s |
| K = 1（验收） | 0.4042 | 0 | — | — | 0 / 0 / 128 | 0 | — | 486 s * |

每一行：128/128 可回放，0 desync，0 rollout 错误，0 rebuild，pool max 128（ABI 2 branch 路径）。胜局全部为 0。
\* K = 1 行与一个 2 线程诊断脚本同时运行，wall 不代表成本。搜索份额约占 live 决策的 65 %；每次搜索 5.3 个候选。
主配置的时间构成：总 policy 341 s，其中 rollout 前向 233 s（模型 214 s），env 45 s，root greedy 前向 46 s。M 是 mid
preset（2.9M），单决策成本约为 11-04 那个 large 检查点的一半（13 ms 对 25 ms）。

### 2.1 去 shaping 的 combat 行

去 shaping 后 combat 视界与 turn 视界持平（都是 −0.052）：40 局过 Act 1 boss（greedy 64），**1 局打过 Act 2 boss**（greedy 0），Act 1 boss 败率 58 %（greedy 40 %）。rollout 结果里 defeat 17,290 次，won 103,660 次。

### 2.2 输在哪里（主配置 vs greedy，按房间汇总所有战斗）

| | greedy：场数 / 败 / 平均掉血 | turn value+hp：场数 / 败 / 平均掉血 | turn hp：场数 / 败 |
| --- | --- | --- | --- |
| Act 1 普通 | 702 / 8 (1.1 %) / 5.1 | 677 / 11 (1.6 %) / 5.3 | 690 / 5 (0.7 %) |
| Act 1 精英 | 160 / 13 (8.1 %) / 23.6 | 146 / **37 (25.3 %)** / 29.9 | 153 / 28 (18.3 %) |
| Act 1 boss | 107 / 43 (40.2 %) / 46.2 | 80 / **42 (52.5 %)** / 48.0 | 95 / 46 (48.4 %) |
| Act 2 普通 | 266 / 28 (10.5 %) | 144 / 23 (16.0 %) | 222 / 14 (6.3 %) |
| Act 2 精英 | 41 / 19 (46.3 %) | 18 / 10 (55.6 %) | 36 / 22 (61.1 %) |
| Act 2 boss | 17 / 17 (100 %) | 5 / 5 (100 %) | 13 / 10（**3 局打过**） |
| 过 Act 1 boss 的局数 | 64 | 38 | 49 |

主配置的损失集中在 Act 1 精英（败率 8 % → 25 %，六种精英都变差，Phrog Parasite 4 → 12 败）和 Act 1 boss；普通战几乎不变。
combat 视界把 Act 1 boss 败率推到 77 %。hp-only 行是唯一出现 Act 2 boss 击杀的行（3 局进入 Act 3），但整体仍是负的且 CI
含 0，3 局不足以说明什么。

## 3. 原因：M 的价值头不是 harness 假设的那种量

搜索叶子分数 = ΔP + V(leaf)（+ hp 项），死亡记 0。这要求 V 估计的是"剩余 protocol 进度"（≥ 0）。11-04 的检查点满足这一点；
M 不满足：

1. **acquisition shaping 的势能债。** M 训练时 `acq_shaping: true`，γ = 1。势能 shaping 在每次拿牌 / 遗物 / 升级时先付奖励，
   终局时一次性扣回 φ(s)，所以 M 的 V(s) ≈ 剩余进度 − φ(s)。在 2.9M 模型上用 greedy 采 19.2 万个 Silent A10 状态：战斗状态中
   **28.7 % 的 V < 0**，hp < 25 % 时 **80.8 % 的 V < 0**（V 分位数 1 %–99 %：−0.155 … 0.336）。于是在难打的回合里，"本回合
   就死"（记 0）比"活下来但 ΔP + V < 0"更高分。记录跑（`--record`，决策与主配置 128/128 一致）里 21,768 次搜索有 2,734
   次 greedy 候选 Q < 0，89 次搜索直接改选了回合内死亡的线。
2. **去掉 shaping 后仍为负。** 诊断补丁（只在 raven 的副本 `m3064/src-phi/` 中，`STS2LA_DESHAPE=1`：叶子 V 加回 φ(leaf)，
   系数取自检查点）把主配置从 −0.079 拉到 −0.052、value-only 从 −0.116 拉到 −0.083，但都仍显著为负。剩下的部分是价值头在
   同一回合不同出牌之间的区分度不足以指导搜索（corr(V, hp%) 在战斗内只有 0.38）；`cycle_cost` 也让 V 偏低但量级小，未单独
   处理。
3. **不是 harness 问题。** K = 1 = greedy；重跑一致；trim_padding 在 M 上 0 翻转（max |Δv| 1.6e-7）；0 desync。

对照：hp-only 打分（完全不看 V）是所有行里最好的，Δ −0.025 且 CI 含 0，说明 M 的 greedy 策略本身已经比"greedy rollout +
回合末短视叶子"强；搜索在这个策略族上没有可榨的余量，除非叶子评估器换掉。

## 4. 没做的 / 注意事项

- **w3m-b u5300 没跑。** `sts2train.lookahead` 的外层评估（`vec_evaluate(...)` 不传 macros）和镜像 env（`VecEnv(...)` 不传
  macros）都不读检查点的 `config.macros`；而 `--greedy` 走 `sts2train.evaluate.evaluate`，它会按 `checkpoint_macros(ck)` 打开
  `rest,shop`。所以对 macros 检查点，greedy 行和前瞻行的环境不同，配对不公平。要测需先给 lookahead 加 macros 透传（外层与镜像
  env 一致）。w3m-b 的检查点已上传到 raven `m3064/ckpt/pinned-0005300.pt`（sha256 `d6c387c2…`），未使用。
- held-out `silent-a10-test-v1` 没跑（dev 上已是负的，不需要选择后验证）。
- 去 shaping 补丁是诊断用的一次性修改，不在仓库里；policy hash 与未补丁行相同（`la:5e9d2524`），只能靠目录名 `phi-*` 和日志
  里的 `DIAG deshape` 行区分。
- raven 是共享机，wall 时间有 ±20 % 的波动；K = 1 行与诊断脚本重叠。
- `nice -n 10 docker run` 只降低了 docker 客户端的优先级，容器进程归 dockerd 管，实际起限制作用的是 `--cpus 8` 和 8 线程设置。

## 5. 下一步含义

- **ExIt（用这个搜索当老师）在 M 上不成立。** 主配置 Δ = −0.079，CI 全在 0 以下，远离 +0.02 的门槛；没有增加胜局，也没有增加
  Act 2 boss 击杀（0 → 0；Act 1 boss 通过数 64 → 38）。用 `--record` 的目标去模仿只会把 M 往更差的方向拉。
- **如果还想要决策时搜索，先修叶子。** 最低限度：对 `acq_shaping` 检查点，叶子 V 加回 φ(leaf)（或死亡记 −φ(root)），并处理
  `cycle_cost`；这能拿回约 1/3 的损失，但仍为负。真正的杠杆是一个在战斗内有区分度的评估器（例如 M 已有的 aux 头
  `combat_hp_pred` / `win_prob` 作为叶子，或单独训练的战斗价值头），然后再测一次同样的配对。在那之前，主线进度应来自训练本身
  （M 的 greedy 0.404 已是 11-04 检查点 + 搜索 0.171 的两倍多），而不是 eval-time 搜索。
- 若要测 macros 检查点（w3m-a / w3m-b），先给 lookahead 加 macros 透传。

## 6. 复现

```bash
# Mac：拉检查点（已认证的 gcloud），按 2 MB 分块 scp 到 raven（整文件 scp/rsync 到 raven 会断）
gcloud storage cp gs://gomoku-zero-20260816-ai-sts2train/runs/sp3-main/checkpoints/pinned-0003064.pt DL/
split -b 2m DL/pinned-0003064.pt m. && scp m.* raven:/data/raven/hack-sts2/lookahead/m3064/parts/
# raven：cat parts/m.* > ckpt/pinned-0003064.pt；bundle wheel 解包到 m3064/src/
cd /data/raven/hack-sts2/lookahead/m3064
./run.sh -m sts2vec.cli check --threads-b 8
./queue.sh "greedy --greedy" \
  "la-turn-value+hp --horizon turn --score value+hp --K 8 --R 1" \
  "la-combat-value+hp --horizon combat --score value+hp --K 8 --R 1"
#  run.sh = nice -n 10 docker run --cpus 8 python:3.13，PYTHONPATH=/w/src，STS2VEC_AOT_LIB=bundle lib，STS2VEC_SEEDSETS=bundle seedsets
#  queue.sh 对每个 spec 执行：python -m sts2train.lookahead eval --seedset silent-a10-dev-v1 \
#     --checkpoint ckpt/pinned-0003064.pt --threads 8 --out runs/m3064-dev128/<name> <flags>
./run.sh -m sts2train.lookahead paired runs/m3064-dev128/greedy runs/m3064-dev128/la-turn-value+hp \
  --out runs/m3064-dev128/paired-la-turn-value+hp.json
# 诊断：K = 1、hp、value、--record 同上；去 shaping 行用 queue-phi.sh（run-phi.sh：PYTHONPATH=/w/src-phi，STS2LA_DESHAPE=1）
# 工具：tools/pair.sh（配对汇总）、tools/cmp2.py（按房间统计）、tools/qan.py（searches.jsonl Q 分析）、
#       tools/vcheck.py（价值头分布）、tools/trimcheck.py（trim_padding 等价性）
```

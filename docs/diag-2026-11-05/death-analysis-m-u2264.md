# 主线 M（sp3-main）死亡分析：u2264 dev-128 greedy-off

日期 2026-11-05。只读分析，数字均标注来源：
- **[E-uXXXX]** = `runs/sp3-main/evals/u000XXXX/greedy-off/games/*.summary.json`（dev-128，`silent-a10-dev-s128`，greedy 策略，n=128）；
- **[E764]** = `runs/sp2-c1/evals/u0000764/greedy-off/`（M 的起点 checkpoint，同一 seedset）；
- **[T]** = `runs/sp3-main/episodes.jsonl` 训练局（fresh-start stream，采样策略，SP1 种子流，非 dev-128）。

## 结论先行

1. 128 局全部是**战斗内死亡**（`final_hp == 0` 128/128，`death.enemies` 128/128 非空），没有 event/rest/shop/treasure 死亡，没有 capped/stuck [E-u2264]。
2. u2264 死亡位置：**Act 1 boss 41 局（32.0%）**，**Act 2 第 6 间以后、boss 之前 43 局（33.6%）**，Act 2 前 5 间 13 局（10.2%），Act 1 boss 前 22 局（17.2%），Act 2 boss 9 局（7.0%）[E-u2264]。
3. **Act 2 boss：到达 9 局，死亡 9 局（0/9 通过）**；Act 1 boss：到达 106 局，死亡 41 局（38.7%）[E-u2264]。
4. 单一最大杀手是 **`DECIMILLIPEDE_ELITE`（Act 2 elite），16/128 = 12.5%**，从 u1264 的 5 局升到 16 局；其次是 6 个 Act 1 boss 合计 41 局，单个 boss 3–10 局 [E-u1264, E-u2264]。
5. 趋势 u764 → u1264 → u1764 → u2264：Act 1 boss 前死亡 40 → 37 → 26 → 22，Act 1 boss 条件死亡率 62.5% → 48.4% → 42.2% → 38.7%，到达 Act 2 33 → 47 → 59 → 65。**损失正从 Act 1 移到 Act 2 中段**，Act 2 boss 一直是 0 通过 [E764, E-u1264, E-u1764, E-u2264]。
6. greedy-on（loop breaker 开）与 greedy-off 在 u2264 **逐局完全相同**（128/128 局段位一致，`loop_breaks` 总数 0）；u1264 也逐局相同（`loop_breaks` 共 1 次）。breaker 对死亡分布无影响 [E-u2264 两种模式, E-u1264 两种模式]。
7. 训练局里不记录卡组 / 遗物 / 升级 / HP / 遭遇，**卡组相关问题无法从现有数据回答**（见“不能回答的问题”）。

## 1. dev-128 评估（greedy-off）

### 1.1 进度定义与楼层映射

`sim/tools/eval/sts2eval/progress.py`（protocol v0.2）：
`progress = ((a-1) + min(c, L_a-1)/L_a) / 3`，`c = max(0, k-1)`（死亡所在房间不算 cleared），`k` = 本 act 已进入房间数（act 1：`k = floor`；act 2：`k = floor - act_start_floor[2]`，这里恒为 17）。
本 seedset 所有地图 Act 1 都是 `L_1 = 17`（boss = floor 17）；Act 2 `L_2 = 16`（boss = floor 33）为主，u2264 有 3 局 `L_2 = 18`（boss = floor 35）[E-u2264 `act_rooms`]。
对应进度：Act 1 boss 死亡 = 0.314；进入 Act 2 = 0.333；Act 2 boss 死亡（L=16）= 0.646。

### 1.2 死亡段位分布（n=128 每列）

| 段位 | u764 [E764] | u1264 [E-u1264] | u1764 [E-u1764] | **u2264 [E-u2264]** |
|---|---|---|---|---|
| A1 floor 1–8 | — | 11 (8.6%) | 8 (6.2%) | 7 (5.5%) |
| A1 floor 9–16 | — | 26 (20.3%) | 18 (14.1%) | 15 (11.7%) |
| A1 boss 前合计 | 40 (31.2%) | 37 (28.9%) | 26 (20.3%) | 22 (17.2%) |
| **A1 boss（floor 17）** | 55 (43.0%) | 44 (34.4%) | 43 (33.6%) | **41 (32.0%)** |
| A2 k1–5（floor 18–22） | 10 (7.8%) | 9 (7.0%) | 19 (14.8%) | 13 (10.2%) |
| **A2 k6+ boss 前** | 21 (16.4%) | 32 (25.0%) | 36 (28.1%) | **43 (33.6%)** |
| A2 boss | 2 (1.6%) | 6 (4.7%) | 4 (3.1%) | 9 (7.0%) |
| 到达 A3 | 0 | 0 | 0 | 0 |
| progress_mean | 0.310 | 0.348 | 0.367 | 0.388 |

### 1.3 条件死亡率（死亡数 / 到达数）

| 指标 | u764 | u1264 | u1764 | u2264 |
|---|---|---|---|---|
| 到达 A1 boss | 88 | 91 | 102 | 106 |
| A1 boss 死亡率 | 55/88 = 62.5% | 44/91 = 48.4% | 43/102 = 42.2% | **41/106 = 38.7%** |
| 到达 A2 | 33 | 47 | 59 | 65 |
| 到达 A2 boss | 2 | 6 | 4 | 9 |
| A2 boss 通过 | 0/2 | 0/6 | 0/4 | **0/9** |

u2264 Act 2 逐房间 hazard（死亡/在险，k = act 2 已进入房间数）[E-u2264]：
k1–3: 0/65；k4: 5/65；k5: 8/60；k6: 8/52；k7: 9/44；k8: 7/35；k9: 0/28；k10: 3/28；k11: 4/25；k12: 0/21；k13: 7/21；k14: 5/14；k15: 0/9；k16(boss, L=16): 8/9；k18(boss, L=18): 1/1。
即 **k4–k8（floor 21–25）共 37 局死亡，占 Act 2 死亡 65 局的 57%**。

u2264 Act 1 逐楼层 hazard：floor 1–6 为 0；f7 5/128，f8 2/123，f9 5/121，f11 1/116，f12 4/115，f13 1/111，f14 3/110，f15 1/107，f17(boss) 41/106 [E-u2264]。

### 1.4 死亡房间类型

`death.room_type` 在全部 768 个 summary 里都是 `null`，房间类型用遭遇 id 后缀（`_BOSS` / `_ELITE` / `_NORMAL` / `_WEAK`）代替；所有局都死在战斗里，故 event/rest/shop/treasure 计数为 0。

| 类型 | u1264 [E-u1264] | u2264 [E-u2264] |
|---|---|---|
| BOSS | 50 (39.1%) | 50 (39.1%) |
| ELITE | 44 (34.4%) | 40 (31.2%) |
| NORMAL | 33 (25.8%) | 38 (29.7%) |
| WEAK | 1 (0.8%) | 0 |
| event / rest / shop / treasure | 0 | 0 |

u2264 按房间位置细分 [E-u2264]：Act 1 boss 前 22 局中 ELITE 18、NORMAL 4；Act 2 k4–k6 全部 NORMAL（21 局）；Act 2 k7–k14 ELITE 22、NORMAL 13。

### 1.5 杀手遭遇排名（u2264 greedy-off，n=128，共 31 个不同遭遇）[E-u2264]

| # | 遭遇 | act | 死亡 | % | u1264 对照 [E-u1264] |
|---|---|---|---|---|---|
| 1 | DECIMILLIPEDE_ELITE | 2 | 16 | 12.5 | 5 |
| 2 | THE_KIN_BOSS | 1 | 10 | 7.8 | 8 |
| 3 | WATERFALL_GIANT_BOSS | 1 | 8 | 6.2 | 9 |
| 3 | VANTOM_BOSS | 1 | 8 | 6.2 | 8 |
| 5 | LAGAVULIN_MATRIARCH_BOSS | 1 | 7 | 5.5 | 7 |
| 6 | THE_OBSCURA_NORMAL | 2 | 6 | 4.7 | 3 |
| 6 | PHANTASMAL_GARDENERS_ELITE | 1 | 6 | 4.7 | 5 |
| 6 | CHOMPERS_NORMAL | 2 | 6 | 4.7 | 4 |
| 9 | KAISER_CRAB_BOSS | 2 | 5 | 3.9 | 3 |
| 9 | HUNTER_KILLER_NORMAL | 2 | 5 | 3.9 | 1 |
| 9 | SPINY_TOAD_NORMAL | 2 | 5 | 3.9 | 6 |
| 9 | PHROG_PARASITE_ELITE | 1 | 5 | 3.9 | 7 |
| 9 | CEREMONIAL_BEAST_BOSS | 1 | 5 | 3.9 | 7 |
| 14 | OVICOPTER_NORMAL | 2 | 4 | 3.1 | 5 |
| 15 | TERROR_EEL_ELITE | 1 | 3 | 2.3 | **10** |
| 15 | INFESTED_PRISMS_ELITE / ENTOMANCER_ELITE / SOUL_FYSH_BOSS / EXOSKELETONS_NORMAL | 1–2 | 各 3 | 各 2.3 | 5 / 4 / 5 / 1 |
| — | 其余 12 个遭遇 | | 各 ≤2，合计 17 | 13.3 | |

集中度：前 1 名 12.5%，前 3 名（DECIMILLIPEDE + THE_KIN + WATERFALL_GIANT/VANTOM）34/128 = 26.6%，前 5 名 49/128 = 38.3%。
变化最大的两项：`TERROR_EEL_ELITE` 10 → 3（Act 1 floor 9–16 的主杀手被基本解决），`DECIMILLIPEDE_ELITE` 5 → 16。注意：eval 只记录死亡，不记录“遇到但打过”的遭遇，所以 DECIMILLIPEDE 的上升无法区分是“遇到次数变多（更多局活到 Act 2 中段）”还是“单次胜率变差”。

u2264 按段位的杀手 [E-u2264]：
- A1 boss（41）：THE_KIN 10、WATERFALL_GIANT 8、VANTOM 8、LAGAVULIN_MATRIARCH 7、CEREMONIAL_BEAST 5、SOUL_FYSH 3。
- A1 boss 前（22）：PHANTASMAL_GARDENERS_ELITE 6、PHROG_PARASITE_ELITE 5、TERROR_EEL_ELITE 3、BYGONE_EFFIGY_ELITE 2、其余各 1。
- A2 k1–5（13）：HUNTER_KILLER 3、SPINY_TOAD 3、THE_OBSCURA 2、EXOSKELETONS 2、其余各 1（全为 NORMAL）。
- A2 k6+ boss 前（43）：**DECIMILLIPEDE_ELITE 16**、CHOMPERS 5、THE_OBSCURA 4、OVICOPTER 3、INFESTED_PRISMS_ELITE 3、ENTOMANCER_ELITE 3、其余 9。
- A2 boss（9）：KAISER_CRAB 5、KNOWLEDGE_DEMON 2、THE_INSATIABLE 2。

### 1.6 各 Act 1 boss 的条件死亡率（有偏，仅供参考）

summary 不记录打过的 boss 是谁。下面用“该 seed 在 u764/u1264/u1764/u2264 任一 eval 里死在 Act 1 boss”来确定其 boss（128 个 seed 中 89 个可确定，无冲突），u2264 到达 Act 1 boss 的 106 局里 26 局 boss 未知（四次都打过了）。因此下表**高估死亡率**（未知的 26 局大概率是“打过”的那部分）[E764, E-u1264, E-u1764, E-u2264]：

| Act 1 boss | u2264 到达（boss 已知） | 死亡 | 死亡率 |
|---|---|---|---|
| THE_KIN_BOSS | 11 | 10 | 91% |
| VANTOM_BOSS | 14 | 8 | 57% |
| WATERFALL_GIANT_BOSS | 17 | 8 | 47% |
| LAGAVULIN_MATRIARCH_BOSS | 15 | 7 | 47% |
| CEREMONIAL_BEAST_BOSS | 11 | 5 | 45% |
| SOUL_FYSH_BOSS | 12 | 3 | 25% |

THE_KIN_BOSS 在 u1264 / u1764 也是 8/8（100%），是四个 checkpoint 里唯一一直没有下降的 Act 1 boss。

### 1.7 其他可观测量

- 逐 seed 稳定性：u1264 与 u2264 同段位 54/128；两者都到 Act 2 的 35 局，u1264 到 47、u2264 到 65 [E-u1264, E-u2264]。
- 死亡时 `max_hp`：u2264 有 79/128 局为 70（其余 75/77/80/81/61 等）[E-u2264]。
- 回合 / 步数：u2264 turns 均值 45.2、steps 均值 335.4；u1264 为 39.5 / 308.3。
- 进度贡献（u2264，各段位“局数占比 × 段内平均进度”）：A2 k6+ 0.170、A1 boss 0.100、A2 boss 0.045、A2 k1–5 0.042、A1 boss 前 0.031，合计 0.3885 [E-u2264]。

### 1.8 greedy-on vs greedy-off

| | u1264 off | u1264 on | u2264 off | u2264 on |
|---|---|---|---|---|
| progress_mean | 0.3475 | 0.3484 | 0.3885 | 0.3885 |
| 逐局段位相同 | — | 128/128 | — | 128/128 |
| loop_breaks 总数 | 0 | 1 | 0 | 0 |

u1264 on/off 唯一差别：一局 A1 f9–16 的死亡遭遇从 PHANTASMAL_GARDENERS_ELITE 变成 CULTISTS_NORMAL（同段位）。breaker 在这个模型上不改变死亡分布 [E-u1264, E-u2264 两种模式]。

## 2. 训练局（episodes.jsonl）

窗口：**u765–964**（M 从 sp2-c1 u764 恢复后的前 200 个 update）对比 **u2065–2264**（u2264 前 200 个 update）。全部是 stream 的 fresh-start（`start_mode: "stream"`，`start_pool: null`，无 `start_kind` 字段），采样策略，种子 SP1-SILENT-*，不是 dev-128 [T, config.json]。
Act 2 boss 用 `floor >= 33` 近似（episodes 不记 `act_rooms`；dev-128 上 3/65 的 Act 2 地图是 18 间，floor 33 在这种地图上不是 boss）。

### 2.1 死亡段位分布

| 段位 | u765–964 [T] | u2065–2264 [T] |
|---|---|---|
| 完成局数 | 107,042（全部 death） | 86,778（death 86,771，stall 7） |
| progress 均值 | 0.2655 | 0.3408 |
| A1 floor 1–8 | 21,618 (20.2%) | 9,421 (10.9%) |
| A1 floor 9–16 | 28,568 (26.7%) | 14,716 (17.0%) |
| **A1 boss（floor 17）** | 45,212 (42.2%) | **30,046 (34.6%)** |
| A2 k1–5（floor 18–22） | 4,574 (4.3%) | 9,472 (10.9%) |
| **A2 k6+ boss 前（floor 23–32）** | 6,336 (5.9%) | **19,443 (22.4%)** |
| A2 floor ≥ 33（≈boss） | 720 (0.7%) | 3,485 (4.0%) |
| A3 | 14 (0.0%) | 188 (0.2%) |

### 2.2 条件死亡率

| 指标 | u765–964 | u2065–2264 |
|---|---|---|
| 到达 A1 boss | 56,856 / 107,042 = 53.1% | 62,634 / 86,771 = 72.2% |
| A1 boss 死亡率 | 45,212 / 56,856 = **79.5%** | 30,046 / 62,634 = **48.0%** |
| 到达 A2 | 11,644 = 10.9% | 32,588 = 37.6% |
| floor 33 死亡率（≈A2 boss） | 710 / 734 = 96.7% | 3,460 / 3,673 = 94.2% |
| 到达 A3（以 A3 死亡局计） | 14 | 188 |

u2065–2264 Act 2 逐楼层 hazard [T]：f21 0.11，f22 0.19，**f23 0.24，f24 0.25，f25 0.24**，f27 0.15，f28 0.15，f29 0.13，f30 0.19，f31 0.28，f33 0.94（f26、f32 为 0：这两层不出战斗死亡）。Act 1 hazard 峰值在 f7 0.054、f15 0.048，f17 0.480。
训练局与 eval 的形状一致：Act 1 boss 是最大的单点死亡，Act 2 floor 21–25 是第二个集中带，Act 2 boss 基本过不去。采样策略在训练中有 188 局到达 A3，greedy eval 128 局中为 0。

### 2.3 字段

`episodes.jsonl` 每行只有：`update, session, slot, seed_text, n, result, act, floor, steps, progress, return, end_kind, cycles`（907,691 行全部只有这 13 个键；无 hook 字段，无 `start_kind`）[T]。
`cycles` 在 u765–964 共 146 次，u2065–2264 共 6 次。

## 3. 含义（只基于上面数字）

u2264 的死亡损失主要落在三处：**Act 1 boss 房（41/128，32.0%，条件死亡率 38.7%）**、**Act 2 的 floor 21–25 / k4–k8（37/128，28.9%，前半为 NORMAL、k7–k8 起出现 ELITE）**、以及 **DECIMILLIPEDE_ELITE（16/128，12.5%，单一遭遇最高且在上升）**；Act 2 boss 虽只占 9 局，但 0/9 通过，是进入 Act 3 的硬阻断。按“遭遇”看损失是**分散的**：31 个不同遭遇，前 5 名只占 38.3%，Act 1 boss 的 41 局分散在 6 个 boss 上（3–10 局）；按“位置”看则**集中**：Act 1 boss 房 + Act 2 k4–k8 两段合计 78/128 = 60.9%。从 u764 到 u2264，Act 1 boss 前死亡减半（40 → 22），Act 1 boss 条件死亡率 62.5% → 38.7%，这部分收益已经转化为 Act 2 中段死亡（A2 k6+ 21 → 43）；训练局的 Act 1 boss 死亡率（79.5% → 48.0%）与 Act 2 f23–25 hazard（约 0.24–0.25）给出同样的形状。若要从 0.389 走向 0.60，按定义需要多数局活到 Act 2 第 ~12 间以后（Act 2 k13 死亡对应 progress ≈ 0.583），而目前 Act 2 的 65 局里只有 21 局活到 k13。

## 不能回答的问题

| 问题 | 原因 |
|---|---|
| 进入 Act 1 / Act 2 boss 时的 HP | summary 只有 `final_hp`（全为 0）和 `max_hp`；`combat_hp_pred` 是模型预测值不是观测值；episodes 无 HP 字段。需用 `history_actions`（128/128 `replayable`）在模拟器里重放获取，本次规则禁止在本机运行 dotnet/sim。 |
| 死亡时卡组大小、升级卡数、遗物数；到达 Act 2 的局卡组构成是否不同 | eval summary 与 episodes.jsonl 都没有 deck / relic / upgrade 字段；同样只能靠重放（`sts2train reportcard` 可重放并输出逐步状态）。 |
| 房间类型（`death.room_type`） | 768 个 summary 全部为 `null`；本文用遭遇后缀代替，所以 event/rest/shop/treasure 只能根据“所有局都在战斗中死亡”推断为 0。 |
| 某遭遇的条件死亡率（死亡 / 遇到次数） | 只记录致死遭遇，不记录打过的战斗；DECIMILLIPEDE_ELITE 上升的原因无法区分是暴露增加还是胜率下降。Act 1 boss 的条件率只能做有偏估计（§1.6）。 |
| 训练局的致死遭遇 / 房间类型 | episodes 无遭遇字段；只能按楼层推断（floor 17 = Act 1 boss）。 |
| 训练局中 Act 2 boss 的精确识别 | episodes 无 `act_rooms`；用 floor ≥ 33 近似，18 间地图会被误计。 |
| 到达 Act 2 后通过 Act 2 boss 的 eval 局情况 | u2264 dev-128 中 0 局通过，无样本。 |

## 数据与方法

本机只用 `gcloud storage` 下载 + 单进程 `python3 -I` 标准库脚本（json / collections / statistics / glob），jsonl 逐行流式读取。数据目录与脚本目录分离。

数据目录（下载于 2026-11-05 约 06:35 UTC）：`/private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/death-analysis/data/`

| 文件 | 来源 | 大小 / 行数 |
|---|---|---|
| `evals/u0001264/{greedy-off,greedy-on}/`、`evals/u0001764/…`、`evals/u0002264/…` | `gs://gomoku-zero-20260816-ai-sts2train/runs/sp3-main/evals/u000XXXX/` | 每目录 128 个 `games/*.summary.json`（4–18 KB/个）+ `report.json`(~46 KB)、`report.md`、`config.json`、`run.json`；u2264 目录合计 2.57 MiB / 264 对象 |
| `sp2-c1-u764/greedy-off/` | `gs://…/runs/sp2-c1/evals/u0000764/greedy-off/` | 128 个 summary |
| `episodes.jsonl` | `gs://…/runs/sp3-main/episodes.jsonl`（用 `gcloud storage cat` 流式下载，因文件在训练中持续追加） | 197,646,064 B，907,691 行，update 764–2687，session 4（u764 恢复）与 5 |
| `config.json`、`status.json`、`cycles.jsonl`(78,856 B)、`evals.jsonl`(56,395 B) | `gs://…/runs/sp3-main/` | — |

命令：
```bash
B=gs://gomoku-zero-20260816-ai-sts2train/runs/sp3-main
gcloud storage cp -r "$B/evals/u0002264/*" data/evals/u0002264/    # 同法 u0001264、u0001764
gcloud storage cp -r gs://gomoku-zero-20260816-ai-sts2train/runs/sp2-c1/evals/u0000764/greedy-off data/sp2-c1-u764/
gcloud storage cat $B/episodes.jsonl > data/episodes.jsonl
S=scripts; E=data/evals
python3 -I $S/eval_deaths.py $E/u0001264/greedy-off $E/u0001764/greedy-off $E/u0002264/greedy-off $E/u0002264/greedy-on $E/u0001264/greedy-on
python3 -I $S/eval_rates.py data/sp2-c1-u764/greedy-off $E/u0001264/greedy-off $E/u0001764/greedy-off $E/u0002264/greedy-off
python3 -I $S/eval_kind_by_floor.py $E/u0002264/greedy-off
python3 -I $S/train_episodes.py data/episodes.jsonl 765 964 2065 2264
```

脚本（不入库）：`/private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/death-analysis/scripts/`
- `eval_deaths.py`：段位、最终楼层、遭遇后缀、杀手排名、按段位杀手、逐 seed 跨 checkpoint 对比。
- `eval_rates.py`：Act 1 / Act 2 逐房间 hazard、boss 条件死亡率、各 Act 1 boss 有偏条件率、`max_hp`。
- `eval_kind_by_floor.py`：遭遇类型 × 房间序号、各段位进度贡献。
- `train_episodes.py`：训练窗口的段位分布、hazard、条件死亡率、字段清单。

使用字段：summary 的 `seed, result, act, floor, final_hp, max_hp, death.{floor,act,room_type,enemies}, act_start_floor, act_rooms, progress, steps, turns, loop_breaks, cycles`；episodes 的 `update, result, act, floor, steps, progress, cycles`。
段位定义：k = 本 act 已进入房间数（见 §1.1）；“boss” = `k == act_rooms[act]`（eval）或 floor 17 / floor ≥ 33（训练）。

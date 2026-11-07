# 限定精英 / boss 战的 HP 叶子前瞻网格（M u3064，2026-11-05）

**结论：5 个配置全部低于贪心，没有一个同时满足"progress Δ ≥ 0"和"Act 2 boss 击杀多于贪心"。** Δ 在 −0.016 到 −0.037 之间；
只 boss 的三行（a、e）CI 不含 0（显著为负），精英 + boss 的 b、c CI 含 0，d（R = 1）显著为负。Act 2 boss 击杀：贪心 0，
b 与 c 各 1，其余 0；但所有配置进入 Act 2 boss 的局数都从 17 降到 11–12，Act 1 boss 通过数从 64 降到 48–56。
研究日志 19:15 的判断（"评测侧搜索暂停，等策略 / 价值头改善后再测"）**被数字确认**。

前序：`lookahead-on-m3064.md`（value+hp 叶子 Δ −0.079；turn/hp 叶子 Δ −0.025、CI 含 0、3 局打过 Act 2 boss），
`value-head-discrimination.md`（M 的价值头在战斗内区分度不足）。本网格只用 `hp` 叶子（不看价值头），加更多推演（R = 4、
K = 16），并用新门控 `--only-fights` 把搜索限定在精英 / boss 战，其余决策全部贪心。

## 1. 门控语义（branch `train/w9-lookahead-gate`，commit `7710411`）

- `--only-fights {all,elite-boss,boss}` → `LookaheadConfig.only_fights`；`FIGHT_GATES = {"all": None, "elite-boss": ("Elite", "Boss"), "boss": ("Boss",)}`。
- 战斗类型的判定：取观测里当前地图节点的类型，`map_nodes[map_row, map_col].point_type`（`MapPointType` 枚举；与
  `sts2vec.runstats` 给 summary 的 `room` 用的是同一来源）。观测在战斗中不带 encounter id，所以按"所在房间的节点类型"判定，
  不按敌人判定。`map_row / map_col` 越界 → 判为不在门内。
- 只有 `live & is_combat & cand_n ≥ 2 & 门内` 的决策进入搜索；门外的被计入 `stats.gated_out` 并走贪心。
- `all` 不改搜索字段，policy hash 与 label 与未加门控时一致；非 `all` 时 `only_fights` 进入 policy hash，label 形如
  `la(8,4,combat,boss,honest)`。
- 单测（同 commit）：门控只读当前节点（Elite / Boss / Monster / 越界四种）；`elite-boss` 下每个被搜索的局都在 Elite / Boss 节点；
  K = 1 + 门控 = 贪心历史。
- raven 上跑的代码 `src-gate/sts2train/lookahead.py` sha256 前 16 位 `b22ec4036f972f5d`，与 commit `7710411` 的文件一致，
  5 个 run 的 `stats.json.code.lookahead_py_sha256` 均为此值。
- 实测佐证：只 boss 的 a、e 两行在 Act 1 精英战上与贪心完全一致（160 场 / 13 败 / 平均掉血 23.0），107 局进入 Act 1 boss 的
  局在 boss 战前的每场战斗结果与血量都与贪心逐局一致（107/107）——即第一次 Act 1 boss 战是**同一状态上的配对比较**。

## 2. 结果（dev-128 `silent-a10-dev-v1`，配对对照贪心）

共同条件：检查点 `pinned-0003064.pt`（weights `3bd9933b400b8fb7`），`score = hp`（不使用价值头），seed 0，8 线程 CPU，
H = 40（combat）/ 12（turn）。贪心行复用前序 `runs/m3064-dev128/greedy`：**0.4042**，0 胜，平均层数 21.31，
Act 1 boss 进入 / 打过 107 / 64，Act 2 boss 进入 / 打过 17 / 0，128/128 可回放。

| 配置 | progress | Δ | t 95 % CI | bootstrap 95 % CI | 好 / 差 / 同 | 改选 / 搜索决策 | 胜 | Act 1 boss 打过（进入 107*） | Act 2 boss 进入 / 打过 | wall | ms / 搜索决策 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 贪心 | 0.4042 | — | — | — | — | — | 0 | 64 / 107 | 17 / 0 | 96 s | — |
| a：combat，K8 R4，只 boss | 0.3860 | −0.0182 | [−0.0358, −0.0005] | [−0.0356, −0.0010] | 14 / 30 / 84 | 879 / 4,607（19.1 %） | 0 | 56 / 107 | 12 / 0 | 1,251 s | 250.6 |
| b：combat，K8 R4，精英 + boss | 0.3882 | −0.0160 | [−0.0399, +0.0080] | [−0.0394, +0.0081] | 31 / 35 / 62 | 1,835 / 9,484（19.4 %） | 0 | 56 / 109 | 12 / **1** | 8,271 s † | 825.3 † |
| c：turn，K8 R4，精英 + boss | 0.3826 | −0.0216 | [−0.0465, +0.0033] | [−0.0457, +0.0029] | 27 / 46 / 55 | 1,530 / 9,890（15.5 %） | 0 | 52 / 102 | 12 / **1** | 349 s | 25.4 |
| d：combat，K8 R1，精英 + boss | 0.3676 | −0.0366 | [−0.0581, −0.0151] | [−0.0584, −0.0154] | 22 / 48 / 58 | 1,475 / 8,852（16.7 %） | 0 | 48 / 97 | 12 / 0 | 717 s | 73.4 |
| e：combat，K16 R4，只 boss | 0.3843 | −0.0199 | [−0.0364, −0.0034] | [−0.0366, −0.0041] | 12 / 29 / 87 | 892 / 4,635（19.2 %） | 0 | 55 / 107 | 11 / 0 | 1,416 s | 285.5 |
| 参照（前序）：turn，K8 R1，不门控 | 0.3793 | −0.0249 | [−0.0583, +0.0085] | [−0.0581, +0.0081] | 36 / 54 / 38 | 3,572 / 25,963（13.8 %） | 0 | 49 / 95 | 13 / 3 | 379 s | 11.3 |

\* 括号外为打过数；斜杠后为进入数（贪心与 a、e 都是 107，精英 + 门控的行因前面路线改变而不同）。
Act 1 / Act 2 boss 的"进入"取 summary 的 `boss_entry`，"打过" = 进入该幕 boss 且最终 `act` 更大（或胜局）。

每一行：128/128 可回放（replay audit：结果、幕、层数、状态 hash 全部复现），0 desync，0 rollout 错误，0 rebuild，
pool max 128（branch 路径）。全部 0 胜、128 loss。
† b 的单次前向耗时 3.2 ms，是 a 的 0.66 ms 的约 5 倍（模型时间 7,272 s 对 844 s，前向数 2.27M 对 1.27M）；它跑在
21:29–23:46 UTC，很可能与 raven 上别的负载重叠。b 的 wall 与 ms 不能当作成本参考；按 a 的单前向成本折算约 1,500 s。

门控的覆盖：只 boss 时搜索占 live 决策 11.3 %（a）/ 11.4 %（e），被门挡掉约 2.35 万个战斗决策；精英 + boss 时占 23–24 %。
a、e 各有 26 局历史与贪心完全一致（搜索没改选，或没到 boss）。

### 2.1 按房间（所有战斗汇总）

| | Act 1 精英：场 / 败 | Act 1 boss：场 / 败 | Act 2 精英：场 / 败 | Act 2 boss：场 / 败 |
| --- | --- | --- | --- | --- |
| 贪心 | 160 / 13 | 107 / 43 | 41 / 19 | 17 / 17 |
| a | 160 / 13（与贪心相同，未搜索） | 107 / 51 | 31 / 17 | 12 / 12 |
| b | 159 / 14 | 109 / 53 | 36 / 20 | 12 / 11 |
| c | 159 / 19 | 102 / 50 | 31 / 17 | 12 / 11 |
| d | 159 / 22 | 97 / 49 | 33 / 16 | 12 / 12 |
| e | 160 / 13（与贪心相同，未搜索） | 107 / 52 | 29 / 17 | 11 / 11 |

### 2.2 第一场 Act 1 boss 战的逐局配对（两边都进入的局）

| | 两边都进入 | 入场前逐局相同 | 只有贪心赢 | 只有搜索赢 | 都输 | 符号检验 p（双侧，精确） |
| --- | --- | --- | --- | --- | --- | --- |
| a | 107 | 107 | 15 | 7 | 36 | 0.134 |
| e | 107 | 107 | 14 | 5 | 38 | 0.064 |
| b | 100 | 28 | 16 | 7 | 32 | 0.093 |
| c | 94 | 22 | 19 | 6 | 27 | 0.015 |
| d | 91 | 23 | 20 | 5 | 25 | 0.004 |

a、e 是最干净的对照：同一入场状态、只在这场 boss 战里搜索，HP 叶子 + R = 4 推演让 Act 1 boss 胜场从 64 降到 56 / 55
（胜率 59.8 % → 52.3 % / 51.4 %）。单看 a 或 e 的不一致对差异未达 0.05，但方向在 5 个配置里一致为负，K 从 8 加到 16 没有改善。
赢下 Act 1 boss 的局，打赢时平均掉血略少（贪心 45.0，a 41.5，e 41.9），但多输的局抵消了这一点。

## 3. 命令

```bash
# raven，目录 /data/raven/hack-sts2/lookahead/m3064（容器配方同前序；run-gate.sh 只把 PYTHONPATH 换成 /w/src-gate）
#   run-gate.sh = nice -n 10 docker run --rm --cpus 8 --user 1001:1002 python:3.13，/venv（torch 2.15.0+cpu，只读），
#                 bundle sts2train-bd6d6f6863 的 lib/Sts2Sim.Vec.so 与 seedsets，OMP/MKL 8 线程
./grid-gate.sh > runs/m3064-dev128/grid-gate.log 2>&1   # 顺序执行，已有 stats.json 的跳过
#   grid-gate.sh -> queue-gate.sh，每个 spec 执行：
#   ./run-gate.sh -m sts2train.lookahead eval --seedset silent-a10-dev-v1 --checkpoint ckpt/pinned-0003064.pt \
#       --threads 8 --out runs/m3064-dev128/<name> <flags>
#   a gate-a-combat-hp-K8R4-boss   --horizon combat --score hp --K 8  --R 4 --only-fights boss
#   b gate-b-combat-hp-K8R4-eb     --horizon combat --score hp --K 8  --R 4 --only-fights elite-boss
#   c gate-c-turn-hp-K8R4-eb       --horizon turn   --score hp --K 8  --R 4 --only-fights elite-boss
#   d gate-d-combat-hp-K8R1-eb     --horizon combat --score hp --K 8  --R 1 --only-fights elite-boss
#   e gate-e-combat-hp-K16R4-boss  --horizon combat --score hp --K 16 --R 4 --only-fights boss

# 配对（本备忘时补算；10,000 次 bootstrap，seed 0；跑在 run.sh 容器里，秒级）
for n in gate-a-combat-hp-K8R4-boss gate-b-combat-hp-K8R4-eb gate-c-turn-hp-K8R4-eb \
         gate-d-combat-hp-K8R1-eb gate-e-combat-hp-K16R4-boss; do
  ./run.sh -m sts2train.lookahead paired runs/m3064-dev128/greedy runs/m3064-dev128/$n \
      --out runs/m3064-dev128/paired-$n.json
done
python3 -I tools/gate_sum.py runs/m3064-dev128 <上面 5 个名字> la-turn-hp   # 汇总行：progress、boss 进入/打过、统计
# 按房间与 Act 1 boss 逐局配对：一次性 stdlib 脚本（读 games/*.summary.json 的 fights / boss_entry / act），未入库
```

grid 时间线（UTC）：a 21:08–21:29，b 21:29–23:46，c 23:46–23:52，d 23:52–00:04，e 00:04–00:28（11-06）。

## 4. 结论

- **门槛：没有任何配置同时给出 Δ ≥ 0 和多于贪心的 Act 2 boss 击杀。** 5 个 Δ 全为负（−0.016 … −0.037）；Act 2 boss 击杀
  只有 b、c 各多 1 局（0 → 1），而这两行 progress 仍为负（CI 含 0），并且进入 Act 2 boss 的局数从 17 降到 12。1 局的差距在
  128 局上不构成证据。
- **限定在 boss 战也救不回来。** a / e 在同一入场状态上只改 boss 战决策，Act 1 boss 胜场 64 → 56 / 55；加大 K（8 → 16）
  没有帮助（−0.018 → −0.020）。这排除了"只在关键战斗里搜索就能避开价值头问题"的设想：HP 叶子 + 贪心 rollout 在 boss 战里
  本身就不如 M 的策略直接出牌。
- **更多推演有一点用，但不够。** 同为 combat / 精英 + boss，R = 4（b）比 R = 1（d）好 0.021（−0.016 对 −0.037），说明 R = 1
  的单次确定化噪声是一部分损失来源；但 R = 4 也只是把 Δ 拉到 0 附近的下方，不是正的。
- **对评测侧搜索的含义：确认研究日志 19:15 的判断——暂停。** 在 M u3064 上，三种叶子（value+hp、value、hp）、两种视野、
  三种门控、R ∈ {1, 4}、K ∈ {8, 16} 共 12 个配置，没有一个 Δ > 0，最好的点估计是 −0.016。成本上，combat 视野每次搜索
  250–290 ms（贪心整轮 96 s，a 1,251 s，约 13 倍）。在策略 / 评估器改善之前，继续调这套搜索的参数没有正期望；主线进度应来自
  训练（第三轮探索修复、第四轮 HP 塑形）。回头测的条件：新检查点的贪心明显更高，或有一个在战斗内有区分度的叶子评估器
  （见 `value-head-discrimination.md`），然后用同一 harness 与同样的配对重测。

## 5. 不能回答的问题

- **对更强的检查点是否仍然为负。** 只测了 M u3064；w3m-a / w3m-b（macros 检查点）因 lookahead 不透传 macros 无法公平配对，未测；
  当前 A / B 线（dev-128 约 0.44–0.45）未测。
- **Act 2 boss 的击杀能力。** 所有行进入 Act 2 boss 的只有 11–17 局，击杀 0–3 局，样本太小，无法区分 0 与 1 或 3。
- **boss 战里搜索为什么更差。** 未做逐决策 Q 分析（本网格没开 `--record`），不知道是 hp 叶子在 40 步视野内偏好保血而拖长战斗、
  还是 rollout 策略在 boss 战里本身弱；`outcomes` 只给汇总（a：defeat 37,377 / won 29,992 / horizon 23,531）。
- **held-out 表现。** 没跑 `silent-a10-test-v1`（dev 上已为负，未做选择，不需要验证）。
- **真实成本。** b 的耗时受 raven 共享负载影响，无法从现有日志确认当时的负载；其余行也有 ±20 % 的波动。
- **其他叶子 / 搜索形式。** 未测以 aux 头（`win_prob`、`combat_hp_pred`）为叶子、MCTS、或"只在 boss 首回合搜索"等变体；
  本网格不能说明它们的结果。

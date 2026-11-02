# STS2-Bench 评测协议 v0.2（模拟器整局赛道，已冻结）

2026-11-01 · 取代 `docs/sts2-bench-protocol-v0.1-2026-10-30.md` 中整局赛道（T3）的计分部分 · 游戏版本 pin v0.111.0 ·
实现：`sim/tools/eval`（`sts2eval`，`sts2eval/progress.py` 是本协议进度指标的唯一实现）。
冻结规则：本文件的定义只能通过新版本号修改（v0.2 → v0.3），每个公布的数字都带 `protocol_version`。

## 1. 范围

- **本协议覆盖**：在源码移植模拟器（`sim/`，Route C）上跑整局（full run），LLM 通过 HTTP 对局服务器
  （`sim/Sts2Sim.Server`，`sim/PLAY.md`），神经网络通过批量数值接口（`sim/Sts2Sim.Vec` + `sim/tools/vec`，`sim/VEC.md`）。
  用途：快速、可并行、可复现的开发评测与排行（sim 赛道）。
- **不覆盖**：正式成绩仍以真实引擎为准（用户规则：真实引擎是 oracle 与考场，模拟器成绩一律标注 "sim"）。
  决策考试（T1 Exam-500）与战斗切片（T2）保持 v0.1 草案状态，未冻结。
- 内容模式：`ported_only`（只抽已移植的遭遇、事件、卡、遗物、药水）。没移植的内容不会出现在 sim 赛道里；
  这是 sim 成绩与真实引擎成绩不能直接混排的原因之一。

## 2. 信息对等（agent 看到什么）

agent 只看到真实游戏玩家能看到的东西，不多不少（`sim/PLAY.md` "Hidden information"；数值接口按 `sim/CONTRACTS.md` §D3.3 的 H1–H15）。

**看得到**：每一步的观测文本（LLM）或观测张量（NN）包括：幕、总层数、回合；HP、格挡、能量、金币、星；能力（图标数字 +
玩家可见细节）、遗物、药水、充能球；完整牌组；手牌（费用、目标、关键词、数值、`effective` 预览、可否打出及原因）；
抽牌堆（**排序后**的列表）、弃牌堆、消耗堆；敌人（HP、格挡、能力、意图：类型、每击伤害、次数、塞牌数；`danger` 汇总行）；
当前阶段的屏幕（地图：可去的房间与其后续、**整张当幕地图**含 Boss；奖励、卡牌奖励、商店、营火、宝箱、事件、选择提示）；
合法动作全集（每个恰好一次）；规则文案（游戏本地化，按版本加载）。静态参考 `GET /catalog`（卡、遗物、敌人招式表，
相当于 wiki）允许查阅。

**看不到（隐藏清单）**：
1. 敌人下一招的身份（`intents[].move` / `move_name` 为 null；敌人回合日志只按效果描述招式）；
2. 抽牌顺序（抽牌堆只给排序列表或计数）；
3. 种子、RNG 流与计数器、概率与"抽袋"状态；
4. 未来的房间、事件、遭遇（只有当幕地图与当幕 Boss）；
5. 未开的宝箱遗物；奖励屏上卡牌奖励的具体卡（打开卡牌奖励后才显示）；
6. 能力的施加者与内核记账字段（施加者 id、回合初值、计时标记）；怪物/遭遇/事件内部状态；
7. 实例 id（敌人 `uid` 例外，玩家同样能跟踪）；存档档案、历史、已访问事件集合。

**揭示开关**：唯一的开关是 `reveal_moves`（创建对局时的 body 字段，或服务器 `--reveal-moves`），**默认关闭**。
`sts2eval` 总是显式发送该字段（`--reveal-moves` 默认 off），并写入 `config.json`、每局转录的 `start` 记录和报表头。
`reveal_moves = true` 的运行只用于调试与消融，**不得上榜**；`reveal_moves` 未记录（v0.2 前的服务器）的运行只作参考。

**工具边界**：每步一个无状态请求；系统提示固定（`sts2eval/prompt.py`，哈希入 `prompt_hash`）；提示中只有观测文本与
"Recent history"（最近 6 个动作、12 行日志），没有联网检索工具；不允许 best-of-n（要做须申报并单列）。

## 3. 官方种子集

| id | 文件 | 角色 | A | 局数 | sha256 |
| --- | --- | --- | --- | --- | --- |
| `std-a0-v1` | `sim/tools/eval/seedsets/a0-s20.json` | 官方 A0 | 0 | 20（5 角色 x 4） | `22bb774255260efb9fbab6dea0df41d26de9d3da49364f37f3fab88bf0d81e01` |
| `std-a10-v1` | `sim/tools/eval/seedsets/a10-s20.json` | 官方 A10 | 10 | 20（5 角色 x 4） | `f072b6dd171cc16da26a053b01a0ec87b7a28028542ca44cfd6108e68bcbee16` |
| `smoke-a0-v1` | `sim/tools/eval/seedsets/a0-s10.json` | 冒烟（流水线检查） | 0 | 10（5 角色 x 2，`std-a0-v1` 的子集） | `ef6f0ac92552406ad00cf9a9cb8c15d290a5e255cf1870ff6b458422ad570ebb` |

- 角色：IRONCLAD、SILENT、DEFECT、REGENT、NECROBINDER；内容 `ported_only`。
- 文件字段：`name`（旧名，仍可用）、`id`、`version`（1）、`role`、`protocol`（"0.2"）、`frozen`（2026-11-01）、`sha256`、`games`。
- **校验和**：`sha256` 取 `{"ascension","content","games":[{"character","seed"}...]}` 的紧凑 JSON（键排序、`separators=(",",":")`、
  games 保持文件顺序）。名字、id、版本不参与。`seedsets.load` 发现校验和不符直接报错；报表用同一校验和识别旧运行
  `config.json` 里嵌入的种子集（`seedset_frozen: true`）。
- **冻结规则**：已冻结的种子集永不修改；要换种子就发新 id（`std-a0-v2`），跨 id 成绩不混排。
- `sts2eval run --seedset std-a0-v1`（或 `a0-s20`）均可；上榜行必须用 `std-*`，冒烟集的行要标注。
- 样本量提醒：20 局时 0 胜的 Wilson 上界是 16.1%，进度均值的 95% 区间约 ±0.07（见 §7 实测），只够区分大差距；
  需要更细的比较时按 v0.1 的功效表加样，并用同种子配对比较。

## 4. 进度指标（v0.2 头牌）

### 4.1 为什么要按幕归一化

层数 `floor`（服务器的 `TotalFloor` = 已进入房间数）在不同局之间分母不一样：
- 每幕地图有 `rows 0..B`：第 0 行是幕首的古神房（Ancient），最后一行是 Boss。房间数 `L = B + 1` = 移植表的
  `BaseNumberOfRooms + 2`：第 1 幕（Overgrowth / Underdocks）15 + 2 = 17，第 2 幕（Hive）14 + 2 = 16，第 3 幕（Glory）13 + 2 = 15，
  合计 48（A0 通关 = 48 层）。
- A10 起（DoubleBoss）最后一幕多一个第二 Boss 行：第 3 幕 16，合计 49。
- GOLDEN_COMPASS 把当幕地图换成 16 个房间的金色路径（rows 0..17，`L = 18`），整局变成 50 或 51 层。

所以 `floor / 48` 会把"拿了金罗盘的局"算成超过 100%，也会让 A10 第二 Boss 的位置和 A0 不一致。v0.2 用每幕各占 1/3 的进度。

### 4.2 定义

对一局的最终状态（结果 `result`、幕 `a`、层 `floor`）：

- `L_a`：第 `a` 幕的房间数 = 该幕**最后一次看到的**地图的最大行号 + 1（观测 `map.nodes`，或 Map 阶段 `obs_text` 的幕地图网格；
  金罗盘重生成地图后取新图）。整局没见过该幕地图时用标准表 17 / 16 / 15（A10+ 第 3 幕 16），报表标 `*`。
- `rooms_reached` `k` = `floor` − 该幕第一个观测时的 `floor`（第 1 幕为 0，即 `k = floor`）。也就是本幕已进入的房间数；
  幕首地图还没出发时 `k = 0`。
- `rooms_cleared` `c` = 胜利时 `L_a`，否则 `max(0, k − 1)`：**结束时所在的房间不算通过**（死在 Boss 房 ≠ 打过 Boss）。
- **progress** = 胜利时 1.0；否则 `((a − 1) + min(c, L_a − 1) / L_a) / 3`，取值 [0, 1)。

性质：整局单调不减，每通过一个房间升一格（标准 A0 局 48 格）；第 1 幕首房 0；第 1 幕 Boss 战死 16/17/3 = 0.314；
第 2 幕开局 1/3；A0 最终 Boss 战死 (2 + 14/15)/3 = 0.978；A10 第一个终局 Boss 战死 (2 + 14/16)/3 = 0.958，第二个 (2 + 15/16)/3 = 0.979；
金罗盘第 2 幕 Boss 战死 (1 + 17/18)/3 = 0.648。所有 `capped` / `stuck` / `crashed` / `error` 局按其最后观测计分（截断不加分；
停在地图屏的截断局少算 1 个房间，算作对截断的轻微惩罚）。`aborted`（整次运行被预算/时间/致命 API 错误中止）的局不完整：
上榜要求 `games summarised = n/n` 且没有 `aborted`。

### 4.3 头牌与副指标

- **头牌**：`progress` 均值 ∈ [0, 1]（报 95% 正态近似区间：均值 ± 1.96·s/√n，截到 [0, 1]）与**胜率**（Wilson 95%）。
  排序先看进度均值（当前所有 LLM 胜率都是 0，进度是唯一能分开的数），胜率并列显示。A0 与 A10 各一榜。
- **副指标（必报）**：幕分布（`act reached`）、旧口径 `floor / 48`（A10 为 `/ 49`）、层数均值/中位/最大、步数（均值、总数）、
  非法动作/局（两次回复都不合法）、被拒回复与重问次数、`stuck` / `capped` / `crashed` 局数、循环提示数、API 错误 / 拒答 / 超时 /
  max_tokens 截断、随机兜底次数、tokens（input / cache write / cache read / output）、成本（总额、每局）、每局分钟数、
  可重放历史数（应为 n/n）。
- 实现：每局 `games/<KEY>.summary.json` 写 `progress`、`rooms_reached`、`rooms_cleared`、`act_rooms_current`、`act_rooms_source`
  （`map` / `default` / `win`）、`act_start_floor`、`act_rooms`、`final_phase`、`protocol_version`；旧的 `progress_pct`（floor / 48）保留。
  每个 Map 阶段的 step 记录多了 `map_row`、`act_rooms`。`report.json` 头牌加 `progress_mean`、`progress_ci95`、`progress_median`、
  `progress_games`、`act_rooms_default`、`capped_games`。
- **重算旧运行**：`uv run --project sim/tools/eval sts2eval report DIR` 总是重算。v0.2 runner 写的 summary 用自身的每幕字段；
  更早的运行从同目录转录 `games/*.jsonl` 的 `act`/`floor` 与 Map 观测里的地图网格重建（已核对：40 局 v0.2 运行里两条路径结果完全一致）。

## 5. 运行身份（一行成绩可比的前提）

`config.json` / `report.json` 记录：

| 字段 | 含义 |
| --- | --- |
| `protocol_version` | 写运行的 runner 的协议版本（`config.json`；v0.2 前的运行为空，报表标 "recomputed"）；`report.json` 另记计分用的版本 |
| `seedset_id`、`seedset_version`、`seedset_sha256`、`seedset_frozen` | 种子集身份（§3） |
| `server_version` | 服务器 `/health` 的 git sha = 观测编码器（obs text / 合法动作）版本 |
| `repo_sha` | 评测器所在仓库的 git sha（`-dirty` 表示有未提交改动，不得上榜） |
| `prompt_hash` | 系统提示的哈希（编码给 LLM 的那一半） |
| `model`、`effort`、`thinking`（及实际发送的 `thinking_param`）、`max_tokens` | 策略配置 |
| `reveal_moves` | 揭示开关（必须 false） |
| `limits` | `step_cap` 1200、`floor_step_cap` 80、`loop_repeat` 6、`game_minutes` 60、`step_timeout_s` 300（v0.2 标准值） |
| `sts2eval_version`、`prices`、`concurrency` | 计量与成本口径 |

可比规则：同榜的两行必须 `protocol_version`（计分）、`seedset_sha256`、`reveal_moves = false`、`limits` 相同；`server_version` 或
`prompt_hash` 不同时须注明，并以差分闸与对局测试说明规则等价，否则分开列。NN 行用 `sts2vec` 版本 + 模型权重哈希代替 `prompt_hash`。

## 6. 转录审计要求

每局一个 `games/<CHARACTER>-<SEED>.jsonl`（`start` / 每步 `step` / `end`），每步含观测全文、合法动作、发送的 token、兜底原因、
每次尝试的原始回复、stop_reason、usage、延迟、成本；summary 含服务器规范 token 列表 `history_tokens`。上榜前：

1. `replayable` = n/n（token 数 = 接受的步数）；
2. 抽样至少 2 局（决赛圈全量）用 `POST /runs`（同 seed、角色、ascension、content、`reveal_moves: false`）+ `history_tokens` 重放，
   结束层、幕、结果必须一致（模拟器确定性）；
3. 所有 `start` 记录 `reveal_moves: false`；`config.json` 里 `repo_sha` 不带 `-dirty`；
4. 报告随机兜底、非法动作、API 错误、`stuck`/`capped` 的局数，任何一项异常高（例如兜底 > 1% 的步）需在榜注说明；
5. 转录与 summary 保存在 `tmp/eval/<run>/`（git-ignored，不提交），上榜行注明目录名；
6. 正式（真实引擎）赛道另按 v0.1 的认证：动作流在真实引擎逐决策重放、状态一致才计入。

## 7. 如何加一个策略

**LLM（`sts2eval`）**：在 `sts2eval/policies.py` 加一个类，接口同 `RandomPolicy` / `ClaudePolicy`：属性 `name`，
`for_game(seed) -> policy`（每局一个实例，确定性 rng），`async choose(obs_text, legal_tokens, recent_history, feedback) -> (候选文本, meta)`
（`meta` 用 `policies.empty_meta()` 起手，填 `usage`、`cost_usd`、`stop_reason`、`raw`、`error`）；在 `cli.py` 的 `--policy` 里注册。
runner 负责合法性解析、一次重问、随机兜底、循环守卫、转录、计分；策略只管回答。跑法：

```bash
uv run --project sim/tools/eval sts2eval run --seedset std-a10-v1 --policy claude --model claude-sonnet-5-5 --effort low \
    --max-tokens 2048 --concurrency 10 --spawn --max-cost-usd 60 --api-key-file ~/.config/sts2eval/anthropic_api_key
```

**NN（`sts2vec`）**：用 `env.pool.add_fresh(character, ascension, seed, content="ported_only")` 按种子集逐局建入口，每个入口跑一局到终局
（`auto_reset` 后读 `last_episode`：win、floor、act、steps……）。逐步记录 `global_i` 的 `act_index + 1`、`total_floor`，以及
`map_nodes` 的最大行号 + 1 作为该幕 `L`，然后调用 `sts2eval.progress.compute(result, act, floor, ascension, act_start_floor, act_rooms)`
计分，并按 summary 的字段写 `games/<KEY>.summary.json` + `config.json`（`policy` = 模型名，`seedset` = 种子集原文），
`sts2eval report DIR` 即可出同口径报表。专用的 NN 评测入口（`sts2vec eval --seedset`）是未完成项。

## 8. 排行榜（sim 赛道，v0.2 口径）

所有行：`ported_only`，`reveal_moves` false（第 4 行除外），limits 为 §5 标准值。Sonnet 行：effort low，thinking adaptive，
max_tokens 2048，prompt `e01daedf52519a6c`，服务器 `c64217d`。随机行：`--policy random --spawn`，服务器 `b3ac9d6`
（sim 代码与本提交相同），评测器为本提交的 v0.2 runner；随机策略按 `CHARACTER:SEED` 播种，可在本提交上原样复跑。

| # | 策略 | 种子集 | progress [95%] | 胜率 [Wilson 95%] | floor/48 或 /49 | 幕分布 | 步数/局 | $/局 | 分钟/局 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | Claude Sonnet 5.5 | `std-a10-v1` (A10, 20) | **0.310** [0.243, 0.377] | 0/20, 0.0% [0.0, 16.1] | 34.0% | 1: 14, 2: 6 | 246.2 | 1.13 | 8.83 |
| 2 | uniform random | `std-a10-v1` (A10, 20) | 0.051 [0.040, 0.062] | 0/20, 0.0% [0.0, 16.1] | 7.3% | 1: 20 | 40.5 | 0 | 0.01 |
| 3 | Claude Sonnet 5.5 | `smoke-a0-v1` (A0, 10，冒烟集) | **0.594** [0.444, 0.744] | 0/10, 0.0% [0.0, 27.8] | 63.1% | 1: 2, 2: 6, 3: 2 | 427.0 | 2.13 | 15.67 |
| 4 | Claude Sonnet 5.5（对等修复前，仅参考） | `smoke-a0-v1` (A0, 10) | (0.581 [0.440, 0.723]) | 0/10 | 61.9% | 1: 1, 2: 7, 3: 2 | 527.1 | 2.53 | 18.29 |
| 5 | uniform random | `std-a0-v1` (A0, 20) | 0.065 [0.052, 0.078] | 0/20, 0.0% [0.0, 16.1] | 9.0% | 1: 20 | 57.1 | 0 | 0.01 |

注：
- 第 4 行来自服务器 `fb9d38c`：意图暴露了敌人下一招（`move`/`move_name`），没有循环守卫，1 局 `capped`（奖励屏循环 700 步），
  `reveal_moves` 未记录——不上榜，只说明修复前后 A0 冒烟进度没有显著差别（0.581 → 0.594，同种子）。
- Sonnet 的 A0 行目前只有冒烟集；`std-a0-v1`（20 局，约 $43）尚未跑，是下一条该补的行。
- 人类参考线（真实引擎，非 sim）：录制者本人 A10 胜率约 11%，社区 A10 均值约 17%（v0.1）。

### 8.1 重算后的头牌表（`sts2eval report`，v0.2 指标）

运行目录（git-ignored，原件在 `.claude/worktrees/sts2-eval-pipeline-a3aee7/tmp/eval/`，重算在副本上做）。

`baseline-sonnet-a10-s20`（`std-a10-v1`，服务器 `c64217d`，墙钟 26.59 分钟，20/20）：

| metric | value |
| --- | --- |
| games | 20 |
| **progress v0.2** (mean [95% CI], median) | **0.310** [0.243, 0.377], 0.314 |
| **win rate** (Wilson 95%) | **0.0%** [0.0%, 16.1%] |
| results | loss 20 |
| floor mean / median / max | 16.6 / 17.0 / 33 |
| floor / 49 (v0.1 progress, secondary) | 34.0% |
| act reached | act 1: 14, act 2: 6 |
| steps mean (total) | 246.2 (4925) |
| illegal actions per game (replies rejected, re-asks) | 0.00 (10, 10) |
| crashes / stuck / capped | 0 / 0 / 0 |
| api errors / refusals / timeouts / max_tokens stops | 0 / 0 / 0 / 1 |
| random fallbacks | 0 |
| api calls | 4935 |
| tokens input / cache write / cache read / output | 6889022 / 22050 / 10859625 / 658963 |
| cost USD (per game) | $22.5947 ($1.1297) |
| minutes per game (mean) | 8.83 |
| replayable histories | 20/20 |

按角色 progress：IRONCLAD 0.422、DEFECT 0.363、REGENT 0.282、SILENT 0.265、NECROBINDER 0.217。

`baseline-sonnet-a0-s10-v2`（`smoke-a0-v1`，服务器 `c64217d`，墙钟 28.41 分钟，10/10）：

| metric | value |
| --- | --- |
| games | 10 |
| **progress v0.2** (mean [95% CI], median) | **0.594** [0.444, 0.744], 0.583 |
| **win rate** (Wilson 95%) | **0.0%** [0.0%, 27.8%] |
| results | loss 10 |
| floor mean / median / max | 30.3 / 30.0 / 48 |
| floor / 48 (v0.1 progress, secondary) | 63.1% |
| act reached | act 1: 2, act 2: 6, act 3: 2 |
| steps mean (total) | 427.0 (4270) |
| illegal actions per game (replies rejected, re-asks) | 0.00 (14, 14) |
| crashes / stuck / capped | 0 / 0 / 0 |
| api errors / refusals / timeouts / max_tokens stops | 0 / 0 / 0 / 0 |
| random fallbacks | 0 |
| api calls | 4284 |
| tokens input / cache write / cache read / output | 6669456 / 22050 / 9424170 / 598830 |
| cost USD (per game) | $21.2672 ($2.1267) |
| minutes per game (mean) | 15.67 |
| replayable histories | 10/10 |

按角色 progress：SILENT 0.728、IRONCLAD 0.646、DEFECT 0.646、REGENT 0.583、NECROBINDER 0.367。两局死在第 3 幕最终 Boss（QUEEN、AEONGLASS，0.978）。

`baseline-sonnet-a0-s10`（对等修复前，服务器 `fb9d38c`，墙钟 33.28 分钟，10/10，仅参考）：

| metric | value |
| --- | --- |
| games | 10 |
| **progress v0.2** (mean [95% CI], median) | **0.581** [0.440, 0.723], 0.521 |
| **win rate** (Wilson 95%) | **0.0%** [0.0%, 27.8%] |
| results | loss 9, capped 1 |
| floor mean / median / max | 29.7 / 27.0 / 48 |
| floor / 48 (v0.1 progress, secondary) | 61.9% |
| act reached | act 1: 1, act 2: 7, act 3: 2 |
| steps mean (total) | 527.1 (5271) |
| illegal actions per game (replies rejected, re-asks) | 0.10 (14, 13) |
| crashes / stuck / capped | 0 / 0 / 1 |
| api errors / refusals / timeouts / max_tokens stops | 0 / 0 / 0 / 0 |
| random fallbacks | 1 |
| api calls | 5284 |
| tokens input / cache write / cache read / output | 8376352 / 0 / 10826916 / 633859 |
| cost USD (per game) | $25.2567 ($2.5257) |
| minutes per game (mean) | 18.29 |
| replayable histories | 10/10 |

三个目录的每局地图都在转录里，`act_rooms_source` 全部为 `map`（没有用标准表兜底的局）；没有一局拿到 GOLDEN_COMPASS，
也没有一局进入 A10 第 3 幕，第二 Boss 与金罗盘的计分只由单元测试和移植源码（`StandardActMap`、`GoldenPathActMap`）覆盖。

## 9. 相对 v0.1 的变更

1. 范围拆分：sim 整局赛道冻结为 v0.2；真实引擎正式赛道、Exam、战斗切片仍为 v0.1 草案。
2. 头牌从"A10-IC-50 通关率"改为 **进度均值（按幕归一化，[0, 1]）+ 胜率（Wilson）**，A0 与 A10 各一榜，五角色都计。
   理由：现阶段所有 LLM 胜率为 0，只有进度能区分；`floor / 48` 无法处理 A10 第二 Boss 与 GOLDEN_COMPASS。
3. 种子集冻结：`std-a0-v1`、`std-a10-v1`（各 20 局）+ 冒烟集 `smoke-a0-v1`，带 `version` 与 `sha256`，加载时校验。
   v0.1 的 "dev 10 + test 50 保密种子" 留给真实引擎赛道。
4. 信息对等写成条文：隐藏清单、`reveal_moves` 默认关、上榜要求 false。
5. 运行身份字段（§5）与可比规则；`config.json` / `report.json` 加 `protocol_version` 与种子集身份。
6. 转录审计要求（§6）；随机兜底、非法、`stuck`/`capped` 单列。
7. `sts2eval report` 用新指标重算旧运行；旧 `floor / 48` 保留为副列。

## 10. 未完成项

- Sonnet `std-a0-v1` 全量 20 局；其他模型（Haiku、Opus、Fable）与 effort 档位。
- 专用 NN 评测入口（`sts2vec` 按种子集跑并写 summary）。
- 重放审计脚本（§6 第 2 条目前手工：`POST /runs` + `history_tokens`）。
- 服务器 `c64217d` → `b3ac9d6` 之间的观测等价性声明（`8c65874` 改了能力显示数值的调用路径），之后新跑的行用 `b3ac9d6` 或更新的 sha。
- 第二 Boss 与金罗盘的计分在真实转录上的验证（需要一局进入 A10 第 3 幕或拿到金罗盘）。

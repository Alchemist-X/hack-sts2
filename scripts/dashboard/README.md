# scripts/dashboard — Silent A10 训练监控与复盘页

线上页面是 claude.ai 上的一个 **Dashboard artifact**（私有，由所有者分享给协作者；页面不在仓库里托管）。本目录是页面源码、
生成 9 个 dataset 的脚本、刷新脚本和校验脚本。研究循环里每 30 分钟刷新一次。

## 页面（`index.html`，v2）

五个 tab，全部只读下面契约里的 dataset，页面里没有写死的读数：

| tab | 内容 | dataset |
| --- | --- | --- |
| 总览 | 当前阶段与结论、指标卡（当前 update、近期新开局进度、跨线最佳 ab-512 / dev-128、ab-512 段终到第三幕最多局数、UI 回访环、本批算力与每小时消耗）、阶段计划表、可勾选线的新开局进度曲线、评测表（按线 / 种子集 / 只看段终筛选）、事件时间线（可按线过滤） | status, plan, metrics, evals, timeline |
| 实验台账 | 谱系图（每条线已跑区间、计划未跑部分、分叉连线，按裁决着色）、台账表（假设、分叉、旗标、对照、段终最佳、配对、裁决、算力）；点行进入单线复盘 | lines |
| 单线复盘 | 一条线的头卡（假设、分叉、旗标、对照、文档）、本线与对照线的评测曲线（带 CI，段终大圆点，dev-128 / ab-512 切换）、段终读数表、配对比较表、训练侧小图、事件经历、裁决与教训 | lines, line_events, evals, metrics |
| 吞吐与过程 | 每条线的吞吐表（update/小时、steps/s、各阶段耗时、显存峰值、每千 update 计算单位、compile 模式）、每小时 update 柱图、steps/s 曲线、耗时拆分堆叠图、12 张中间过程小图（熵、KL、解释方差、clipfrac、梯度范数、harvest、存档开局、回访环、到第三幕占比等，对照线虚线叠加） | throughput, metrics |
| 架构 | A10 训练流水线图、全景图（真实引擎 / 离线工具 / Sts2Sim）、run 层循环图、系统固定事实表 | system |

运行时约定（Dashboard 类型）：开头 `<meta name="dash-one-writer">`，一个 `<style>`（全部挂在 `.sa10` 下）、一个
`<script>`；绑定用 `data-source` / `data-field` / `data-row` / `data-row-key`，绘图用 `dash.draw(name, fn)` 对应
`[data-draw=name]`，取数 `dash.data(id) → {status, data}`，`dash.onData`、`dash.colors`、全局 d3 v7、CSS token
`var(--color-fg)` 等；不联网、不用 innerHTML 写数据、不用 storage（tab 与筛选状态放 `window.__sa10dash`）。
页面是 `#dash-root` 里的片段，须小于 256 KB（`check_page.py` 卡 200 KB）。

## 数据集契约（dashboard v2）

每个 dataset 是平铺行对象的 JSON 数组，浮点保留 4 位小数；给用户看的文字是中文，id / key 为 ASCII。
页面只读这些 id 和字段，`dash_data.py` 只写这些字段（值可以是 null）。

| id | 行 | 字段 |
| --- | --- | --- |
| `status` | 1 行 | updated_at, stage, stage_label, takeaway, run, gpu_session, update, env_steps_m, progress_fresh_recent, entropy, approx_kl, sps, cycles_per_update_recent, cycle_games_frac_recent, units_used, units_cap, balance, next_report, rows_metrics, rows_evals, lines_running（逗号分隔线 id）, best_ab512, best_ab512_line, best_ab512_update, best_dev128, best_dev128_line, best_dev128_update, act3_best, units_rate_per_h |
| `plan` | 按 stage | stage, label, status（running / pending / done / failed）, target_updates, started, eta, note |
| `timeline` | 新的在前 | time（EST "YYYY-MM-DD HH:MM"）, kind（ok / info / bad）, event, lines（逗号分隔，可空） |
| `metrics` | 按 run, update（抽稀） | run（= 线 id）, update, env_steps, time (ISO), progress_fresh, progress_all, entropy, approx_kl, sps, episodes, t_env_s, t_infer_s, t_update_s, t_total_s, gpu_mem_peak_mb, harvest_live, harvest_start_share, save_episodes, save_wins, save_progress_gain_mean, boss2_kills, boss_bonus_sum, explained_variance, clipfrac, grad_norm, cycles_total, cycle_games_frac, act3_frac, fresh_win_rate |
| `evals` | 按 run, update, seedset, mode | run, update, seedset（dev-128 / ab-512 / blind-256 / test-100 / other）, seedset_id, mode（greedy_off / greedy_on / sample_off / sample_on）, progress_mean, ci_lo, ci_hi, games, wins, clear_rate, act3, stuck, interventions, cycles, floor_mean, weights_hash, endpoint, time |
| `lines` | 按 id（实验台账） | id, run, title, hypothesis, fork_line, fork_update, flags, control, gpu_session, started, ended, status（running / done / stopped / planned / lost）, updates_from, updates_to, updates_done, gpu_hours, units_est, endpoints（JSON 字符串）, best_ab512, best_ab512_update, best_dev128, best_dev128_update, paired（JSON 字符串）, verdict（adopted / neutral / rejected / stopped / pending）, stop_reason, lessons, docs（逗号分隔仓库路径）, order |
| `line_events` | 按 line, time, event | line, time（EST "YYYY-MM-DD HH:MM"）, kind（start / segment / eval / pair / decision / incident / stop / note）, event, update, value |
| `throughput` | 按 run | run, gpu_session, hardware, updates, hours, upd_per_hour, sps_mean, t_env_mean, t_infer_mean, t_update_mean, t_total_mean, gpu_peak_mb, units_per_1000_updates, compile（eager / compiled / mixed / unknown）, envs, rollout_steps, note |
| `system` | 按 key | key, value, note（架构页上的数字只来自这里） |

`lines.endpoints` = `[{update, ab512, ab512_lo, ab512_hi, dev128, act3_ab512}]`（只含段终评测），`lines.paired` =
`[{update, vs, seedset, delta, ci_lo, ci_hi, conclusion}]`，都是 JSON 字符串，页面自己 `JSON.parse`。

## 一条命令刷新

```bash
scripts/dashboard/dash_refresh.sh <ops 目录>        # 或 STS2_OPS_DIR=<ops 目录> scripts/dashboard/dash_refresh.sh
python3 -I scripts/dashboard/check_page.py scripts/dashboard/index.html <ops 目录>/out     # 发布前校验
```

`dash_refresh.sh` 依次：

1. 读 Colab 余额与消耗速率（`colab --auth=adc usage` 的 `Current balance` / `Usage rate`），追加到
   `<ops>/balance_log.jsonl`；读不到时余额记 `-`。
2. 增量拉取桶里新出现的段终评测报告（`runs/<run>/evals/endpoint/<name>/{report,repeats}.json`）到
   `<ops>/endpoints/<run>/<name>/`（已缓存的跳过；`DASH_NO_PULL=1` 跳过）。ab-512 读数只在这些报告里。
3. 运行 `dash_data.py`（台账、线事件、系统文件默认取仓库里的 `docs/results/lines-silent-a10.json`、
   `docs/results/line-events-silent-a10.jsonl`、`scripts/dashboard/system-silent-a10.json`）。
4. 列出 `out/` 里 9 个文件和字节数。

也可以直接调生成脚本；不给 `--endpoints` / `--balance-log` / `--results` 时，默认取 state 所在目录（或
`$STS2_OPS_DIR`）下的 `endpoints/`、`balance_log.jsonl`，以及仓库里最新的 `docs/results/evals-silent-a10-*.json`：

```bash
python3 -I scripts/dashboard/dash_data.py <ops>/dash_state.json <ops>/dash_events.jsonl <ops>/out <余额> \
  --lines docs/results/lines-silent-a10.json --line-events docs/results/line-events-silent-a10.jsonl \
  --system scripts/dashboard/system-silent-a10.json \
  [--mirror <镜像>] [--endpoints <ops>/endpoints] [--results <json>] [--balance-log <jsonl>] [--units-rate <速率>]
```

路径都可用环境变量覆盖：`DASH_STATE`、`DASH_EVENTS`、`DASH_OUT`、`DASH_MIRROR`（默认 `<ops>/mirror`，没有时
`<ops>/../mirror`）、`DASH_ENDPOINTS`、`DASH_LINES`、`DASH_LINE_EVENTS`、`DASH_SYSTEM`、`DASH_RESULTS`、
`DASH_BALANCE_LOG`（默认 `<ops>/balance_log.jsonl`；测试时指到别处，免得往真日志里追加）。

### `check_page.py` 查什么（任何一项失败退出码为 1）

- markup 里每个 `data-source` 是已生成的 dataset，每个 `data-field` / `data-row-key`（按最近的 `data-source` 祖先）
  在该 dataset 的行里存在；
- 脚本读的每个 dataset id 都已生成，契约里的每个 dataset 页面都读；`field(...)` 读的 status 字段、曲线与小图用的
  metrics 字段都在输出里；
- 契约列出的每个字段在每一行都存在（允许 null），枚举字段取值合法；
- `line_events.line`、`lines.fork_line` / `control`、`paired[].vs`、status 里的线 id 都是 `lines.id`；
- 页面 < 200 KB、每个 out 文件 < 3 MB；
- 一个 `<style>`、一个 `<script>`、开头是 dash-one-writer meta，没有 innerHTML / fetch / storage；
  `<script>` 过 `node --check`（有 node 时）。

## 发布（编排器执行，不在刷新脚本里）

1. 把 `out/*.json` 每个作为资产上传到 Dashboard artifact（Artifact publish，`url` = 该 artifact，`asset: true`，
   `file_paths` 一次最多 25 个）；结果给出每个文件的 `/_blob/...` url。
2. 逐个更新 `datasets/<id>` 文档（`status`、`plan`、`timeline`、`metrics`、`evals`、`lines`、`line_events`、
   `throughput`、`system`），把数据源指向第 1 步的新 url；v2 新增的 `lines`、`line_events`、`throughput`、`system`
   第一次发布时要新建对应的 dataset 文档。
3. 页面源码有改动时，更新 `files/index.html` 文档的文本为本目录的 `index.html`。
4. 该类型拒收超过 256 KB 的文档：数据只能以资产 url 引用，不能把 JSON 内联进 dataset 文档；页面本身也须小于 256 KB。
5. 旧资产不再被引用后可以删除（只删自己上传、已无引用的资产）。

## 输入

| 文件 | 谁维护 | 内容 |
| --- | --- | --- |
| `dash_state.json`（ops 目录） | 手工 | 阶段、一句话结论、plan 表、`units_cap`、`balance_at_batch_start`、可选 `topups`（`[{time, units}]` 或数字）、`runs = [{name: 线 id, dir: 镜像目录}]` |
| `dash_events.jsonl`（ops 目录） | 追加写 | 时间线事件 `{time (EST), kind: ok/info/bad, event, 可选 lines}`；事件文字里的"余额 X"也被当作余额读数 |
| `docs/results/lines-silent-a10.json` | 手工（台账） | 每条线一行，字段同下面的 `lines` 契约；文字字段原样输出 |
| `docs/results/line-events-silent-a10.jsonl` | 追加写 | 每条线的经历 `{line, time, kind, event, update, value}` |
| `scripts/dashboard/system-silent-a10.json` | 手工 | 架构页的固定事实 `[{key, value, note}]`；架构页上的数字只来自这里 |
| `mirror/<run>/` | `mirror_loop.sh` | `metrics.jsonl`、`evals.jsonl`、`status.json`、`config.json`、`train.log` |
| `endpoints/<run>/<name>/` | `dash_refresh.sh` | 段终报告（ab-512 / dev-128 采样 ×3） |

## 生成规则（台账永不在数字上过期）

- **线 ↔ 镜像目录**：台账行的 `run`（如 `w10-s`）加上 state 里同名的 `runs`。几条线共用一个目录时（C 与 C2 都在
  `w4-c`），每条线取 `updates_from` 之后、下一条线 `updates_from` 为止的 update；评测与段终报告同样切分。
- **台账数字由镜像重算**：`updates_done` = 最后一个 update − `updates_from`；`gpu_hours` = 指标时间戳的活跃时长
  （相邻两行间隔 ≤ 20 分钟计入，更长的间隔视为运行时丢失 / 重启，只计该行自身的 `t_total_s`）；
  `units_est` = `gpu_hours` × 6.73；`endpoints` = 段终更新点上的 ab-512（贪心关 breaker，含 CI 与到 act 3 局数）与
  dev-128 读数，文件里有数时覆盖手填值，手填而文件没有的保留；`best_*` 取该线全部 ab-512 / dev-128 贪心关评测的最高点；
  `started` / `ended` 为空时取首末指标时间。`paired`、`verdict`、`lessons` 等文字原样输出。台账没有或不合法的 `status` 由 `status.json` 推断（写着 running 但最后一行指标已超过 1 小时记 `stopped`），
  不合法的 `verdict` 记 `pending`（停掉的线记 `stopped`）。
- **evals**：种子集映射 `silent-a10-dev-v1 → dev-128`、`silent-a10-ab-v1 → ab-512`、`*-blind-* → blind-256`、
  `*-test-* → test-100`、其余（20 局的 `silent-a10-v1` 训练中评测）→ `other`；模式 `greedy-off → greedy_off` 等，
  无 mode 字段的按 policy / breaker 推断；`act3` 来自 `act_reached`（字典 `{"3": n}` 或按幕计数的列表）。
  `endpoint` 为真：目录以 `evals/endpoint` 开头、来自段终报告、或 update 是该线的段终点（台账 `endpoints` 的 update、
  `updates_to`、有段终报告的 update）。同一 (线, update, 种子集, 模式) 多来源时优先级：镜像 > 段终报告 > results JSON >
  台账手填（缺的字段互相补）。台账有、文件还没有的段终读数也会生成一行，保证 ab-512 不丢。
- **line_events**：台账事件原样（时间统一成 EST `YYYY-MM-DD HH:MM`，未知 kind 记 `note`）；文件里有、台账事件
  还没覆盖的段终读数自动补一条 `segment` 事件（文字末尾标"（自动）"）。
- **timeline**：事件没有 `lines` 时，从事件文字里按线 id 自动识别（"GPU A" 之类不算；`E′`/`F′` 记 E/F，
  `W0′`/`C0′`/`C1′` 记重跑线）。
- **metrics 抽稀**：契约是"每条线最后 300 个 update 全保留、之前每 5 行取 1"；超过 3 MB（`--max-metrics-bytes`）时
  逐级加大：之前每 10 行 → 已结束的线只全保留最后 100 个 → 每 20 / 40 / 80 行。11-06 的 20 条线 21.8k 行在第三级
  （运行中 300 / 已结束 100 / 每 10 行）落到约 2.5 MB；按契约原样是约 5.0 MB。
- **throughput**：每条线的 update 数（首末 update 之差）、活跃小时、update/小时、各阶段耗时均值、显存峰值、
  每千 update 的计算单位；`compile` 来自 `config.json` 各会话（`off` = eager，其余 = compiled，两者都有 = mixed），
  没有时看 `train.log` 的 device 行，再没有记 `unknown`。
- **status 新增**：`lines_running`（台账 status 为 running 的线）、跨线最佳 ab-512 / dev-128（贪心关）、`act3_best`
  （ab-512 段终的到 act 3 局数最大值）、`units_rate_per_h`（`--units-rate`；没有时用 state 的
  `units_rate_per_h`；再没有用 balance log 里 1 小时内最新一条的 `rate`；再没有用近 3 小时的余额读数；再没有按运行中的
  GPU 会话数 × 6.73）。余额参数比最新的余额读数（balance log 或事件里的"余额 X"）低得超过 3 台 GPU 此后可能烧掉的量
  （+2 单位）时视为过期（例如充值前抄下的数），打印警告并改用最新读数。
  `units_used` = `balance_at_batch_start` + 充值 − 余额；充值优先取 state 的 `topups`，没写时把余额读数里的正跳变
  （> 1 单位）当充值（会少算跳变前后那段时间的消耗，误差为几个单位）。
- 所有浮点数保留 4 位小数；缺字段、坏行、坏文件一律跳过，不让整次刷新失败。

## 文件

- `index.html` — 页面源码（v2）。
- `dash_data.py` — 生成脚本（stdlib）。
- `dash_refresh.sh` — 一次刷新：余额 → 段终报告增量拉取 → 生成 → 列出文件（不上传）。
- `check_page.py` — 页面与 out 文件的交叉校验。
- `system-silent-a10.json` — 架构页的固定事实（system dataset 的来源）。
- `dash_state.example.json` — 2026-11-05 23:41 EST 的状态快照，作为 `dash_state.json` 的格式示例。
- 实验台账与线事件在 `docs/results/`（见那里的 README）。真实的 `dash_state.json`、`dash_events.jsonl`、镜像、
  `endpoints/` 和 `out/` 不进仓库。

`dash_state.json` 里 `runs` 的 `dir` 写相对路径 `mirror/<run>`（相对于 `$STS2_OPS_DIR`，未设置时相对于
`dash_state.json` 所在目录）或绝对路径。一个线 id 可以对应多个目录（例：B 的 `w3m-b-hist` + `w3m-b`，桶里被覆盖的
那段历史另存一份）。

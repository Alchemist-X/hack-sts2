# scripts/colab/ops — Colab A100 训练线的运维脚本

Silent A10 主线在两张 Colab A100 运行时上跑（GPU A、GPU B），每张卡上一条"线链"：一个线驱动跑完写 `<tag>: done`，
下一个包装脚本看到它再接着跑。断点续训、GCS 镜像、保活与轮换都靠本目录的脚本。线驱动本身是 `scripts/colab/w3m_line.sh`，
取数是 `scripts/colab/w3m_fetch.py`，训练包由 `scripts/train/make_colab_bundle.sh` 生成（见 `sim/tools/train/README.md` "Colab bundle"）。

VM 上的固定布局（`/content/sts2`）：`gcs-sts2-ckpt.json`（GCS 服务账号 key，**只放在这里，不进仓库、不打印**；本机副本在
`~/.config/sts2train/gcs-sts2-ckpt.json`）、`sts2train-<sha>/`（解包后的训练包）、`w3m-src/`、`w5-src/`（仓库源码快照，
提供 `scripts/colab/w3m_line.sh` / `w3m_fetch.py`）、`gcs_sync.py`、`gcs_pull.py`、`launch_*.sh`、`runs/<run>/`（run 目录）
和 `runs/<run>.driver.log`（驱动日志，在 run 目录**外面**，不被镜像）。桶：`gs://gomoku-zero-20260816-ai-sts2train/runs/<run>/`。

## (a) 线链

`w3m_line.sh BUNDLE RUN_DIR LINE FORK_CKPT [OVERLAP]`：从 FORK_CKPT 分叉（只做一次），按 `ENDS` 分段 `run_train.sh --resume auto`
到固定 `--max-updates`，每段终点的 pinned checkpoint 做 ab-512 greedy-off 确认评测，最后一段再加 ab-512 greedy-on 与 dev-128
采样×3。幂等可续：重跑同一命令即接着跑。日志标签 `w3m-${TAG:-$LINE}`，结束行 `w3m-<tag>: done`。公共配方（v2 + M 的
`--cycle-cost 0.01 --keep-ckpts 60`）：`--ascension 10 --characters SILENT --d-model 192 --state-dim 384 --depth 3 --rollout 64
--lr 3e-4 --amp --compile default --eval-every 100 --eval-at-every 100 --pin-every 100 --max-hours 3 …`（完整 FLAGS 见脚本）。
LINE `a` 用 `--overlap $OVERLAP`；LINE `b` 另加 `--overlap off --harvest boss:1536,elite:512,act2:1024 --harvest-prob 0.25 --harvest-start-share 0.33`。

| 轮 | GPU A | GPU B | 起点 → 终点 | 包 | 关键参数 |
| --- | --- | --- | --- | --- | --- |
| 主线 M | `launch_sp3.sh`（`runs/sp3-main`） | — | C1 u764 → u3064 | `sts2train-542a1e4eb8` | `sp2_serial.sh` 的 FLAGS + `--cycle-cost 0.01 --keep-ckpts 60 --max-hours 6 --eval-at 1264,1764,2264,2764,3064` |
| 过夜 A / B | `w3m_line.sh … runs/w3m-a a <M pinned-0003064> off` | 同上，`runs/w3m-b b` | M u3064 → u7664，段终点 `3564 4064 … 7564 7664` | `sts2train-413f510188` | A：`--macros rest,shop`，熵退火 0.02 → 0.005；B：A + harvest |
| 第二轮 C / D | `launch_c2.sh`（`runs/w4-c`，日志 `w4-c.driver.log`） | `launch_d2.sh`（`runs/w4-d`，`w4-d.driver.log`） | **B 的** u7664 → u9164（段 `8164 8664 9164`） | `sts2train-bd6d6f6863` | `TAG=c/d ENT0=ENT1=0.005 FORK_U=7664 LAST=9164`；C `MACROS=all`，D `MACROS=rest,shop`（对照）；LINE a，overlap off |
| 第三轮 E′ / F′ | `launch_e.sh`（`runs/w9-e`，`w9-e.driver.log`） | `launch_f.sh`（`runs/w9-f`，`w9-f.driver.log`） | B 的 u7664 → u9664（段 `8164 8664 9164 9664`） | `sts2train-57b85d8f0d` | `TAG=e/f`，熵恒定 0.005，`MACROS=rest,shop`；E′ `EXTRA="--explore-kinds ChooseRestOption,BuyEntry:remove --explore-eps 0.3 --explore-logp policy"`，F′ `EXTRA=""`。预注册 `docs/round3-plan-2026-11-05.md` |
| 第四轮 G / H（草案） | 待写 | 待写 | E′ 的 u9664 终点（E′ 未采纳则用第三轮分叉点）→ u11664 | `sts2train-0cf4bbe2c9` | 见 `docs/round4-plan-2026-11-06.md` |

- 分叉点 11-05 18:55 从 A 改成 B 的 `pinned-0007664.pt`：GPU A 上的 C、E′ 用 `w3m_fetch.py --wait 36000` 从桶取到
  `/content/sts2/fork/`；GPU B 上的 D、F′ 直接用本地 `runs/w3m-b/checkpoints/pinned-0007664.pt`。
- 每个包装脚本先等前驱的结束行：C 等 `w3m-a.driver.log` 里的 `w3m-a: done`，D 等 `w3m-b: done`，E′ 等 `w4-c.driver.log` 里的
  `w3m-c: done`，F′ 等 `w4-d.driver.log` 里的 `w3m-d: done`；每 300 s 查一次。前驱末行是异常结束（`: stop`、`failed (rc N)`、
  `check failed`、`fetch failed`、`pins it)`，E′/F′ 还认上一级包装的 `nothing to continue: done` / `giving up: done` /
  `exited rc N: done`）或等待超时（C 30 h、D 32 h、E′/F′ 48 h）时，打一行以 `: done` 结尾的说明并退出 1，让保活停掉运行时。
  所以包装脚本**所有**收尾行都以 `: done` 结尾；包装脚本的输出要 `>>` 到自己的 `runs/<run>.driver.log`。
- `w3m_line.sh` 训练段失败会重试（同一段最多 4 次，连续 3 次失败停）；train.log 尾部出现编译门（`compiled minibatch update vs eager … MISMATCH` /
  `CompiledMismatchError`）时追加 `--compile off` 再试。

## (b) 三道守护

1. **东京 VM 保活** `colab_keepalive.sh [INTERVAL=300]`（跑在 GCP 东京 VM 的 `~/sts2keep/`，独立于 Mac）：每 5 分钟对
   `~/sts2keep/sessions.txt`（每行 `NAME DRIVER_LOG`）里的每个会话 `colab exec` 一次内核 ping（GPU 运行时内核空闲约 20 分钟会被回收），
   顺便读该日志末行；末行以 `: done` 结尾就 `colab stop` 该运行时（停止计费）并把它从表里删掉。`DRIVER_LOG` 必须是链上**最后一环**
   的日志（现在是 `w9-e.driver.log` / `w9-f.driver.log`），否则中间一环结束就会把卡停掉。日志 `~/sts2keep/keepalive.log`，删
   `~/sts2keep/RUN` 停循环。`ping_loop.sh SESSION [INTERVAL]` 是早期在 Mac 上跑的单会话版本（两次失败写 `ALERT-<SESSION>`）。
2. **Mac 轮换守护 v2** `watch_rotate.sh`：每 120 s 跑一次 `colab sessions`。`managed.txt` 每行
   `SESSION LINE RUNS NEXT`（RUNS 按链顺序逗号分隔，例：`sts2c a w3m-a,w4-c,w9-e sts2a2`；NEXT 是替补会话名）。受管会话消失、
   且不是保活故意停掉的 → 新建 A100 会话 NEXT（`colab --auth=adc new --gpu A100 -s NEXT`），传训练包和 key，把这条线 RUNS 里的
   **每个** run 目录用 `gcs_pull.py --newest-ckpt-only --skip-existing` 从桶恢复，在启动任何 trainer 之前核对
   `status.json`、`metrics.jsonl`、`evals.jsonl` 都在，然后在**一次** ssh 里重启这条线的整条包装链。若保活是在失败行
   （`abnormally|exited rc|giving up|failed|: stop`）之后停掉运行时的，写 ALERT 文件，不轮换；以 `Hardware: CPU` 出现的受管会话
   （代理自动建的幽灵会话）先 stop 再按丢失处理；恢复不完整时把该行改成 `#HOLD …` 等人工处理。必填 `STS2_OPS_DIR`（managed.txt / watch.log / ALERT / RUN / stage.tgz 所在目录），
   `DRY_RUN=1 ONE_TICK=1` 只打印外部命令。
3. **GCS 镜像**：`gcs_sync.py`（VM 上，每个 run 目录一个，`w3m_line.sh` 自动起，`--interval 60 --min-age 60`，日志
   `runs/<run>.gcs_sync.log`）把 checkpoints（写完 60 s 后）、metrics/evals/status/config/summary/train.log/spec 和 `evals/**`
   增量传到桶，远端从不删除（本地 `--keep-ckpts` 删掉的 checkpoint 桶里仍在）。**桶没有对象版本**：覆盖即丢。
   Mac 上 `mirror_loop.sh LOCAL_DIR [600]` 把每个 sp*/w3m-*/w4-* run 的 metrics/evals/status/config/train.log 拉到
   `LOCAL_DIR/<run>/` 供监控页使用（`scripts/dashboard/`；w9-* 目前不在它的匹配式里）。

## (c) 手工在新 A100 上续一条线（`resume_b_sts2b3.sh` 的做法）

以 GPU B 链（D 断在 u8664 之后、F′ 未起）为例：

1. 本机 `colab sessions` 确认旧会话已不在、新名字未被占用；`colab --auth=adc new --gpu A100 -s <NEW>`；按 `ssh_config.example` 在 `~/.ssh/config` 加一段 `Host colab-<NEW>`。
2. 一次 scp 传上 stage 包（`gcs_pull.py`、`gcs_sync.py`、`launch_*.sh`、`w3m-src/`、`w5-src/`、训练包 tar.gz）和 key，解包到 `/content/sts2`，key `chmod 600`。
3. 在 VM 上跑 `resume_b_sts2b3.sh`（`setsid nohup … > /content/sts2/resume_b.log`）。注：2026-11-06 实际生效的是第二版
   `resume_b2_sts2b3.sh`（未导入仓库）：第一次恢复 ssh 在 900 s 被切、后台残留的 gcs_pull 与脚本自己的拉取同时写文件，第二版在
   下面的步骤之外逐个比对小文件 / 最新 checkpoint / 端点报告与桶内 md5、不一致就重下（两轮仍不一致则不启动），补写 `w3m-b: done` 前先 grep 去重。脚本依次：
   - `gcs_pull.py --prefix runs/w4-d --dest runs/w4-d --newest-ckpt-only --skip-existing`：只取最新的 `ckpt-*.pt` 和全部 `pinned-*.pt`，可中断重跑；
   - 核对 `status.json`、`metrics.jsonl`、`evals.jsonl` 非空，缺任何一个就**不启动**（否则 trainer 新写的 metrics 会被 gcs_sync 盖到桶里的完整历史上）；打印 status 的 update 和已有的终点评测；
   - 从桶重取线 B 的分叉点 `runs/w3m-b/checkpoints/pinned-0007664.pt`：新运行时上没有 `runs/w3m-b/`，而 F′ 要从它分叉（`w3m_line.sh` 会等这个文件、核对 updates = 7664，等不到 3600 s 就 `stop`）；
   - 往 `runs/w3m-b.driver.log` 补写一行 `w3m-b: done`：线 B 早已结束，但驱动日志在 run 目录外、没被镜像，新运行时上不存在，`launch_d2.sh` 会一直等它（最长 32 h）；
   - `setsid nohup` 起 `launch_d2.sh`（`>> runs/w4-d.driver.log`）和 `launch_f.sh`（`>> runs/w9-f.driver.log`）；D 从恢复的 checkpoint 接着跑，F′ 等 `w3m-d: done`。
4. 东京 `~/sts2keep/sessions.txt` 把旧会话名换成新名，日志指向链尾（`w9-f.driver.log`）；`managed.txt` 同步更新。

## (d) 事故与教训

- 运行时 24 小时硬上限：`sts2b`（11-04 18:39 EST 启动）在 11-05 18:43–18:49 EST 被回收（线 B 约 u7440；轮换守护 18:43 检测到并建 `sts2b2`）；长线要预先排好轮换。
- 对不存在的会话名 ssh/scp，代理会自动建一个同名 CPU 会话顶上：先 `colab sessions`。
- 线 B（2026-11-05）：轮换后小文件还没恢复就起了 trainer，新 metrics 经 gcs_sync 覆盖了桶里 u3064–u7425 的训练期 metrics/episodes 历史（评测与 checkpoint 完好）→ 现在恢复后先核对三份小文件。
- w4-d u8664（2026-11-06 04:38Z）：trainer 的 compiled-vs-eager 门在 loss≈0 时被 bf16 舍入噪声触发（loss rel 2.3e-2 > 1e-2），退出级联成包装脚本的 `: done`，保活在 04:45Z（23:45 EST）停卡；D 到 00:40 EST 才在 `sts2b3` 上续训，GPU B 损失约 1 小时 → `w3m_line.sh` 现在重试并回退到 `--compile off`。
- Colab 代理每个运行时只允许一条 ssh（第二条 HTTP 429）：命令合并成一次 ssh。
- 杀包装脚本用 PID 或带方括号的模式（`pkill -f 'launch_[df]'`），不要用会匹配到 ssh 自身 shell 命令行的模式。

## (e) 文件

| 文件 | 跑在哪 | 作用 |
| --- | --- | --- |
| `gcs_sync.py` | VM | run 目录 → 桶的增量镜像（不删远端） |
| `gcs_pull.py` | VM | 桶 → run 目录恢复；`--newest-ckpt-only`、`--skip-existing` |
| `launch_sp3.sh` | VM | 主线 M（C1 u764 → u3064）；FLAGS 取自 VM 上的 `sp2_serial.sh`（未导入仓库） |
| `launch_c2.sh` / `launch_d2.sh` | VM A / VM B | 第二轮 C（`--macros all`）/ D（对照）包装 |
| `launch_e.sh` / `launch_f.sh` | VM A / VM B | 第三轮 E′（探索修复）/ F′（对照）包装 |
| `resume_b_sts2b3.sh` | VM | 一次性：在新运行时上恢复 D 并重启 D、F′ 包装 |
| `colab_keepalive.sh` | 东京 VM | 内核保活 + 链尾 `: done` 时停卡 |
| `ping_loop.sh` | Mac | 早期单会话保活；`ALERT-<SESSION>` 写到 `$STS2_OPS_DIR`（默认脚本目录） |
| `mirror_loop.sh` | Mac | 桶 → 本机小文件镜像（监控页数据源） |
| `watch_rotate.sh` | Mac | 轮换守护 v2（见 (b)） |
| `ssh_config.example` | Mac | `Host colab-NAME` 模板（ProxyCommand `colab --auth=adc ssh --proxy-mode -s NAME`） |

- **Colab CLI 的会话名是客户端本地的**（`~/.config/colab-cli/sessions.json`）：在 Mac 上 `colab new -s NAME` 之后，东京保活机对 NAME 做 `colab exec` 会报
  "Session not found"（它的 `colab sessions` 只显示 `[?] <id>`，按 id 也不能 exec）。每建一个会话都要把这份 `sessions.json` 拷到东京的同一路径
  （`gcloud compute scp`），保活才能 ping 到它（2026-11-06 23:10，sts2b4）。线规格运行器 v2 的 `keepalive_v3.sh` 同样依赖这个注册表。
- **远端起启动器不要用内联 `bash -c "pgrep … ; bash launch_x.sh"`**：pgrep 会匹配到这条 bash -c 自己（命令行里含 `bash launch_x.sh`），报 already-running
  却什么都没起（2026-11-06 两次：C4、W20a）。统一用运行时上的 `start_line.sh LAUNCHER DRIVER_LOG`（pgrep 的模式 `bash launch_x[.]sh` 不会匹配该脚本自己）。

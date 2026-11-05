# 过夜交接（2026-11-04 23:00 EST 写，覆盖到 11-05 15:00 EST）

用户离开约 16 小时；本机预计 11-05 06:00 后中断（本会话、Mac 端的 ping/镜像循环、监控页刷新都会停）。训练与保活不依赖本机。

## 正在跑什么（两台 Colab A100）

| 运行时 | 任务 | 起止 | 驱动脚本 / 日志（VM 上） |
| --- | --- | --- | --- |
| `sts2train`（GPU A） | 主线 M：C1 续训 u764→u3064（循环成本 0.01，无宏动作）。00:42 因 --max-hours 6 在约 u2560 停下，01:00 用 `resume_sp3.sh` 续到 u3064 | 18:42 → 约 02:15 | `/content/sts2/runs/sp3-main`，`sp3-main.launch.log` |
| `sts2train`（GPU A） | 过夜线 A：从 M 的 `pinned-0003064.pt` 分叉，+宏动作（rest,shop）+熵退火 0.02→0.005，u3064→u7664（+6 亿步，每 500 update 固定评测 + ab-512 关 breaker） | 约 02:15 → 约 13:30 | `w3m-src/scripts/colab/w3m_line.sh … a …`，日志 `/content/sts2/runs/w3m-a.driver.log`，run `/content/sts2/runs/w3m-a` |
| `sts2b`（GPU B） | 复验 R：种子 2 的 W0′→C0′→C1′→终点比较 | 18:45 → 约 23:00 | `sp2r_serial.sh`，`sp2r_serial.log`，比较结果 `/content/sts2/runs/cmp/*.txt` |
| `sts2b`（GPU B） | 过夜线 B：A 的配方 + harvest（boss:1536,elite:512,act2:1024，prob 0.25，share 0.33），同一分叉点 | 约 23:10 启动等待，02:15 后开训 → 约 14:15 | `w3m_line.sh … b …`，日志 `/content/sts2/runs/w3m-b.driver.log`，run `/content/sts2/runs/w3m-b` |

两条过夜线的官方读数在 u7664；判据见 `docs/w3m-overnight-plan-2026-11-04.md`（宏动作回退规则：A 的 u3564、u4064 都显著低于 M 的 u3064 则回退；B 的止损规则：从 u4064 起连续两点 B−A 的区间全在 0 以下则停 B；≥0.60/≥0.10 是目标不是承诺）。

## 保活与数据（不依赖 Mac）

- **保活**：东京 VM（`gcloud compute ssh instance-tokyo-0701-predict-raven …`）上 `~/sts2keep/colab_keepalive.sh` 每 5 分钟对 `~/sts2keep/sessions.txt` 里的运行时做一次内核 ping；驱动日志末行出现 `done` 时自动 `colab stop` 该运行时（不再计费）。日志 `~/sts2keep/keepalive.log`；删除 `~/sts2keep/RUN` 可停止循环。东京 VM 上放了 Colab CLI、会话名表和 ADC（单人 VM，按规则允许）。
- **数据**：VM 上每个 run 目录由 `gcs_sync.py` 每分钟镜像到 `gs://gomoku-zero-20260816-ai-sts2train/runs/<run>/`（checkpoint、pinned、metrics/evals/cycles/episodes、评测目录）。VM 丢失时用 `gcs_pull.py` 恢复后重跑同一驱动命令即可续训。
- **本机**：06:00 前每 30 分钟刷新监控页（claude.ai artifact 492zmT59ngRDmG3GqkmMC7）；06:00 后页面停更，数据以桶为准。

## 回来后怎么看结果（约 15:00 EST）

1. `gcloud storage ls gs://gomoku-zero-20260816-ai-sts2train/runs/` 看 `w3m-a`、`w3m-b` 的 `evals.jsonl`（`mode` 为 greedy-on / greedy-off 的 eval_at 行）和 `evals/endpoint/`。
2. 在本机 `train-w3m` 工作树里 `scripts/colab/w3m_compare.sh 7664`（拉取 M、A、B 的评测目录并跑全部配对比较），或直接读 VM 上的 `/content/sts2/runs/cmp/`（若运行时已被保活脚本停掉，则只能用桶里的评测目录在本地 CPU 跑 `sts2train compare`）。
3. 决定：按计划文档的规则判宏动作是否保留、harvest 是否采纳；选出下一轮主线起点（通常是官方终点的 pinned checkpoint）。
4. 下一轮候选（已就绪或在做）：v2 全提示宏动作（wave 4，分支 train/w4，完成后 `--macros all`，含真游戏映射）、large 预设（需先在 A100 上跑重叠执行预检 `w3m_preflight_overlap.sh`）、以战斗血量头为叶子的搜索。

## 预算

22:30 余额 654 单位；过夜两条线约 165–180 单位；预计 11-05 15:00 余额约 470。用户给的上限：24 小时 400 单位。

## 01:00 更新

- M 在 00:42 因 `--max-hours 6` 提前停止（约 u2560），01:00 已续训到 u3064（预计 02:15 到达）；线 A 驱动因等待超时（4 小时）退出，已用 WAIT_MAX_S=28800 重启；线 B 的分叉 checkpoint 等待窗口改为 10 小时。两条过夜线预计 02:15 起跑，A 约 13:30、B 约 14:15 结束。
- 主线 M 的 u2264 固定评测：dev-128 关 breaker 0.389 [0.364, 0.413]，65/128 进入第二幕，0 通关，为目前最佳。

# 过夜交接（2026-11-04 23:00 EST 写，覆盖到 11-05 15:00 EST）

用户离开约 16 小时；本机预计 11-05 06:00 后中断（本会话、Mac 端的 ping/镜像循环、监控页刷新都会停）。训练与保活不依赖本机。

## 正在跑什么（两台 Colab A100）

| 运行时 | 任务 | 起止 | 驱动脚本 / 日志（VM 上） |
| --- | --- | --- | --- |
| `sts2train`（GPU A） | 主线 M：C1 续训 u764→u3064（循环成本 0.01，无宏动作）。00:42 因 --max-hours 6 在约 u2560 停下，01:00 用 `resume_sp3.sh` 续到 u3064 | 18:42 → 约 02:55 | `/content/sts2/runs/sp3-main`，`sp3-main.launch.log` |
| `sts2train`（GPU A） | 过夜线 A：从 M 的 `pinned-0003064.pt` 分叉，+宏动作（rest,shop）+熵退火 0.02→0.005，u3064→u7664（+6 亿步，每 500 update 固定评测 + ab-512 关 breaker） | 约 02:55 → 约 19:30–20:00 | `w3m-src/scripts/colab/w3m_line.sh … a …`，日志 `/content/sts2/runs/w3m-a.driver.log`，run `/content/sts2/runs/w3m-a` |
| `sts2b`（GPU B） | 复验 R：种子 2 的 W0′→C0′→C1′→终点比较 | 18:45 → 约 23:00 | `sp2r_serial.sh`，`sp2r_serial.log`，比较结果 `/content/sts2/runs/cmp/*.txt` |
| `sts2b`（GPU B） | 过夜线 B：A 的配方 + harvest（boss:1536,elite:512,act2:1024，prob 0.25，share 0.33），同一分叉点 | 约 23:10 启动等待，约 03:00 开训 → 约 20:30–21:30 | `w3m_line.sh … b …`，日志 `/content/sts2/runs/w3m-b.driver.log`，run `/content/sts2/runs/w3m-b` |

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

## 01:30 更新（时间线修正 + 第二轮已武装）

- **时间线修正**：M 续训实测 12.4 s/update（固定评测只要 24 s 一次，可忽略），M 到 u3064 约 **02:55 EST**；
  线 A 4600 update ≈ 16 h → 约 **19:30–20:00 EST** 结束，线 B（harvest 略慢）约 **20:30–21:30**。官方读数 u7664 因此在
  用户回来（15:00）之后；15:00 时 A 大约在 u6500。
- 01:00 那次对线 B 等待脚本的重启实际没有存活（ssh 退出时被带走），01:08 已重新以 `setsid nohup` 启动并核实在跑
  （`w3m-b.driver.log` 每分钟一行 waiting，`w3m_fetch.py` 进程在）。
- **第二轮已武装为自动接续**（`docs/round2-plan-2026-11-05.md`，预注册）：A 结束后 GPU A 自动跑 C（A 终点 + `--macros all`），
  B 结束后 GPU B 自动跑 D（A 终点 + `rest,shop`，对照），各 +1500 update，约 11-06 01:30–03:00 EST 结束，合计约 75 单位。
  东京保活现在盯 `w4-c.driver.log` / `w4-d.driver.log`。取消方法见该文档。
- wave-4 包（`sts2train-d21adf4ad8`，AOT `03f89cc37d4f`）已解包到两台 VM 的 `/content/sts2/` 并放进桶 `bundles/`；
  驱动脚本 `scripts/colab/w3m_line.sh` 加了 `ENT0`/`ENT1`/`TAG` 旋钮（`6175dc8`，默认不变），VM 上在 `/content/sts2/w5-src/`。
- **wave 5 在跑**（Opus，远端测试）：孪生对 `prompt` 宏的映射，分支 `train/w5-twin-prompt`，工作树
  `.claude/worktrees/train-w5-twin-prompt`；完成后需要集成进本分支（若本机 06:00 前没完成，分支上有提交即可接着做）。
- 预算：01:00 余额 619.6；过夜两线约 235 单位、第二轮约 75 单位；到 11-06 05:30 预计余额约 300。24 小时窗口内约 300 单位，低于 400 上限。

## 02:15 更新

- M u2764 固定评测 dev-128 关 breaker 0.390 [0.368, 0.413]（63/128 进二幕），与 u2264 的 0.389 持平：最近 500 update 趋平。
  看 A/B 的 u3564 / u4064 段终点是否继续平——若平，下一轮的杠杆要换（见死亡分析）。
- 死亡分析 `docs/diag-2026-11-05/death-analysis-m-u2264.md`（2593f7b）：128 局全部战斗死亡；Act 1 boss 占 32%（到达者 38.7% 死亡），
  Act 2 第 4–8 间 + Act 1 boss 房合计 61%；`DECIMILLIPEDE_ELITE` 12.5% 为最大单一杀手；Act 2 boss 到达 9 局全灭。
  日志缺 HP / 卡组 / 房间类型（`death.room_type` 恒为 null 是 bug）→ wave 6 在补（分支 `train/w6-episode-fields`，Opus，远端门禁，零行为改动）。
- wave 5 已合入 PR 分支（`970b178`，已推送）：孪生可在真游戏上执行 `--macros all` 的提示宏；DECLINE 在两种模式下都映射成 skip。
  合并提交全套远端门禁通过（build、train 215、vec 131、eval 84、live 101）。**还没在真游戏上跑过**；第一次真跑要看 `prompt_macro.steps`
  确认哪些 STS2_MCP 提示在最后一次选择后自动关闭。
- 东京保活脚本已加固（超时强杀、文件捕获、失败重试）并重启，07:11Z 起两台 ping 正常。

## 03:30 更新

- wave 6 合入 PR 分支（`bd6d6f6`）：训练对局行与评测摘要新增 hp / max_hp / deck / upgraded / relics / gold / potions / killer /
  boss_entry / room，评测摘要另有 fights 列表，`death.room_type` 修复；零行为改动（三组冒烟 weights_hash 与基线相同），
  成本约 0.6 ms / rollout step。合并提交的全套远端门禁结果见本节末。
- 新包 `sts2train-bd6d6f6863.tar.gz`（AOT 同 `03f89cc37d4f`，库逐字节相同；raven 容器冒烟通过）已放到两台 VM 与桶 `bundles/`，
  第二轮 C / D 的包装脚本已换成 `launch_c2.sh` / `launch_d2.sh`（只改包路径）。过夜线 A / B 不变（仍是 w3m 包，无新字段）。
- 回来后的第一份诊断可以直接用 C / D 的 `evals/endpoint/*/summary` 与 `episodes.jsonl` 回答"死在哪、进 boss 时血量多少、卡组什么样"。

## 03:05 更新：分叉已发生

- M 到 u3064，dev-128 关 breaker **0.404 [0.378, 0.430]**（64/128 进二幕，0 通关），新最佳。`pinned-0003064.pt` 07:57Z 进桶。
- 02:56 EST 线 A 分叉开训（`runs/w3m-a`，gcs_sync 已起）；02:58 线 B 取到同一 checkpoint 开训（`runs/w3m-b`）。
- wave 7 合入并推送（`c4109be`）：`scripts/analysis/death_profile.py` + `scripts/colab/r2_compare.sh N`（第二轮一键读数，
  见 `docs/round2-plan-2026-11-05.md` "读结果"）。

## 04:00 更新：首批固定评测

- dev-128 贪心关 breaker：线 A u3100 0.407、u3200 0.406、**u3300 0.434 [0.409, 0.460]**（81/128 进二幕）；线 B u3100 0.403、
  u3200 0.405、u3300 0.413 [0.388, 0.437]；分叉点 M u3064 0.404。宏环境切换后固定评测没有回落；两线训练期闭环 0。
- 评测点是每 100 update 的整百（3100、3200、…），段终 3564 / 4064 / … 另有 ab-512。

## 05:00 更新：第一段终点 u3564

| 线 | dev-128 关 breaker（固定评测） | ab-512 关 breaker（段终） |
| --- | --- | --- |
| A（宏 + 熵退火） | u3300 0.434、u3400 0.428、u3500 0.428、**u3564 0.4295 [0.404, 0.455]**（80/128 进二幕） | 0.431（512 局，0 胜，最远楼层 45） |
| B（A + harvest） | u3300 0.413、u3400 0.402、u3500 0.412、**u3564 0.4033 [0.379, 0.428]** | 0.425（512 局，0 胜，最远楼层 48） |
| 分叉点 M u3064 | 0.404 [0.378, 0.430] | — |

- 宏回退规则（A 的 u3564、u4064 都显著低于 M u3064 才回退）在 u3564 不满足：A 高于 M。B 与 M 持平，B−A 未配对约 −0.026；
  B 的止损规则从 u4064 起算（连续两点 B−A 区间全在 0 以下）。配对比较用 `scripts/colab/w3m_compare.sh 3564` 或 VM 上 `/content/sts2/runs/cmp/`。
- 两线已进入第二段（到 u4064，约 06:40 EST），每段起点都是 `--resume auto`（session 计数 +1）。

## 05:30 更新（本机中断前最后一次）

- A u3705、B u3690，第二段进行中（到 u4064 约 06:40 EST）。dev-128 关 breaker 每 100 update 一点（±0.025 噪声）：
  A u3300–u3700 = 0.434、0.428、0.428、0.430、0.402、0.429（均值约 0.425）；B = 0.413、0.402、0.412、0.403、0.419、0.393（约 0.407）；
  分叉点 M u3064 0.404。仍 0 通关。
- 本机 06:00 后中断：监控页停在 05:30 的刷新；本机的 ping / 镜像循环随之停止；训练、GCS 镜像、东京保活、第二轮自动接续都不依赖本机。
- 回来后按"回来后怎么看结果"一节操作；第二轮读数用 `scripts/colab/r2_compare.sh 9164`，死亡画像用 `scripts/analysis/death_profile.py`。

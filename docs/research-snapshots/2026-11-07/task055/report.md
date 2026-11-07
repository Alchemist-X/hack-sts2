# Task055: S 纵向 dev 输入

状态：独立验收通过，已按授权部署生产 `controller.py` 单文件；未暂停 S、未启动新控制 tick、未调用研究模型或 GPU、未修改账本。

## 原因与改动

054b 的 `matched_comparisons` 仅取其他实验同决策量结果。S 在 25,034,752 决策的下一次 dev 结果进入研究输入时，原逻辑无法提供 S 自身 10,092,544 决策 dev 基线。

本次保留 `matched_comparisons` 原语义，追加 `longitudinal_dev_history`（最多 8 项）和解释字段。仅取同一实验、同一 config hash、eval protocol 和 dev seed manifest hash，且更早决策量的 `result` 事件；要求 origin 与 eval job 同实验、job `done` 且无 error、有 `outcome`，事件的评测 checkpoint、manifest、metrics 与 job outcome 一致，manifest termination 为 completed，games 等于冻结 dev seed 数。输入只含 event/job ID、训练 checkpoint manifest hash、评测 manifest hash、决策量、摘要 games/wins/rate/CI/progress/CI/progress_games/replayable/results；无原始 seed、blind、权重或未来信息。S 25M 节点附自然语言计划指导：先对照 S10M，根据进度、胜率、不确定性、异常分母与成本判断；明显改善再考虑 50M，停滞/退步先诊断一个因素，目标达成先 freeze 再 blind；训练曲线仅诊断，不新设统计门禁。

## 源与验证

- 精确 054b controller 基线 SHA-256：`834a5fbaf30f455fba840a0eaee0e609dacf392d2f6d9eec5bdcd1bcff785509`。
- 补丁：`/private/tmp/sts2-task055/controller-054b-to-055.patch`，SHA-256 见验收报告。
- 目标源码：`/private/tmp/sts2-task055/work/sim/tools/modal_research/controller.py`，SHA-256 `0367dec5a58d633b166d0c4ff1cdf46bac47ff0aedcec79c7e823e850a58dfbf`。
- fixture：`/private/tmp/sts2-task055/work/sim/tools/modal_research/tests/test_longitudinal_input_055.py`，SHA-256 `b74234764b16ea249400c515028dfd7bfc860c49c06ba44add962a7fdd93ffe5`。覆盖排除当前决策、错 config/protocol/dev hash、失败或无 outcome、origin 不匹配、上限 8，以及 prompt 实际包含 S 10M，排除 raw seed/weights。
- 独立终审：`/private/tmp/sts2-task055/acceptance-055.md`。 reviewer 用真实 S10M 已接受 event+eval job payload/outcome 在临时 SQLite 复建，证明新 helper 只返回该 10,092,544 历史。相关 19 项测试独立重跑通过。

## 部署与回读

- 精确部署脚本：`/private/tmp/sts2-task055/deploy_reviewed_055.py`，SHA-256 `78533dee6d04b33b6b1e2015d972b9f73273ca6e646092d3caafffbbd7ca738f`。
- 部署前生产现场 SHA 与预期旧 SHA 一致；root 保护 stage 中目标源码与脚本 SHA 逐一复核。脚本独占备份、同目录 pending 原子替换、fsync 与读回，异常自动回滚。
- 生产文件：`/var/lib/sts2-modal-production/control-047-m/sim/tools/modal_research/controller.py`，部署后 SHA-256 `0367dec5a58d633b166d0c4ff1cdf46bac47ff0aedcec79c7e823e850a58dfbf`。
- 备份：`/var/lib/sts2-modal-sol-048/task055-stage/controller.py.pre055`，回读 SHA-256 `834a5fbaf30f455fba840a0eaee0e609dacf392d2f6d9eec5bdcd1bcff785509`。
- 部署后只读权威账本回读：指定 S 续训 job `train-5b6e5d60a290250888d8b107b029fc0b:1` 仍 `running`；study `stopped=null, analysis_paused=null`；latest decision `decision-d63c1f6d5211cf42cb85fa7d6e01a226` action `PAUSE`；hold 行数 conservative=5、cost=0、research_usage=2。源码替换本身不写账本。未触发训练或研究调用。

限制：这是未来研究输入修复，需待 S 25M 的真实 dev 完成及后续正常控制周期，才能观察到生产研究 packet。当前未生成新的研究决策。

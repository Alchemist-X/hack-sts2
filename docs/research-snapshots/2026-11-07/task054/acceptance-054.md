# Task054 独立代码与单次开训准入验收

结论：**准入通过**。用户最新授权允许以新鲜工作区累计 gross 作为已发生 Modal 用量，已终态但 `actual=NULL` 的旧 Modal 最大预留保留在原始账本与历史展示中，不再永久占用新作业准入。此结论只适用于下述固定 S 续训路径；真实部署、提交及训练结果仍待生产回执核验。

- 审查补丁 `/private/tmp/sts2-task054/054-live-052-budget.patch` SHA-256 `fbe3d5cffb078e9bb32e14af410dc7634599987b614ce49ebbdf86f24d80d01e`。三份旧源 SHA 分别为 store `2032eeb71b336576bcfede22a1bf40e9968e3451b3915dcd174ef4677667e4a3`、controller `83d85d957f4c473a5e9981ea1a67004940d6cf0a17fb47d8f0124f10dc72d070`、runtime `3117e22de3b2377d8d9eb78413f9f1748b82a0655b5e43dd096ee77b8d36413f`；候选 SHA 为 `f59d66fae6d43240c67324bdba6ad9fb587dd30f19841d26b87273f739f5947e`、`6c13111f286201c935d471f5a51bfce495c10d8edb7d321bf33c86d6f27fbe4f`、`3780c722ede7785f7c10729cb907c9ede264e3fd58e448a45bb1214acb5fd335`。候选树中补丁 `git apply -R --check` 通过。
- `budget_policy` 仅在精确 PAUSE、已审 S experiment/checkpoint、原 $100 批准和 $42.50 gross/$30 net 限额、≤120 秒工作区计量快照及无活动 GPU 作业时，以同一事务写入 opt-in 事件、清精确 stop、创建一个原调度 S train intent。旧 PAUSE decision 不改，旧 WAIT/PAUSE 不再次决策。重复同入口因 policy/event/claim 拒绝。
- 新有效项目占用为工作区累计 gross + 已知非 Modal actual + 未知非 Modal reserve + 活动或未证终态 Modal reserve。`modal_ledger` 对已证终态的旧 Modal 保留原始 `reserve`、`actual=NULL` 与历史展示，准入时以工作区 gross 计算；未证终态、活动与不明作业仍全额留款。054初版的 `_create_job`、`_can_submit` 均要求≤120 秒快照；该持续门禁后来按下文054b修正为现有900秒有效期。Broker 的 send/worker admission 复用 `_can_submit`。过期计量不能新增/提交 GPU。
- 我独立运行 `/private/tmp/sts2-task054/test_budget_054.py` 四项通过，fixture SHA-256 `3993c1b4e9703e2cdc5854ff9ee4fa13d19456691412aaff179508b5954f7cff`。同宿主私有 clone 回执 `/private/tmp/sts2-task054/clone-result.json` SHA-256 `60863e3f7fcd99153ef638a99d4477a94566f15b759393fe1d0a6d7d0e375652`：单个 `train-5b6e5d60a290250888d8b107b029fc0b:1` intent，reserve $7.30，原 config/checkpoint，target 25,034,752；重复调用拒绝，旧 decision hash 不变。旧终态 Modal 原预留 $21.02 只作历史展示；18 个旧 access probe 费用未知，其非 Modal 部分仍留 $11.70，Modal 部分也继续占用；含新任务活动 Modal 预留 $21.50，平台余 $15.32，项目余 $61.12。此回执为 clone，不是生产启动证据。
- 审查固定部署脚本 `/private/tmp/sts2-task054/deploy_reviewed_054.py` SHA-256 `26b19d3883bcb25cb5015d5e958440481b53a2c3832322603ce4addc7c2648cd` 与唯一 marker 创建脚本 `/private/tmp/sts2-task054/create_request_054.py` SHA-256 `495bf50d2bd1d59ecd7ffda2816952bd6ab50b9c8fd346b84bae6dab101f89d4`。先停 timer 并确认 service inactive、核生产三基线 SHA，备份并原子替换；marker 由 root 在原 monitor 0700 目录独占创建为0600。第一 service tick 只消费 marker/写事务；结果和唯一 intent 后核通过，第二普通 tick 才允许真实发送 S；再恢复 timer。若无有效≤120秒计量或事务失败，保留 claim 与 PAUSE，不重试。无新权限、凭据或 unit 配置。

执行后仍须核：生产 result 与唯一 S job/remote ID、原 config/checkpoint/target/dev256、旧作业 reserve/actualNULL/holds/decision 未改、新 opt-in event、真实快照和当前 $100/$42.50/$30 限额；无其他新 GPU/research job。真实费用继续未知，历史预留不等于现金结算。

## 054 首次现场结果与 054b 必要修复

生产首次054 marker 因快照年龄181秒而超出首次120秒门禁，事务安全回滚：PAUSE、S、policy 与旧holds 未改；原claim/result保留，timer已恢复。S 当时并未启动。原因是首次120秒门禁与既有300秒刷新间隔冲突，属于本方时限设计问题。

054b补丁 `/private/tmp/sts2-task054/054b-live-054.patch` SHA-256 `011bc6ee54865c17f47b9ddb426a210ee76bfba0c0985a9db0e55caac342ee50`，只改 broker/controller/runtime。持续准入读取既有现场 `max_age_seconds=900`；无metering绑定拒绝。首次054b marker显式尝试刷新既有 workspace metering，然后仍按≤120秒的已验证快照准入；供应商刷新失败时若已有快照仍在120秒内，合同允许使用，真正过期拒绝。旧054 marker/result不复用，使用独立054b root0600 claim。

我在精确候选package上独立重跑 `test_budget_054.py` 五项与 `test_metering_054.py` 两项，7项通过；快照年龄500秒可持续准入，901秒拒绝提交，强制刷新调用及失败保留旧时间均覆盖。补丁在候选树 `git apply -R --check` 通过。最终三源SHA：broker `359ef7c15c6cc1a9ee212cd6dbb88a202526784afcbb55f42f89ec83028b7029`、controller `834a5fbaf30f455fba840a0eaee0e609dacf392d2f6d9eec5bdcd1bcff785509`、runtime `53ec96e2c9ade9d24b0821444e37079563114969178a566fae9c1df0626cb258`。部署脚本 `deploy_reviewed_054b.py` SHA-256 `d9d7b7aa88ac2907fd11f471f654ce049c7a081dfdfffa3a8f25e897904abc66` 固定旧054与新054b源SHA；唯一marker脚本 `create_request_054b.py` SHA-256 `a9509c201ac0a293ec187e1898e8c7da5b421eaee823f2dfb0444fc8407d491f`。

054b按root明确授权已放行owner实施；生产部署、唯一S提交及实际运行状态待现场回执复核。首次054失败不构成Modal供应商阻断。

## 生产后核

054b部署回执 `/private/tmp/sts2-task054/deploy-result-054b.json` SHA-256 `0c52cb6924d958edbdd5c1401cd18a8d6c96c0301ca67cafe70848fde1695673` 固定旧/新三源 SHA；初版054部署回执 SHA-256 `82f7d95ee66cd7b7023807c297dbaa21700bdd5210926dac3133c3a7a9dbaa48`。首次失败和第二次成功的 marker 分离，`first-success-readback.txt` SHA-256 `a483f11ca55da44e1c2ce387cb517255a11be650d09271c4713a6fb54f7c3f7c` 证明旧054 claim/result hash未变、054b result `status=complete`、快照年龄11秒、唯一新S train intent、opt-in event/policy存在、stop清除；原PAUSE decision仍为 `decision-d63c1f6d5211cf42cb85fa7d6e01a226`。

`running-readback.json` SHA-256 `ad40de575dd0a6cb7cf7b9a25a5dcdd53f48e9bb9a46194c4dbf8759ade979d9` 和 `running-readback-2.txt` SHA-256 `047a7f35b7c874657c422d0ab8398d388061dfd9a2bd78d4a70720d7aa86e049`：唯一新S job `train-5b6e5d60a290250888d8b107b029fc0b:1` 为 controller `running`、broker `submitted`，Modal sandbox `sb-QjlwybqIUj5Ic3qTDrRopl`。起点10,092,544、目标25,034,752、原checkpoint/config/dev256哈希和reserve $7.30一致；timer已恢复active，原$100/$42.50 gross/$30 net限额保持。`actual=NULL`，`broker_input_present=false`，因此目前证实的是供应商已接收并建sandbox，worker首条训练进度尚待只读监测，不能声称训练样本已推进。

`history-invariants.json` SHA-256 `db38f7b57de4bef575484c00e3d9416df9ff19153718179822ba7cbeddf5265a` 将054前同宿主clone与生产旧行逐行哈希比较：旧25 jobs、16 events、2 decisions、25 billing、2 research origins，5 conservative holds、2 research usage holds、0 cost holds均 `unchanged=true`。新S为第26个job，旧unknown现金未结算。`status-postcheck.json` SHA-256 `bc7081775ef8300bbf083fb57586d00d33d47687d6e0b00e115f256f80b784ba` 显示当前唯一活动job是S，budget policy生效、历史终态Modal reserve $21.02保留展示、非Modal未知hold $11.70，平台有效剩余 $15.32、项目有效剩余 $61.12，stop为null。

最终结论：054b代码、一次激活事务、真实Modal提交与历史不变量均通过独立验收。worker进度和后续dev256结果仍待正常监测；不停止或重发S，不将未知费用写为零。

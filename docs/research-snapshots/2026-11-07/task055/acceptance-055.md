# Task055 研究输入独立验收

候选只改后续研究任务的输入，不触发研究模型调用或训练控制。实际生产部署尚待独立核验。

- 基线为054b controller SHA-256 `834a5fbaf30f455fba840a0eaee0e609dacf392d2f6d9eec5bdcd1bcff785509`；候选patch `/private/tmp/sts2-task055/controller-054b-to-055.patch` SHA-256 `636d4226ec8930ff1177aa37b38d811155879bbdbb1e3865ba92c4c53d776a21`，目标controller SHA-256 `0367dec5a58d633b166d0c4ff1cdf46bac47ff0aedcec79c7e823e850a58dfbf`。新增fixture SHA-256 `b74234764b16ea249400c515028dfd7bfc860c49c06ba44add962a7fdd93ffe5`。
- `longitudinal_dev_history` 仅收同一experiment、同config hash、同eval protocol、同dev seed manifest的更早 result，且来源job为无error、done eval、有outcome；再次核 event manifest/metrics 与 outcome 一致，checkpoint hash合法，game数与冻结dev seed count一致。按决策数递减最多8条；摘要仅列 headline、event/job ID与checkpoint/manifest SHA，不含per-seed、blind、权重或原始输出。原`matched_comparisons`的跨实验同决策数语义不变，新增字段明确标为同实验纵向dev历史。
- 只在 S 的25,034,752结果输入中加短期研究计划上下文，提示将10,092,544的已验dev作为纵向基线，结合进步、胜率、异常分母与费用判断下一步；没有自动统计门禁或自动决策。当前S worker/预算/旧决定均不受此代码路径影响。
- 我独立重跑 `test_research_usage`、`test_goal_and_idle_047`、`test_longitudinal_input_055`，共19项OK。并从本地已接受S10M证据 `formal-milestone-1-evidence.json` 取真实event、eval job payload/outcome，在临时SQLite构造同源记录，调用候选helper得到且只得到10,092,544历史，真实字段与严格过滤兼容。

部署脚本 `/private/tmp/sts2-task055/deploy_reviewed_055.py` SHA-256 `78533dee6d04b33b6b1e2015d972b9f73273ca6e646092d3caafffbbd7ca738f` 仅接受现有生产controller SHA `834a5fba…` 与审核候选 `0367dec5…`，要求root执行和受保护stage，独占保存备份后同目录原子替换并读回，失败自动回滚。调用方案只做现场SHA核验、传入已审两文件、运行一次部署脚本；不停止/重启服务、不调用tick、不碰worker或账本。已依据root授权通知唯一owner可以实施；现场SHA、认证或审批不符即停。

生产部署后核：唯一owner回执见项目 `artifacts/modal-research/2026-11-04/research/task055/report.md`；已审部署脚本返回 `deployed=true`，现场controller读回 SHA-256 `0367dec5a58d633b166d0c4ff1cdf46bac47ff0aedcec79c7e823e850a58dfbf`，root保护备份读回旧 SHA-256 `834a5fbaf30f455fba840a0eaee0e609dacf392d2f6d9eec5bdcd1bcff785509`。只读权威库回执显示S指定续训job仍 `running`，study `stopped=null, analysis_paused=null`，latest旧PAUSE decision不变，holds行数 conservative5/cost0/research_usage2。部署未停止/重启服务、未触发tick、研究模型/GPU调用或账本写。该回执支持单文件控制输入部署完成；新字段实际出现在生产研究packet仍需S25M真实dev完成后观察。

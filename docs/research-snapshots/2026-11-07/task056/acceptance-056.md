# Task056 Modal access probe 终态预留独立验收

候选补丁仅改生产 `store.py` 的056预算视图；不写权威账本、不停止S、不调用tick、Modal或模型。生产部署待固定脚本终审。

- 基线store SHA-256 `f59d66fae6d43240c67324bdba6ad9fb587dd30f19841d26b87273f739f5947e`，候选patch `/private/tmp/sts2-task056/056-live-store.patch` SHA-256 `bca54d4aa2fdac3b71ceac875b9c562f36aad910ae1c1db8480f9c50fe75d1ab`，目标store SHA-256 `ddaba51e930326a696d3ce69431caffe40be853e7d44a9e28e18880f09e4ed2e`。055 controller维持已部署 `0367dec5…`。
- 改动仅在现有 `terminal_modal_history_v1` 用户opt-in已启用时使用单一固定predicate。它对9个具体job ID分别钉 category/status/reserve/report ref/report SHA，另检 `vendor=modal`、`actual=NULL`、`state=external_finished_cost_unknown` 以及各类完成证明；未证终态/活跃/其他unknown继续原reserve。相同predicate用于项目ledger、Modal ledger和历史展示；原jobs.reserve、actual、billing、holds和decisions不改，现金费用仍未知。政策未启用时走原逻辑。
- `/private/tmp/sts2-task054/modal-probe-audit.json` SHA-256 `e5d814b81a1fa162dc9cf63c98a15522a419b8da05112792e5ce92eb0c6a9d80` 的9项 tuple 与候选逐项完全一致，总原reserve1420 cents；审计已分别验证报告文件hash和终态。7项明确provider结束，1项零submit，1项固定报告的三个隔离CPU子Sandbox结束；后者不伪称生产账本含隔离子job。
- 我独立运行 `test_budget_056.py` 四项OK（fixture SHA-256 `7bfb406cce2d0e96c7c0a48f2ad9a2020690d2a371c2c340bc12b16f27bcc50f`），覆盖政策未启用、精确9项释放、错proof与活跃继续占用、重复读不改历史。同宿主production只读内存编译干跑 `/private/tmp/sts2-task056/live-ro-dryrun.json` SHA-256 `3a09a03e882d32255fdb411017859b8bf7469f82aa8c945438b7ee2813c33389`：9个精确ID，旧有效ledger `[745,3320]`→候选`[745,1900]`，Modal held `2150`→`730` cents（活跃S完整保留），历史展示`2102`→`3522` cents，`db_total_changes=0`；非Modal unknown $11.70仍留。

固定单文件部署脚本 `/private/tmp/sts2-task056/deploy_reviewed_056.py` SHA-256 `3fa1127134db34dce4d5e6100ac214da0c97ce982ffab63a0aaec3dfe5492bdb` 要求root、保护的stage、精确旧/新store SHA及已部署055 controller SHA；独占备份、同目录原子替换、读回失败回滚。调用不停止/重启timer或服务，不调用tick/模型/Modal、不写DB。已按root授权通知owner可部署一次；现场源、认证或审批不符即停。

待核：部署后只读确认S仍运行、原始job/holds/decisions无改、实际有效预算下降恰1420 cents，真$100/$42.50/$30上限不变。

## 生产后核

`deploy-result.json` SHA-256 `c312405c06d00dacbba233b1572ff1efa442ef5c72db7f2959a1fe46db3e3042` 报告 `deployed=true`，读回store为候选 SHA `ddaba51e…`、controller保持055 SHA `0367dec5…`。owner只替换store源，未停/重启服务或timer、未手动tick、未调用Modal/模型、未写账本。

只读审计 `modal-probe-audit-post.json` SHA-256 `52960048216781b971a88c096e11675fdb9a88bda0a303b582f4d96e1f1e1913` 与部署前18笔access probe逐行比较：ID集合完全相同，18/18记录全字段相同，9笔目标Modal原reserve合计1420 cents、`actual=NULL`和billing保持。工作区gross从$6.30升至$7.45，是后续真实计量采样，不是本补丁结算。

自然后核 `status-full-post.json` SHA-256 `486a00b3ddc95072a6ae2ffdf2239705bf3af661b40ee141cc072af9f1397c5d` 与 `evidence-manifest.json` SHA-256 `06833fdca0f60f3fc52c697f717c85b44f0b47436341d6ebdc9cfa971a6d0f93`：有效held $14.50＝进行中dev256 eval $2.80＋非Modal未知 $11.70；Modal held $2.80，历史终态Modal原reserve $42.52仍展示，平台gross限$42.50/net限$30和研究$100批准不变，stop为null、旧PAUSE decision不变、timer active。S已由正常timer推进至25,034,752并留新checkpoint；当前唯一活动job为dev256 eval `eval-1b3462b7f4d08a751308a362a164cf94:1` running。这是既有调度自然进度，不能把训练/评测归因于056源码部署。

最终结论：056单文件部署及预算视图修正验收通过；9笔旧预留仅从**未来有效占用**中去除，原始账本与未知现金保持。dev256仍运行，结果尚未验收。

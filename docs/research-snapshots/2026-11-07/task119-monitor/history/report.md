# STS2 自主研究实验历史

37 条实际训练/资源探针/诊断，27 个不同正式dev点；另列 10 个未执行方案与 35 条工程经历。正式点全部0胜，最高已保存均值为114对照0.431070796875，目标未达到。

当前保存状态：M116与S115第二段运行中。115首段无中点dev；116无正式成绩；117/118未执行。running是02:26Z附近不可变快照，不是生成时实时查询。

[机器时间线](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task119-monitor/history/experiments.json) · [全部来源与SHA](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task119-monitor/history/sources.json)。剂量单位为环境决策，不是独立游戏数。

|实验|类型 / 状态|训练剂量（口径）|正式进度点（胜/n）|
|---|---|---:|---|
|[task022-s-bf16-probe](#task022-s-bf16-probe) task022 s bf16 probe|resource_probe / failed|完成学习0；采集131,072|无正式结果|
|[task023-s-fp32-probe](#task023-s-fp32-probe) task023 s fp32 probe|resource_probe / completed|131072|无正式结果|
|[original-s-formal](#original-s-formal) Original S Silent A10 formal training|training / completed|25034752|10.09M: 0.258339 (0/256)；25.03M: 0.255897 (0/256)|
|[original-m-formal](#original-m-formal) Original M Silent A10 formal training|training / completed|10092544|10.09M: 0.236548 (0/256)|
|[task046-m-probe](#task046-m-probe) task046 m probe|resource_probe / completed|131072|无正式结果|
|[task046-l-probe](#task046-l-probe) task046 l probe|resource_probe / failed|完成学习0；采集131,072|无正式结果|
|[task062-entropy002](#task062-entropy002) Lower entropy .002|training / completed|10092544|10.09M: 0.204604 (0/256)|
|[task062-epochs4](#task062-epochs4) Four PPO epochs|training / completed|10092544|10.09M: 0.184206 (0/256)|
|[task065-reward-choices](#task065-reward-choices) Public reward-choice candidate input|training / completed|10092544|10.09M: 0.247965 (0/256)|
|[task064-behavior-v2](#task064-behavior-v2) S25M behavior and two pilot loop diagnoses|diagnostic / completed|无新增训练；诊断/复演动作与样本见明细|无正式结果|
|[task070-lambda1](#task070-lambda1) Lambda one|training / completed|10092544|10.09M: 0.245543 (0/256)|
|[task071-hp-potential](#task071-hp-potential) HP potential .1|training / completed|10092544|10.09M: 0.242020 (0/256)|
|[task072-lambda1-rollout256](#task072-lambda1-rollout256) Lambda one with 512×256 rollout|training / completed|10092544|10.09M: 0.217663 (0/256)|
|[task076-act-bonus](#task076-act-bonus) First Act2/Act3 entry bonus +1|training / completed|10092544|10.09M: 0.240249 (0/256)|
|[task078-a0-feasibility](#task078-a0-feasibility) Fresh Silent A0 training feasibility probe|training / completed|5111808|无正式结果|
|[task081-diagnostic](#task081-diagnostic) 076首幕胜负与循环公开复演|diagnostic / completed|无新增训练；诊断/复演动作与样本见明细|无正式结果|
|[task082-failed-attempt](#task082-failed-attempt) S循环成本首次部署：发布失败|training_attempt / failed|accepted 0；观察2,621,440|无正式结果|
|[task084-s](#task084-s) S：循环惩罚 .01 长训|training / completed|100007936|25.03M: 0.259488 (0/256)；50.07M: 0.268037 (0/256)；100.01M: 0.281326 (0/256)|
|[task088-m](#task088-m) M：循环惩罚 .01 长训|training / completed|25034752|25.03M: 0.216189 (0/256)|
|[task089-failed-attempt](#task089-failed-attempt) 公开变化辅助目标首次GPU运行：保存失败|training_attempt / failed|accepted 0；观察≥1,310,720|无正式结果|
|[task090-diagnostic](#task090-diagnostic) M20M四例公开行为诊断|diagnostic / completed|无新增训练；诊断/复演动作与样本见明细|无正式结果|
|[task094-s](#task094-s) S：公开状态变化辅助目标 .1|training / completed|50069504|25.03M: 0.255706 (0/256)；50.07M: 0.272954 (0/256)|
|[task095-diagnostic](#task095-diagnostic) 09425M公开变化预测校准|diagnostic / completed|无新增训练；诊断/复演动作与样本见明细|无正式结果|
|[task097-s](#task097-s) S：奖励候选输入 + 公开变化辅助目标|training / completed|100007936|25.03M: 0.265170 (0/256)；50.07M: 0.253676 (0/256)；100.01M: 0.277133 (0/256)|
|[task099-s](#task099-s) S：候选感知 critic|training / completed|200015872|25.03M: 0.272552 (0/256)；50.07M: 0.282829 (0/256)；100.01M: 0.293940 (0/256)；150.08M: 0.350540 (0/256)；200.02M: 0.342184 (0/256)|
|[task100-diagnostic](#task100-diagnostic) 09725M八例奇巧、奖励、路线与战斗公开诊断|diagnostic / completed|无新增训练；诊断/复演动作与样本见明细|无正式结果|
|[task104-s](#task104-s) S：公开手选 B0，从零训练至25M|training / completed|25034752|无正式结果|
|[task109-s](#task109-s) S：直接过夜续训，最终445.8M|training / completed|445775872|445.78M: 0.404479 (0/256)|
|[task110-final-dev](#task110-final-dev) 445M最终检查点的正式dev256与固定历史对照|formal_evaluation / completed|仅评测；模型445,775,872；新增训练0|无正式结果|
|[task111-training-diagnostic](#task111-training-diagnostic) 109末段训练日志诊断|diagnostic / completed|无新增训练；诊断/复演动作与样本见明细|无正式结果|
|[task112-diagnostic](#task112-diagnostic) 445M八例原动作公开回放诊断|diagnostic / completed|无新增训练；诊断/复演动作与样本见明细|无正式结果|
|[task113-temperature-diagnostic](#task113-temperature-diagnostic) 固定445M策略：T1与T1.2及真实回报校准|diagnostic / completed|无新增训练；诊断/复演动作与样本见明细|无正式结果|
|[task114-control](#task114-control) S对照：普通PPO续训50M|training / completed|495845376|495.85M: 0.431071 (0/256)|
|[task114-strategic-kl](#task114-strategic-kl) S处理：Map/Rest均匀前向KL β=.02|training / completed|495845376|495.85M: 0.383822 (0/256)|
|[task114-boss-diagnostic](#task114-boss-diagnostic) 114成功Act2 Boss五例原动作复盘|diagnostic / completed|无新增训练；诊断/复演动作与样本见明细|无正式结果|
|[task115-s](#task115-s) S：从114对照继续固定新增200M|training / running|636092416|无正式结果|
|[task116-m](#task116-m) M：当前B0配方全新100M容量试验|training / running|33030144|无正式结果|

## 实验详情

<a id="task022-s-bf16-probe"></a>
### task022-s-bf16-probe · task022 s bf16 probe

状态：failed（原记录：failed）。

假设：Check full-rollout numerical consistency and device resource feasibility before formal training.

结果：BF16 pre-optimizer parity rejected; collected rollout is not trained actions.

改动：BF16 pre-optimizer parity rejected; collected rollout is not trained actions.

边界：A single resource/numerical gate is not a formal trained-policy quality result.；Collected decisions before a failed optimizer gate are not completed learning updates.

证据：[acceptance-022.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/acceptance-022.json) (`07de3fa68fbf`)

<a id="task023-s-fp32-probe"></a>
### task023-s-fp32-probe · task023 s fp32 probe

状态：completed（原记录：completed）。

假设：Check full-rollout numerical consistency and device resource feasibility before formal training.

结果：One batch, no steady-state or efficacy estimate; explained_variance invalid from known telemetry variable shadowing.

改动：strict FP32 TF32 off

边界：A single resource/numerical gate is not a formal trained-policy quality result.；Collected decisions before a failed optimizer gate are not completed learning updates.

证据：[acceptance-023.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/acceptance-023.json) (`410b07a2ede4`)

<a id="original-s-formal"></a>
### original-s-formal · Original S Silent A10 formal training

状态：completed（原记录：completed）。

假设：Frozen S capacity arm with fresh Silent A10 stream and strict FP32. First evaluated milestone uses exactly 77 complete rollouts.

结果：S10M→S25M did not establish improvement; paired delta -0.00244143359375 with CI spanning zero, not certain regression.

改动：Frozen S architecture capacity arm

问题与恢复：S25M formal evaluation succeeded; subsequent research agent returned invalid_response and classifier surfaced research_usage_unverified. Original failed job, unknown usage/cost and prior WAIT/PAUSE preserved; bounded Tasks059/061/063 recovery launched new pilots rather than repeating S training.

边界：Single initialization and reused development seeds; no blind confirmation.；Exact start/end UTC and per-experiment final cash are unknown in this bounded extraction.

证据：[formal-milestone-1-evidence.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/formal-s/formal-milestone-1-evidence.json) (`7926cc362491`)，[formal-spec.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task071/formal-spec.json) (`7c68cb72b415`)，[ready.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task058/train-25m/ready.json) (`2785cfba94de`)，[ready.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task058/eval-25m/ready.json) (`eb8dfc1f9cd2`)，[evidence-summary.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task058/evidence-summary.json) (`288eef237520`)，[root-paired-analysis.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task058/root-paired-analysis.json) (`429824cda874`)，[report.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task064/v2/report.md) (`c8ba24697ed0`)，[report.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task063/report.md) (`a989f5e6c4da`)

<a id="original-m-formal"></a>
### original-m-formal · Original M Silent A10 formal training

状态：completed（原记录：completed）。

假设：Compare the M architecture against S at matched new-decision milestones and the same frozen dev protocol.

结果：No wins on complete dev256; threshold not met.

改动：Frozen M architecture capacity arm

边界：Single initialization and reused development seeds; no blind confirmation.；Exact start/end UTC and per-experiment final cash are unknown in this bounded extraction.；Final M10M head followed two train jobs; its immediate parent was not extracted from the small archived milestone summary, so no parent is guessed.

证据：[formal-milestone-1-evidence.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/formal-m/formal-milestone-1-evidence.json) (`2a4725800fc2`)，[spec.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task046/formal-m/spec.json) (`0ce95ef4a2aa`)

<a id="task046-m-probe"></a>
### task046-m-probe · task046 m probe

状态：completed（原记录：completed）。

假设：Check full-rollout numerical consistency and device resource feasibility before formal training.

结果：M gate passed; engineering checkpoint is excluded from formal M lineage.

改动：M gate passed; engineering checkpoint is excluded from formal M lineage.

边界：A single resource/numerical gate is not a formal trained-policy quality result.；Collected decisions before a failed optimizer gate are not completed learning updates.

证据：[terminal-evidence.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task046/results/terminal-evidence.json) (`1c62c5fbfe61`)

<a id="task046-l-probe"></a>
### task046-l-probe · task046 l probe

状态：failed（原记录：failed）。

假设：Check full-rollout numerical consistency and device resource feasibility before formal training.

结果：First real4096 learner forward OOM: requested2.02GiB with1.23GiB free on79.17GiB device; not a formal L run.

改动：First real4096 learner forward OOM: requested2.02GiB with1.23GiB free on79.17GiB device; not a formal L run.

边界：A single resource/numerical gate is not a formal trained-policy quality result.；Collected decisions before a failed optimizer gate are not completed learning updates.

证据：[terminal-evidence.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task046/results/terminal-evidence.json) (`1c62c5fbfe61`)，[verification.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task046/verification.md) (`eb6161f8f76d`)

<a id="task062-entropy002"></a>
### task062-entropy002 · Lower entropy .002

状态：completed（原记录：completed）。

假设：Lower entropy pressure improves action preference separation and greedy progress.

结果：Clear regression on this matched-dev comparison at one initialization; no support for continuing this low-entropy recipe.

改动：ppo_config.ent_coef: 0.02 -> 0.002

边界：Single initialization, reused public dev256, exploratory comparisons without familywise correction; no blind evidence.；Training totals/learning rewards are not formal evaluation metrics.；Exact start/end UTC and realized cash not extracted; authorization and report timestamps are not substituted.

证据：[entropy002.spec.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task062/entropy002.spec.json) (`d190d69d40fb`)，[ready.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task062/entropy002-train/ready.json) (`0c2ac4f9d6d2`)，[ready.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task062/entropy002-eval/ready.json) (`757305825d3b`)，[analysis.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task060/entropy002-analysis/analysis.json) (`dc30f9a3618a`)

<a id="task062-epochs4"></a>
### task062-epochs4 · Four PPO epochs

状态：completed（原记录：completed）。

假设：Four PPO epochs improve the use of each fixed-size on-policy rollout.

结果：Clear regression on this matched-dev comparison at one initialization; no support for continuing this four-epoch recipe.

改动：ppo_config.epochs: 1 -> 4

边界：Single initialization, reused public dev256, exploratory comparisons without familywise correction; no blind evidence.；Training totals/learning rewards are not formal evaluation metrics.；Exact start/end UTC and realized cash not extracted; authorization and report timestamps are not substituted.

证据：[epochs4.spec.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task062/epochs4.spec.json) (`507fc9d7ecd6`)，[ready.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task062/epochs4-train/ready.json) (`1cee578a5a0a`)，[ready.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task062/epochs4-eval/ready.json) (`1207834bf816`)，[analysis.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task060/epochs4-analysis/analysis.json) (`0a713c575bce`)

<a id="task065-reward-choices"></a>
### task065-reward-choices · Public reward-choice candidate input

状态：completed（原记录：completed）。

假设：Public offer-specific card and alternative features improve fixed-greedy dev progress.

结果：No demonstrated improvement; paired interval spans zero, so neither improvement nor clear regression is established for this one initialization.

改动：Actor reads public reward_choices through the existing two reward macro pointers.

边界：Single initialization, reused public dev256, exploratory comparisons without familywise correction; no blind evidence.；Training totals/learning rewards are not formal evaluation metrics.；Exact start/end UTC and realized cash not extracted; authorization and report timestamps are not substituted.

证据：[spec.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task065/worker-packet-learner-gated/spec.json) (`e084e94cd3bc`)，[ready.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task065/reward-choices-train/ready.json) (`4944dc440444`)，[ready.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task065/reward-choices-eval/ready.json) (`972bf1966c92`)，[analysis.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task060/rewardchoices-analysis/analysis.json) (`c1d88e029a18`)

<a id="task064-behavior-v2"></a>
### task064-behavior-v2 · S25M behavior and two pilot loop diagnoses

状态：completed（原记录：completed）。

假设：Distinguish first-boss combat failures from premature EndTurn and identify public loops in sampled regressed arms.

结果：Selected baseline failures were actual Boss/other fights; no legal PlayCard existed at observed EndTurns. Selected regressions showed public shop/rest cancel cycles.

改动：Passive public observation of unchanged fresh greedy FP32 policies; selected16S25M +4entropy002 stuck +4epochs4 stuck.

问题与恢复：Original16-case v1 packet never reserved/launched; replaced by one reviewed24-case v2, not two actual diagnostic runs.

边界：Purposeful endpoint-selected samples; not estimates of dev-wide mechanism prevalence.；Old dev did not retain histories/state hashes: endpoint parity and diagnostic self-replay do not prove equality of every old action.；No counterfactual branch or optimality result.

证据：[report.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task064/v2/report.md) (`c8ba24697ed0`)，[findings.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task064/v2/findings.json) (`906162cc30bc`)，[terminal.receipt.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task064/v2/terminal.receipt.json) (`44c92553c2d2`)，[independent-result-acceptance.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task064/v2/independent-result-acceptance.md) (`9072015df05d`)，[root-v2-authorization.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task064/root-v2-authorization.json) (`e90bcc106a9e`)

<a id="task070-lambda1"></a>
### task070-lambda1 · Lambda one

状态：completed（原记录：completed）。

假设：Removing in-window GAE decay improves fixed-budget Silent A10 greedy progress.

结果：No improvement. The prespecified exploratory percentile interval is just below zero (upper bound about -0.000024), a borderline negative result rather than robust large deterioration; single-initialization and multiplicity limitations apply.

改动：ppo_config.lam: 0.99 -> 1.0

边界：Single initialization, reused public dev256, exploratory comparisons without familywise correction; no blind evidence.；Training totals/learning rewards are not formal evaluation metrics.；Exact start/end UTC and realized cash not extracted; authorization and report timestamps are not substituted.；Negative percentile interval upper bound is only -0.000023697851562504: borderline, not robust evidence of a large decline.

证据：[lambda1.spec.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task070/lambda1.spec.json) (`8a19a983b14f`)，[ready.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task070/accepted/train/ready.json) (`c225c44c9b47`)，[ready.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task070/accepted/eval/ready.json) (`8130ad46f01d`)，[analysis.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task074/lambda1/analysis.json) (`ad5c1c5eb5ff`)

<a id="task071-hp-potential"></a>
### task071-hp-potential · HP potential .1

状态：completed（原记录：completed）。

假设：Potential differences may improve early survival credit under an approximate critic.

结果：Exploratory paired interval below zero for this initialized model; no gain from HP potential.

改动：Add potential shaping from 0.1 times public HP divided by public max HP.

边界：Single initialization, reused public dev256, exploratory comparisons without familywise correction; no blind evidence.；Training totals/learning rewards are not formal evaluation metrics.；Exact start/end UTC and realized cash not extracted; authorization and report timestamps are not substituted.；HP potential complete fresh A10 return offset is -0.08; exact value offset cancels TD changes. Approximate learner benefit was the hypothesis.

证据：[spec.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task071/worker-packet-learner-gated/spec.json) (`4285a8d6d729`)，[ready.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task071/accepted/train/ready.json) (`65db19ab9b63`)，[ready.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task071/accepted/eval/ready.json) (`f43f46b32192`)，[analysis.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task074/hp01/analysis.json) (`98bae80956c0`)，[report.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task068/report.md) (`2200836dc5dc`)

<a id="task072-lambda1-rollout256"></a>
### task072-lambda1-rollout256 · Lambda one with 512×256 rollout

状态：completed（原记录：completed）。

假设：Longer on-policy windows at lambda one reduce critic bootstrap boundaries and improve fixed-budget progress.

结果：Exploratory paired interval below zero versus lambda1; no gain from longer rollout.

改动：rollout_shape: 2048 envs x 64 steps -> 512 envs x 256 steps; batch constant

边界：Single initialization, reused public dev256, exploratory comparisons without familywise correction; no blind evidence.；Training totals/learning rewards are not formal evaluation metrics.；Exact start/end UTC and realized cash not extracted; authorization and report timestamps are not substituted.；Changing 2048env×64 to512env×256 keeps decisions/update but changes state correlation, bootstrap/aux exact-target frequency and compute; comparison is not pure GAE depth.

证据：[lambda1-rollout256.spec.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task072/lambda1-rollout256.spec.json) (`f115412f1909`)，[ready.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task072/accepted/train/ready.json) (`00b6bc1b6433`)，[ready.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task072/accepted/eval/ready.json) (`5306b0053f11`)，[analysis.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task074/rollout256/analysis.json) (`d0e9e5e9dcc1`)

<a id="task076-act-bonus"></a>
### task076-act-bonus · First Act2/Act3 entry bonus +1

状态：completed（原记录：completed）。

假设：A discrete reward for reaching the next act may strengthen boss and survival credit.

结果：Exploratory paired interval below zero for this initialized model; no gain from act-entry bonus.

改动：Award +1 learning reward at the first public entry into each of Act 2 and Act 3.

边界：Single initialization, reused public dev256, exploratory comparisons without familywise correction; no blind evidence.；Training totals/learning rewards are not formal evaluation metrics.；Exact start/end UTC and realized cash not extracted; authorization and report timestamps are not substituted.；Act-entry bonus changes the training objective and can reorder policy preferences; dev scoring remains unshaped.

证据：[spec.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task076/worker-packet-learner-gated/spec.json) (`b6866a75e81f`)，[ready.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task076/accepted/train/ready.json) (`f74f5c9d3116`)，[ready.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task076/accepted/eval/ready.json) (`52700b7e359f`)，[analysis.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task076/analysis/analysis.json) (`a4c4b6d4ce17`)，[history-check.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task076/analysis/history-check.json) (`9c9fbd541372`)

<a id="task078-a0-feasibility"></a>
### task078-a0-feasibility · Fresh Silent A0 training feasibility probe

状态：completed（原记录：completed）。

假设：Whether easier autonomous A0 training supplies materially deeper/positive trajectories for a possible later curriculum.

结果：Completed5,111,808A0 decisions;32,084closed episodes,341Act2,0Act3,0wins. This dose did not supply hoped-for deep successful episodes; no formal A0/A10 policy quality measurement.

改动：{"ascension":0,"baseline":"originalS PPO/architecture/strictFP32","new_namespace":"A0CURR078SILENTNOV05","rollout_shape":{"envs":2048,"rollout":64}}

问题与恢复：V1 failed before SandboxCreate RPC:74,737argv bytes exceeded65,536SDK limit; original failure/fence and unknown cost retained.；Task080 authorized one new v2 transport attempt with compressed verified payload; actual scientific recipe unchanged.；task080/v2-terminal.json is empty; relied on nonempty worker-report and saved terminal-ledger evidence, not that empty placeholder.

边界：These are closed-episode counts under a changing sampling policy, not greedy evaluation or iid generalization rates.；2048 in-flight episodes excluded from closed denominators; no reusable accepted checkpoint published.；No curriculum transfer was executed by this task.

证据：[design.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task078/design.md) (`b3af4b287df0`)，[worker-report.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task080/worker-report.json) (`560000bedbd7`)，[v2-terminal-ledger.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task080/v2-terminal-ledger.json) (`98cdac4739a8`)，[implementation.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task080/implementation.md) (`23c184ef2630`)，[packet-v2-ready.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task080/packet-v2-ready.json) (`bd1ed574559e`)

<a id="task081-diagnostic"></a>
### task081-diagnostic · 076首幕胜负与循环公开复演

状态：completed（原记录：completed）。

假设：检查固定自有策略的公开行为或机制；不作为训练样本或正式质量评测。

结果：44/44终态一致；15个Act2、8个首幕F17败例、21stuck、1cap的按结果取样。发现药水均首回合用、满血HEAL和商店移除取消循环；不是217次原训练跨幕轨迹。

边界：purposeful/reused exposed dev案例；不代表总体发生率或模型质量。；15 Act2/8 floor17 loss/21 stuck/1 capped为选择类别且有重叠；去重后44例，不可将类别数相加成45。

证据：[terminal.receipt.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task081/terminal.receipt.json) (`fd962b66191b`)，[root-launch-decision.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task081/root-launch-decision.json) (`88aafae81d5e`)，[packet.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task081/packet.json) (`649f4cad6f08`)，[scientific-report.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task081/analysis/scientific-report.md) (`6042f87f6c79`)

<a id="task082-failed-attempt"></a>
### task082-failed-attempt · S循环成本首次部署：发布失败

状态：failed（原记录：failed_no_accepted_checkpoint）。

假设：与后继084相同cycle_cost=.01科学配方。

结果：实际GPU训练到u20/2,621,440观察步，但发布NameError math，accepted steps=0；084是新fresh恢复，不继承失败权重。

问题与恢复：task084-publication-repair

证据：[actual-first-job.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task082/actual-first-job.json) (`4549d880d623`)，[root-recovery-decision.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task084/root-recovery-decision.json) (`d6474d3c8581`)

<a id="task084-s"></a>
### task084-s · S：循环惩罚 .01 长训

状态：completed（原记录：quality_stopped_at_100M）。

假设：对明确的商店/休息取消回访闭环施加 .01 成本，测试能否改善持续训练。

结果：25→50→100M进度.25949→.26804→.28133；100−50配对增量+.013288875、95%CI约[.001665,.025065]，仍0胜/0Act3，低于.33门槛停止。不能将剂量收益全部归因cycle成本。

改动：相对原S新增UI revisit cycle cost=.01；fresh S

问题与恢复：task082-failed-attempt；task084-publication-repair；task083-terminal-reserve-history；task086-budget-bridge

边界：单次初始化；复用dev256、多阶段与历史顺序对照；非blind泛化结果。；训练日志和终态摘要不能补造逐动作因果；费用未在此重新核算。；训练update622记录native_table_overflow=2，表身份及影响未知；eval无全程overflow计数，不能写all native flags zero。

证据：[root-recovery-decision.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task084/root-recovery-decision.json) (`d6474d3c8581`)，[actual-first-job.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task084/actual-first-job.json) (`e2904917c371`)，[dev25m-analysis.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task084/analysis/dev25m-analysis.json) (`936464d91514`)，[dev50m-analysis.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task084/analysis/dev50m-analysis.json) (`e1e67803b364`)，[dev100m-analysis.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task084/analysis/dev100m-analysis.json) (`f1c43c3b8124`)

<a id="task088-m"></a>
### task088-m · M：循环惩罚 .01 长训

状态：completed（原记录：quality_stopped_at_25M）。

假设：在同cycle成本与25M剂量下考察M容量是否比S改善。

结果：25M .21619，较084S25M差−.043298988，95%CI约[−.056397,−.030125]；0胜，仅6Act2，未过.23门槛。不是容量绝对上限结论。

改动：S→M；fresh初始化；其余科学配方沿084

问题与恢复：task088-platform-cap-hold

边界：单次初始化；复用dev256、多阶段与历史顺序对照；非blind泛化结果。；训练日志和终态摘要不能补造逐动作因果；费用未在此重新核算。；1200s vs S1500s分段及fresh_episode重启次数不同；平台cap曾暂停，不能把等待算训练。

证据：[root-scientific-selection150.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task088/root-scientific-selection150.json) (`a1dad880a2bd`)，[actual-start-notification.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task088/analysis/actual-start-notification.json) (`a70d515e5483`)，[dev25m-analysis.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task088/analysis/dev25m-analysis.json) (`b020a58d928d`)

<a id="task089-failed-attempt"></a>
### task089-failed-attempt · 公开变化辅助目标首次GPU运行：保存失败

状态：failed（原记录：failed_no_accepted_checkpoint）。

假设：与094相同transition auxiliary=.1科学配方。

结果：实际GPU运行但TorchVersion元数据不被weights_only安全加载接受，未产生accepted或持久checkpoint；094修字符串producer后fresh启动。

问题与恢复：task094-checkpoint-metadata-repair

证据：[root-actual-launch-record.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task091/root-actual-launch-record.json) (`af2f70819ad2`)，[first-worker-terminal-diagnostic.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task091/first-worker-terminal-diagnostic.json) (`db18524ccba3`)，[root-recovery-scientific-selection.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task094/root-recovery-scientific-selection.json) (`d2ed2b0d77bc`)

<a id="task090-diagnostic"></a>
### task090-diagnostic · M20M四例公开行为诊断

状态：completed（原记录：completed）。

假设：检查固定自有策略的公开行为或机制；不作为训练样本或正式质量评测。

结果：观察Beckon能量顺序、T1格挡药无伤害使用、13营火全HEAL与实际过首幕案例；只说明局部行为，非M25正式质量。

问题与恢复：task090-gc-startup-repair

边界：purposeful/reused exposed dev案例；不代表总体发生率或模型质量。

证据：[root-behavior-acceptance.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task090/root-behavior-acceptance.json) (`569d348ef876`)，[completed-outcome.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task090/completed-outcome.md) (`d5bf045fedc1`)，[root-GC-startup-recovery.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task090/root-GC-startup-recovery.json) (`98f613684e2e`)

<a id="task094-s"></a>
### task094-s · S：公开状态变化辅助目标 .1

状态：completed（原记录：quality_stopped_at_50M）。

假设：训练所选动作后的四维公开净变化，测试更明确的局部后果监督是否改善策略。

结果：25M .25571、50M .27295，0胜；50M对084同剂量增量+.004916324，95%CI[−.007219236,.017382480]跨零，未见明确质量收益；.28门槛停止。

改动：transition auxiliary coefficient=.1；fresh S；cycle cost=.01

问题与恢复：task089-failed-attempt；task094-checkpoint-metadata-repair

边界：单次初始化；复用dev256、多阶段与历史顺序对照；非blind泛化结果。；训练日志和终态摘要不能补造逐动作因果；费用未在此重新核算。

证据：[root-recovery-scientific-selection.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task094/root-recovery-scientific-selection.json) (`d2ed2b0d77bc`)，[root-actual-recovery-launch.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task094/root-actual-recovery-launch.json) (`8bbdc7da9b46`)，[dev25m-analysis.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task094/analysis/dev25m-analysis.json) (`2bf3839d21b9`)，[dev50m-analysis.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task094/analysis/dev50m-analysis.json) (`f78640e7fb50`)

<a id="task095-diagnostic"></a>
### task095-diagnostic · 09425M公开变化预测校准

状态：completed（原记录：completed）。

假设：检查固定自有策略的公开行为或机制；不作为训练样本或正式质量评测。

结果：4×128前缀；PlayCard敌HP等局部预测优于零变化基线。EndTurn23个非零损血全部向0收缩，但target-conditioned统计不能单独证明条件期望失准；药水仅12、无终局/Boss/后幕覆盖。

边界：purposeful/reused exposed dev案例；不代表总体发生率或模型质量。

证据：[receipt.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task095/actual-25m/receipt.json) (`b331fdc732a4`)，[interpretation-compact.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task095/actual-25m/interpretation-compact.json) (`11550dc3db9b`)，[interpretation-addendum-target-conditioning.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task095/actual-25m/interpretation-addendum-target-conditioning.json) (`aa9ff105f39f`)，[root-scientific-acceptance.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task095/root-scientific-acceptance.json) (`f4dfd1e37ee5`)

<a id="task097-s"></a>
### task097-s · S：奖励候选输入 + 公开变化辅助目标

状态：completed（原记录：quality_stopped_at_100M）。

假设：补入当前奖励选择候选表达，测试拿牌信息缺口是否限制策略。

结果：25/50/100M .26517/.25368/.27713，全部0胜/0Act3；50M经前瞻任务098改为诊断门继续，100M仍未达原.33门而停止。094无100M正式对照，不能构造其反事实曲线。

改动：reward_choices_v1输入；沿094的transition auxiliary=.1；fresh S

问题与恢复：task098-future50-gate-amendment

边界：单次初始化；复用dev256、多阶段与历史顺序对照；非blind泛化结果。；训练日志和终态摘要不能补造逐动作因果；费用未在此重新核算。；训练u304 bootstrap cap1保留；初始化张量哈希差异与输入处理改变不可混同为权重继承。

证据：[root-scientific-selection.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task097/root-scientific-selection.json) (`c2f7dafbe205`)，[root-first-actual-launch.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task097/root-first-actual-launch.json) (`730512d3de31`)，[dev25m-analysis.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task097/analysis/dev25m-analysis.json) (`3fb211268ce5`)，[dev50m-analysis.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task097/analysis/dev50m-analysis.json) (`e4cde1cd7288`)，[dev100m-analysis.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task097/analysis/dev100m-analysis.json) (`367753f6affb`)

<a id="task099-s"></a>
### task099-s · S：候选感知 critic

状态：completed（原记录：completed_200M_goal_unmet）。

假设：将合法候选h2的detach均值拼入value状态，以改善决策阶段价值预测；不声称解决长程信用。

结果：25/50/100/150/200M全点保留；100M对097同剂量+.016807430、95%CI[.005658202,.028243739]，但25M后runtime变化混杂。200M .342184、87Act2、0Act3/0胜；150M点值.35054更高不据此宣称确定退步，也不选最好点代替终点。

改动：state_legal_candidates_detached_mean_v1；W扩为[W,0]；沿097 reward_choices + .1 transition aux；fresh S；纯统计logging

问题与恢复：task103-runtime-migration

边界：单次初始化；复用dev256、多阶段与历史顺序对照；非blind泛化结果。；训练日志和终态摘要不能补造逐动作因果；费用未在此重新核算。；25M前Sandbox16，之后Functions32，同模型/optimizer/RNG恢复但运行环境与采样吞吐改变；非纯critic单因果。；累计训练table_overflow32、12次恢复丢弃24576在途局；没有200M匹配控制。；task099→task104仅决策/计划关系，不是权重继承；104由主整合者另记。

证据：[root-scientific-selection.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task099/root-scientific-selection.json) (`cc85f090dd5a`)，[root-actual-activation-acceptance.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task099/root-actual-activation-acceptance.json) (`ccd81e2e29dd`)，[dev25m-analysis.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task099/analysis/dev25m-analysis.json) (`31b63d75526b`)，[dev50m-analysis.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task099/analysis/dev50m-analysis.json) (`c0e600e1a1bd`)，[dev100m-analysis.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task099/analysis/dev100m-analysis.json) (`d6893a9851c3`)，[dev150m-analysis.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task099/analysis/dev150m-analysis.json) (`a72e3f3dc37d`)，[dev200m-analysis.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task099/analysis/dev200m-analysis.json) (`f843b1074113`)，[root-dev200-scientific-acceptance.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task099/root-dev200-scientific-acceptance.json) (`eceb01723f17`)，[execution-mode-actual-cutover25m.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task099/analysis/execution-mode-actual-cutover25m.json) (`73380578c5db`)

<a id="task100-diagnostic"></a>
### task100-diagnostic · 09725M八例奇巧、奖励、路线与战斗公开诊断

状态：completed（原记录：completed）。

假设：检查固定自有策略的公开行为或机制；不作为训练样本或正式质量评测。

结果：8/8 fresh CPU greedy摘要与原GPU终态吻合且均自身可复演，不证明原GPU动作序列。最好4例未到Act2Boss；5个奇巧+合法主动弃牌同回合机会均未接组合，实际72弃牌prompt无奇巧候选。78奖励组76拿2跳、28营火全HEAL；数据不回灌。

边界：purposeful/reused exposed dev案例；不代表总体发生率或模型质量。

证据：[root-actual-execution-acceptance.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task100/root-actual-execution-acceptance.json) (`5561ee70e161`)，[root-scientific-acceptance.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task100/root-scientific-acceptance.json) (`48bfd8be54d9`)，[receipt.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task100/actual/receipt.json) (`1a43c2199afd`)，[science-preexecution-binding.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task100/science-preexecution-binding.json) (`fa7606b7ad01`)

<a id="task104-s"></a>
### task104-s · S：公开手选 B0，从零训练至25M

状态：completed（原记录：completed）。

假设：把可公开确定的 PromptSelect 手牌关系送入现有候选编码，改善手选的归纳偏置。

结果：实际完成25,034,752步，但没有本分支25M正式dev；用户改为直接过夜训练109。

改动：B0 public_handselect 输入；保留候选感知 critic、transition aux .1、cycle cost .01；未继承099权重。

问题与恢复：首段资源预算映射415误配，按保存修复记录改映射后恢复；不改变科学配方。；第二段已实际完成但旧collector canonical pending；自动流程在任何dev发起前明确停用，原失败/待收集状态保留。

边界：099为配方参照，不是权重父模型。；不得用09925M冒充同分支25M前测。

证据：[root-final-scientific-plan-acceptance.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task104/root-final-scientific-plan-acceptance.json) (`1b6e9e3d27b9`)，[root-actual-fallback-submission-acceptance.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task104/root-actual-fallback-submission-acceptance.json) (`8be24ab8d8c4`)，[root-user-direct-route.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task109/root-user-direct-route.json) (`2bcacbd49031`)，[same-branch-baseline-availability.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task110/analysis/same-branch-baseline-availability.json) (`aa3998005c65`)

<a id="task109-s"></a>
### task109-s · S：直接过夜续训，最终445.8M

状态：completed（原记录：completed）。

假设：在同一B0配方上延长自生成训练，检查学习是否继续积累。

结果：正式均值.40448，0/256胜，145到Act2、1到Act3；相对099200M主要实用对照提升+.062294，95%CI[.041939,.082223]。

改动：配方不变；用户要求一个8小时独立Function、定期持久检查点，无Raven自动研究接力。

问题与恢复：按8小时max_hours正常终止；旧status仍running是早于final的快照，不代表终态仍活跃。；训练结束后按用户新请求单独执行110正式dev，未在过夜训练内自动评测。

边界：剂量、B0输入及运行历史同时不同，不能只归因步数或架构。；同分支25M没有正式评测。；109仅保留尾部755更新训练raw，不能当完整3210更新历史。

证据：[root-user-direct-route.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task109/root-user-direct-route.json) (`2bcacbd49031`)，[root-overnight-arranged.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task109/root-overnight-arranged.json) (`dd2ee5efaded`)，[root-terminal-confirmation.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task109/after-overnight-status/root-terminal-confirmation.json) (`ca504ec343c1`)，[run--final.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task109/after-overnight-status/run--final.json) (`c62a9fb587ac`)，[root-final-evaluation-acceptance.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task110/root-final-evaluation-acceptance.json) (`9980333dd538`)，[same-branch-baseline-availability.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task110/analysis/same-branch-baseline-availability.json) (`aa3998005c65`)，[report.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task111/report.md) (`ead0cba46f43`)

<a id="task110-final-dev"></a>
### task110-final-dev · 445M最终检查点的正式dev256与固定历史对照

状态：completed（原记录：completed）。

假设：原始设计未知，不补造。

结果：同256种子对099200M主要比较、099150M次要比较均显示进度提升；全部仍0胜，目标未达到。

边界：唯一20k PCG64110099矩阵；单训练实例、重复使用开发种子。；该条引用task109的同一次dev，不应重复计入评测调用或曲线。

证据：[root-final-evaluation-acceptance.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task110/root-final-evaluation-acceptance.json) (`9980333dd538`)，[analysis-plan.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task110/analysis/analysis-plan.json) (`3415bf8e225c`)，[analysis-execution-receipt.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task110/analysis/analysis-execution-receipt.json) (`1edb962a7205`)

<a id="task111-training-diagnostic"></a>
### task111-training-diagnostic · 109末段训练日志诊断

状态：completed（原记录：completed）。

假设：原始设计未知，不补造。

结果：现有755行/u2647–3401尾部约99M决策中Act3占.1846%；693次table flag无法归属具体表。

边界：日志只保留本次3210更新的23.52%；GAE自举残差不是MC校准，不能据此认定critic欠拟合。

证据：[report.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task111/report.md) (`ead0cba46f43`)，[read-receipt.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task111/read-receipt.json) (`ccaec588c609`)

<a id="task112-diagnostic"></a>
### task112-diagnostic · 445M八例原动作公开回放诊断

状态：completed（原记录：completed）。

假设：原始设计未知，不补造。

结果：8例3161步原动作重建全匹配；32次营火全HEAL/0满血，25次可避精英均避；有毒/小刀协同、药水时序和Sandpit规则错误；136手选帧580候选B0无确定错配。

边界：固定分层目的样本，不能外推总体行为率；没有新actor或反事实试验。

证据：[root-final-acceptance.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task112/root-final-acceptance.json) (`90a843f65332`)，[scientific-report.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task112/analysis/scientific-report.md) (`859fcafea394`)，[execution-receipt.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task112/actual/public-output/execution-receipt.json) (`20f428e147f2`)，[summary.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task112/pointer-check/summary.json) (`ada3f9dab529`)

<a id="task113-temperature-diagnostic"></a>
### task113-temperature-diagnostic · 固定445M策略：T1与T1.2及真实回报校准

状态：completed（原记录：completed）。

假设：区分战略替代动作支持集中与全局采样温度影响，并检查首Act价值预测对真实剩余训练回报。

结果：T1.2−T1=-.026836，CI[-.055866,+.002518]，未见升温收益但不能断言确定退步；营火SMITH入口概率极低。

问题与恢复：一次分析在RNG前因概率1+2.22e-16被严格边界拒绝；保留原失败，独审1e-12容差且不clip后，同输入一次有界恢复。

边界：128新隔离种子配对，非greedy正式dev；温度效应不是训练熵因果。；MC偏差不能单独确诊容量/优化/GAE问题。

证据：[science-plan.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task113/science-plan.json) (`d54683db6ea4`)，[ready.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task113/actual/ready.json) (`d73253ea3d21`)，[handle.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task113/actual/handle.json) (`67f6088ec9ae`)，[compact-results.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task113/analysis/compact-results.json) (`c2f619695d2e`)，[scientific-report.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task113/analysis/scientific-report.md) (`8748346e6e3e`)

<a id="task114-control"></a>
### task114-control · S对照：普通PPO续训50M

状态：completed（原记录：completed）。

假设：Near-deterministic Map/RestSite legal policy support prevents experience with alternative strategic choices; a forward-KL-to-uniform term can restore support with nonvanishing logit gradients without applying the extra term to Combat

结果：普通续训相对父模型+.026592，CI[.007415,.046396]，仍0胜；被选为115父模型。

改动：β=0，普通PPO续训和同一被动日志。

问题与恢复：训练native table overflow=795，影响未知；不等于formal eval瞬态flag0。

边界：每臂仅一次训练实例，episode bootstrap不包含训练随机性。；同祖先完整恢复后各自新种子范围；共享445M祖先不能重复计为新算力。

证据：[science-plan.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task114/science-plan.json) (`626ffb189247`)，[final.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task114/actual/control/final.json) (`6b9c7ebf43bb`)，[provider-terminal-receipt.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task114/actual/control/provider-terminal-receipt.json) (`2d821cb24f37`)，[control-submission-receipt.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task114/ops/control-submission-receipt.json) (`5d0b0044d484`)，[analysis-result.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task114/analysis/actual/analysis-result.json) (`d3dbd6bf96e1`)，[scientific-report.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task114/analysis/scientific-report.md) (`62f03642e243`)，[cycle-mechanism-note.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task114/training-analysis-actual/cycle-mechanism-note.md) (`d9b0698706f6`)

<a id="task114-strategic-kl"></a>
### task114-strategic-kl · S处理：Map/Rest均匀前向KL β=.02

状态：completed（原记录：completed）。

假设：Near-deterministic Map/RestSite legal policy support prevents experience with alternative strategic choices; a forward-KL-to-uniform term can restore support with nonvanishing logit gradients without applying the extra term to Combat

结果：T−C=-.047248，CI[-.068580,-.026065]；该具体处理未奏效，不晋升。

改动：仅Map/RestSite≥2合法候选加入KL(Ulegal||π)，β=.02，各phase固定1/2；保持T1采样、entropy.02、奖励和模型。

问题与恢复：训练native table overflow=66，影响未知；不等于formal eval瞬态flag0。；战略phase熵明显提高，但训练Rest闭环183337（对照14），SMITH→取消样例；训练循环不是正式greedy stuck。

边界：每臂仅一次训练实例，episode bootstrap不包含训练随机性。；同祖先完整恢复后各自新种子范围；共享445M祖先不能重复计为新算力。

证据：[science-plan.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task114/science-plan.json) (`626ffb189247`)，[final.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task114/actual/treatment/final.json) (`f25082f18e6a`)，[provider-terminal-receipt.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task114/actual/treatment/provider-terminal-receipt.json) (`7f14a61355b3`)，[treatment-submission-receipt.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task114/ops/treatment-submission-receipt.json) (`3653349f78c3`)，[analysis-result.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task114/analysis/actual/analysis-result.json) (`d3dbd6bf96e1`)，[scientific-report.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task114/analysis/scientific-report.md) (`62f03642e243`)，[cycle-mechanism-note.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task114/training-analysis-actual/cycle-mechanism-note.md) (`d9b0698706f6`)

<a id="task114-boss-diagnostic"></a>
### task114-boss-diagnostic · 114成功Act2 Boss五例原动作复盘

状态：completed（原记录：completed）。

假设：原始设计未知，不补造。

结果：C固定4例KnowledgeDemon、T唯一TheInsatiable均由公开原动作证明过Boss；毒与防御协同，T实际使用FranticEscape管理Sandpit。

边界：只选成功目的样本，不能估总体战术率；T正例不能证明KL导致该打法。

证据：[boss-case-report.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task114/post-dev-boss-replay/analysis/boss-case-report.md) (`6049a2353417`)，[execution-receipt.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task114/post-dev-boss-replay/actual/public-output/execution-receipt.json) (`805f61634d63`)，[actual-acceptance.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task114/post-dev-boss-replay/independent-review/actual-acceptance.json) (`6d916bddfefe`)

<a id="task115-s"></a>
### task115-s · S：从114对照继续固定新增200M

状态：running（原记录：running）。

假设：普通PPO继续训练仍有进步，固定额外200M检查长期学习；中点仅工程检查，不设质量门槛。

结果：S1实际100,007,936新步完成，无中点dev；S2按保存快照运行，最终质量未知。

改动：配方不变：β0、transition aux.1、cycle.01、gamma1/lambda.99/rollout64/epochs1。

问题与恢复：S1 inspection记录1927次post-step table flags，非游戏数；其他7项列出flag为0；具体表/消费位置未记录，不能据此停训或归因。；S1正式终态收集完成后按独立准入启动S2；两段有fresh-episode恢复边界。

边界：任何训练进度/奖励均值不能替代dev。；live status与最新durable checkpoint不同步，已分别保存。

证据：[science-plan.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task115/science-plan.json) (`548320e5ba99`)，[provider-terminal-receipt.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task115/actual/segment1/provider-terminal-receipt.json) (`0e0773dc8134`)，[final.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task115/actual/segment1/collected-metadata/final.json) (`6b0f55b1aeb1`)，[inspection.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task115/actual/segment1/training-inspection/inspection.json) (`e937baec5bc6`)，[submission-receipt.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task115/segment2-actual-binding-preparation/submission-receipt.json) (`56c97b6aa024`)，[S2-observation.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task116/actual/observer-status-20261107T022640Z/S2-observation.json) (`432ceaa41f09`)，[report.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task115/observation-overflow-diagnosis/report.md) (`45234cb4610d`)

<a id="task116-m"></a>
### task116-m · M：当前B0配方全新100M容量试验

状态：running（原记录：running）。

假设：在当前输入/critic/aux配方下重新观察M容量；旧088M不是此配方，也不恢复其权重。

结果：实际GPU门禁通过且正在训练；尚无正式dev，不报告能力提升或下降。

改动：相对当前S主要是M容量；fresh随机初始化、新训练种子空间，非成熟S权重扩展。

边界：fresh M100M与成熟S不是同剂量容量对照。；6小时上限下不足完整目标须标删失，不能伪称100M。

证据：[science-plan.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task116/science-plan.json) (`edf98387857c`)，[root-science-freeze.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task116/root-science-freeze.json) (`c687097c1fcb`)，[root-actual-startup-observation.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task116/root-actual-startup-observation.json) (`7fcc0706ea71`)，[submission-receipt.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task116/ops/submission-receipt.json) (`e55df5a91d68`)，[M-observation.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task116/actual/observer-status-20261107T022640Z/M-observation.json) (`aa887bb3f83a`)


## 未执行方案

- **task060-fork-curriculum**：Controlled fork and curriculum research options。Proposal only; not an executed cross-config or cross-ascension transfer. [fork-curriculum-proposal.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task060/fork-curriculum-proposal.md)
- **task064-behavior-v1**：Original16-case behavior packet。Never reserved/launched; superseded by24-case v2. [root-v2-authorization.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task064/root-v2-authorization.json)
- **task067-same-recipe-extension**：Same-recipe accepted-checkpoint extension design。Design not an additional training arm. [design.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task067/design.md)
- **task075-a0-a10-curriculum**：Fixed A0→A10 versus A10→A10 schedule design。Proposed5.11M A0+4.98M A10 and restart-matched control not executed within tasks001–080. [engineering-options.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task075/engineering-options.md)
- **task105-sil-fresh**：SIL完整局正优势自模仿原型。仅前瞻方案/隔离代码准备，未作为实际训练或评测结果。 [root-prototype-authorization.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task105/root-prototype-authorization.json)
- **task106-sil-fork**：自有检查点SIL fork原型。仅前瞻方案/隔离代码准备，未作为实际训练或评测结果。 [root-owned-fork-preparation.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task106/root-owned-fork-preparation.json)
- **task107-ppo-fork**：099最终200M固定父PPO续训方案。仅前瞻方案/隔离代码准备，未作为实际训练或评测结果。 [root-science-freeze.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task107/root-science-freeze.json)
- **task108-handoff**：后续交接/训练及公开summary保留方案。仅前瞻方案/隔离代码准备，未作为实际训练或评测结果。 [root-science-and-mode-b-freeze.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task108/root-science-and-mode-b-freeze.json)
- **task117-value-calibration**：S115最终检查点的随机策略价值校准。仅前瞻方案/隔离代码准备，未作为实际训练或评测结果。 [root-preparation-scope.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task117/root-preparation-scope.json)
- **task118-m300**：M116固定完整100M父模型续至300M。仅前瞻方案/隔离代码准备，未作为实际训练或评测结果。 [root-single-call-execution-amendment.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task118/root-single-call-execution-amendment.json) [science-plan.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task118/science-plan.json)

## 工程经历（不是新增科学实验）

- **engineering-001-019**：environment, recipe, isolation, controller implementation and deployment gates; no verified formal GPU training before020。environment, recipe, isolation, controller implementation and deployment gates; no verified formal GPU training before020 [decisions-prefix-463546.jsonl](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task119-monitor/history/snapshots/decisions-prefix-463546.jsonl)
- **task020-s-tiny-failed-publish**：task020-s-tiny-failed-publish。 [execution-summary.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/task020/execution-summary.json)
- **task021-s-tiny-resume**：task021-s-tiny-resume。 [execution-summary.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/task021/execution-summary.json)
- **engineering-028**：CPU controller smoke failed before Sandbox submit; GPU/model creates0。CPU controller smoke failed before Sandbox submit; GPU/model creates0 [execution-summary.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/task028/execution-summary.json)
- **task033-tiny-unattended-gpu-loop**：task033-tiny-unattended-gpu-loop。 [acceptance-033.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/acceptance-033.json)
- **engineering-047-053**：capability/control/authority/monitor/budget repair and read-only reviews; do not add separate scientific training arms。capability/control/authority/monitor/budget repair and read-only reviews; do not add separate scientific training arms [report.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task051/report.md)
- **task054**：Budget override / historical terminal reserve handling。 [production-outcome.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task054/production-outcome.md)
- **task055**：Longitudinal dev context input。 [report.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task055/report.md)
- **task056**：Historical access-probe reserve visibility。 [production-outcome.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task056/production-outcome.md)
- **task057**：Original invalid-response handoff candidate。 [existing-diagnosis.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task057/handoff-candidate/existing-diagnosis.json)
- **task059**：Durable public final/schema errors and usage classification recovery。 [production-outcome.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task059/production-outcome.md)
- **task061**：Sole-ledger budget and monitor recovery。 [report.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task061/report.md)
- **task063**：Causal bounded research recovery and per-experiment budget accounting。 [report.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task063/report.md)
- **task066**：Pilot monitor lineage/display。 [production-outcome.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task066/production-outcome.md)
- **task068**：HP potential implementation/CPU semantics tests。 [report.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task068/report.md)
- **task069**：Research instruction configuration。 [report.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task069/report.md)
- **task073**：Credit pilot monitor display。 [production-outcome.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task073/production-outcome.md)
- **task074**：Frozen paired pilot analysis; no new model run。 [report.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task074/report.md)
- **task077**：Research prompt shortening。 [operations-production.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task077/operations-production.md)
- **task079**：Act-bonus monitor display。 [production-outcome.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task079/production-outcome.md)
- **task080**：A0 single transport recovery。 [implementation.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task080/implementation.md)
- **task084-publication-repair**：import math发布修复。082失败后只修artifact history导入，084新immutable identity/fresh初始化；未伪造accepted checkpoint。 [root-recovery-decision.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task084/root-recovery-decision.json)
- **task083-terminal-reserve-history**：已终态预留历史化。保留原未知费用/预留与事件；只将已证实终态历史预留移出活动占用，不宣称现金豁免。 [root-decision.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task083/root-decision.json) [live-postcheck.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task083/live-postcheck.json)
- **task085-blind-standby**：单次blind条件待命。只有精确dev资格才可释放；本段所有正式模型未达资格，不能把standby当已跑blind。 [root-deploy-decision.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task085/root-deploy-decision.json)
- **task086-budget-bridge**：同配置短段预算桥接。仅workspace余量阻塞时有界900/600秒同配置恢复；没有新增科学配方或提高平台限额。 [root-deploy-decision.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task086/root-deploy-decision.json) [live-quote-readonly.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task086/live-quote-readonly.json)
- **task087-transport-probe**：实际CPU传输与去重探针。一个正例CPU Sandbox exit0、重复同ID实际FileExistsError且run_count1；独立idleSandbox124无artifact。纯合成输入，不是科学训练或blind评测。 [independent-actual-acceptance.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task087/independent-actual-acceptance.md) [terminal-ledger.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task087/terminal-ledger.json)
- **task088-platform-cap-hold**：M20M平台额度等待与恢复。M20.447M曾真实平台cap hold；用户调整限额后由同accepted head续25M，等待不能算训练或新实验。 [root-cap-observation-block.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task088/root-cap-observation-block.json) [root-user-resume-M004.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task088/root-user-resume-M004.json)
- **task094-checkpoint-metadata-repair**：TorchVersion元数据保存修复。只将producer版本元数据转str，保留weights_only安全加载；独立验收后094 fresh恢复，不继承089失败权重。 [root-recovery-scientific-selection.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task094/root-recovery-scientific-selection.json) [root-actual-recovery-launch.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task094/root-actual-recovery-launch.json)
- **task089-cpu-smoke**：公开变化辅助头512步CPU工程smoke。真实fresh A10 CPU512步/4env/2updates、checkpoint resume；12终局HP标签、0药水机会。优化前工程验证，不是正式科学arm/质量结果。 [native-smoke.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task089/integration/native-smoke.json) [proposal-science.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task089/proposal-science.md)
- **task090-gc-startup-repair**：CPU诊断GC启动修复。初次wrapper关闭ServerGC，2.67秒失败且0局/0动作；保留失败后明确一次有界恢复，4案例不换选。 [root-GC-startup-recovery.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task090/root-GC-startup-recovery.json) [completed-outcome.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task090/completed-outcome.md)
- **task098-future50-gate-amendment**：097未来50M改诊断门。用户要求更高早期容忍后，在50M结果前更改未来50M进度/胜局门为诊断；25M已通过历史不改，100M原.33门仍执行；不改变当前worker科学。 [root-actual-future50-acceptance.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task098/root-actual-future50-acceptance.json)
- **task103-runtime-migration**：099 Sandbox16→Functions32运行环境迁移。099已接受25M处恢复同模型/optimizer/RNG，首次Function实际25M→49.94M并完成独立发布/恢复审计。103是runtime与CPU并行度变更，不是独立科学arm或新fresh权重；对09950M以后比较构成执行混杂。 [root-formal-functions32-migration-completion.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task103/root-formal-functions32-migration-completion.json) [execution-mode-actual-cutover25m.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task099/analysis/execution-mode-actual-cutover25m.json) [root-actual-sdk-repair-and-hold-classification.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task103/root-actual-sdk-repair-and-hold-classification.json)
- **task104-budget-collector-recovery**：预算映射修复与collector/本地镜像恢复保留；实际25M checkpoint得到独验，但该分支无dev，用户改为109直接Function。。预算映射修复与collector/本地镜像恢复保留；实际25M checkpoint得到独验，但该分支无dev，用户改为109直接Function。 [root-actual-repair-and-child-output-acceptance.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task104/root-actual-repair-and-child-output-acceptance.json) [same-branch-baseline-availability.json](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task110/analysis/same-branch-baseline-availability.json)
- **task115-overflow-readonly**：1927 table flags固定源码审计与现有回放availability检查；未修native、未改M/S2、未据低频认定无害。。1927 table flags固定源码审计与现有回放availability检查；未修native、未改M/S2、未据低频认定无害。 [report.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task115/observation-overflow-diagnosis/report.md) [existing-evidence-availability.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/task115/observation-overflow-diagnosis/existing-evidence-availability.md)
- **engineering-formal-intake**：unexecuted scheduling/resource design; not a measured32CPU experiment。unexecuted scheduling/resource design; not a measured32CPU experiment [formal-intake.md](/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2/artifacts/modal-research/2026-11-04/research/formal-intake.md)

## 使用边界

- 本目录是已保存证据的历史投影，不是SDK/账本实时查询；running必须显示as-of。
- 正式dev只用已接受结果；训练reward、闭局率、诊断样本不混入正式曲线。
- 未知时间、身份、费用为null；提交或观察时间不冒充真实起止时刻。
- 原S10/25同一谱系；099→104只继承配方，104重新随机初始化。
- 114两臂共用445M祖先，各新增50M；不可重复累计祖先为新增训练。
- 115S1无中点dev，116无formalresult，117/118仍为准备。
- dev256反复暴露且单训练实例；blind1000未使用，目标未达到。
- 早期工程按阶段分组，未声称逐项重建所有CPU测试。
- 费用使用独立budget组件，不由wall时间或reserve推算实际账单。

completed仅表示执行终态，不保证达到原最大target；accepted/完成学习为0不代表没有环境采集、GPU计算或费用。

诊断/复演条目的训练剂量0或null不代表动作数0；诊断样本和原动作重建步数在各条目明细中另列。

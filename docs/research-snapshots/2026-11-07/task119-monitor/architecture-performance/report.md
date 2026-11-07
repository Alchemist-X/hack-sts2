# 架构与训练性能：监控数据接口

本目录是保存证据的快照，不是新一轮运行监控。`architecture.json`提供SVG节点/边、S/M/L版本参数和配方演进；`performance.json`提供实验资源、65个至多25次更新的窗口、13个status和14个checkpoint中间点；`sources.json`提供每项来源路径、SHA及字节数。experiment_id已与研究history作者统一；正式成绩由history数据提供，这里不重复保存评分。

当前S为Policy **2,941,072＋transition37,188＝2,978,260**；当前M116为**16,253,584＋74,052＝16,327,636**。Policy已包含旧win/combat辅助头，不能重复加。旧M046/088是16,106,128，不能当当前M。旧L105,592,336仅在H10080GB首次4096有梯度前向OOM，0 optimizer step；没有L正式训练或评测曲线。

图中actor与critic共享encoder/state；critic只对legal-candidate mean路径detach，state梯度仍回trunk。reward_choices明细走candidate option bank，不直接进state池化；B0恢复可公开证明的HandSelect指针。transition辅助头读取动作前state与selected h2，之后的公开变化只作训练目标，不能画成未来状态输入actor。主图表示104之后当前B0配方；历史图须按`experiment_capabilities`关闭当时尚不存在的分支。参数数目相同不代表输入路由相同。094/097/099/104是配方演进，不能全部画成权重继承；104是fresh。

四种口径必须区分：

- `env_steps_per_s`是当前更新在checkpoint/eval之前的决策吞吐。
- `env_steps_per_s_session`用本次会话新增决策除以train_start后的时间。
- `wall_s`累计update循环时间，并会继承祖先值，不是整个provider墙钟。
- `session_s`是当前trainer会话时间，不保证包含最终上传和Function收尾。

决策数不是游戏局数；2048×64＝131072是采样batch，4096是learner minibatch。不能拿calendar中间hold时间当GPU工作时间，也不能把parent/child累计剂量、metrics窗口与snapshot重复相加。

分阶段时间是host elapsed。infer包含预处理、采样以及传输/同步所在区间，不是纯GPU kernel时间。独立sync、网络上传与完整provider墙钟未测时均为null；不能补0。prefetch与learner可能重叠，不可作为独立可叠加阶段。checkpoint阶段可能包含同步publication callback，最终loop外保存/上传则可能不在iteration内。可堆叠已知iteration阶段及unallocated项，不能再叠后台prefetch。

覆盖限制：109仅保留u2647–3401尾755行，早期缺失不能补线为0；114两臂各完整382行；115第一段有早期metrics、多次live快照和完整终态time split；M116与S115第二段只有保存的status/checkpoint快照。status可落后于持久化指针，应分别显示计数与时间。最新保存status截至**2026-11-07T02:23:25Z**，后续真实进度不能从这些静态文件外推。历史实验未提取到硬件证据的字段保持null，不用别的运行配置补齐。

数据只轻量解析既有标量日志，没有读取模型/seed正文、实例化模型、SDK查询、新游戏、GPU或统计重采样。时间估计独立放在`estimates`，不是真实完成时间。JSON解析、source_refs解析、参数相加及窗口决策数守恒已核对。

主架构图是依据冻结源码的新重绘；此前独立可视图的查找与版本出处另作补充，未声称复制旧图。

限定目录查找结果：找到旧`sts2-training-progress.html`（Task092/v4-with09750m），它是进度曲线而非架构图；`docs/live-harness-architecture.md`第7/54行有2026-09-09旧LLM+MCP harness Mermaid，不能表示当前Modal PPO。可借用布局，不能沿用其组件含义。未在本次限定相关目录中找到旧PPO架构HTML/SVG；出处及哈希已加入sources。

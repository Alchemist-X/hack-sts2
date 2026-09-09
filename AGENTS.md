# STS2 gameplay 与复盘

开始或继续 gameplay 前，先读 `docs/sts2-live-protocol.md`、对应版本/角色的 `knowledge/priors/` 记忆，以及该局 `plan.json`、`resume.md`、`run-review.md`（若存在）。v0.107.1 战士记忆是 `knowledge/priors/v0.107.1/ironclad/forum-notes-2026-09-09.md`。

同时读取 `knowledge/priors/v0.107.1/ironclad/rule-source-notes-2026-09-09.md`：它包含规则来源优先级、Wiki版本漂移和拿牌统计分母校正。当前harness与改进闭环的实现边界见 `docs/live-harness-architecture.md`。

每局用独立目录保存 live.log、动作前后状态、完整可见候选、知识引用、计划和报告；跨会话续玩沿用该局目录并记录检查点恢复。使用 `scripts/sts2_live.py`，不要再使用写死日期的旧代码快照。每个报告都包含 token、缓存输入、上下文压缩、拿牌/跳牌、药水与 SL 次数；缺数据标未知。

外显内容是可核验的决策摘要、算式、预期与不确定性。执行器不替模型选择策略，不要求或保存私密推理链。抽牌堆只使用无序公开集合；只有牌效明确放回顶部时，才记录公开已知的牌顶。不读取隐藏 RNG 或未来结果。

多选以已选索引变化确认，不重复点击超时动作；变化动画只是预览。异常先观察、必要时视觉核对，保留证据。快速 SL 用右上齿轮 → 保存并退出 → 继续游戏，通常回到房间开头；要记录原因和回滚范围，训练与评测分开，不把读档的样本算无读档。

只在隔离运行时操作 MCP，不修改用户游戏存档或把 MCP 装入人类游戏。遵守 `CLAUDE.md` 的 Recorder 与数据约定。此仓库的轨迹、存档、生成日志不提交 Git。

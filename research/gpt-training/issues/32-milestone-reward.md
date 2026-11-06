# [GPT research] 评估过幕奖励与奖励稀疏度，保留原协议评测

GitHub: https://github.com/Alchemist-X/hack-sts2/issues/32

## 区分两个改动

删除逐房间进度奖励会更稀疏；保留进度并增加 Boss 里程碑只是改变权重。当前后幕与胜局暴露很少，优先测试第二种。

## 候选

现有 reward = Δprotocol_progress + 1×win − 0.01×verified_cycle。
实验初值：在现有奖励上加 0.05×击败第一幕Boss + 0.10×击败第二幕Boss。数字是待检验候选，不是已批准训练配方；每局每个里程碑最多一次，依据真实击败/过幕事件，不按进入 Boss 房间发奖。既有通关 +1 不重复发放。

## 验收与风险

- [ ] 验证事件唯一性、恢复后去重、终局/截断、自举回报与日志口径一致。
- [ ] 原始协议进度、奖励各组成分别记录；不把新 reward 上升称作模型更强。
- [ ] 检查稳定过前幕但牺牲后期构筑的偏差，审视路线、奖励选择及资源消耗。
- [ ] 固定网络与采样配置单因素比较；纯 Boss 稀疏奖励留作后续独立消融。
- [ ] 如声称 policy-invariant shaping，必须证明势函数及终止边界条件；普通 Boss bonus 不具有自动保证。

参考：[Ng et al. 1999](https://ai.stanford.edu/~ang/papers/shaping-icml99.pdf)。

## 共同实验约定

这是研究提案，不代表已经实现、部署或证明有效。保持 Silent A10、自主 fresh 开局、公开观测、greedy 无 search/breaker 评测；不引入人类轨迹、存档/harvest、外部模型权重或 blind 选型。使用同一父 checkpoint 的匹配对照（改变结构时控制初始化），同等新增有效环境步数，并同时报告实际计算时间和费用；有希望的结果用多个训练随机种子复验。预先固定评测节点，25M 是观察点，不设置“25M 必须达到某进度”的事后淘汰线。评价仍用原协议平均进度、各幕到达率和完整通关率。

共享代码、证据摘要与实验索引：[研究目录](https://github.com/Alchemist-X/hack-sts2/tree/codex/sts2-gpt-research-share-20261106/research/gpt-training)。已验收参考谱系 Task099 在 200,015,872 步的 dev256 为 0.34218443359375、0/256 胜；150M 点值 0.35054000390625，不把点值回落当作确定退步。

# [GPT research] 长程信用分配：rollout/GAE、自举误差与 1200 步截断

GitHub: https://github.com/Alchemist-X/hack-sts2/issues/33

## 问题

当前 2048 env×64 步=131072 transition，gamma=1、lambda=0.99。64 是片段长度，不是游戏长度或最大可学习时距；片段外依赖 Value 自举。完整通关长度尚无该谱系实证，600–1000 步只是讨论中的估算。

## 候选与混杂控制

- 比较 2048×64 与 512×256，保持总 rollout batch=131072、minibatch4096、epochs1、每批32次主优化器更新。
- 明确这同时改变环境多样性，是采样配比实验，不能声称纯长度因果。
- 若保持2048 env而改256步，batch与策略更新间隔均扩大4倍，作为另一种设计单列。
- lambda 改动另设消融；gamma=1不表示有限片段的GAE完全不依赖估计。

## 验收

- [ ] terminal、timeout、rollout边界、auto-reset后的next observation与pre-reset final observation分别测试。
- [ ] 按真实距终局距离分析 V/return 误差；窗口位置不能充当距终局距离。
- [ ] 检查每局1200决策上限是否截掉健康的小刀/弃牌长回合；不把所有 cap 当死亡，也不未经新协议改变正式评测口径。
- [ ] 比较后幕暴露、样本效率、耗时、GPU/CPU分段耗时及恢复删失。

参考：[GAE](https://arxiv.org/abs/1506.02438)。

## 共同实验约定

这是研究提案，不代表已经实现、部署或证明有效。保持 Silent A10、自主 fresh 开局、公开观测、greedy 无 search/breaker 评测；不引入人类轨迹、存档/harvest、外部模型权重或 blind 选型。使用同一父 checkpoint 的匹配对照（改变结构时控制初始化），同等新增有效环境步数，并同时报告实际计算时间和费用；有希望的结果用多个训练随机种子复验。预先固定评测节点，25M 是观察点，不设置“25M 必须达到某进度”的事后淘汰线。评价仍用原协议平均进度、各幕到达率和完整通关率。

共享代码、证据摘要与实验索引：[研究目录](https://github.com/Alchemist-X/hack-sts2/tree/codex/sts2-gpt-research-share-20261106/research/gpt-training)。已验收参考谱系 Task099 在 200,015,872 步的 dev256 为 0.34218443359375、0/256 胜；150M 点值 0.35054000390625，不把点值回落当作确定退步。

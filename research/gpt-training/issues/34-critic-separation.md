# [GPT research] 评估 Actor/Critic 共享主干的梯度冲突与独立编码器

GitHub: https://github.com/Alchemist-X/hack-sts2/issues/34

## 假设

现有 Actor 和 Value 共用 Transformer。候选汇总 detach 只阻断候选那条 Value 梯度，Value 仍沿 state 路径更新共享编码器。二者目标可能冲突，但目前未测到冲突，不能据此认定是故障。

## 顺序

1. 先记录共享层 policy/value 梯度范数、夹角/余弦及分幕样本量，验证诊断本身不改变训练。
2. 先完成 Value 头容量对照，再考虑独立 Critic 编码器或部分共享。
3. 控制初始化、Actor参数和优化剂量；报告额外参数/FLOPs、显存和 wall-clock。

## 验收

- [ ] 原始策略输出、checkpoint语义、优化器状态和随机流边界清楚。
- [ ] 独立网络收益同时报告计算成本；不把更大总模型的改善直接归因于解除冲突。
- [ ] 如果诊断没有明显冲突，仍按实测表现决定，不先验断言独立一定更好。

参考：[PPO 37 details，第13项共享/独立网络](https://iclr-blog-track.github.io/2022/03/25/ppo-implementation-details/)。

## 共同实验约定

这是研究提案，不代表已经实现、部署或证明有效。保持 Silent A10、自主 fresh 开局、公开观测、greedy 无 search/breaker 评测；不引入人类轨迹、存档/harvest、外部模型权重或 blind 选型。使用同一父 checkpoint 的匹配对照（改变结构时控制初始化），同等新增有效环境步数，并同时报告实际计算时间和费用；有希望的结果用多个训练随机种子复验。预先固定评测节点，25M 是观察点，不设置“25M 必须达到某进度”的事后淘汰线。评价仍用原协议平均进度、各幕到达率和完整通关率。

共享代码、证据摘要与实验索引：[研究目录](https://github.com/Alchemist-X/hack-sts2/tree/codex/sts2-gpt-research-share-20261106/research/gpt-training)。已验收参考谱系 Task099 在 200,015,872 步的 dev256 为 0.34218443359375、0/256 胜；150M 点值 0.35054000390625，不把点值回落当作确定退步。

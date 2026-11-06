# [GPT research] Silent A10 训练路线与 co-author 共享索引

GitHub: https://github.com/Alchemist-X/hack-sts2/issues/37

## 目标与当前证据

Silent A10，自主 fresh 训练；目标为平均协议进度至少0.6、完整通关率超过0.1。Task099 原200M计划已完成，已验收 dev256=0.34218443359375、0/256胜；最高已观测150M点值0.35054000390625。目标未达到，blind1000未用于选型。Task104是公开HandSelect输入修复的后续实验；已有24,641,536训练步的已接受检查点，尚无其质量评测（归档证据截至2026-11-06T05:01:50Z）。

## 提案索引

- [ ] #31 对照评估 Value 容量、拟合不足与完整回报校准
- [ ] #32 评估过幕奖励与奖励稀疏度，保留原协议评测
- [ ] #33 长程信用分配：rollout/GAE、自举误差与 1200 步截断
- [ ] #34 评估 Actor/Critic 共享主干的梯度冲突与独立编码器
- [ ] #35 按 PPO 37 implementation details 审计实现与分层评测
- [ ] #36 可行性实验：候选动作 Double DQN 与自主经验回放

## 实验顺序与决策

1. 先做实现/分层诊断，保留原配方继续训练作为匹配对照。
2. Value扩容和过幕奖励分别单因素比较；初始化、训练剂量、评测节点预先固定。25M/50M可做观察点，不要求25M达到任意进度门槛；有希望者按预先计划扩展并多随机种子复验。
3. 较长rollout、共享/独立critic放在明确混杂设计后；Q-learning作为有界独立臂，不静默替换主线。
4. 持久化假设、代码/配置SHA、数据来源、有效步数、真实耗时/费用与停止条件。数值错误、来源不明或超资源上限属于工程停止；策略收益用固定协议和不确定性判断。
5. 任何CPU测试/源码发布均不等于GPU质量验证；旧谱系与Task099、Task104各自分开记录。

## 共享入口

[分支 README](https://github.com/Alchemist-X/hack-sts2/tree/codex/sts2-gpt-research-share-20261106) · [研究代码和进度](https://github.com/Alchemist-X/hack-sts2/tree/codex/sts2-gpt-research-share-20261106/research/gpt-training)

本 issue 记录用户要求的研究路线及分享内容，不改变生产调度、预算、checkpoint、服务或既有主线程计划。

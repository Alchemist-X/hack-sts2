# [GPT research] 按 PPO 37 implementation details 审计实现与分层评测

GitHub: https://github.com/Alchemist-X/hack-sts2/issues/35

## 目的

把代码正确性、估值诊断和策略质量分开。对照 Shengyi Huang 等的 [The 37 Implementation Details of Proximal Policy Optimization (2022-03-25)](https://iclr-blog-track.github.io/2022/03/25/ppo-implementation-details/)。用户提到的 zengyiyan 拼写尚未确认为此作者；这是最可能匹配。LLM相关补充：[RLHF PPO N details (2023-10-24)](https://huggingface.co/blog/the_n_implementation_details_of_rlhf_with_ppo)，不能直接照搬四模型/RLHF配方。

## 清单

- [ ] rollout-oldlogp重建、首个minibatch更新前ratio=1、合法候选mask、entropy及padding。
- [ ] GAE/bootstrap：真实terminal归零、合法外部截断保留同局final-value、reset不串局。
- [ ] 核对Adam epsilon、学习率、每轮epochs、advantage归一化范围、全局grad clipping、value clipping和损失权重；写明与参考差异，不能一次全改。
- [ ] 分开报告actor/critic/aux optimizer steps与环境决策数。
- [ ] 按幕/phase报告样本量和估值误差；全局loss可能被第一幕主导。
- [ ] 记录候选/公开表截断、恢复丢弃在途局、非有限值、cap；评测健康不等于所有训练步健康。
- [ ] 每个实验证据区分proposal、CPU test、deployed、GPU run、quality eval；固定dev与最终blind用途。

已知参考事实：Task099 200M累计Act2学习步4.9252%、Act3约0.0087%，原始报告有32次native_table_overflow flag，不能宣称全部健康计数为零。

## 共同实验约定

这是研究提案，不代表已经实现、部署或证明有效。保持 Silent A10、自主 fresh 开局、公开观测、greedy 无 search/breaker 评测；不引入人类轨迹、存档/harvest、外部模型权重或 blind 选型。使用同一父 checkpoint 的匹配对照（改变结构时控制初始化），同等新增有效环境步数，并同时报告实际计算时间和费用；有希望的结果用多个训练随机种子复验。预先固定评测节点，25M 是观察点，不设置“25M 必须达到某进度”的事后淘汰线。评价仍用原协议平均进度、各幕到达率和完整通关率。

共享代码、证据摘要与实验索引：[研究目录](https://github.com/Alchemist-X/hack-sts2/tree/codex/sts2-gpt-research-share-20261106/research/gpt-training)。已验收参考谱系 Task099 在 200,015,872 步的 dev256 为 0.34218443359375、0/256 胜；150M 点值 0.35054000390625，不把点值回落当作确定退步。

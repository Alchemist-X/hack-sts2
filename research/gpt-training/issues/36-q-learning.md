# [GPT research] 可行性实验：候选动作 Double DQN 与自主经验回放

GitHub: https://github.com/Alchemist-X/hack-sts2/issues/36

## 初步判断

可用。动作是离散、状态相关的合法候选，适合 Q(s,a) 对每个候选打分。应复用公开实体与候选编码器，不能用固定全局动作编号假设，也不能把PPO logit当成已经校准的Q值。保留PPO主线，先做独立有界对照。

## 最小方案

- online Q与target Q各自编码当前状态和候选；策略在合法候选中epsilon-greedy探索，正式评测greedy。
- Double DQN目标：a*=argmax合法a Q_online(s_next,a)，y=r+gamma*(1-terminal)*Q_target(s_next,a*)；真实终态不做空集合max，timeout按任务定义单独处理。
- 回放保存公开(s,候选,已选动作身份,r,s_next,next候选,terminal/truncated)及版本/谱系。候选行号仅在对应状态有效，不能跨状态复用索引，也不缓存会随训练过时的embedding作为唯一数据。
- 回放只收新臂自身前瞻fresh在线经验；空buffer开始，不读取人类、旧099轨迹、dev/blind或存档。可比较继承自有encoder和随机初始化，但PPO对照必须匹配初始化。
- 首先uniform replay + Double DQN；n-step、按幕/整局抽样、prioritized replay逐项消融。多步off-policy回报需说明行为策略偏差/截断或校正，不声称天然无偏。
- 冻结target更新规则、replay容量/磁盘上限、每个新transition的learner更新比、预热和探索日程。

## 为什么可能有帮助 / 为什么可能失败

重用少量后幕经历可能提高样本效率；但从未经历的胜局不会凭空出现。候选max可能带来过估计，旧策略回放可能覆盖偏差，第一幕淹没buffer，函数逼近+bootstrapping+off-policy可能不稳定。较少新环境步不一定更省GPU时间或钱。

## 验收

- [ ] 合法mask、候选顺序变化、empty terminal、n-step跨episode边界、timeout与恢复回放一致性测试。
- [ ] 日志包含分幕buffer占比/抽样概率、样本年龄、TD误差、Q/真实return校准、更新比与target drift。
- [ ] 以相同新环境步数比较PPO，再单独报告相同wall-clock/费用效率；多个种子确认。
- [ ] 数值发散、不可界定回放来源或超资源上限时按预定条件停止；算法表现结论用原协议dev/最终blind。

参考：[Double DQN](https://arxiv.org/abs/1509.06461)、[Prioritized Replay](https://arxiv.org/abs/1511.05952)、[Rainbow](https://arxiv.org/abs/1710.02298)。

## 共同实验约定

这是研究提案，不代表已经实现、部署或证明有效。保持 Silent A10、自主 fresh 开局、公开观测、greedy 无 search/breaker 评测；不引入人类轨迹、存档/harvest、外部模型权重或 blind 选型。使用同一父 checkpoint 的匹配对照（改变结构时控制初始化），同等新增有效环境步数，并同时报告实际计算时间和费用；有希望的结果用多个训练随机种子复验。预先固定评测节点，25M 是观察点，不设置“25M 必须达到某进度”的事后淘汰线。评价仍用原协议平均进度、各幕到达率和完整通关率。

共享代码、证据摘要与实验索引：[研究目录](https://github.com/Alchemist-X/hack-sts2/tree/codex/sts2-gpt-research-share-20261106/research/gpt-training)。已验收参考谱系 Task099 在 200,015,872 步的 dev256 为 0.34218443359375、0/256 胜；150M 点值 0.35054000390625，不把点值回落当作确定退步。

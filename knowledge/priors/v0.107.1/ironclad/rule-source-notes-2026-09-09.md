# 规则来源与拿牌统计校正

运行版本为v0.107.1 / 59260271。当前游戏状态、卡面、状态文本与受支持的引擎预览优先；精确匹配游戏程序集的本地源码用于解释机制。Wiki与Spire Codex是补充资料。不要把检索成功当作规则解释正确。

异蛙寄生虫从Infect与Lash交替；Spire Codex对应版本数据中的random分类包含不可达RAND节点，不能照搬。沙漏的同版本Codex数据漏了激光次数；本地源码为两段。2026-09-09访问的Wiki沙漏页面带Beta提示，Ebb A9数值26与本地版本32不同。当前数值应从本地引擎核对。

拿牌统计：本局17次普通卡牌奖励，拿14跳3，拿牌率82.4%。Spire Codex社区统计v0.107.1、单人、标准、战士A10的15,832局显示跳牌32.6%。但其当前lake SQL和旧实现均把每个非空card_choices块算成奖励，其中包含商店未买卡；本局按该算法变成23块、拿14跳9，即60.9%。这两个比例的分母不同，不能据此证明本局拿牌过多，也不能据此证明少拿了。社区样本自愿上传、局长不同，仍有选择偏差。

不要为了达到某个拿牌率机械跳牌。每次奖励比较拿牌和跳牌：解决下一个明确威胁的收益、同类牌冗余、能量/过牌是否支持、关键牌出现率及长期状态处理能力。单张消耗牌不能自动等同于稳定消耗循环；抽到能力牌不等同于能支付它并安全启动。

实机回归在独立实验体检查点副本进行：震荡波使敌人意图22→18，与我一战单段目标预览6→9，与事前算式一致。用户随后要求退出，已退出全部游戏，没有继续挑战。详细工程图见 docs/live-harness-architecture.md；测试、统计与来源记录见 output/source-of-truth-2026-09-09/。

来源：
- https://slaythespire.wiki.gg/wiki/Slay_the_Spire_2%3APhrog_Parasite
- https://slaythespire.wiki.gg/wiki/Slay_the_Spire_2%3AAeonglass
- https://spire-codex.com/community-stats?bracket=solo%3Aa10%3Astandard%3Aironclad%3Av0.107.1
- https://github.com/ptrlrd/spire-codex/blob/43af6997e02c28894e1ca516116778fc25914c09/backend/app/services/lake_stats.py#L667
- https://github.com/ptrlrd/spire-codex/blob/43af6997e02c28894e1ca516116778fc25914c09/lab/build.sql#L100

第三局实机与源码补充：v0.107.1 RewardsSet.WithRewardsFromRoom（RewardsSet.cs:88）对最终幕Boss直接跳过常规战后奖励生成，所以A10第三幕双Boss之间没有正常金币/选牌/药水补给；燃烧之血仍战后回复。不要把第一首领结束后的空奖励误判为MCP漏字段，也不能以“可能掉药”作为第二首领资源预算。

# Slay the Spire 2 人类+智能体轨迹 Benchmark — 分类阅读清单

## ① 模型玩 StS 的评测研究

- **[AgenticSTS: A Bounded-Memory Testbed for Long-Horizon LLM Agents](https://arxiv.org/abs/2607.02255)** (arXiv-only 2607.02255, 2026年7月, Alaya Lab/上海交大等) — 在 StS2 上构建了 5 层类型化有界记忆合约的测试台（每次决策全新 prompt、不累积对话），固定 A0 基线胜 3/10、加触发式策略技能层升至 6/10（仅方向性结论，Fisher 精确检验 p≈0.37；对照开发者公布的 16% 人类胜率），并发布 298 条带条件标签的轨迹与冻结的记忆/技能快照 —— 目前唯一已发表的 StS2 智能体工作，是你必须引用并定位差异的直接先行者：它只有智能体轨迹（无人类数据）、单一难度、每格仅 10 局的小样本统计，这些缺口恰好是"大规模人类轨迹 + 标准化评测协议"能填补的空间；它引用的 STS2MCP/HermesBridge/AI-Spire 工具生态也应一并覆盖。

- **[Orak: A Foundational Benchmark for Training and Evaluating LLM Agents on Diverse Video Games](https://arxiv.org/abs/2506.03610)** (ICLR 2026 poster, KRAFTON/NVIDIA) — 基于 MCP 的 12 游戏 LLM 智能体 benchmark，含 StS1（状态预处理为结构化文本）：小型开源模型在 StS 上得 0 分（70B 级也仅 5–9%），Gemini-2.5-Pro 最强（StS 51.9%），并附专家轨迹微调数据集、排行榜、对战场和智能体模块消融框架 —— 顶会最接近的先例：STS2 benchmark 必须对标其 StS 赛道的接口设计、轨迹格式与模块消融方法；其"StS 是 12 款游戏中最难之一"的结论也是立项论据。关键差异点：Orak 的轨迹是 LLM 生成的，你的独特资产是人类轨迹与更深的逐决策标注。

- **[Language-Driven Play: Large Language Models as Game-Playing Agents in Slay the Spire](https://dl.acm.org/doi/10.1145/3649921.3650013)** (FDG 2024, Bateni & Whitehead, UC Santa Cruz) — 首个同行评审的 LLM 玩 StS 研究（简化重实现 MiniSTS 的战斗部分）：LLM 仅凭卡牌文本就能理解协同并展现无需专门训练的长期规划优势，而前瞻/搜索智能体在即时协同场景更强、LLM 单步并非最优 —— "LLM 能从卡牌文本直接上手 StS"的经典引文；其战斗简化设置和走子质量评测是全流程 STS2 轨迹 benchmark 的天然对照，并启发把战略层（跨层路线）与战术层（逐回合）决策分开评测。

- **[LLMs May Not Be Human-Level Players, But They Can Be Testers](https://dl.acm.org/doi/10.1145/3748634)** (CHI PLAY 2025 / PACM HCI, Xiao & Yang) — 用通用 prompt 的 LLM 智能体玩 Wordle 和 StS，发现其表现与人类感知难度显著强相关，可作自动化难度测试器 —— 证明 LLM 在 StS 上的胜率/进度信号能追踪人类难度，可用于校准 STS2 benchmark 的难度分层，并论证智能体轨迹承载的不只是"智能体水平"还有游戏平衡信息。

- **[Analysis of Uncertainty in Procedural Maps in Slay the Spire](https://arxiv.org/abs/2504.03918)** (FDG 2025, Bazzaz & Cooper, Northeastern) — 对 2 万局真实 StS 运行的信息论分析：获胜局呈现更高的归一化路径熵（更冒险），资深玩家在后期 Act 有独特的风险模式 —— 提供了从大规模日志计算的人类轨迹基线与基于熵的走图指标，可直接复用为 STS2 benchmark 中评分智能体地图导航决策的参考统计量与轨迹级特征。

- **[Are You Lucky or Skilled? An Analysis of Elements of Randomness in Slay the Spire](https://ieeexplore.ieee.org/document/10333231/)** (IEEE CoG 2023, Malmö) — StS 运行数据分析：获胜玩家拥抱而非回避随机性，携带 1.82 倍的随机效果卡并策略性利用不可预测效果 —— 同行评审证据表明 StS 结果混杂了运气与技巧，对 benchmark 方法论至关重要：必须做种子控制、足够局数、方差感知指标，并定义胜率之外的技巧指标。

- **[SAG-Agent: Enabling Long-Horizon Reasoning in Strategy Games via Dynamic Knowledge Graphs](https://arxiv.org/abs/2510.15259)** (arXiv-only 2510.15259, 2025年10月) — 像素输入的 GUI 智能体，把原始像素交互转化为持久化状态-动作图并配混合利用/探索奖励做长程规划，在 StS 与文明 5 上评测 —— 代表 StS 智能体的视觉/GUI 输入一端（相对 Orak/AgenticSTS 的结构化状态 API）；记录完整轨迹的 STS2 benchmark 可同时服务两种输入模态，其持久图记忆是记忆架构消融的对照点。

- **[Rethinking Agent Design: From Top-Down Workflows to Bottom-Up Skill Evolution](https://arxiv.org/abs/2505.17673)** (arXiv-only 2505.17673, 2025年5月) — 游戏无关的智能体从原始屏幕+鼠标经试错推理自下而上习得技能，在 StS 上无游戏特定 prompt 清到 13 层（得分 81，98.56% 执行响应率，所有无先验基线全失败）—— 展示了技能库可从 StS 游玩中涌现；长轨迹记录恰好支撑这类技能演化研究，其"到达层数/执行成功率"是 STS2 未通关局的候选中间指标。

- **[MiniStS: A Testbed for Dynamic Rule Exploration](https://ceur-ws.org/Vol-3926/paper7.pdf)** (EXAG @ AIIDE 2024, Bateni & Whitehead) — 开源极简 StS 重实现，专为替换/变异游戏规则设计，研究智能体应对动态或未见机制的能力 —— "StS 作为研究测试台"的先行者；其规则变异视角提示了一条 STS2 benchmark 轴线：StS2 的新卡牌/新机制天然击穿模型背诵的 StS1 攻略知识，测的是真正的规则理解而非回忆。

- **[UrzaGPT: LoRA-Tuned Large Language Models for Card Selection in Collectible Card Games](https://arxiv.org/abs/2508.08382)** (arXiv-only 2508.08382, 2025年8月, JKU Linz) — 在标注的 MTG 轮抽日志上 LoRA 微调开源 LLM：GPT-4o 零样本选牌准确率 43%，1 万步 LoRA 模型达 66.2%，逼近但未及领域专用轮抽器 —— 最接近的组卡决策类比：以专家日志为准的选牌准确率是干净的监督指标，且轨迹微调收益巨大，可作为 STS2 benchmark 中"卡牌奖励/商店决策"子任务（以高手人类局为标准答案）的模板。

## ② Benchmark 设计的参考坐标系

- **[Dungeons and Data: A Large-Scale NetHack Dataset](https://arxiv.org/abs/2211.00539)** (NeurIPS 2022 D&B Track) — NLD：从 NAO 公共服务器（2009–2020）刮取约 150 万条人类轨迹共 100 亿状态转移，加 10 万局符号机器人的 30 亿带动作-分数转移，以 ttyrec 式压缩存储（38TB→229GB，10 CPU 下 288K fps 流式读取）—— 与 STS2 轨迹 benchmark 最相似的存在（在既有游戏上叠加人类轨迹数据集）。三条核心教训：被动收集的人类数据常缺动作标签（NLD-NAO 只有状态，是公认痛点）——而你的方案恰能做到精确动作捕获；压缩与流式吞吐是一等设计问题；用带标签的机器人语料补齐动作缺口。

- **[The PokeAgent Challenge: Competitive and Long-Context Learning at Scale](https://arxiv.org/abs/2603.15563)** (NeurIPS 2025 Competition Track) — 双赛道 NeurIPS 竞赛（Showdown 对战 + 绿宝石 RPG 速通），2000 万+对战轨迹、启发式/RL/LLM 基线、100+ 队伍，赛后转为带持久排行榜的"活 benchmark"；发现宝可梦对战与标准 LLM benchmark 几乎正交 —— 一年前的最新剧本：海量轨迹+基线→NeurIPS 竞赛→活 benchmark。2000 万轨迹定义了 2026 年"大规模"的标尺；其双赛道切分（战术对战 vs 长程通关）与 STS2 的战斗级/全局级决策一一对应。

- **[BALROG: Benchmarking Agentic LLM and VLM Reasoning On Games](https://arxiv.org/abs/2411.13543)** (ICLR 2025) — 聚合 6 个长程 RL 环境（BabyAI/Crafter/TextWorld/Baba Is AI/MiniHack/NetHack）为统一 LLM/VLM 智能体 benchmark，带细粒度进度指标；前沿模型在 NetHack 上惨败，且视觉输入常比文本更差 —— 当前 LLM 游戏评测的标准范式，也是 STS2 benchmark 的直接同级对标。教训：顶线胜率接近零时必须定义细粒度进度指标（到达的层/Act 而非仅胜负）；文本态与像素态分开评测；维护公开排行榜。

- **[BEDD: The MineRL BASALT Evaluation and Demonstrations Dataset](https://arxiv.org/abs/2312.02405)** (NeurIPS 2023 D&B Track oral) — 把 2021/2022 BASALT 竞赛正式化为 benchmark：4 个模糊 Minecraft 任务的约 1.4 万条人类演示视频（2600 万图像-动作对），外加 3000+ 稠密的智能体 vs 人类成对人工评判 —— 示范了如何把竞赛采集的人类演示沉淀为持久 benchmark，以及用成对偏好评判来评价难以打分的行为。STS2 中胜率可打分，但"这套 deck 打得地不地道"可能需要 BEDD 式的比较评判。

- **[The NetHack Learning Environment](https://papers.nips.cc/paper/2020/hash/569ff987c643b4bedf504efda8f786c2-Abstract.html)** (NeurIPS 2020) — 把 NetHack 3.6.6 包装为快速、程序生成、高随机的 RL 环境，附标准 Gym 任务与 TorchBeast 基线，成为"跑得便宜但极难"的 roguelike benchmark 原型 —— roguelike benchmark 的奠基模板：STS2 共享其核心属性（程序生成、永久死亡、高方差）。教训：交付快速无头接口 + 规范任务切分，依托游戏自身深度而非发明合成任务。

- **[The MineRL BASALT Competition on Learning from Human Feedback](https://arxiv.org/abs/2107.01969)** (NeurIPS 2021 Competition Track) — 竞赛设计论文：4 个自然语言描述的 Minecraft 任务，配人类演示数据集与模仿学习基线，用人工评判取代奖励函数 —— 用竞赛引导社区与数据的蓝图（NetHack Challenge、PokeAgent 后来走的同一条 NeurIPS 竞赛路线）。教训：演示数据+基线+评测协议三件套一起发布，游戏数据集才会成为 benchmark。

- **[lmgame-Bench: How Good are LLMs at Playing Games?](https://arxiv.org/abs/2505.15146)** (ICLR 2026, UCSD/Hao AI Lab) — 把 6 款流行游戏做成可靠 LLM 评测，统一 Gym 式 API、可开关的感知/记忆/推理脚手架，直面视觉感知脆弱、prompt 敏感性与数据污染，评测 13 个前沿模型 —— 点名了 2026 年任何游戏 benchmark 必须向审稿人交代的三大失效模式。STS2 自带污染优势：2025–2026 年才发售，在多数模型训练截止之后，不像 StS1 的 wiki/攻略早已饱和预训练数据——这个新鲜度论证应放在论文最前面。

- **[VideoGameBench: Can Vision-Language Models Complete Popular Video Games?](https://arxiv.org/abs/2505.18134)** (ICLR 2026) — 10 款 90 年代游戏由 VLM 从原始像素实时游玩、无游戏特定脚手架；最强模型仅完成 0.48%，去掉延迟混淆的暂停 Lite 模式也只 1.6% —— 两条教训：实时像素游玩对前沿模型仍近乎地板分，所以 STS2 的回合制是特性——它把决策质量与感知/延迟隔离（暂停式模式应为默认）；人类轨迹正是让 STS2 论文成为 D&B 赛道而非纯游戏评测的差异化要素。

- **[SmartPlay: A Benchmark for LLMs as Intelligent Agents](https://arxiv.org/abs/2310.01557)** (ICLR 2024) — 6 款游戏各自映射到 9 项智能体能力量规（规划、空间推理、历史学习、理解随机性等）—— 能力分解的示范：不要只报胜率，要把每类游戏/决策映射到它所探测的技能。STS2 可原生做到这一点（选卡=价值估计、走图=长程规划、药水时机=不确定性下的资源管理），单一游戏产出逐技能诊断。

- **[The Hanabi Challenge: A New Frontier for AI Research](https://arxiv.org/abs/1902.00506)** (AIJ vol. 280, 2020) — 提出合作型不完全信息卡牌游戏 Hanabi 作为心智理论/临场协作的挑战域，附开源环境与规范评测协议（自博弈与交叉博弈两种范式）—— 卡牌游戏"挑战论文"的典范：靠精确论证游戏隔离了哪些认知能力、预先定义评测范式而成功，而非靠数据规模。STS2 挑战论文应同样论证 deckbuilding roguelike 独有地隔离了什么（协同估值、程序化不确定性下的风险调整规划）是 NetHack/宝可梦测不到的。

- **[Interactive Fiction Games: A Colossal Adventure (Jericho)](https://arxiv.org/abs/1909.05398)** (AAAI 2020) — Jericho：包装 30+ 人类创作的互动小说游戏，附"辅助轮"（合法动作检测、通关攻略、对象树），是 TextWorld（2018）文本智能体谱系的后继 —— 该谱系确立了人类创作的游戏比合成任务更有 benchmark 生命力，且可选"辅助轮"让一个工件同时服务简单/困难两种评测模式。STS2 状态可文本化，此谱系直接适用；攻略的等价物 = 获胜的人类轨迹。

- **[Cradle: Empowering Foundation Agents Towards General Computer Control](https://arxiv.org/abs/2403.03186)** (ICML 2025) — 截图输入、键鼠输出的智能体框架（自反思、任务推断、技能积累、记忆），无 API 完成《荒野大镖客 2》40 分钟任务并操作商业软件 —— 代表"无 API、纯像素"的一极。STS2 的设计决策：明确 benchmark 落在结构化状态访问（BALROG/Orak 式）与原始屏幕控制（Cradle 式）之间的哪一档——支持双档能扩大受众，且 STS2 是 Steam 发行，像素级游玩才是真实部署形态。

- **[PokeLLMon: A Human-Parity Agent for Pokemon Battles with Large Language Models](https://arxiv.org/abs/2402.01118)** (arXiv-only 2402.01118, 2024, Georgia Tech) — 首个在宝可梦 Showdown 达到大致人类水平的 LLM 智能体（天梯 49% 胜率），靠对战反馈的上下文内 RL、知识增强生成、一致动作生成抑制"恐慌换人" —— 回合制、部分随机、带属性/协同系统的战斗中 LLM 智能体的先例，是机制上最接近 StS 卡战的近亲。其记录的失效模式（幻觉机制、压力下恐慌行为）正是 STS2 轨迹 benchmark 可对照人类基线量化的行为。也是一记警钟：没有标准化评测的 agent 论文停留在 arXiv——benchmark 框架才是进顶会的通道。

## ③ 怎么训练才能让模型规划得更远

- **[Stream of Search (SoS): Learning to Search in Language](https://arxiv.org/abs/2404.03683)** (COLM 2024 spotlight) — 从零预训练 LM 学习线性化的搜索轨迹——包含探索、死胡同和回溯——再用 STaR/APA 策略改进；比只在最优轨迹上训练的搜索准确率高 25%，还发现了新搜索策略 —— 让模型见到"脏"搜索（错误与回溯）能教会上下文内自我纠错，这正是不确定性下的 run 规划所需；SoS-然后-RL 的管线是 STS2 决策轨迹的现成训练配方。

- **[Beyond A*: Better Planning with Transformers (Searchformer)](https://arxiv.org/abs/2402.14083)** (TMLR 2024, Meta FAIR) — 训练 transformer 先输出 A* 的完整搜索执行轨迹（节点展开）再输出方案，然后在自己更短的成功轨迹上自举微调；用比 A* 少 26.8% 的搜索步最优解出 93.7% 未见 Sokoban，以 5–10 倍小的模型和 1/10 数据击败只学解的模型 —— 直接证据：训练在搜索轨迹而非最优解上才让 transformer 学会规划；STS2 benchmark 可用模拟器树搜索生成走图/战斗前瞻轨迹作监督，套用同一自举循环。

- **[Dualformer: Controllable Fast and Slow Thinking by Learning with Randomized Reasoning Traces](https://arxiv.org/abs/2410.09918)** (ICLR 2025, Meta FAIR) — 在随机丢弃子片段的搜索轨迹上训练单一 transformer，得到可直答（快）、可全推理（慢）、可自选的模型；30×30 未见迷宫 97.6% 最优（超 Searchformer 的 93.3%）且推理步少 45.5% —— Searchformer 最强后继：轨迹 dropout 训练带来决策时可控算力，完美匹配 StS 一局中琐碎选择（快模式）与关键路线/商店/Boss 决策（慢模式）的混合。

- **[Group-in-Group Policy Optimization for LLM Agent Training (GiGPO)](https://arxiv.org/abs/2505.10978)** (NeurIPS 2025) — 把免 critic 的组式 RL（GRPO）扩展到长程智能体，双层优势估计——回合级轨迹组 + 从多次 rollout 中重复出现的"锚点状态"构建的步级组；ALFWorld +12%、WebShop +9% 且开销不变 —— 面向智能体的 GRPO 信用分配 SOTA：一局 STS 约 50 个楼层决策却只有一次延迟胜负信号，GiGPO 的锚点状态分组（同一地图节点、不同动作）与可设种子、可重放的 STS2 rollout 天然契合。

- **[RAGEN: Understanding Self-Evolution in LLM Agents via Multi-Turn RL (StarPO)](https://arxiv.org/abs/2504.20073)** (arXiv-only 2504.20073, 2025) — 提出 StarPO 轨迹级多轮 RL（PPO/GRPO 变体），在随机环境（Bandit/Sokoban/FrozenLake/WebShop）上训练 LLM 智能体；诊断出"Echo Trap"坍缩模式并用 StarPO-S（轨迹过滤、引入 critic、梯度稳定）修复 —— 可迁移性最强的研究：字面上就是在小型随机多轮游戏上 RL 微调 LLM，其不稳定失效模式目录与 rollout 塑形选择（多样初始状态、交互粒度）是 STS2 benchmark RL 赛道必然会撞上的问题清单。

- **[Agent Q: Advanced Reasoning and Learning for Autonomous AI Agents](https://arxiv.org/abs/2408.07199)** (arXiv-only 2408.07199, 2024, MultiOn/Stanford) — 对多步智能体轨迹做引导式 MCTS，用 AI 自我批判为分支排序，再用离策略 DPO 在树偏好上微调；Llama-3-70B 在真实 OpenTable 订位任务上从 18.6% 提到 81.7% 零样本成功率 —— "搜索生成训练数据"的模板：在 STS2 模拟器上做树搜索可产出步级偏好对（同一节点的好/坏分支）供 DPO/RL 使用，全程无需人工标注。

- **[Decision Transformer: Reinforcement Learning via Sequence Modeling](https://arxiv.org/abs/2106.01345)** (NeurIPS 2021) — 把离线 RL 重构为回报条件化的自回归序列建模——在 (return-to-go, 状态, 动作) token 上训练因果 transformer，测试时以高目标回报为提示；在 Atari 与 Key-to-Door 信用分配任务上匹敌或超越离线 RL 基线 —— 把已记录游戏轨迹变成策略而无需奖励工程的奠基配方；STS2 benchmark 可按局结果（胜利/进阶层数）做条件，从混合质量的人类局中提取高于平均的打法。

- **[Multi-Game Decision Transformers](https://arxiv.org/abs/2205.15241)** (NeurIPS 2022) — 单一回报条件化 transformer 训练于最多 46 款 Atari 游戏的轨迹（41 训练+留出迁移集），跨游戏逼近人类水平，性能随模型规模清晰扩展、可快速微调到新游戏 —— DT 面向通才游戏智能体的关键后继：一个模型能吸收异质游戏轨迹并迁移，对应到在一个模型里跨 STS 角色、进阶等级和种子训练。

- **[Scaling Laws for Imitation Learning in Single-Agent Games (NetHack)](https://arxiv.org/abs/2307.09423)** (TMLR 2024) — 为 NetHack 与 Atari 专家轨迹上的行为克隆拟合算力/数据/模型幂律；算力最优的 BC 智能体全面超越 NetHack 先前 SOTA 1.5 倍，IL 损失与游戏回报均按可预测幂律平滑扩展 —— NetHack 是 roguelike 路线规划最接近的已发表类比；这篇论文告诉你 BC 基线持续提升需要多少人类 STS2 轨迹数据与算力，并预测模仿在何处饱和。

- **[Video PreTraining (VPT): Learning to Act by Watching Unlabeled Online Videos](https://arxiv.org/abs/2206.11795)** (NeurIPS 2022 Outstanding Paper, OpenAI) — 在小规模带标签 Minecraft 数据上训练逆动力学模型，为约 7 万小时网络视频打伪标签，再做大规模行为克隆加 RL 微调；智能体造出钻石工具——约 2.4 万步的长程任务，纯 RL 从未解决 —— 长程游戏能力"先大规模模仿、后 RL"的经典配方；对 STS2 的启示是先克隆大规模人类局日志再谈 RL，且仅 BC 就能在多步规划上走得出人意料地远。

- **[Amortized Planning with Large-Scale Transformers: ChessBench](https://arxiv.org/abs/2402.04494)** (NeurIPS 2024, Google DeepMind) — 把 Stockfish 16 的动作价值标注（1000 万局、150 亿数据点）监督蒸馏进至多 2.7 亿参数的 transformer；无搜索策略达到 Lichess 快棋 2895 Elo 并解决难题，附干净的扩展/消融分析，数据集与权重开源 —— 证明强引擎的搜索可经纯监督学习摊销为前馈策略；STS2 类比是用模拟器搜索价值标注游戏状态、训练价值/策略头——同时也是"benchmark 以标注数据集形式发布"的范本。

- **[SPIRAL: Self-Play on Zero-Sum Games Incentivizes Reasoning via Multi-Agent Multi-Turn RL](https://arxiv.org/abs/2506.24119)** (ICLR 2026) — 在零和游戏（Kuhn 扑克、井字棋、简单谈判）上全在线多轮自博弈 RL，配角色条件化优势估计；纯游戏训练把推理 benchmark 提升最多 10%（8 个基准），胜过在 2.5 万条专家游戏轨迹上的 SFT，多游戏训练最强 —— "用游戏 RL 训模型"的卡牌端证据：游戏 RL 能灌注可迁移的规划模式（EV 计算、分情况讨论），且自博弈课程能击败专家轨迹模仿——是 benchmark 必备的重要基线对照。

- **[DeepSeek-R1: Incentivizing Reasoning Capability in LLMs via Reinforcement Learning](https://arxiv.org/abs/2501.12948)** (Nature 2025) — 在可验证任务上的大规模结果奖励 RL（GRPO）催生涌现的长思维链、自我验证与回溯（R1-Zero），再以冷启动 SFT + 多阶段 RL 精炼并蒸馏进小模型 —— 2025–26 年智能体规划工作赖以构建的 o1/R1 式推理 RL 经典配方；STS2 benchmark 恰好提供这一训练范式应用于长程游戏规划所需的可验证、模拟器可核查的奖励信号（局结果、HP 增减、战斗解决）。

---

## 综合建议

文献版图已经清晰：StS 作为评测对象已被顶会承认（Orak 把 StS1 列为 12 款游戏中最难之一，FDG/CHI PLAY/CoG 系列打好了方法论地基），roguelike 轨迹数据集有 NetHack 的成熟模板（NLE→NLD→模仿扩展律），而 StS2 本身只有一篇 arXiv 的 AgenticSTS——小样本（每格 10 局）、纯智能体轨迹、单一难度、无排行榜。缺口恰好是这个项目的三件独有资产：**被动采集的大规模人类轨迹**（NLD 证明这是最稀缺的资源，但它没有动作标签——你的**精确动作捕获**直接解决这个公认痛点）、**同环境配对的智能体轨迹**（人类 vs 智能体逐决策对照是 BEDD 式评判和 PokeLLMon 式失效分析的前提，现有工作全部缺失）。要够到 NeurIPS D&B / ICLR 级别，论文应当：像 Hanabi 那样开篇论证 deckbuilding roguelike 独有地隔离了协同估值与程序化不确定性下的风险调整规划；像 BALROG 那样定义细粒度进度指标并针对 CoG/FDG 两篇随机性研究做种子控制与方差感知评测；把 StS2 的 2025–2026 发售日期作为对 lmgame-Bench 三大质疑（尤其数据污染）的先天豁免正面陈述；并按 PokeAgent 剧本发布"轨迹+基线（BC/DT/GRPO 各一条，取自第③节配方）+评测协议+排行榜"四件套，为后续竞赛和"活 benchmark"留好接口。

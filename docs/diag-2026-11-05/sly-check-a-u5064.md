# 奇巧（Sly）专项核查：线 A pinned-0005064，dev-128 贪心（2026-11-05 17:30 EST）

数据：行为审计回放落盘（`scratchpad/audit/a-u5064-dev128/{decisions,games}.jsonl`，47,375 个决策、128 局，与官方评测逐种子一致）；
脚本 `scratchpad/audit/scripts/sly_check.py`（stdlib）。奇巧 = 关键词 Sly（"若此牌在回合结束前从手牌被丢弃，则免费打出"）。
自带奇巧的牌（模拟器源码）：Reflex、Tactician、Ricochet、Flick Flack、Abrasive、Untouchable；Hand Trick / Master Planner 可赋予。

| 指标 | 数值 |
| --- | --- |
| 最终牌组里有自带奇巧牌的局 | 31 / 128（Flick Flack 27 局 32 张；Ricochet 3；Untouchable 2；Tactician 1；Reflex 0） |
| 弃牌触发遗物 | 铜钹 3 局、结实绷带 3 局 |
| 战斗决策中手里有奇巧牌的 | 1,185 / 36,702（3.2%） |
| 弃牌"出口"打出次数 | 2,338（生存者 1,412、投掷匕首 464、暗藏匕首 306、计算下注 122、钢铁风暴 13、早有准备 9、杂技 6、必备工具 4、影步 2） |
| 弃牌提示（出口之后的 HandSelect） | 1,947 |
| 其中有奇巧牌可选 | 83 |
| **有奇巧可选时选了奇巧牌** | **76 / 83 = 92%** |
| 弃的是诅咒 / 状态牌 | 293 / 1,947（15%；Ascender's Bane 202、Clumsy 33、Wound 12…） |
| 最常弃的牌 | Strike 758、Defend 548、Ascender's Bane 202、Flick Flack 70（即奇巧触发）、Bouncing Flask 33 |
| Hand Trick / Master Planner 打出 | 0 / 0（从未进牌组） |

**结论**
1. 奇巧触发**会用**：手里有奇巧牌时 92% 的弃牌选它，Flick Flack 被弃 70 次都是触发。机制被理解了。
2. 但**不围绕它构筑**：128 局只有 1 张 Tactician、0 张 Reflex；奇巧牌 97% 的战斗决策里根本不在手上。弃牌出口倒是很常用（生存者、投掷匕首）。
3. 弃牌选择大体合理：优先弃 Strike / Defend 和诅咒；弃 Bouncing Flask 33 次值得再看（毒构筑的核心牌）。
4. 环境侧：手牌行带奇巧位（模型读到）；**弃牌提示的选项行不带关键词位**（模型只能靠卡牌嵌入识别）——92% 的命中率说明目前够用，但这是信息对等里可以补齐的一处（`env-information-check.md` 会给结论）。

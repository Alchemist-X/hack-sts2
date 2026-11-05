# 行为审计：线 A pinned-0005064，dev-128 贪心（2026-11-05）

对象：`runs/w3m-a/checkpoints/pinned-0005064.pt`，`silent-a10-dev-v1` 128 局（THE SILENT，A10），greedy、loop breaker 关、
room macros `rest,shop`。数据：逐决策回放 `a-u5064-dev128/{decisions,games}.jsonl`（47,375 个决策；与官方评测逐种子
result/act/floor 128/128 一致，progress 均值 0.4351；replay audit 128/128；生成方式见该目录 README）。

脚本（stdlib，`python3 -I`，全部在 scratchpad `/private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/audit/scripts/`）：
`behaviour_audit.py`（主表 → `behaviour_audit.json`）、`behaviour_audit_extra.py`（对照与追踪 → `behaviour_audit_extra.json`）、
`pick_rates.py`、`pick_context.py`、`discard_ethereal.py`。卡牌类型 / 稀有度 / 费用取自 `knowledge/source/v0.111.0/cards.jsonl`，
卡池取自 `sim/specs/inventory/cards-silent-defect.classified.json`。

结局分组（下文通用）：**A1 死亡** 47 局；**A2 途中死亡**（进了 Act 2、没到 Act 2 boss）60 局；**到 A2 Boss** 21 局（其中 2 局
打过、进 Act 3 后死亡）。128 局全负。

“七巧板”在 v0.111.0 本地化里不存在；对应的机制是关键词 **Sly（奇巧）**：“回合结束前从手牌被弃掉，就免费打出”。
本文按奇巧理解。

## 结论先行

1. **构筑方向：只有毒系有可辨认的倾向，小刀和弃牌/奇巧基本没有成型；而且选牌是固定偏好，不看已有构筑。**
   - 最终牌组 40/128 判为“毒”，8 局“小刀”，1 局“弃牌/奇巧”，0 局“防御”；43 局混合，36 局无方向。
   - Noxious Fumes 被选 38/39，Bouncing Flask 42/47。毒系牌的选取率与牌组里已有几张毒牌无关：已有 0 张时 61.8%，1–2 张时 58.4%，3 张以上 55.8%。
   - Footwork 0/47、Accuracy 0/26、Tactician 0/44、Reflex 0/37、Accelerant 0/46、Finisher 0/39。
2. **奇巧/弃牌：战术上会用，构筑上不追。**
   - 弃牌时有奇巧牌可选的 81 次里，弃了奇巧牌 76 次；均匀随机只会是 17 次。Flick Flack 70/70 次可弃都弃了。
   - 手里有 Beckon / Infection（留手回合末掉血）时，38 次里弃了 37 次。
   - 有些用法明显浪费：Flick Flack 被花费用手动打出 157 次。Hand Trick 给技能牌加上奇巧之后，56 次里有 41 次又手动打掉了这张牌，同回合把它弃掉的只有 4 次。
   - 环境给了选项卡牌 id，但选项行不带关键词位，也不带提示来源。
3. **路线：完全避战精英，靠火堆续命。**
   - 下一步可以绕开精英时，257 次里 0 次选精英；194 场精英战全部是“前方只剩精英格”被迫进入。
   - 有火堆可选时选火堆 213/223，商店 112/120，问号房 327/451，普通怪 98/516。
   - 由于只看局部、不看全图，Act 1 实际打精英 1.12 场，而所选路线的理论最少是 0.56 场。
4. **火堆：453/453 全部休息，从未锻造（升级）。** 满血休息 35 次，≥80% HP 休息 113 次；锻造各行的概率之和最大为 0.000。
   商店删牌 0 次：203 个有删牌行的商店决策，概率质量合计只有 0.01，其中 49 次可以删诅咒。
5. **A2 Boss 的死因是输出不足**：21 局只过 2 局。
   - Kaiser Crab 1/10：第 4 回合的 57 点攻击意图前，Boss 只掉 15–36% 血。
   - The Insatiable 1/8：毒叠到 12–29 层，但起量太慢，第 7–10 回合 Boss 还剩 18–45% 血。
   - 输掉的局里，药水大多已在 Boss 战中用掉：带药水死只有 3/19。
6. **判定**：存在真实、与结局相关的策略成分：奇巧触发、弃有害状态牌、毒系偏好、Act 1 Boss 前带成长牌。
   但也有大量确定性的病态捷径：永远休息、从不升级、从不删牌、几乎不跳卡（跳过率 6.3%，牌组膨胀到 28–32 张）、
   避战精英只做局部避让、跳过免费宝箱遗物 24/175。详见第 5 节。

---

## 1. 构筑方向（Archetype）

### 1.1 分组（只列本 dump 里出现过的 id，或 Silent 卡池中同组的 id）

| 组 | card id |
| --- | --- |
| 起始 | STRIKE_SILENT, DEFEND_SILENT, NEUTRALIZE, SURVIVOR |
| 小刀 shiv | ACCURACY, BLADE_DANCE, BLADE_OF_INK, BLADE_SYMPHONY, CLOAK_AND_DAGGER, FAN_OF_KNIVES, FINISHER, HIDDEN_DAGGERS, INFINITE_BLADES, KNIFE_TRAP, LEADING_STRIKE, PHANTOM_BLADES, STORM_OF_STEEL, UP_MY_SLEEVE |
| 毒 poison | ACCELERANT, BOUNCING_FLASK, BUBBLE_BUBBLE, CORROSIVE_WAVE, DEADLY_POISON, ENVENOM, HAZE, MIRAGE, NOXIOUS_FUMES, OUTBREAK, POISONED_STAB, SNAKEBITE |
| 弃牌/奇巧 discard_sly | ABRASIVE, ACROBATICS, CALCULATED_GAMBLE, DAGGER_THROW, FLICK_FLACK, HAND_TRICK, MASTER_PLANNER, MEMENTO_MORI, PREPARED, REFLEX, RICOCHET, SHADOW_STEP, TACTICIAN, TOOLS_OF_THE_TRADE, UNTOUCHABLE |
| 防御 block | AFTERIMAGE, ANTICIPATE, BACKFLIP, BLUR, DEFLECT, DODGE_AND_ROLL, ESCAPE_PLAN, FOOTWORK, LEG_SWEEP, MALAISE, PIERCING_WAIL, SHADOWMELD, WELL_LAID_PLANS, WRAITH_FORM |
| 通用攻击 | ASSASSINATE, BACKSTAB, DAGGER_SPRAY, DASH, ECHOING_SLASH, FLECHETTES, GRAND_FINALE, MURDER, PINPOINT, POUNCE, PRECISE_CUT, PREDATOR, SERPENT_FORM, SKEWER, SLICE, STRANGLE, SUCKER_PUNCH, SUPPRESS, THE_HUNT, TRACKING |
| 功能 | ADRENALINE, BULLET_TIME, BURST, EXPERTISE, EXPOSE, NIGHTMARE, SIDESTEP |
| 诅咒/状态 | 卡牌类型为 Curse / Status 的牌（ASCENDERS_BANE, CLUMSY, INJURY, …） |
| 非 Silent | 其他职业 / 无色牌 |

v0.111.0 的 Silent 卡池里没有 Catalyst、Corpse Explosion、Concentrate、Eviscerate、Sneaky Strike。HIDDEN_DAGGERS 和
STORM_OF_STEEL 既弃牌又产小刀，这里归入小刀组。它们的弃牌行为在第 2 节单独统计。

判定规则（四个构筑组）：某组 ≥4 张，且比第二多的组多 ≥2 张 → 该组；最多的组 ≥3 张 → 混合；否则 → 无方向。

### 1.2 最终牌组分类 vs 结局（n=128）

| 最终牌组 | n | A1 死亡 | A2 途中死亡 | 到 A2 Boss | 平均楼层 | 平均 progress |
| --- | --- | --- | --- | --- | --- | --- |
| 毒 | 40 | 3 | 24 | 13 | 28.0 | 0.541 |
| 小刀 | 8 | 0 | 6 | 2 | 26.5 | 0.510 |
| 弃牌/奇巧 | 1 | 1 | 0 | 0 | 17 | 0.314 |
| 防御 | 0 | – | – | – | – | – |
| 混合 | 43 | 13 | 24 | 6 | 23.3 | 0.444 |
| 无方向 | 36 | 30 | 6 | 0 | 15.9 | 0.294 |

混合局的细分：含毒 15+11+…，以“毒+小刀”为主。最终牌组分类有幸存者偏差（活得久的局拿的牌多），所以下面在固定时点再分类一次：

| 时点 | 毒 | 混合 | 无方向 | 小刀 | 弃牌 |
| --- | --- | --- | --- | --- | --- |
| Act 1 Boss 入场时 → Boss 胜率 | **22/24（92%）** | 27/38（71%） | 29/47（62%） | 1/1 | 2/3 |
| Act 2 开局时 → 到 A2 Boss 比例 | 6/21（29%） | 6/29（21%） | 9/27（33%） | 0/2 | 0/2 |

Act 2 开局牌组里有毒牌 0–1 / 2–3 / 4+ 张时，到 A2 Boss 的比例分别是 4/21、11/37、6/23。
有成长型能力牌（Noxious Fumes、Infinite Blades、Afterimage、Serpent Form 等，见脚本 `SCALING_POWERS`）的局是 11/49，没有的是 10/32。
**毒系对过 Act 1 Boss 有明显正相关，但对 Act 2 推进无可见帮助。**

各结局组的最终牌组平均张数：

| 组 | n | 起始 | 毒 | 小刀 | 弃牌/奇巧 | 防御 | 通用攻击 | 诅咒/状态 | 牌组 | 有成长能力牌 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| A1 死亡 | 47 | 12.2 | 1.8 | 0.7 | 1.0 | 0.6 | 3.2 | 1.5 | 21.5 | 19% |
| A2 途中死亡 | 60 | 11.8 | 3.6 | 2.1 | 1.1 | 1.1 | 4.7 | 1.8 | 28.1 | 77% |
| 到 A2 Boss | 21 | 11.8 | 4.2 | 1.9 | 1.3 | 1.8 | 6.9 | 1.5 | 31.7 | 76% |

起始牌约 12 张始终留在牌组里：火堆从不升级、商店从不删牌，见 3.4 / 3.5 节。

### 1.3 选卡（卡牌奖励）

1,343 次卡牌奖励。只统计最终取卡或跳过的决策；有 37 次界面上同时有两份奖励，会重复计“提供”。

- **跳过 85 次（6.3%）**：Act 1 76/965，Act 2 9/371。按当时牌组大小统计：15–19 张时 34/480，20–24 张时 35/423，25–29 张时 9/213，30 张以上时 2/75。牌组越大越不跳，与常识相反。
- 各组选取率（取 / 提供）：

| 组 | 取 / 提供 | 选取率 |
| --- | --- | --- |
| 通用攻击 | 479 / 949 | 50.5% |
| 毒 | 309 / 613 | 50.4% |
| 功能 | 56 / 154 | 36.4% |
| 小刀 | 164 / 629 | 26.1% |
| 非 Silent | 30 / 157 | 19.1% |
| 防御 | 104 / 762 | 13.6% |
| 弃牌/奇巧 | 116 / 899 | 12.9% |

- 几乎必拿：Backstab 35/36、Noxious Fumes 38/39、Expose 34/37、Leg Sweep 38/42、Bouncing Flask 42/47、Hidden Daggers 34/42、Afterimage 13/15、Serpent Form 13/15、Assassinate 15/15。
- 从不拿：Ricochet 0/127、Backflip 0/118、Deflect 0/118、Prepared 0/115、Anticipate 0/103、Up My Sleeve 0/52、Footwork 0/47、Accelerant 0/46、Tactician 0/44、Haze 0/43、Phantom Blades 0/43、Finisher 0/39、Reflex 0/37、Accuracy 0/26。另外 Untouchable 只拿过 1/111，Acrobatics 1/40。
- 是否按构筑选牌（`pick_context.py`），以下是“提供了该组牌时，取了该组牌”的比例：

| 牌组里已有同组牌 | 0 张 | 1–2 张 | 3 张以上 |
| --- | --- | --- | --- |
| 毒 | 84/136 = 61.8% | 143/245 = 58.4% | 82/147 = 55.8% |
| 小刀 | 73/264 = 27.7% | 72/214 = 33.6% | 19/50 = 38.0% |

  毒系完全不随已有构筑上升；小刀略升，但 n 很小。**行为像一张固定的卡牌优先级表**：便宜攻击 + 毒 + 少数强牌，不像“向某个方向收敛”。

示例（SI085，后来打过 A2 Boss）：Act 1 每次奖励都拿，依次是 Backstab、Dash、Expose、Predator、Bouncing Flask、Sucker Punch、Poisoned Stab。
Act 1 Boss 奖励是 Outbreak / Master Planner / Well-Laid Plans，选了跳过。Act 2 又拿了 Calculated Gamble、Echoing Slash、Precise Cut+、Noxious Fumes、Flick Flack、Afterimage。

## 2. 弃牌与奇巧

### 2.1 弃牌出口的使用

| 打出（贪心，128 局） | 次数 |
| --- | --- |
| Survivor | 1,412 |
| Dagger Throw | 464 |
| Hidden Daggers | 306 |
| Calculated Gamble | 122 |
| Storm of Steel / Prepared / Acrobatics / Tools of the Trade / Shadow Step | 13 / 9 / 6 / 4 / 2 |
| 收益牌：Hand Trick / Memento Mori | 87 / 22 |
| 奇巧牌被**手动**打出（付费） | Flick Flack 157、Abrasive 4、Ricochet 3、Untouchable 2 |

战斗决策里能打出至少一张弃牌出口（Survivor、Dagger Throw、Hidden Daggers、Prepared、Acrobatics、Calculated Gamble）时，
手里有奇巧牌时打出口的比例是 63/210 = 30.0%，没有奇巧牌时是 1,571/4,948 = 31.8%。
**没有“为了触发奇巧而打弃牌出口”的迹象。**
Flick Flack 手动打出的 157 次里，有 31 次当时手里就有可打的弃牌出口。

### 2.2 弃牌选择（1,981 次弃牌选择）

来源：Survivor 1,354、Dagger Throw 429、Hidden Daggers 142、回合开始弃牌 22（提示位置 `f9858a16`，推断为 Tools of the Trade）、Prepared 14、Gambler's Brew 14、Acrobatics 6。

| 被弃的牌 | 次数 | 份额 | 均匀随机期望 |
| --- | --- | --- | --- |
| 攻击 | 826 | 41.7% | 876 |
| 技能 | 674 | 34.0% | 873 |
| 诅咒/状态 | 360 | 18.2% | 181 |
| 奇巧 | 76 | 3.8% | 17 |
| 能力 / 其他（Quest） | 26 / 19 | 2.3% | 34 |

条件统计：

| 条件 | n | 选中 | 均匀期望 |
| --- | --- | --- | --- |
| 选项中有奇巧牌 | 81 | **76** | 17.2 |
| 选项中有诅咒/状态 | 626 | 360 | 181 |
| 其中有 Beckon/Infection（留手回合末掉血） | 38 | **37** | 15.1 |
| 其中有 Ethereal 诅咒（Ascender's Bane / Clumsy / Dazed，留手会在回合末消耗） | 459 | 249 | 118.8 |
| 选项中有不可打出的牌 | 979 | 733 | 532 |
| 无诅咒、无奇巧时，弃 Strike/Defend | 1,235 | 1,051 | 818 |
| 无诅咒、无奇巧时，弃 Neutralize（0 费核心牌） | 244 次可选 | 1 | – |

- 奇巧牌按 id：Flick Flack 70/70、Ricochet 4/4、Tactician 2/4、Untouchable 0/4（可弃却没弃）。
- 最常被弃：Strike 752、Defend 548、Ascender's Bane 204、Flick Flack 63（+Flick Flack+ 7）、Clumsy 34、Bouncing Flask 31。
  **Bouncing Flask 31 次 / Noxious Fumes 12 次属于弃掉毒核心牌。**
- 同时有 Strike 和 Defend 可选时，弃 Strike 547 次、Defend 272 次。
- 小瑕疵：Ascender's Bane / Clumsy 带 Ethereal，留在手里回合末会被消耗（本场战斗不再出现）。弃掉它们反而让它们回到弃牌堆、之后再抽到。
  这类弃牌 249 次，是看似合理、实则略亏的选择。

示例（SI006 t161–163）：手牌 `FLICK_FLACK*, SNAKEBITE, DEFEND, SURVIVOR, STRIKE, HIDDEN_DAGGERS, ASCENDERS_BANE!`，3 费。
打 Survivor → 弃 Flick Flack（免费 AoE），然后用剩下的 2 费打 Defend。这是正确的奇巧用法。

### 2.3 Hand Trick（赋予奇巧）

Hand Trick 共打出 87 次，其中进入选牌提示 56 次。被选中加奇巧的技能：Defend 37、Deadly Poison 5、Survivor 3、Snakebite 3。
之后同回合的去向：手动打出 41 次（白给奇巧）、未处理 11 次、被弃掉触发 4 次。
**模型没有学会 Hand Trick 的用途，基本把它当成一张格挡牌。**

注：同目录 `sly-check-a-u5064.md` 写的“Hand Trick / Master Planner 打出 0/0、从未进牌组”与本 dump 不符：Hand Trick 打出 87 次，最终牌组出现在 12 局。
`env-information-check.md` 写的“动态授予的 Sly 出现 0 次”也与此处不符：同回合动态奇巧被弃 4 次。

### 2.4 环境是否给足信息

- 弃牌提示（HandSelect）的 `prompt_options` 逐项给出卡牌 `item_id` 和 `upgrade`。dump 里 1,915/1,966 个首选提示的选项与手牌同序同内容；不一致的 51 个是 Hand Trick 只列技能牌。
  同时模型能读到 `hand` 表，里面每张牌有 `keyword_mask`（含动态奇巧位）和是否可打出。所以“这张能被奇巧触发”的信息存在。
- 有三处缺口：
  - 选项行不带关键词位、费用。
  - **`prompt_site_hash` 被模型显式跳过**（`model.py` 的 `skip` 集合），模型也读不到触发提示的牌。
    Survivor 的弃牌和 Hand Trick 的“选一张技能加奇巧”都是 HandSelect / Hand / min1 / max1，模型只能靠选项集合（Hand Trick 只列技能）和上下文区分。
    这可能是 2.3 节 Hand Trick 用法错误的部分原因，未验证。
  - 卡牌奖励、商店、火堆的卡牌只以 id 嵌入出现，`card_static` 不读。
- 结论：**对静态奇巧牌，信息够用，行为也对（94%）；对动态奇巧和提示来源，信息有缺口。**

## 3. 路线、精英、火堆、商店

### 3.1 精英

| | 可避开精英的 Travel 决策 | 选精英 | 均匀期望 |
| --- | --- | --- | --- |
| 全部 | 257 | **0** | 122 |

194 次精英战入口 Travel 全部是“所有可选节点都是精英”的被迫选择。

各节点类型的偏好（决策里同时有 ≥2 种类型时）：

| 类型 | 选中 / 有该类型可选的决策 | 均匀期望 |
| --- | --- | --- |
| 火堆 | 213 / 223 | 105.7 |
| 商店 | 112 / 120 | 53.5 |
| 问号 | 327 / 451 | 210.4 |
| 普通怪 | 98 / 516 | 258.3 |
| 精英 | 0 / 257 | 122.2 |

每 act 实际打的精英，与该 act 第一步所选节点出发的最少 / 最多精英数比较（`map_routes`，模型不读，仅供分析）：

| | 走完该 act 的局 | 实际打精英（均值） | 路线最少（均值） | 路线最多（均值） | 恰好最少 | 分布 |
| --- | --- | --- | --- | --- | --- | --- |
| Act 1 | 113 | 1.12 | 0.56 | 3.11 | 59（52%） | 0 场 24 局、1 场 55 局、2 场 30 局、3 场 4 局 |
| Act 2 | 21 | 1.00 | 0.43 | 3.19 | 11（52%） | 0 场 7 局、1 场 8 局、2 场 5 局、3 场 1 局 |

只有局部避让，没有全局规划：多步决策中选“前方最多精英数”最小的节点 333/525（均匀 251.5），而选前方最多火堆、最多商店的节点更强（454/723、341/546）。
结果常被逼进“只剩精英格”的路段。

示例（SI085 Act 2 第 9–12 层）：
Rest 64/77 HEAL → 前方只剩精英，被迫进 → Rest 52/77 HEAL → 再次被迫精英。
第 6 层它曾在 Elite 和 Unknown 之间选了 Unknown。

### 3.2 好局 vs 坏局

同一个贪心策略在不同种子上运行，所以差别主要来自地图和奖励。

| | A1 死亡（到 A1 Boss 的 32 局） | A2 途中死亡（60） | 到 A2 Boss（21） |
| --- | --- | --- | --- |
| Act 1 精英场数 | 1.38 | **0.85** | **1.52** |
| Act 1 商店 / 火堆 / 问号 次数 | 0.78 / 3.00 / 3.72 | 1.27 / 3.18 / 3.50 | 1.10 / 3.05 / 3.33 |
| Act 2 开局：遗物 / 牌组张数 / 金币 / 药水 | – | 4.95 / 23.6 / 85 / 0.75 | 5.10 / 23.0 / 89 / 0.71 |
| Act 2 精英场数（胜） | – | 0.45（0.10） | 1.00（1.00） |
| 火堆选择 | 100% 休息 | 100% 休息 | 100% 休息 |
| 最终遗物 | 4.96 | 6.83 | 9.38 |
| 最终金币 | 147 | 103 | 76 |

- 好局在 Act 1 多打了约 0.7 场精英（被迫的），Act 2 开局多约 0.15 个遗物。路线、火堆、商店行为本身没有系统差别。
- A2 途中死亡集中在 Act 2 第 4–8 层（43/60）。39 局死于普通怪，21 局死于精英；Decimillipede 杀 12 局，Entomancer 杀 6 局。
- 每场战斗平均掉血（占最大 HP 的比例）：

| | 普通怪 | 精英 | Boss |
| --- | --- | --- | --- |
| Act 1 | 7.9% | 31% | 60% |
| Act 2 | 18.1% | 41% | 75% |

  **问题在 Act 2 普通战的伤害效率，不在路线选择。**

### 3.3 火堆

| | 休息 | 锻造 | 其他（LIFT/HATCH/DIG 可选 25 次） |
| --- | --- | --- | --- |
| Act 1 / 2 / 3 | 360 / 90 / 3 | 0 / 0 / 0 | 0 |
| HP <40% / 40–60% / 60–80% / ≥80% | 110 / 94 / 136 / 113 | 0 | 0 |

- 满血休息 35 次，例如 SI004 t102 70/70 → HEAL。≥90% HP 休息 70 次。
- 休息行的概率在 453 次里全部是 1.000，锻造各行合计最大 0.000。确定性的“永远休息”。
- 火堆从未锻造，所以没有“先升级哪张牌”的数据。最终牌组的升级卡（平均 1.3 / 2.3 / 3.4 张）来自事件和升级奖励。
  事件 DeckUpgrade 提示 37 次，选 Defend 17 次、Dash 4 次。

### 3.4 商店

- 访问 223 次，购买 347 次：卡 176、药水 139、遗物 16、无色牌 16。最常买 Noxious Fumes 23、Fire Potion 14、Poisoned Stab 14。
- **删牌 0 次**：203 个有删牌行的决策，删牌概率质量合计 0.01，其中 49 次可以删诅咒。

### 3.5 遗物

- 宝箱遗物跳过 24/175（13.7%）：Meal Ticket 5、Joss Paper 5、Potion Belt 2 等。免费遗物几乎总该拿。
- 精英 / Boss 奖励遗物只有 3/255 没领。

## 4. Boss 战

### 4.1 汇总

| | n | 入场 HP | 回合（均值 / 中位） | 入场药水 | 用掉 | 剩余 | 带药水死 | 牌组有成长能力牌 | 战中打出能力牌 | Boss 剩余血量 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| A1 Boss 胜 | 81 | 64.7（89%） | 9.9 / 9 | 1.54 | 1.23 | 0.31 | – | 52% | 56% | – |
| A1 Boss 负 | 32 | 49.4（70%） | 10.2 / 10 | 1.00 | 0.97 | 0.03 | 1/32 | 25% | 31% | 26% |
| A2 Boss 胜 | 2 | 73.5（96%） | 8.5 | 1.0 | 0.5 | 0.5 | – | 2/2 | 2/2 | – |
| A2 Boss 负 | 19 | 54.2（76%） | 6.8 / 7 | 1.05 | 0.84 | 0.21 | 3/19 | 74% | 63% | **49%** |

A1 Boss 按 Boss 的胜场 / 场数：Ceremonial Beast 15/17、Soul Fysh 16/21、Lagavulin Matriarch 15/20、Waterfall Giant 13/20、
Vantom 14/22、The Kin 8/13。A1 Boss 的胜负与入场 HP、是否带成长牌明显相关。

药水总体：使用 713 次。普通怪 314、精英 204、Boss 149、问号房战斗 38、非战斗 8。47% 在第 1 回合用。
战斗中药水栏全空的决策占 41%。死亡时还有药水的只有 9/128 局。**没有囤药水的问题**。

### 4.2 A2 Boss（21 局：Kaiser Crab 1/10、The Insatiable 1/8、Knowledge Demon 0/3）

- **Kaiser Crab（总血 428）**：伤害意图基本都是 25 → 34 → 14 → **57**，第 4 回合是大招。
  输的 9 局里，活到第 4 回合的 8 局，第 4 回合开始时 Boss 平均还剩约 70% 血（47–87%）；SI084 只撑了 3 回合，Boss 掉到 379。
  唯一胜局 SI121：入场满血 77/77，第 1 回合就打出 Noxious Fumes + Infinite Blades，之后两次 Afterimage，毒稳定在 16–19 层，第 8 回合击杀。
- **The Insatiable（341）**：打得长（6–10 回合），Boss 第 5、9 回合加 3 点力量。
  毒能叠到 12–29 层，但 7 局输局中 Noxious Fumes 3 局没打出、3 局第 2–5 回合才打出、1 局第 1 回合打出，死亡时 Boss 还剩 18–45% 血。
  胜局 SI085：Noxious Fumes 第 1 回合打出，第 4 回合再打一张，第 9 回合毒 33 层，Boss 剩 34 血时击杀。
- **Knowledge Demon（399）**：拖到第 7–13 回合，死时 Boss 剩 23–47% 血。
- 输掉的 19 局里，有 12 局在战中打出了 Noxious Fumes 或其他成长能力牌。
  所以不是“没有成长计划”，而是**起量慢、前期输出不足、格挡跟不上**。
  牌组 26–39 张，加上约 12 张从未升级的起始牌，抽到核心牌的频率很低。

## 5. 判定：在学游戏，还是在走捷径？

像真实策略的部分（稳定、且和结局相关）：
- 奇巧触发：有奇巧可弃时弃了 76/81，均匀期望 17。
- 弃 Beckon/Infection：37/38。
- 不弃 Neutralize：1/244。
- 毒系与少数强牌的高选取率：Noxious Fumes 38/39，Backstab 35/36。
- 毒牌组在 Act 1 Boss 胜率 92%，无方向牌组 62%。
- 药水正常使用：带药水死 9/128。
- 极少留能量空过回合：3/6,455。

像捷径或病态的部分（确定性、违背常识）：
- 火堆 453/453 休息，包括 35 次满血。
- 0 次锻造，0 次删牌。
- 避战精英 0/257，但没有全局路线规划，194 场精英全是被迫进入。
- 跳卡 6.3%，牌越多越不跳，牌组膨胀到约 30 张。
- 选牌不看已有构筑：毒牌选取率 62% → 56%，随已有毒牌增加反而略降。
- Footwork、Accuracy、Tactician、Reflex 等关键件 0 选。
- Hand Trick 的奇巧 41/56 被手动打掉。
- 跳过免费宝箱遗物 24/175。

这组行为与“楼层进度”指标高度吻合：休息、避战、回避风险能最大化期望层数。但这条路的上限就是 Act 2：A2 Boss 2/21 通过，128 局 0 胜。

方法本身是可复现的：CPU/GPU 逐局一致，128 个固定种子，replay audit 通过。
**但只看 progress 均值会掩盖这些病态**：若干决策维度（锻造、删牌、主动打精英）已经塌缩成确定性的 0，要靠 dump 级别的行为指标才看得见。
建议把“锻造率、删牌率、跳卡率、可避精英选择率、奇巧命中率”列为评测门槛的附属指标。

## 不能回答的问题

- **为什么从不锻造 / 删牌**：贪心 argmax 只给出当前偏好。它是训练早期塌缩、奖励设计（progress 偏好回血）、还是 macro 行编码问题，需要训练 rollout 的采样记录，这里没有。
  采样策略下这些行是否偶尔出现，本 dump 也看不到。
- **如果锻造 / 删牌 / 打精英会不会更好**：需要反事实（lookahead 或强制改动的 A/B），本审计没跑。
- **事件选择是否合理**：事件选项只有 `key_hash`，没有可读文本 id（README），未评估。
- **战斗微操质量**：出牌顺序、目标选择、按意图格挡，除弃牌外未审计。PlayCard 平均置信度 0.598，主要是同名牌之间的平局。
- **构筑与结局的因果**：同一策略跨种子，差异来自地图和奖励；只能给相关性。A2 Boss 只有 n=21，Act 2 结论的统计功效低。
- **回合开始弃牌（提示位置 `f9858a16`，22 次）**：归为 Tools of the Trade 是推断，未逐局核对其能力。
- **Act 2 开局 HP**：第一个 Act 2 决策位于先古事件（Ancient），在可能的回血之前，所以没有用于对比。
- **“七巧板”**：按奇巧（Sly）理解。若项目负责人指的是某个具体遗物，v0.111.0 本地化里没有同名条目，需要确认。

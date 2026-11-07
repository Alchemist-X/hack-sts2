# 环境信息核查：观测是否给了策略人类所拥有的猎手（THE SILENT）信息

日期：2026-11-05。对象：line A `pinned-0005064`（arch `d192/s384/depth3`，`aux_heads` + `deck_counts`，macros `rest,shop`，
encoder `3b73317739c650bd`，bundle `sts2train-bd6d6f6863`）。依据：`sim/VEC.md`（Schema / Field reference / 玩家视角规则
H1–H15）、`sim/tools/train/sts2train/{features,model}.py`（与 raven 上 bundle 的 `audit/lib/sts2train` 逐字节一致，已 diff）、
schema 的 `card_static` 表（raven 容器内探针读取）、`knowledge/source/v0.111.0/{cards,keywords,relics}.jsonl`，以及
`a-u5064-dev128/decisions.jsonl`（128 局、47,375 个决策）的汇总统计。

## 结论（先说）

1. **弃牌选择本身信息是够的。** 战斗内 HandSelect 弃牌提示时，`prompt_options` 逐项给出手牌的 `item_id`（卡牌 id）+
   `upgrade` + `ordinal`；同时 `hand` 表仍在（含每张牌的当前费用、`keyword_mask` 的 Sly 位、是否可打出）。dump 里 2,059 个
   战斗内 HandSelect 中 2,008 个的选项与手牌**同序同内容**（其余 51 个是 HAND_TRICK 只列技能牌、TOUCH_OF_INSANITY 的过滤，属正常）。
   “七巧板”在 v0.111.0 里对应关键词 **Sly（奇巧）**：“回合结束前从手牌被弃掉则免费打出”；TACTICIAN / REFLEX / UNTOUCHABLE /
   FLICK_FLACK / RICOCHET 静态带 Sly（`keyword_mask` bit 6 = 64）。结果上：有 Sly 选项的 84 个弃牌提示中模型 77 次弃了 Sly 牌。
2. **但有结构性缺口，主要不在“看不见”，而在“看见的方式”：**
   - 提示选项行**不带**费用 / 关键词 / 数值，候选打分也**不指回手牌槽位**；Sly 只能靠卡牌 id 嵌入（静态 Sly 可学）或经
     trunk 注意力间接关联。HAND_TRICK / MASTER_PLANNER **动态授予**的 Sly 无法与具体选项对齐（本 dump 中这种情况出现 0 次，
     所以尚未造成实际损失）。
   - 选牌（`TakeCard` 宏）、商店、删牌、火堆升级、牌组/牌堆里，卡牌**只以 id 嵌入**出现：没有费用、类型、数值、关键词。
     schema 已发布 `card_static` 静态表，但**模型不读**。卡牌“知识”全部要靠 RL 信用分配从 id 嵌入里学出来。
   - 地图：模型读 `map_nodes`，但整张图被单层线性 + 均值池化成一个 token，`Travel` 候选只拿到目标节点自身的特征；
     `map_routes`（前方最少/最多精英、火堆、商店数）**不读**。人类能一眼看到整条路线。
   - 事件：选项只有 `key_hash`（文本键的哈希）+ 遗物 id；`event_option_values`（按钮上显示的 HP/金币数值）**不读**。
3. **反方向（策略看到人类看不到的）未发现泄漏。** 抽牌顺序、RNG、未来房间、宝箱遗物、怪物招式 id 均按 H1–H15 屏蔽，并有
   C# 泄漏测试；奖励卡在奖励界面可见（H7，人类点开再关也免费可见），宝箱、事件随机性不可见。唯一值得记录的是
   **无记忆**带来的反向劣势：策略没有历史（上回合意图、本回合已打出牌数），人类有。

## 机制 → 人类能看到 → 观测里有没有 → 字段

“模型读”= 在 `features.USED_BUFFERS` 内且 `model.Policy` 实际使用；“只在 schema”= 环境发布但模型不读。

| 机制 | 人类能看到 | 观测里有没有 | 字段（模型实际怎么用） |
| --- | --- | --- | --- |
| 弃牌提示：能弃哪些牌 | 手牌全部高亮可选 | 有，模型读 | `prompt_options[i].{item_id, upgrade, picked, kind_of_item, ordinal}` → `prompt_mlp`（card32(id) + upgrade + ordinal + picked）；候选 `cand.option_index` 指向该行，`cand.card_id` → `c_card(card_emb)` |
| 弃牌提示：提示的来源/数量 | 卡面文字“弃 1 张” | 有，模型读 | `global_i.{prompt_kind, prompt_min, prompt_max, prompt_picked, prompt_pile}`；来源卡只间接可知（`prompt_site_hash` 被模型**跳过**，不嵌入） |
| 手牌的 Sly（含动态授予） | 卡面关键词 | 有，模型读（仅 hand 表） | `hand.keyword_mask` → `bits(·, 8)`（CardKeyword 恰 8 个成员，Sly = bit 6，完整）。`prompt_options` **无** keyword 列 |
| 手牌费用 / 能否打出 | 卡面费用、灰显 | 有，模型读 | `hand.{cost_current, cost_base, playable, playable_reason_id}` |
| 弃牌后会触发什么（Sly 免费打出、Tingsha、Tough Bandages、Memento Mori） | 卡面/遗物文字 | 部分：id 有，**效果文字无** | Sly 位 + 卡 id 嵌入；遗物只有 `relics.{relic_id, counter, status, used_up, stack}`，TINGSHA / TOUGH_BANDAGES 无计数器，效果须从 id 学 |
| 本回合已弃牌数 | Memento Mori 卡面数字实时更新 | 间接有 | 无计数字段；`hand_dmg` / `hand_f` 用 `CardPreview.Damage`，含 `CalculatedDamageVar`，故 Memento Mori 的预览伤害包含弃牌加成 |
| 本回合已打出攻击数（Finisher） | 卡面“(Hits X times)” | **没有** | `hand.hits` 用 `PlayerView.HitCount`：“运行时计算命中数的卡报 1”；无“本回合已打出牌数”字段 |
| 抽牌堆组成（多重集） | 点开抽牌堆可看（顺序隐藏） | 有，模型读（压缩） | `draw_counts[card_id*2+upg]` → `_piles`：log(1+份数)·card_emb 求和 + 去重数 + 升级数 → 一个向量。精确份数不再可逐项读出 |
| 弃牌堆 / 消耗堆 | 可点开看，含顺序 | 多重集有；顺序只在 schema | `discard_counts`、`exhaust_counts`（模型读，同上压缩）；`discard_order` / `exhaust_order` 不读 |
| 抽牌堆中被附魔/诅咒的牌 | 可看 | 只在 schema | `draw_marked`、`global_x.draw_marked_n` 不读 |
| 敌人意图 + 伤害数字 | 意图图标 + 数字 | 有，模型读 | `enemy_intents[j][k].{type, damage(单次、已含修正), times, card_count}`，每敌 3 条；`global_i.incoming_damage/hits`、`global_f.incoming_net_hp` |
| 敌人中毒 / 虚弱 / 易伤层数 | 图标 + 数字 | 有，模型读 | `enemy_powers[j][k].{power_id, amount}`（`DisplayAmountToPlayer`，每敌 12 条）→ 每敌均值池化成 32 维 |
| 玩家能力（Accuracy、Noxious Fumes、Phantom Blades、下回合效果等） | 图标 + 数字 | 有，模型读 | `powers_player.{power_id, amount, is_debuff}`（24 条） |
| 本回合生成的 Shiv | 手里就看得到 | 有 | Shiv 在 `hand` 里（card_id=SHIV，带 Exhaust 位）；消耗掉的在 `exhaust_counts`（Knife Trap 所需） |
| 能量 / 最大能量 / 下回合能量 | 能量球；遗物计数器 | 当前有，“下回合”只经遗物/能力间接 | `global_i.{energy, max_energy}`；`relics.counter`；`powers_player` |
| 药水 | 图标 + 文字 | id 有，文字无 | `potions.{potion_id, usable_now, target_type}` |
| 牌组构成 | 牌组界面（卡面全文） | 只有 id | `deck.{card_id, upgrade, enchant_id, enchant_amount, affliction_id}` → 计数加权 bag + 7 个计数特征；**无费用/类型/数值** |
| 卡牌奖励（宏 `TakeCard`） | 卡面全文（含是否升级） | **只有 id**；升级标志丢失 | 候选 `cand.card_id` → `c_card(card_emb)` + `c_copies`（牌组里已有几张）；`reward_choices`（含 upgrade 与 22 个卡面列）**不读**（VEC.md “Known gaps” 已列） |
| 商店 | 卡面、价格 | id + upgrade + 价格 | `shop.{category, item_id, upgrade, price, stocked, on_sale, affordable}`；卡用 `item_emb`（与 `card_emb` 不是同一张嵌入表） |
| 火堆选项 / 升级哪张 | 选项 + 升级预览 | 选项有；升级预览无 | `rest.{option_id, enabled}`；`Rest:SMITH:<card>` 宏行只带 `card_id` |
| 地图路线（前方精英、火堆、商店） | 整张地图 | 拓扑有、规划特征不读 | `map_nodes.{point_type, visited, current, legal_next, child_mask, quest}` + 行列嵌入 → 单层 + 全图均值；`Travel` 只拿目标节点那一行；`map_routes` 不读 |
| 本层 Boss | 地图顶部 | 有 | `global_i.{boss_id, second_boss_id}` |
| 事件选项内容 | 按钮文字与数值 | 只有哈希 | `event_options.{key_hash, ordinal, enabled, locked, proceed, relic_id}`；`event_option_values` 不读 |
| 怪物身份 | 立绘 | 有 | `enemies.monster_id`（+ hp/block/flank/behind） |
| 怪物招式历史 / 循环位置 | 人类记得 | **没有**（策略无记忆） | 仅 `global_i.{turn, round}`；招式 id 按 H8 屏蔽 |

## 卡牌知识是怎么给网络的（问题 2）

- **手牌行（`hand`，以及只在 CardReward 阶段出现的 `card_reward`）** 经 `Policy._cards`：`card_emb`(48) + type / target / rarity
  嵌入 + enchant / affliction 嵌入 + 前 4 个动态变量的 `var_id` 嵌入与 `var_val`（symlog）+ `upgrade, cost_current, cost_base,
  enchant_amount, playable, hits` + `bits(keyword_mask, 8)` + `bits(playable_reason_id, 8)` + `hand_f`（有效格挡/群伤）+
  `hand_dmg`（对每个敌人的有效伤害）。
- **其他位置（牌组、牌堆、奖励宏、商店、删牌/升级宏、提示选项）** 只有 card id 嵌入（及 upgrade 标志，奖励宏连 upgrade 都没有）。
- **`card_static`**（schema 发布：费用 / 升级费用、类型、稀有度、目标、关键词及升级后关键词、伤害/格挡/段数及升级值、前 4 个
  变量及升级值）**模型完全不读**。也就是说，网络从未被告知“DEADLY_POISON 施加 5 层毒”，除非这张牌在手里。

能否区分：

| 对比 | 手牌里（hand 行） | 手牌以外 |
| --- | --- | --- |
| DEADLY_POISON vs POISONED_STAB | 能：type Skill(2) vs Attack(1)；变量 `PoisonPower=5` vs `Damage=6, PoisonPower=3`；Stab 有 `hand_dmg` | 只能靠两个不同 id 的嵌入 |
| BLADE_DANCE vs CLOAK_AND_DAGGER | 能：`Cards=3` + Exhaust 位 vs `Block=6, Cards=1`；C&D 有 `block_eff` | 同上 |
| 但语义 | 变量名 `Cards` 在 Blade Dance（加 Shiv）与 Acrobatics（抽牌）里同名，含义须从 id 学 | — |
| 毒 / 虚弱 / 易伤层数 | 敌人 `enemy_powers.amount`、玩家 `powers_player.amount`，可见 | — |

（注：任务清单中的 SNEAKY_STRIKE、EVISCERATE、CONCENTRATE、CATALYST 不在 v0.111.0 词表中；本版对应物是 MEMENTO_MORI、
ACCELERANT、Sly 关键词等。）

## 具体缺口（按对“猎手构筑”影响排序）

1. **构筑决策看不到卡面。** `TakeCard`（1,258 次）、`Buy`、`Remove`、`Rest:SMITH` 只拿到 `card_emb[card_id]`。缺：
   `reward_choices` 的 22 列（尤其 `upgrade`、cost、type、vars、keyword_mask）与 `card_static`。后果：发展“小刀猎 / 毒猎”需要
   先在战斗里把 id 嵌入学成语义，再迁移到选牌——这是长链信用分配。修复成本低：把 `card_static[card_id]` 拼进 `card_emb`
   的输入（全位置共享），并让 TakeCard 行指向 `reward_choices` 行。
2. **提示选项行缺每实例属性且不指回手牌。** 缺：选项行的 `cost_current`、`keyword_mask`（动态 Sly）、enchant；或
   一个 `hand_slot` 指针（选项与手牌同序，`ordinal` 即手牌下标，env 侧补一列即可）。
3. **Finisher 段数**：缺“本回合已打出攻击数”（或 `hits` 使用 `CalculatedHits` 的当前值）。当前影响小（128 局终局牌组仅 1 张）。
4. **地图规划**：缺 `map_routes`（elites_min/max、rests、shops、boss_dist）进入 `Travel` 候选。与“是否避开精英”直接相关。
5. **事件数值**：缺 `event_option_values`。
6. **牌堆精确份数**被压成一个 bag 向量；人类能逐张数。属表示容量问题而非信息缺失。
7. **次要**：`discard_order`、`draw_marked`、`hand_x.{affliction_amount, dampened}`、`global_x.ancient_id` 不读。

## 反方向：信息对等（问题 3）

- 未发现策略看到人类看不到的东西：H1（抽牌顺序只给多重集，C# 泄漏测试 600 个重洗状态字节一致）、H2（无 RNG/种子）、H3（只有
  本层地图与本层 Boss）、H6（宝箱开前遗物 id=0）、H8（无招式 id）、H10（能力按 UI 显示值，AMBERGRIS 隐藏）。
- H7：奖励界面的卡牌内容通过宏可见，与人类“点开再关闭不消耗”等价；且模型实际只拿到 id。
- `hand_dmg`、`incoming_net_hp` 是人类可从 UI 自行得到的预计算量，不构成额外信息，只降低学习难度。
- `enemies.uid` 是唯一实例 id，仅用于定位目标，无信息量。
- 需注意的是训练/搜索层而非观测层：lookahead 若用 `branch` / `clone_determinized`，隐藏状态被重采样（VEC.md “Determinized
  clones”），不读真实未来；若用普通 `Clone()` 走真实 RNG 则会是“偷看未来”，需由 lookahead 实验自行确认用的是哪种。
- 反向劣势：策略无记忆（无 RNN / 历史），看不到上回合意图和本回合出牌记录；人类有。

## 不能回答的问题

- **真实游戏 UI 的逐项对照**未做实机核验：上表“人类能看到”列来自 VEC.md 的玩家视角规则与卡面模板（`{InCombat:…}` 占位
  说明游戏会在战斗中显示实时数值），未在游戏里截图确认（例如抽牌堆界面在 StS2 是否排序、招式名是否在悬停时显示）。
- `hand.var_val_k` 对 `CalculatedHits` 一类变量写的是基础值还是当前计算值：文档写“base IntValue”，未在运行中验证。
- 模型**是否真的利用了**这些字段（例如 Sly 弃牌 77/84 是靠 id 嵌入还是靠 hand 行的 Sly 位）只能用消融/探针回答，本次未做。
- 动态授予 Sly 后再弃牌的情形在该 dump 中出现 0 次，无法从数据评估第 2 条缺口的实际损失。
- 本核查只针对 `pinned-0005064` 所用 bundle；其他 checkpoint 若开启 `macros all` 或改了 `USED_BUFFERS`，结论需重查。

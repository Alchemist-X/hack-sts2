# 三局打到第三幕 Boss 的对局是怎么输的（wave 19，2026-11-06）

**结论先行**：三局都是在第三幕 Boss 房里**掉血速度快过推进速度**：输出跟不上 Boss 的血量，大伤害回合手里又抽不到格挡。三局各自的死法不同：
TEST_SUBJECT 是三形态共 636 HP，靠血量和多段攻击把人耗死；QUEEN 用 99 回合的虚弱 / 脆弱 / 易伤加 Chains of Binding 压住了出牌；
AEONGLASS 是 Wither 状态牌在回合末掉血，吃掉格挡后再挨攻击。三局在格挡上的共同点：吃大伤害（未挡伤害 ≥10）的那些回合，
回合末手里**一次也没有剩下能打的格挡牌**。问题不在策略不肯防，而在牌组提供的格挡不够、抽不到；另有几处小的出牌顺序失误和一次赛前丢药水。
三局死在 Boss 房，协议进度都是 0.958333，对 Boss 打掉多少 HP 不影响这个数。

## 复现

- 方式：raven 上用 `scripts/test_remote.sh`（`TESTBOX_NAME=w19`，镜像 `sts2-testbox:c025f32e8cbd`，HEAD 26338c3 现编译的模拟器）跑
  `w19_dump.py`。它是 `audit_dump.py` 的 `Rec` 记录器套在 `sts2train.evaluate.greedy_policy` 外面（动作仍由原贪心策略选，
  包装层只解码观测并记录），然后原样调用 `sts2train.evaluate.evaluate`：greedy、loop breaker 关、macros 取自 checkpoint
  （日志 `room macros: rest,shop,prompt`）。种子集是只含这几个种子的子集（C3 两局一批，S 一局一批）。
- 结果：三局的 `final_state_hash`、`history_actions`（逐项相等）、楼层、回合数、决策数都与官方 summary 一致。官方 summary 由 GPU 跑出，
  这次在 CPU 上跑，按子集重新组批：

| 局 | checkpoint | final_state_hash（官方 = 回放） | history_actions | 楼层 / 回合 / 决策 | server_version |
|---|---|---|---|---|---|
| D1A10SI005 | C3 pinned-0011300（md5 95c63602…） | ea0551c8208d8232 | 相同 | 48 / 114 / 922 | sts2vec/3b73317739c650bd |
| D1A10SI116 | C3 pinned-0011300 | f06572382fc2b8da | 相同 | 48 / 112 / 824 | 同上 |
| D1A10SI032 | S pinned-0011000（md5 0725f020…） | c6ae9d523d1755cb | 相同 | 48 / 97 / 733 | 同上 |

- 独立复核（wave 19 verify）：另开 testbox `w19v`，HEAD 4f2d428（与 26338c3 只差一篇研究日志），直接跑 raven 原版 `audit_dump.py`（md5 8156e4e3…，未包装），SI116、SI005 各自单局一批。两局 `final_state_hash` / `history_actions` / 楼层 / 回合 / 决策数 / fights / boss_entry 与官方 summary 全部相等，replay audit 1/1；逐决策与 w19 落盘相比只有 3 处概率第 4 位小数不同，所选动作全同。SI116 Boss 战逐回合表与 SI005 两张 Boss 表由独立脚本 `diag19/verify/scripts/vtable.py` 重算，数字一致；复核改了 4 处：死亡回合的伤害与剩余 HP（原先写成 "?" 或取回合开始值）、SI005 的每回合输出（332 是 T1–T10 的 10 回合）、SI032 死时的 Boss HP 与毒层（取自 w19 落盘，S 局本次未重放），以及 SI005 KAISER_CRAB 行里"压大招"的描述。
- 数据：`<scratchpad>/diag19/data/out-{c3,s}/decisions.jsonl`（逐决策，格式同 `scripts/analysis/audit/README.md`）、
  `data/tables.json`（逐回合派生量）、`data/analysis.txt`（逐回合原始明细：起手、敌方 powers、回合末剩余手牌）。
  脚本：`diag19/scripts/{w19_dump.py, audit_dump.py, analyze.py, tables.py, run_in_box.sh}`。

**表格读法**：一行是一个玩家回合。"HP / 结束格挡 / 能量"分别是回合开始时的 HP、按下 EndTurn 时的格挡、回合开始时的能量
（括号里是回合末没用掉的能量）。"对各敌造成伤害"是本回合开始到下一回合开始之间敌方 HP 的减少量，包含毒和荆棘的结算。
"敌方意图"取 EndTurn 那一刻的观测，已计入本回合施加的虚弱等效果。"受到伤害"是本回合 EndTurn 到下一回合开始之间 HP 的减少量，
括号内是意图减格挡；"其他"是两者之差，来源有：PLATING（回合末加格挡，为负）、REGEN（回复，为负）、Wither 状态牌
（回合末在手里会掉血，为正），以及攻击者在本回合死亡、攻击没有发生（为负）。

**对 brief 的一处更正**：brief 写的"0 potions"是终局时的药水数。进 Boss 房时 SI005 有 2 瓶（FORTIFIER、BOTTLED_POTENTIAL），
SI032 有 1 瓶（REGEN_POTION，共 4 个槽），只有 SI116 是 0 瓶。

---

## 1. C3 u11300 · D1A10SI005 · TEST_SUBJECT（70/70 进场，死于第三形态第 2 回合）

**机制**（`sim/Sts2Sim.Core/Monsters/Defs/TestSubject.cs`，A10 数值）：一共三个形态，HP 分别为 111、212、313，合计 636。第一形态带
ENRAGE 3，玩家每打一张技能牌它就加力量（T1 回合内意图从 16 涨到 21）。第二形态带 PAINFUL_STABS（未被格挡的攻击会往玩家牌组塞 Wound），
Multi-Claw 每次使用多 1 段：观测到 8×3、8×4、8×5、8×6、8×7、11×8。第三形态带 NEMESIS / INTANGIBLE，有 45 点的 Big Pounce。

| 回合 | HP / 结束格挡 / 能量 | 打出的牌 | 对各敌造成伤害 | 敌方剩余 HP | 敌方意图(回合末) | 受到伤害(意图-格挡; 其他) |
|---|---|---|---|---|---|---|
| T1 | 70 / 48 / 5 | ASSASSINATE>e0 BACKSTAB>e0 BACKSTAB>e0 POUNCE>e0 DEFEND_SILENT+ THE_HUNT>e0 LEG_SWEEP>e0 | TEST_SUBJECT:88 | TEST_SUBJECT:23 | TEST_SUBJECT:Attack21x1 | 0 (21-48=0) |
| T2 | 70 / 13 / 3 | DEFEND_SILENT DEFEND_SILENT STRIKE_SILENT>e0 HIDDEN_DAGGERS+ SHIV+>e0 SHIV+>e0 | TEST_SUBJECT:23->dead |  | TEST_SUBJECT:Attack23x1+Debuff | 7 (23-13=10; 其他 -3) |
| T3 | 63 / 10 / 3 (剩1) | BLADE_DANCE BACKFLIP HIDDEN_DAGGERS (HandSelect:EXPOSE+) (HandSelect:CLUMSY) | —(无敌人) | TEST_SUBJECT:212 | — | 0 (0-10=0) |
| T4 | 63 / 18 / 3 | LEG_SWEEP>e0 CLOAK_AND_DAGGER SHIV>e0 | TEST_SUBJECT:14 | TEST_SUBJECT:198 | TEST_SUBJECT:Attack8x3 | 5 (24-18=6; 其他 -1) |
| T5 | 58 / 26 / 3 | AFTERIMAGE CLOAK_AND_DAGGER SHIV>e0 DARK_SHACKLES>e0 BOUNCING_FLASK SURVIVOR | TEST_SUBJECT:26 | TEST_SUBJECT:172 | TEST_SUBJECT:Attack1x4 | 0 (4-26=0) |
| T6 | 58 / 12 / 3 | SUCKER_PUNCH>e0 NOXIOUS_FUMES NOXIOUS_FUMES P:FORTIFIER | TEST_SUBJECT:32 | TEST_SUBJECT:140 | TEST_SUBJECT:Attack8x5 | 28 (40-12=28) |
| T7 | 30 / 26 / 3 | CLOAK_AND_DAGGER+ SHIV>e0 SHIV>e0 NEUTRALIZE+>e0 DEFEND_SILENT SNAKEBITE>e0 | TEST_SUBJECT:51 | TEST_SUBJECT:89 | TEST_SUBJECT:Attack8x6 | 22 (48-26=22) |
| T8 | 8 / 17 / 3 | PIERCING_WAIL+ BACKFLIP+ BLADE_DANCE SHIV>e0 SHIV>e0 SHIV>e0 | TEST_SUBJECT:57 | TEST_SUBJECT:32 | TEST_SUBJECT:Attack2x7 | 0 (14-17=0) |
| T9 | 8 / 28 / 3 (剩1) | CLOAK_AND_DAGGER+ SHIV>e0 SHIV>e0 DEFEND_SILENT+ P:BOTTLED_POTENTIAL SHIV>e0 | TEST_SUBJECT:32->dead | TEST_SUBJECT:313 | TEST_SUBJECT:Attack11x8 | 0 (88-28=60; 其他 -60) |
| T10 | 8 / 28 / 3 | SHIV>e0 CLOAK_AND_DAGGER+ SHIV>e0 SHIV>e0 DEFEND_SILENT+ SNAKEBITE+>e0 HIDDEN_DAGGERS+ SHIV+>e0 SHIV+>e0 | TEST_SUBJECT:9 | TEST_SUBJECT:304 | TEST_SUBJECT:Attack11x3 | 5 (33-28=5) |
| T11 | 3 / 21 / 3 | DEFEND_SILENT+ STRIKE_SILENT+>e0 STRIKE_SILENT>e0 | TEST_SUBJECT:17（至 EndTurn） | TEST_SUBJECT:287（EndTurn 时，毒 17 未结算） | TEST_SUBJECT:Attack45x1 | 致死: 剩 3 HP, 45-21=24 |

**牌组（43 张，7 张升级）**：STRIKE_SILENT×6, DEFEND_SILENT×4, BACKSTAB×2, BLADE_DANCE×2, BOUNCING_FLASK×2, CLOAK_AND_DAGGER×2, DEFEND_SILENT+×2, LEG_SWEEP×2, NOXIOUS_FUMES×2, THE_HUNT×2, AFTERIMAGE, ASCENDERS_BANE, ASSASSINATE, BACKFLIP, CLOAK_AND_DAGGER+, CLUMSY, DARK_SHACKLES, EXPOSE+, HIDDEN_DAGGERS, HIDDEN_DAGGERS+, NEUTRALIZE+, PIERCING_WAIL+, POUNCE, SLICE, SNAKEBITE, SUCKER_PUNCH, SURVIVOR
**遗物（16）**：RING_OF_THE_SNAKE, LARGE_CAPSULE, VAJRA, WAR_PAINT, PANTOGRAPH, UNCEASING_TOP, VAMBRACE, PAELS_LEGION, DAUGHTER_OF_THE_WIND, BRONZE_SCALES, GREMLIN_HORN, GORGET, BRILLIANT_SCARF, RAZOR_TOOTH, RED_MASK, VENERABLE_TEA_SET
**药水**：进场 FORTIFIER、BOTTLED_POTENTIAL。T6 在格挡 4 时用了 FORTIFIER（格挡 4→12），T9 用了 BOTTLED_POTENTIAL。
第 44 层火堆上丢弃了 2 瓶 FOUL_POTION（p = 0.648 / 0.847）。

**死因**：
1. **输出不足以对付 636 HP**：T1–T10 打掉 332 HP（111 + 212 + 9），约 33/回合；算上 T11 的 17 点，11 回合共 349 HP，平均约 32/回合。第二形态 212 HP 花了 T4–T9 共 6 回合（约 35/回合），
   同期 Multi-Claw 从 8×3 涨到 11×8。
2. **血量在第二形态耗光**：T6（意图 40，格挡 12，掉 28）、T7（意图 48，格挡 26，掉 22）两回合掉了 50 HP，进第三形态时只剩 8/70。
   T6 的起手是 SUCKER_PUNCH、NOXIOUS_FUMES×2、SNAKEBITE、STRIKE，**手里一张格挡牌都没有**。策略打了 Sucker Punch（p=0.67），
   接着连打两张 Noxious Fumes，最后用 Fortifier 把 4 点格挡变成 12；没用的那瓶 Bottled Potential 在这几个候选里概率为 0.000。
3. 第三形态 313 HP 带 INTANGIBLE。T10 打出 9 张牌（多是 0 费的 Shiv），只造成 9 点伤害；T11 剩 3 HP，面对 Big Pounce 45 只叠到 21 格挡，死亡。
   按这场的输出速度，第三形态还要大约 10 回合才能打完，所以就算 T10 全力防御也救不回来，真正的损失发生在第二形态。

## 2. C3 u11300 · D1A10SI116 · TORCH_HEAD_AMALGAM + QUEEN（56/57 进场，第 8 回合死亡）

**机制**（`Queen.cs`、`TorchHeadAmalgam.cs`、`ChainsOfBindingPower.cs`）：QUEEN 419 HP，必须打死它才算赢；AMALGAM 211 HP，带
MINION_POWER，死了不结束战斗，而且它一死 QUEEN 会转入 Off With Your Head（4×5）/ Execution（18）。QUEEN 的出招：T1 Puppet Strings 给玩家
CHAINS_OF_BINDING 3（每回合前 3 张抽到的牌被"Bound"，Bound 的牌每回合只能打 1 张）；T2 You're Mine 给 FRAIL / WEAK / VULNERABLE 各 99 层
（从此格挡 −25%、攻击 −25%、受到伤害 +50%）；之后每回合 Burn Bright（给 AMALGAM 加力量，自己加 20 格挡）。

| 回合 | HP / 结束格挡 / 能量 | 打出的牌 | 对各敌造成伤害 | 敌方剩余 HP | 敌方意图(回合末) | 受到伤害(意图-格挡; 其他) |
|---|---|---|---|---|---|---|
| T1 | 56 / 32 / 3 | AFTERIMAGE+ BACKSTAB>e0 MIND_BLAST>e0 FLECHETTES>e0 DEFLECT+ PRECISE_CUT+>e0 | TORCH_HEAD_AMALGAM:63, QUEEN:0 | TORCH_HEAD_AMALGAM:148, QUEEN:419 | TORCH_HEAD_AMALGAM:Attack32x1; QUEEN:CardDebuff | 0 (32-32=0) |
| T2 | 56 / 20 / 5 (剩1) | DAGGER_SPRAY+ ECHOING_SLASH+ CALCULATED_GAMBLE LEG_SWEEP+>e0 | TORCH_HEAD_AMALGAM:34, QUEEN:25 | TORCH_HEAD_AMALGAM:114, QUEEN:394 | TORCH_HEAD_AMALGAM:Attack16x1; QUEEN:Debuff | 0 (16-20=0) |
| T3 | 56 / 26 / 5 (剩1) | ADRENALINE CLOAK_AND_DAGGER SHIV>e0 ECHOING_SLASH BACKFLIP ESCAPE_PLAN NOXIOUS_FUMES ANTICIPATE+ DEFEND_SILENT | TORCH_HEAD_AMALGAM:19, QUEEN:7 | TORCH_HEAD_AMALGAM:95, QUEEN:387 | TORCH_HEAD_AMALGAM:Attack9x3; QUEEN:Buff+Defend | 1 (27-26=1) |
| T4 | 55 / 5 / 4 (剩2) | SUCKER_PUNCH>e0 DEFEND_SILENT | TORCH_HEAD_AMALGAM:11, QUEEN:2 | TORCH_HEAD_AMALGAM:84, QUEEN:385 | TORCH_HEAD_AMALGAM:Attack19x1; QUEEN:Buff+Defend | 14 (19-5=14) |
| T5 | 41 / 6 / 5 | SERPENT_FORM PIERCING_WAIL+ DEFEND_SILENT | TORCH_HEAD_AMALGAM:10, QUEEN:3 | TORCH_HEAD_AMALGAM:74, QUEEN:382 | TORCH_HEAD_AMALGAM:Attack11x1; QUEEN:Buff+Defend | 5 (11-6=5) |
| T6 | 36 / 15 / 4 (剩1) | LEG_SWEEP+>e0 SUCKER_PUNCH>e0 NEUTRALIZE+>e0 | TORCH_HEAD_AMALGAM:22, QUEEN:4 | TORCH_HEAD_AMALGAM:52, QUEEN:378 | TORCH_HEAD_AMALGAM:Attack12x3; QUEEN:Buff+Defend | 21 (36-15=21) |
| T7 | 15 / 12 / 4 | LEG_SWEEP>e0 ECHOING_SLASH NOXIOUS_FUMES+ | TORCH_HEAD_AMALGAM:19, QUEEN:5 | TORCH_HEAD_AMALGAM:33, QUEEN:373 | TORCH_HEAD_AMALGAM:Attack22x1; QUEEN:Buff+Defend | 10 (22-12=10) |
| T8 | 5 / 3 / 3 (剩1) | ANTICIPATE+ STRIKE_SILENT>e0 DEADLY_POISON+>e0 | TORCH_HEAD_AMALGAM:12, QUEEN:0（至 EndTurn） | TORCH_HEAD_AMALGAM:21, QUEEN:373（EndTurn 时） | TORCH_HEAD_AMALGAM:Attack23x1; QUEEN:Buff+Defend | 致死: 剩 5 HP, 23-3=20 |

**牌组（38 张，12 张升级）**：DEFEND_SILENT×5, NOXIOUS_FUMES×2, ADRENALINE, AFTERIMAGE+, ANTICIPATE+, ASCENDERS_BANE, BACKFLIP, BACKSTAB, CALCULATED_GAMBLE, CLOAK_AND_DAGGER, CLUMSY, DAGGER_SPRAY+, DEADLY_POISON+, DEFLECT+, DODGE_AND_ROLL, ECHOING_SLASH, ECHOING_SLASH+, ESCAPE_PLAN, FLECHETTES, HAND_TRICK, LEG_SWEEP, LEG_SWEEP+, MIND_BLAST, NEUTRALIZE+, NOXIOUS_FUMES+, PIERCING_WAIL+, PRECISE_CUT+, REBOUND, SERPENT_FORM, SNAKEBITE+, STRIKE_SILENT, SUCKER_PUNCH, SURVIVOR
**遗物（11）**：RING_OF_THE_SNAKE, SILVER_CRUCIBLE, HAPPY_FLOWER, FRESNEL_LENS, CANDELABRA, BRONZE_SCALES, ASTROLABE, REGAL_PILLOW, TINGSHA, DIAMOND_DIADEM, ICE_CREAM
**药水**：进场 0 瓶。第 47 层（Boss 前最后一个火堆）在地图上**丢弃了 FLEX_POTION**（p = 0.750）。第 44、46 层的普通战里用掉了 STRENGTH_POTION
和 DUPLICATOR。第 45 层商店有 VULNERABLE_POTION（$50，当时身上 52 金），买的是 NOXIOUS_FUMES（$36）。

**死因**：**debuff 机制压垮了输出，牌组没有任何解法**。debuff 生效前的 T1–T2 打了 122 伤害（61/回合）。T2 吃到 You're Mine 之后，
T3–T7 只打了 102（约 20/回合），其中打到 QUEEN 身上的是 21；而 QUEEN 每回合还叠 20 格挡。7 个回合 QUEEN 只从 419 掉到 373。
同时 VULNERABLE 99 让 AMALGAM 的攻击都放大 1.5 倍：T4 掉 14、T6 掉 21（12×3 对 15 格挡）、T7 掉 10。T4、T6、T8 回合末分别剩 2、1、1 点能量，
手里剩下的牌都被标成不可打（Bound / 诅咒）：T6 剩两张 DEFEND_SILENT!，是 Chains of Binding 让它们这回合打不出去。
攻击几乎全打在 e0（AMALGAM）上。这个目标选择本身不一定错（AMALGAM 死后 QUEEN 会转入高伤招式），但按 20/回合的输出，
419 + 20/回合格挡的 QUEEN 无论如何都打不完。

## 3. S u11000 · D1A10SI032 · AEONGLASS（81/94 进场，第 7 回合死亡；T7 开始时 Boss 剩 140/535、39 层毒，死时（T7 EndTurn）124/535、41 层毒）

**机制**（`Aeonglass.cs`、`WitheringPresencePower.cs`、`Cards/Defs/Wither.cs`）：535 HP，开局 ARTIFACT 3。WITHERING_PRESENCE：玩家每打 6 张牌，
往手里塞一张 WITHER。WITHER 是不能打出的状态牌，回合末若还在手里，对玩家造成 3 点伤害，这个伤害可以被格挡吸收。
Increasing Intensity（本局 T3、T6）：往弃牌堆加 2 张 WITHER，所有 WITHER 伤害 +3，Boss 自身加力量。

| 回合 | HP / 结束格挡 / 能量 | 打出的牌 | 对各敌造成伤害 | 敌方剩余 HP | 敌方意图(回合末) | 受到伤害(意图-格挡; 其他) |
|---|---|---|---|---|---|---|
| T1 | 81 / 15 / 4 | BACKSTAB+>e0 LEG_SWEEP+>e0 ENVENOM+ P:REGEN_POTION | AEONGLASS:41 | AEONGLASS:494 | AEONGLASS:Attack26x1+Defend | 6 (26-15=11; 其他 -5) |
| T2 | 75 / 18 / 3 | EXPOSE+>e0 SERPENT_FORM STRIKE_SILENT+>e0 STRIKE_SILENT>e0 STRIKE_SILENT>e0 PRECISE_CUT+>e0 | AEONGLASS:83 | AEONGLASS:411 | AEONGLASS:Attack12x2 | 5 (24-18=6; 其他 -1) |
| T3 | 70 / 11 / 3 | SNAKEBITE+>e0 STRIKE_SILENT>e0 OMNISLICE>e0 BACKFLIP+ NEUTRALIZE>e0 | AEONGLASS:72 | AEONGLASS:339 | AEONGLASS:StatusCard+Buff | -3 (0-11=0; 其他 -3) |
| T4 | 73 / 1 / 3 | STRIKE_SILENT>e0 BOUNCING_FLASK+ INFINITE_BLADES | AEONGLASS:64 | AEONGLASS:275 | AEONGLASS:Attack30x1+Defend | 27 (30-1=29; 其他 -2) |
| T5 | 46 / 12 / 3 | SHIV>e0 POUNCE+>e0 DEFEND_SILENT DEFEND_SILENT | AEONGLASS:42 | AEONGLASS:233 | AEONGLASS:Attack16x2 | 25 (32-12=20; 其他 +5) |
| T6 | 21 / 8 / 3 | SHIV>e0 DAGGER_THROW+>e0 (HandSelect:WITHER) BLUR DAGGER_THROW>e0 (HandSelect:DEFEND_SILENT) | AEONGLASS:93 | AEONGLASS:140 | AEONGLASS:StatusCard+Buff | 0 (0-8=0) |
| T7 | 21 / 25 / 3 | SHIV>e0 BACKFLIP+ LEG_SWEEP+>e0 | AEONGLASS:16（至 EndTurn） | AEONGLASS:124（EndTurn 时） | AEONGLASS:Attack26x1+Defend | 致死: 剩 21 HP, 26-25=1 + 状态牌 |

**牌组（39 张，8 张升级）**：STRIKE_SILENT×5, DEFEND_SILENT×4, DEADLY_POISON×3, ASCENDERS_BANE, BACKFLIP+, BACKSTAB, BLADE_DANCE+, BLUR, BOUNCING_FLASK+, CLUMSY, DAGGER_SPRAY, DAGGER_THROW, DAGGER_THROW+, DOUBT, ENVENOM, EXPOSE+, INFINITE_BLADES, LEADING_STRIKE, LEG_SWEEP, LEG_SWEEP+, MEMENTO_MORI, NEUTRALIZE, NOXIOUS_FUMES, OMNISLICE, PIERCING_WAIL, POUNCE+, PRECISE_CUT+, SERPENT_FORM, SNAKEBITE, SURVIVOR
**遗物（18）**：RING_OF_THE_SNAKE, WINGED_BOOTS, BELLOWS, LUCKY_FYSH, PENDULUM, NUTRITIOUS_SOUP, DAUGHTER_OF_THE_WIND, REPTILE_TRINKET, HORN_CLEAT, AKABEKO, BRONZE_SCALES, STONE_CRACKER, TRI_BOOMERANG, ETERNAL_FEATHER, CENTENNIAL_PUZZLE, STRAWBERRY, POTION_BELT, LANTERN
**药水**：进场 REGEN_POTION（T1 就喝了）。POTION_BELT 给了 4 个槽，进场时只装了 1 瓶。

**死因**：**伤害回合抽不到格挡，加上 Wither 回合末掉血**。这一局的输出是三局里最强的：6 回合打掉 395（约 66/回合），死时 Boss 剩 124 HP、
身上 41 层毒。只算毒，4 个敌方回合就是 41+40+39+38 = 158 ≥ 124，大约差 4 回合就能赢。
掉血集中在三个回合：T4 意图 30，起手 STRIKE、DOUBT、INFINITE_BLADES、BOUNCING_FLASK+、MEMENTO_MORI，**没有格挡牌**，最后只有 1 格挡，掉 27；
T5 意图 32 加一张 WITHER，格挡 12，掉 25；T7 剩 21 HP，挡住了 26 的攻击中的 25，但手里有 3 张 WITHER 加 1 张 DOUBT，又没有弃牌手段，死亡。
按源码推算，经过两次 Intensity 后每张 WITHER 是 9 点，3 张共 27，先把 25 格挡吃光，再加 26 点攻击，总伤害超过 21 HP。
T6 用 DAGGER_THROW+ 弃掉过一张 WITHER，说明策略认识这张牌。出牌顺序上有一处可见的失分：T5 手里有 3 张 DEFEND、3 点能量，
策略先打 POUNCE+（p=0.978），只打出两张 DEFEND，格挡 12；三张 DEFEND 全打大约 15 格挡。

---

## 第二幕 KAISER_CRAB 是怎么赢的（SI005、SI032）

两局都是 11 回合惨胜，打完只剩个位数 HP，之后靠火堆和遗物回血才进的第三幕。CRUSHER 219 + ROCKET 209 = 428 HP；
一只螃蟹死后，另一只会叠大量格挡（观测到 99 和 96）并加力量。

| 局 | 进场 → 打完 HP | 回合 | 牌组 | 打法与关键牌 | 最大掉血 |
|---|---|---|---|---|---|
| SI005 | 70/70 → 3 | 11 | 35 张（5 升级） | T1 两张 BACKSTAB 打 ROCKET，NOXIOUS_FUMES 前后打了两次（CRUSHER 身上毒叠到 29）；Shiv 系（HIDDEN_DAGGERS、BLADE_DANCE、CLOAK_AND_DAGGER）；ROCKET 的大招只压住了两次：T2 的 30 被 PIERCING_WAIL+ 压到 12，T4 的 57 被 SUCKER_PUNCH 的虚弱压到 28；T7 的 34、T9 的 35 没有减伤，只靠格挡硬吃（掉 21、16）；DARK_SHACKLES 和 PIERCING_WAIL 打在 T1 ROCKET 4 点的小攻击上（压到 0）；T8 打死 CRUSHER，T11 用 SLICE + THE_HUNT 收掉 ROCKET | T7 21（34+5 对 18 格挡）、T9 16 |
| SI032 | 76/87 → 6 | 11 | 29 张（2 升级） | T1 靠 VIGOR 8 加 BACKSTAB+ 和两张 STRIKE+，对 CRUSHER 打出 57；DEADLY_POISON、INFINITE_BLADES、MEMENTO_MORI；T6 打死 CRUSHER，T9–T10 对 ROCKET 打出 56 / 59，T11 收尾 | T9 31（35 对 4 格挡）、T4 18 |

逐回合表在附录。第二幕 Boss 的 HP 池（428）比第三幕（535–636）小，大招的间隔也长，所以同样"输出 + 硬扛"的打法在第二幕刚好够用，到第三幕就不够了。

---

## 跨局共性

| 指标 | SI005 TEST_SUBJECT | SI116 QUEEN+AMALGAM | SI032 AEONGLASS |
|---|---|---|---|
| Boss HP 池 | 636（三形态） | 419 必杀（+20 格挡/回合）+ 211 随从 | 535 |
| 打掉 / 回合数 | 349 / 11 → **32/回合** | 224 / 7 → **32/回合**（debuff 后 **20**） | 395 / 6 → **66/回合** |
| 掉血合计 / 最大单回合 | 70 / 28 | 56 / 21 | 81（含 Regen）/ 27 |
| 未挡伤害 ≥10 的回合 | T2 T6 T7 T11（T9 当回合击杀第二形态，意图未生效） | T4 T6 T7 T8 | T1 T4 T5 |
| 这些回合末手里剩的可打格挡牌 | 0 | 0（剩余能量被 Bound 卡住） | 0 |
| 牌组 / 升级 / 基础牌 / 诅咒 | 43 / 7 / 14 / 2 | 38 / 12 / 8 / 2 | 39 / 8 / 11 / 3 |
| 进场药水 | 2（Boss 战中用掉） | 0（Boss 前丢掉 FLEX） | 1（T1 用掉，4 槽） |
| 死时 Boss 剩余（死亡回合 EndTurn 时） | 287/313（第三形态，毒 17） | QUEEN 373/419（格挡 16），AMALGAM 21/211 | 124/535（毒 41） |

1. **输出速度对不上 Boss 的 HP 池**：两局每回合打 20–35，对的是 600 以上的 HP 池；最好的一局 66/回合，也只差 4 回合。三局都没有成型的
   成长引擎：SERPENT_FORM、ENVENOM、NOXIOUS_FUMES 都有，但多在战斗中途才打出，牌组里还掺着 8–14 张 Strike / Defend / Neutralize / Survivor。
2. **牌组大、升级少、诅咒没删**：38–43 张，升级 7–12 张，每局都带着 ASCENDERS_BANE 和 CLUMSY（SI032 还有 DOUBT）。牌组越大，关键牌和格挡牌出现在手里的概率越低：
   11 个大伤害回合里有 2 个回合起手**一张格挡牌都没有**（SI005 T6、SI032 T4），其余回合起手只有 1–3 张格挡牌。这和线 A 审计的结论一致：火堆从不锻造、商店从不删牌（E′ 探索修复要解决的问题）。
3. **策略不是不防，是没东西可防**：未挡伤害 ≥10 的 11 个回合里，回合末没有一次剩下可打的格挡牌。能看到的出牌失误都不大：SI032 T5 先打 POUNCE+
   而不是第三张 DEFEND（约少 3–5 格挡）；SI005 T6 在格挡 4 时用 FORTIFIER（倍率按观测 4→12），而且同回合连打两张 NOXIOUS_FUMES；
   SI005 T10 把 9 张牌打进了 INTANGIBLE；SI116 T7 剩 2 能量时选了 ECHOING_SLASH（p=0.991）而不是 DEFEND（在 FRAIL 下约 3 格挡）。
4. **药水**：SI116 在 Boss 前一层丢掉 FLEX_POTION（p=0.75）；SI005 在第 44 层丢掉 2 瓶 FOUL_POTION；SI032 有 4 个槽却只带 1 瓶进 Boss 房。
   SI116 在第三幕商店没买药水；SI032 第三幕唯一一次商店（第 39 层）没有药水购买行，它只在第 29 层买过 2 瓶。药水在 Boss 战里基本上一来就用掉（SI032 T1 就喝了 REGEN）。
5. **Boss 特有机制**：Wither（回合末在手掉血、吃格挡）、Chains of Binding 加 99 层三重 debuff、多形态加 Intangible。牌组对这些都没有针对性的解法，
   比如弃牌手段、ARTIFACT 类遗物或药水、能清 debuff 的牌；在手牌允许的范围内，策略的应对是对的（Dagger Throw 弃掉 Wither，大意图回合打 Piercing Wail / Dark Shackles）。

## 训练信号建议（每条对应上面一个现象）

- **Boss 房里没有进度梯度**（对应 1）：死在第三幕 Boss 房，协议进度都是 0.958333，和 Boss 剩多少 HP 无关；赢的奖励（win bonus 1.0）又几乎采样不到。
  S 的 boss school 只在第二幕及以后**打死**Boss 时给奖励，对"打掉 60% 但没打死"同样没有信号。建议：在 Boss 房死亡的 episode 加一个
  "Boss 总 HP 被打掉的比例"的终局奖励（多形态按总池算，系数要小于 win bonus），再从这类第三幕 Boss 战的存档起局做课程训练（boss-start curriculum）。
  第三幕 Boss 在 dev-128 上只出现 3 次，策略在训练里见到它们的次数应该也很少（这是推断，没有核对训练数据）。
- **牌组质量**（对应 2）：已经在推进的 E′ 探索修复（锻造 / 删牌）是直接解法。另外可以让 Boss 存档起局的价值信号回传到火堆和商店决策：
  同一个 Boss 存档，用不同牌组（删牌或锻造前后）的对照局估计价值差。
- **药水保留**（对应 4）：环境侧把 `DropPotion` 只在"槽满且有新药水可拿"时开放，地图和火堆上不再给这一行，可以直接去掉 SI116、SI005 这类丢药。
  训练侧可以在 Boss 房进场时给一个小的、与药水数量相关的势函数奖励（acq shaping 现在只覆盖牌组、遗物和升级）。
- **防御时机**（对应 3）：策略的行为基本正确，瓶颈在格挡供给，不建议专门做防御奖励。`hp_shaping_coef` 类的掉血惩罚放在 Boss 房里，
  会把 SI032 T5 这类"多 1 张 DEFEND"的取舍推向防御，但收益小。
- **机制覆盖**（对应 5）：Boss 存档起局的课程同时覆盖 Wither、Queen、Test Subject 的机制。按现在的训练分布，这些机制只在通关到第三幕末尾时才会出现。

## 未复现 / 未核对

- 三局都完全复现，没有未复现项。
- 没有核对的内容：Wither 的每张伤害（9）是按源码推算的，观测里没有逐张数值，只有 T5 那一张的推算 6 与观测的"其他 +5"（扣掉 Regen 1）吻合；
  FORTIFIER、BOTTLED_POTENTIAL 的具体效果按观测描述（4→12 格挡），没有查源码；训练中第三幕 Boss 的出现频率没有统计。

---

## 附录：第二幕 KAISER_CRAB 逐回合

### SI005（C3）

| 回合 | HP / 结束格挡 / 能量 | 打出的牌 | 对各敌造成伤害 | 敌方剩余 HP | 敌方意图(回合末) | 受到伤害(意图-格挡; 其他) |
|---|---|---|---|---|---|---|
| T1 | 70 / 2 / 3 | BACKSTAB>e1 BACKSTAB>e1 NOXIOUS_FUMES DARK_SHACKLES>e1 SNAKEBITE>e0 P:SKILL_POTION (ChooseCard:PIERCING_WAIL) PIERCING_WAIL | CRUSHER:10, ROCKET:27 | CRUSHER:209, ROCKET:182 | CRUSHER:Attack8x1; ROCKET:Attack0x1 | 2 (8-2=6; 其他 -4) |
| T2 | 68 / 29 / 3 | PIERCING_WAIL+ CLOAK_AND_DAGGER SHIV>e1 SLICE>e1 STRIKE_SILENT>e1 HIDDEN_DAGGERS SHIV>e1 SHIV>e1 | CRUSHER:11, ROCKET:34 | CRUSHER:198, ROCKET:148 | CRUSHER:Attack0x1; ROCKET:Attack12x1 | 0 (12-29=0) |
| T3 | 68 / 6 / 3 | EXPOSE>e1 BOUNCING_FLASK BACKFLIP NEUTRALIZE+>e0 | CRUSHER:29, ROCKET:3 | CRUSHER:169, ROCKET:145 | CRUSHER:Attack5x2+Debuff; ROCKET:Buff | 2 (10-6=4; 其他 -2) |
| T4 | 66 / 14 / 3 | SUCKER_PUNCH>e1 SURVIVOR (HandSelect:DEFEND_SILENT) STRIKE_SILENT>e1 | CRUSHER:19, ROCKET:24 | CRUSHER:150, ROCKET:121 | CRUSHER:Buff; ROCKET:Attack28x1 | 13 (28-14=14; 其他 -1) |
| T5 | 53 / 4 / 3 | NOXIOUS_FUMES POUNCE>e0 BLADE_DANCE SHIV>e0 SHIV>e0 SHIV>e0 | CRUSHER:43, ROCKET:5 | CRUSHER:107, ROCKET:116 | CRUSHER:Attack17x1+Defend; ROCKET:Sleep | 13 (17-4=13) |
| T6 | 40 / 27 / 3 | LEG_SWEEP>e0 BACKFLIP | CRUSHER:26, ROCKET:11 | CRUSHER:81, ROCKET:105 | CRUSHER:Attack12x1; ROCKET:Attack10x1 | 0 (22-27=0) |
| T7 | 40 / 18 / 3 | DEFEND_SILENT+ DEFEND_SILENT DEFEND_SILENT | CRUSHER:29, ROCKET:14 | CRUSHER:52, ROCKET:91 | CRUSHER:Attack5x1; ROCKET:Attack34x1 | 21 (39-18=21) |
| T8 | 19 / 34 / 3 | POUNCE>e0 LEG_SWEEP>e1 SLICE>e0 DEFEND_SILENT+ HIDDEN_DAGGERS SHIV>e0 SHIV>e0 | CRUSHER:52->dead, ROCKET:14 | ROCKET:77 | CRUSHER:Attack10x2+Debuff; ROCKET:Buff | 0 (20-34=0) |
| T9 | 19 / 19 / 3 | CLOAK_AND_DAGGER+ SHIV>e0 SHIV>e0 BACKFLIP NEUTRALIZE+>e0 DEFEND_SILENT | ROCKET:20 | ROCKET:57 | ROCKET:Attack35x1 | 16 (35-19=16) |
| T10 | 3 / 2 / 3 | SNAKEBITE>e0 STRIKE_SILENT>e0 NEUTRALIZE+>e0 | ROCKET:39 | ROCKET:18 | ROCKET:Sleep | 0 (0-2=0) |
| T11 | 3 / 1 / 3 (剩3) | SLICE>e0 THE_HUNT>e0 | ROCKET:击杀 | 全灭 | ROCKET:Attack12x1 | — |

### SI032（S）

| 回合 | HP / 结束格挡 / 能量 | 打出的牌 | 对各敌造成伤害 | 敌方剩余 HP | 敌方意图(回合末) | 受到伤害(意图-格挡; 其他) |
|---|---|---|---|---|---|---|
| T1 | 76 / 17 / 3 | BACKSTAB+>e0 STRIKE_SILENT+>e0 STRIKE_SILENT+>e0 LEG_SWEEP+>e1 DEADLY_POISON+>e0 | CRUSHER:57, ROCKET:3 | CRUSHER:162, ROCKET:206 | CRUSHER:Attack14x1; ROCKET:Attack4x1 | 1 (18-17=1) |
| T2 | 75 / 25 / 3 | INFINITE_BLADES STRIKE_SILENT>e0 DEFEND_SILENT DEFEND_SILENT | CRUSHER:18, ROCKET:3 | CRUSHER:144, ROCKET:203 | CRUSHER:Attack4x1; ROCKET:Attack22x1 | 1 (26-25=1) |
| T3 | 74 / 14 / 3 | SHIV>e0 STRIKE_SILENT>e0 LEG_SWEEP>e0 MEMENTO_MORI>e0 | CRUSHER:33, ROCKET:0 | CRUSHER:111, ROCKET:203 | CRUSHER:Attack5x2+Debuff; ROCKET:Buff | 0 (10-14=0) |
| T4 | 74 / 10 / 3 (剩1) | SHIV>e1 STRIKE_SILENT>e1 NEUTRALIZE>e1 SURVIVOR (HandSelect:DEFEND_SILENT) DEADLY_POISON>e1 PRECISE_CUT+>e1 | CRUSHER:4, ROCKET:31 | CRUSHER:107, ROCKET:172 | CRUSHER:Buff; ROCKET:Attack28x1 | 18 (28-10=18) |
| T5 | 56 / 4 / 3 | EXPOSE+>e0 SHIV>e0 PIERCING_WAIL BOUNCING_FLASK P:CUNNING_POTION SHIV+>e0 SHIV+>e0 SHIV+>e0 | CRUSHER:49, ROCKET:4 | CRUSHER:58, ROCKET:168 | CRUSHER:Attack11x1+Defend; ROCKET:Sleep | 7 (11-4=7) |
| T6 | 49 / 4 / 3 | SHIV>e0 STRIKE_SILENT>e0 STRIKE_SILENT>e0 BOUNCING_FLASK+ DEADLY_POISON+>e0 PRECISE_CUT+>e0 | CRUSHER:58->dead, ROCKET:3 | ROCKET:165 | CRUSHER:Attack17x1; ROCKET:Attack10x1 | 9 (27-4=23; 其他 -14) |
| T7 | 40 / 18 / 3 | LEG_SWEEP>e0 SHIV>e0 DEFEND_SILENT STRIKE_SILENT+>e0 | ROCKET:5 | ROCKET:160 | ROCKET:Attack21x1 | 3 (21-18=3) |
| T8 | 37 / 15 / 3 | SHIV>e0 DEADLY_POISON+>e0 LEG_SWEEP+>e0 | ROCKET:12 | ROCKET:148 | ROCKET:Buff | 0 (0-15=0) |
| T9 | 37 / 4 / 3 | SHIV>e0 STRIKE_SILENT+>e0 STRIKE_SILENT>e0 BOUNCING_FLASK+ DAGGER_THROW>e0 (HandSelect:DEADLY_POISON) | ROCKET:56 | ROCKET:92 | ROCKET:Attack35x1 | 31 (35-4=31) |
| T10 | 6 / 15 / 3 | SHIV>e0 STRIKE_SILENT>e0 MEMENTO_MORI>e0 NEUTRALIZE>e0 DEFEND_SILENT DEFEND_SILENT PRECISE_CUT+>e0 | ROCKET:59 | ROCKET:33 | ROCKET:Sleep | 0 (0-15=0) |
| T11 | 6 / 21 / 3 | SHIV>e0 STRIKE_SILENT>e0 LEG_SWEEP+>e0 DEFEND_SILENT | ROCKET:击杀 | 全灭 | ROCKET:Attack12x1 | — |

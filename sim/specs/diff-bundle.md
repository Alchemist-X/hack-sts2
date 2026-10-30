# Diff bundle 契约 v1 — oracle 轨迹 ↔ sim 重放

2026-10-30。生产者：`sts2rec export-bundles`（Python）；消费者：`Sts2Sim.Diff`
（C#）。一个 JSON 文件 = 一次战斗尝试（save&quit 重试各算一个 attempt，
已证实重试从同一 room-entry 存档重启、counter 相同）。bundle 是轨迹衍生
数据：只写仓库外（默认 scratchpad），**永不提交**。

本轮范围：每局的**首场战斗**（floor-2 首战），counter 可由配方推出；
后续战斗需真实 counter 快照（native/replay.mcr 解码或 recorder 扩展，P0.5）。

## 顶层字段

```jsonc
{
  "bundle_version": 1,
  "source": { "session": "<id>", "part": "<dir>", "attempt": 1,
              "act": 1, "floor": 2 },          // act 1 基（room_entered 的 act 是 0 基，+1）
  "game_version": "v0.111.0",
  "seeding": "xxh64",                           // v0.110.1+ = "xxh64"；v0.107.1 = "legacy_v18"
                                                 // seed 带 "old" 前缀的跨版本续局 ⇒ 不合格
  "seed": "G0G2JMFEYHRS",
  "character": "IRONCLAD",
  "ascension": 10,
  "encounter_id": "NIBBITS_WEAK",               // events room_entered.data.room_model_id 去前缀
  "player": { "hp": 64, "max_hp": 68 },         // 战斗首个快照（首决策前）
  "deck": [ { "id": "STRIKE_IRONCLAD", "upgraded": false }, ... ],
      // 严格按游戏 Deck 顺序：起始卡组原序 + A5+ 末尾追加 ASCENDERS_BANE
      // + Neow 所得卡按获得顺序追加；原位升级（NEOWS_TALISMAN 等）不改顺序
  "relics": [ "BURNING_BLOOD", "NEOWS_TALISMAN" ],  // 必须全部 ∈ 战斗惰性白名单
  "rng_offsets": { "Niche": 1 },                // 开战前各流已消耗的 counter 数
      // 首战配方：Niche = 6·[KALEIDOSCOPE] + 1·[NEW_LEAF] + 1·[NEOWS_BONES]，
      // 其余 sim 相关流（Shuffle/MonsterAi/CombatTargets）= 0
  "decisions": [
    { "index": 0,
      "pre_state": { <projection> },            // actions[].state_seq 对齐的快照投影
      "action": { "type": "play_card", "hand_index": 2,
                  "card_id": "BASH", "upgraded": false,
                  "target_combat_id": 1 } },     // 或 {"type": "end_turn"}
    ...
  ],
  "outcome": "win" | "loss" | "abandoned",
  "final_hp": 52                                 // 战斗内最后快照的玩家 HP（胜利后治疗不计）
}
```

决策定义：`play_card` + `end_turn`。排除 `ready_to_begin_enemy_turn`（与
end_turn 重复）和 context 为 SINGLETON.MULTIPLAYER_SCALING_MODEL 的自动
`player_choice`。`hand_index` = 该实例在 pre_state 手牌中的位置（exporter 用
canonical 的实例解析从 `combat_card_index` 求得）；`card_id`/`upgraded` 作
交叉校验。

## projection（两侧必须按同一规则生成；只含 id，不含本地化名）

```jsonc
{
  "turn": { "player_turn": 1, "phase": "player_play" },
  "player": {
    "hp": 64, "max_hp": 68, "block": 0, "energy": 3, "max_energy": 3,
    "hand": [ { "id": "...", "upgraded": false, "cost": 1, "can_play": true }, ... ],
                                   // 手牌顺序；cost 为 int，X 费记 "X"
    "draw_count": 5, "discard_count": 0, "exhaust_count": 0,
    "draw_multiset": [ "DEFEND_IRONCLAD", "DEFEND_IRONCLAD+", ... ],
                                   // 升级以 "+" 后缀编码，排序后的 multiset（抽堆顺序是隐藏信息）
    "discard": [ ... ], "exhaust": [ ... ],   // 游戏顺序，同样 "+" 编码
    "powers": [ { "id": "...", "amount": 2 } ]  // 仅可见能力；amount = DisplayAmount
  },
  "enemies": [                      // 按 combat_id 升序；仅存活敌人
    { "combat_id": 1, "monster_id": "NIBBIT",
      "hp": 11, "max_hp": 11, "block": 0,
      "powers": [ ... ],
      "intents": [ { "type": "Attack", "attack_value": 6, "attack_times": 2 } ] }
  ]                                 // 非攻击意图只比 type；attack_* 解析自 label（×=U+00D7）
}
```

字段分级：**hard**（hp/max_hp/block/energy/手牌 id+upgraded+cost+can_play/
三堆计数/能力 id+amount/意图 attack 数值/turn）——任一不等即 hard 分歧；
**soft**（draw_multiset、discard/exhaust 内容——依赖本地化名→id 映射；非攻击
意图附加值）——不等记 soft 分歧。首个 hard 分歧即停并输出最小复现（bundle
路径、决策下标、字段路径、双侧值、双侧完整投影）。

## sim 侧约定

- RunRngSet 构造按 `seeding`：`xxh64` 走现行构造；`legacy_v18` 每流用
  `RngSeeds.LegacyV18Kernel(seed, name, 0)`（核对迁移公式
  SerializableRunV18ToV19.cs:64-66,164-173 + StringHelper.cs:120-134）。
- `rng_offsets` 单位 = counter：对该流连续拉取 k 次（每次 1 个 u64，counter+1）。
- Create 签名（本轮新增 upgraded 支持）：
  `CombatSim.Create(RunRngSet rng, IReadOnlyList<CardSpec> deck, string encounterId,
   int ascension, int? currentHp, int? maxHp)`，`CardSpec = (string Id, bool Upgraded)`。
  deck 顺序 = bundle 顺序，不排序。
- 投影时点 = 每个决策执行前（开局投影 = setup+首抽后、首决策前）。

## exporter 侧约定

- 语料根（只读）：`~/Library/Application Support/Sts2Recorder/sessions/`。
- 本地化名→id：用手牌（有 id）跨语料建映射，Strike/Defend 按角色消歧；
  "+" 后缀 = 升级。解析失败 ⇒ 该战斗不合格（原因记档），不猜。
- 合格性过滤（全过才出 bundle；不合格写入 reasons 索引做覆盖率账）：
  遭遇已移植；deck 可重建且每张 ∈ sim 注册表（升级卡需已移植升级值）；
  遗物全 ∈ 白名单；战斗内未用药水；seeding 可判定（无 "old" 前缀）。
- 遗物白名单（战斗内惰性，反编译核验过）：BURNING_BLOOD、GOLDEN_PEARL、
  NUTRITIOUS_OYSTER、PHIAL_HOLSTER、WINGED_BOOTS、STONE_HUMIDIFIER、
  LAVA_ROCK、SILVER_CRUCIBLE、LOST_COFFER、KALEIDOSCOPE、NEOWS_TALISMAN、
  POMANDER、NEW_LEAF、FISHING_ROD（仅前 2 场 Monster 战斗）、CURSED_PEARL、
  PRECISE_SCISSORS、PRECARIOUS_SHEARS、DOWSING_ROD、NEOWS_BONES、
  NEOWS_SACRIFICE（药水未用）、HEFTY_TABLET、ARCANE_SCROLL、SCROLL_BOXES、
  LEAD_PAPERWEIGHT、LEAFY_POULTICE、NEOWS_TORMENT、LARGE_CAPSULE。
  **明确排除**：BOOMING_CONCH（战斗钩子）、SMALL_CAPSULE（随机遗物不可知）、
  SILKEN_TRESS（enchant 改牌）。白名单限 Neow 池 + 各角色 starter；
  名单外遗物一律不合格。

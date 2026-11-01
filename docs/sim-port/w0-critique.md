# W0 completeness critique

2026-11-01 · W0 critic (Opus) · branch `claude/sim-source-recreation-9392df` · game v0.111.0.

Reviewed: `sim/specs/inventory/*.json` (13 raw kind manifests, `hooks.json`, `pools.json`, `ported.json`,
`summary.json`, 9 `*.classified.json`, `kernel-gaps.json`, `run-layer-gaps.json`, `corpus-inventory.json`,
`subsystems.json`), `sim/tools/inventory/*`, `docs/sim-port/design-*.md`, `docs/sim-port/inventory-*.md` and
`docs/p1-sim-full-port-plan-2026-11-01.md`. Rule truth: `output/server-bootstrap/linux-decompiled/` (cited as
`<ns-dir>/<File>.cs:<line>`, prefix `MegaCrit.Sts2.Core.` dropped) and its source assembly
`output/server-bootstrap/linux-assembly/sts2.dll`. Corpus: `~/Library/Application Support/Sts2Recorder/sessions/`,
read only. Check scripts and their outputs: `tmp/sim-port/critic/` (git-ignored scratch; Python 3 stdlib, two
throwaway .NET 9 console probes). No reviewed file was changed.

## 结论

1. **机械覆盖达标。** 13 类 1,622 个 `.cs` 文件在原始清单中各登记恰一次（独立脚本复核，另有 `VakuuCardSelector.cs`
   记为非模型）；7 类分类清单共 1,550 条，恰好覆盖这 7 类的 1,550 个模型文件，每条都有合法的 `tier`(1–4)、
   `kernel_requirements`、`prompts`。1,621 个 id 用真 .NET 9 正则复跑 `Slugify`，0 误差。199 个 Cmd 重载、
   186 个 `AbstractModel` 公有虚/抽象成员、148 个公有 `Hook` 分发器在内核设计文档和 JSON 中全部出现；RoomType、
   RewardType、RestSiteOption、MapPointType 在 run 层设计中全覆盖；12 个 GameAction 子类中
   `VoteForMapCoordAction`、`VoteToMoveToNextActAction`、`UndoEndPlayerTurnAction`、`ConsoleCmdGameAction`
   没有任何文档覆盖（G13）。随机抽 15 条分类逐一对源码，无实质错误。
2. **三个阻断性设计冲突**，必须在 W1 合同冻结前由编排者裁决：
   (a) 选择 API 有三套互不兼容的方案（内核：答案带重放 + `ChoosePick/ChooseConfirm`、默认有序、无取消；
   run 层：可恢复 `PendingPrompt` + `PromptSelect/Confirm/Cancel`、严格递增索引；子系统：自动判定先于暂停信号）；
   (b) 内核断言 "per-listener 延迟提示在 v0.111.0 不可达、直接抛 `NotSupportedException`" 被可达链推翻：
   `GremlinHorn.AfterDeath` / `DarkEmbracePower.AfterSideTurnEnd` / `JossPaper.AfterSideTurnEnd` 抽牌 →
   `HellraiserPower` 自动打出带 Strike 标签的 `SeekerStrike`（无色，单人可得）→ `FromCombatPile` 在 per-listener
   上下文里暂停；
   (c) run 层 "多选只按递增索引" 会丢掉游戏真实可达的结果顺序：牌组选择界面返回 `HashSet` 插入序，
   `MorphicGrove`、`Astrolabe` 按这个顺序消耗变形 RNG。
3. **状态归属四处分歧**：遗物/药水实例归 `PlayerState`（内核）还是 `RunPlayer`（run 层 K3）；战斗内金币/HP/牌组
   改动走 `RunEffects` 日志（内核）还是直接写 `RunPlayer`（run 层）；`CombatSim.Create` 深拷 RNG（`sim/CONTRACTS.md:106-107`）
   与 K6 "必须用活的 `RunRngSet`" 冲突且内核阶段 0 合同未列；解锁输入有 `UnlockState`/`UnlockProfile`/`RunProfile`
   三个名字。另有 42 个纯 run 作用域分发器被排在 W1，但 W1 没有 `RunState` 类型可迭代。
4. **清单缺陷**：3 条 `model_id` 错误（怪物 `Byrdpip`、`PaelsLegion`，事件 `LostWisp` 被写成 `RELIC.*`，校验器只查
   类内重复所以 `--check` 仍为 0）；acts/characters/enchantments/afflictions/modifiers/orbs 共 71 个文件没有分类；
   三个卡牌切片的 `tier`（含/不含依赖）、`port_batch` 编号、`prompts` 结构互不一致；`kernel_requirements` 是 919 个
   自由文本，只有 64 个跨文件复用，无法与 `kernel-gaps.json` 连接；同一能力的 tier 在不同分类器间 21–35 处不一致；
   版本差异判定与差分闸机械 diff 有 21 处互相矛盾（已裁决：`Orobas` 是真规则变更却被事件分类器判 `same`；
   `DualWield` 是差分闸误报）；分类脚本都在 git-ignored 的 `tmp/`，游戏更新后无法重生成。
5. **反编译丢失可直接恢复。** 9 处规则相关的 `LdMemberToken` 静态数组已用 `System.Reflection.Metadata` 从
   `sts2.dll` 读出（§7），全部等于 v0.107.1 值；4 份文档里的这组开放问题可以关闭。
6. **差分闸**：20 段抽样里的 18 种动作类型、全语料 19 种中的 18 种都有映射；只缺 `card_reward_obtained`（2 条）。
   但 `events.jsonl` 正是游戏 `CombatHistory` 17 种条目的逐条镜像（`mod/src/EventTap.cs:61`），设计没有把它当作
   硬对比通道。
7. **计划层**：P1 计划里的 A1–A20（应为 A0–A10）、185/149（应为 182 个钩子虚方法/148 个公有分发器）、
   "Scry/球体提示"（v0.111.0 不存在）、W1 的 "GameVersion 开关"（与差分闸方案 iii 冲突）、W4 才做 exporter v2
   （差分闸要求 G1 在 W3 内容批之前可用）都需修正；recorder 改动（存档拷贝 R1/R2）与 MCP 覆盖农场没有波次和所有者。

## 0. Method

| Check (brief) | Script / evidence | Result |
| --- | --- | --- |
| every `.cs` in the 13 kind dirs (+ Mocks) exactly once across raw manifests | `tmp/sim-port/critic/check_accounting.py` (own class parser, not the inventory tool) | pass: 1,622 files = 1,621 entries + 1 non-model; no missing, duplicate or extra file; every entry's class is declared in its file and equals the file stem |
| every file of the 7 classified kinds exactly once across `*.classified.json` | same script | pass: 1,550 entries (cards 240 + 182 + 182 over three slices); `id`, `model_id`, `class` equal the raw manifest |
| ids follow `Slugify` | .NET 9 console probe `tmp/sim-port/critic/slug/` running the game's regexes (`Helpers/StringHelper.cs:35-73`) on all 1,621 classes; category per `Models/ModelDb.cs:465-488`, `Models/ModelId.cs:81-91` | entry ids 1,621/1,621; **3 wrong `model_id`** (§2 B6) |
| every classified entry has `tier`/`kernel_requirements`/`prompts` | `tmp/sim-port/critic/` inline checks | pass on all 1,550 (int 1–4, list, list); 71 files of 6 kinds have no classification at all (G1) |
| design docs cover every Cmd class and Hook virtual | `tmp/sim-port/critic/check_surfaces.py` (regex over `Commands/*.cs`, `Commands.Builders/AttackCommand.cs`, `Models/AbstractModel.cs`, `Hooks/Hook.cs`) | pass: 20 Cmd classes + `AttackCommand`, 199 public overloads, all in `kernel-gaps.json` `cmd_facades` and `design-combat-kernel.md` §3; 186 public virtual/abstract members (182 hooks, 3 infra, abstract `ShouldReceiveCombatHooks`) all in the doc; 148 public dispatchers all in the doc and JSON (`hooks.json` also lists the private `ModifyDamageInternal`, hence "149") |
| run layer covers RoomType, RewardType, RestSiteOption, GameAction subclasses | grep over `design-run-layer.md` and `run-layer-gaps.json` | RoomType 9/9, MapPointType 9/9, RewardType 6/6 non-`None` values (`RemoveCard` appears as `CardRemovalReward`), Reward subclasses 7/7, RestSiteOption 9/9 (Mend as multiplayer-only); GameAction 12 subclasses: 4 named in the run-layer doc, 3 combat ones named in the kernel/subsystems docs, `GenericHookGameAction` only described ("a hook action", kernel §4.2), **4 not covered** (G13) |
| diff gate accounts for recorder action types | `tmp/sim-port/critic/corpus_sample.py` (20 parts: 10 v0.107.1, 4 v0.110.1, 6 v0.111.0, seed 20261101) and `corpus_full_actions.py` (all 1,508 parts incl. 127 gzipped) | sample: 18 kinds, all mapped; census: 19 kinds, `card_reward_obtained` unmapped (G15) |
| 15 random classified entries vs source | `tmp/sim-port/critic/picks.json` (seed 15150111, stratified by file) | 15/15 consistent (§6) |
| inventory tool self-checks | `python3 sim/tools/inventory/extract_models.py --check`; `python3 -m unittest discover -s sim/tools/inventory` | exit 0; 14/14 tests; both blind to B6 |

## 1. What is complete

- Raw manifests: file accounting, registry membership, pool/act resolution and the `Slugify` entry rule are correct
  and reproducible (`extract_models.py`, deterministic, digest-pinned). `hooks.json` scope classes agree with
  `kernel-gaps.json` for all 148 public dispatchers once `both` (R+C) is read as the kernel's `combat` (22
  dispatchers, 33 virtuals); sim presence agrees for all 148.
- `design-run-layer.md` Appendix B lists exactly the 46 run-scope and 12 both-scope virtuals that `hooks.json`
  marks missing (script check).
- Multiplayer-only cards: the three card slices and `powers.classified.json` agree on the same 37 classes, equal to
  the files containing `CardMultiplayerConstraint.MultiplayerOnly`.
- `subsystems.json` §4 history-reader list (45 files: 29 cards, 12 powers, 4 relics) and its affliction-applier
  list (7 powers) match the tree.

## 2. Blocking issues (decide before the W1 contract freeze)

### B1 — Three incompatible choice APIs

| Aspect | `design-combat-kernel.md` §4.4–4.6 | `design-run-layer.md` §3.4, §3.12, §8.1 K1 | `design-subsystems.md` §1.1, §2 | `design-diff-gate.md` §5 |
| --- | --- | --- | --- | --- |
| mechanism | answer-tape re-execution from `S0.DeepClone()`; explicit suspendable state machines rejected | "a prompt suspends the current effect as `PendingPrompt` … continuation key"; K1 = "resumable PendingPrompt API shared with combat" | selector stack whose bottom is the agent | n/a |
| action kinds | `ActionKind` + `ChoosePick`, `ChooseConfirm` | `RunActionKind` + `PromptSelect`, `PromptConfirm`, `PromptCancel`; no `ChoosePick` although the Combat phase "delegates 1:1" | — | `choose` → `ChoosePick`×n + `ChooseConfirm`; run level "onto `RunActionKind`" |
| multi-select order | `Ordered` defaults to true; picks in any order | "strictly increasing indices, so each subset has exactly one action sequence" | — | unspecified |
| cancel | none (`Cancelable` false in combat) | `PromptCancel` (Smith, Cook, shop removal) | `Cancelable` sites listed | unspecified |
| prompt kinds | `ChoicePromptKind {HandSelect, HandUpgrade, PileSelect, ChooseCard, SimpleGrid}` | "deck/hand/grid/choose-a-card/bundle", no enum | 13 entry points | `kind (hand, grid, choose_one, bundle, index)` |
| scope | `CombatSim` only (`S0` is a `CombatState`) | needs `RunState` prompts (events, rest site, shop, relic pickup) | — | — |

Re-execution and resumable continuations are different contracts; the run layer assumes W1 delivers the latter.
Decision needed: one `ChoiceRuntime` for both scopes (re-execution over `RunState` incl. embedded combat is the
natural generalisation), one action vocabulary (append `ChooseCancel`; drop the run-only names or alias them), one
prompt-kind enum that also covers `FromDeckForUpgrade/Transformation/Enchantment/Removal/Generic`,
`FromSimpleGridForRewards`, `FromChooseABundleScreen`, card reward, rewards screen and rest-site cancel paths.

### B2 — Per-listener prompt deferral is reachable in single player

`design-combat-kernel.md` §4.2 (last row) and §4.6 state that no v0.111.0 prompt is reachable inside the six
per-listener-context dispatchers and plan to throw `NotSupportedException`. The check looked only at direct prompt
sites and direct auto-plays in those overrides. Transitive chain:

1. Listener draws with the per-listener `HookPlayerChoiceContext`: `Models.Relics/GremlinHorn.cs:130` (`AfterDeath`
   of an enemy) → `:91` `CardPileCmd.Draw(choiceContext, …)`; `Models.Powers/DarkEmbracePower.cs:207`
   (`AfterSideTurnEnd`) → `:144`; `Models.Relics/JossPaper.cs:405` (`AfterSideTurnEnd`) → `:278`. Contexts are
   created per listener at `Hooks/Hook.cs:2364` (`AfterDeath`) and `:6170`, `:6225` (`AfterSideTurnEnd`).
2. `Models.Powers/HellraiserPower.cs:141` `AfterCardDrawnEarly` auto-plays every drawn Strike-tagged card
   (`:69`) with the same context (`:93` `CardCmd.AutoPlay(choiceContext, card, null)`; `Commands/CardCmd.cs:523`
   passes it to `OnPlayWrapper`; the branching wrapper is transparent, `Models/CardModel.cs:667`).
3. `Models.Cards/SeekerStrike.cs:141` (Strike tag `:178`, pool COLORLESS) prompts via `FromCombatPile`, which
   signals before counting options (`Commands/CardSelectCmd.cs:3378`), so the pause happens even when the selection
   auto-resolves. `SculptingStrike` (Necrobinder, `FromHand`) is the other Strike-tagged prompting card.

All pieces are single-player content (Hellraiser and DarkEmbrace are Ironclad rares, GremlinHorn/JossPaper shared
relics). The planned exception would crash on a legal state, and a synchronous re-execution engine cannot express
"run the rest of the pass, then resume the paused listener" without a continuation for that listener. None of the
classifiers flagged the chain (`HellraiserPower`, `SeekerStrike`, `GremlinHorn`, `JossPaper` entries have no
nested-prompt note). Decision needed: emulate the deferral (a narrow continuation for auto-play chains), or accept a
documented divergence class and make it an ineligibility reason, never an exception.

### B3 — Deck multi-select order is rule-relevant

All deck and grid selection screens collect picks in a `HashSet<CardModel>` and return it
(`Nodes.Screens.CardSelection/NDeckTransformSelectScreen.cs:75`, `:313`; `NDeckCardSelectScreen.cs:72`, `:291`;
`NDeckEnchantSelectScreen.cs:89`, `:370`; `NDeckUpgradeSelectScreen.cs:81`, `:354`; `NSimpleCardSelectScreen.cs:195`,
`:358`), so the result order is the add/remove history (the same .NET property `design-run-layer.md` §2.6 relies on
for map points). Consumers iterate it with RNG: `Models.Events/MorphicGrove.cs:106` (2 picks) then `:133`
`CardCmd.TransformToRandom(current, Rng)` per card; `Models.Relics/Astrolabe.cs:65` (N picks) likewise. The run
layer's "strictly increasing indices" makes half of the human outcomes of a two-card transform unreachable and breaks
replay of recorded orders. Adopt the kernel rule (`Ordered` = true unless a spec shows order cannot matter) for
run-scope prompts too.

### B4 — Combat/run state ownership conflicts

| Item | Kernel (§5, §6 phase-0 members) | Run layer (§2.2, §8.1, §8.3) | Note |
| --- | --- | --- | --- |
| relics, potions | `PlayerState.Relics : List<RelicModel>`, `PotionModel?[] PotionSlots` | `RelicInstance`/`PotionInstance` owned by `RunPlayer` (K3) | one object model and one owner needed |
| gold, max HP, deck edits in combat | hashed `RunEffects` log on `CombatState`, applied by the run layer | thieves/`HeistPower` "hit `RunPlayer.Gold`"; HP "copy-out, or shared storage — K6 decides" | contradictory |
| RNG | silent; `sim/CONTRACTS.md:106-107`: `Create` deep-clones the `RunRngSet` | K6: combat on the live `RunRngSet`, no clone, `SetUpCombat`/`StartCombat` split must accept it | K6 missing from the kernel's phase-0 list |
| unlock input | `UnlockState Unlocks` on `CombatState`, `sealed class UnlockState` stub | `UnlockProfile { Epochs, EncountersSeen, NumberOfRuns, DiscoveredActs, ProgressNumberOfRuns, WongoPoints }` | `design-subsystems.md` §5 calls it `RunProfile` |
| factories | cards-kernel owns `Cards/CardFactory.cs` | R3 owns `Run/Factories/` (CardFactory run part, `PotionFactory`, `RelicFactory`) | the game has one `CardFactory` and one `PotionFactory` with combat and out-of-combat entry points (`Factories/PotionFactory.cs:22`, `:37`; `Factories/CardFactory.cs:93`, `:130`) |
| canonical tags | no allocation for new W1 state (relics, potions, orbs, history, `RunEffects`, prompt state) | proposes 0x55xx–0x59xx incl. 0x57xx for relic/potion/enchantment instances | W1 lands first; allocate in phase 0 |

### B5 — Run-scope dispatchers have no listener source in W1

The kernel's phase 0 promises `Hook.X for all 148 dispatchers`; the run layer's K2 requires the 46 run-scope and
12 both-scope ones from W1. The 42 pure run dispatchers iterate `runState.IterateHookListeners(null)` (deck cards
with enchantments, relics, potions; `Runs/RunState.cs:829-933`), but `RunState`/`RunPlayer` are W2 (R1) types.
Either freeze an `IRunListenerSource` (deck, relics, potions in game order) in W1 phase 0, or move the 42 run
dispatchers to W2 R6.

### B6 — Wrong `model_id` on colliding class names

`sim/specs/inventory/monsters.json` `Byrdpip` → `RELIC.BYRDPIP` (expected `MONSTER.BYRDPIP`), `PaelsLegion` →
`RELIC.PAELS_LEGION` (`MONSTER.PAELS_LEGION`); `events.json` `LostWisp` → `RELIC.LOST_WISP` (`EVENT.LOST_WISP`);
copied into `monsters.classified.json` and `events.classified.json`. Cause: `ctx.bases.setdefault(td.name, …)` keys
base classes by bare class name (`sim/tools/inventory/extract_models.py:266-279`), so `category_type`
(`:282-294`, used at `:990`) takes the relic's base for every same-named class. `validate` checks duplicates only
within a kind (`:1926-1927`), and `sim/specs/inventory/README.md` claims `model_id` is unique across the tree.
Save import (`CATEGORY.ENTRY` ids, `design-run-layer.md` §7.3) and any id join would mis-type these three models.

## 3. Contradictions between documents

| # | Topic | Statement A | Statement B | Source verdict / resolution |
| --- | --- | --- | --- | --- |
| C1 | choice API | kernel §4.4–4.6 | run layer §3.12, K1 | B1 |
| C2 | per-listener prompts | kernel §4.2, §4.6: unreachable, throw | subsystems §1.1 lists them as possible; open question 5 | reachable (B2) |
| C3 | prompt pause vs auto-resolution | kernel §4.1 step 3–4: `SignalPlayerChoiceBegun` before counting for `FromHand`, `FromSimpleGrid`, `FromCombatPile`, `FromHandForUpgrade` | subsystems §1.1 steps 3–5 put auto-resolution before the selector/screen for all 13 entry points | kernel correct (`Commands/CardSelectCmd.cs:3681-3686`, `:3306-3310`, `:3375-3379`); the subsystems order holds only for deck prompts (`:3614-3640`) and on the selector path |
| C4 | multi-select order | kernel `Ordered` default true | run layer strictly increasing | B3 |
| C5 | relic/potion/gold/HP/RNG/unlock ownership | kernel §5, §6 | run layer §2.2, §8.1, §8.3 | B4 |
| C6 | recorder save copy | run layer §7.4: Harmony postfix on `SaveManager.SaveRun` | diff gate §4.3 R1: use the `Saved` event | diff gate correct: `SaveRun` returns a `Task` (`Saves/SaveManager.cs:1172`), `Saved` fires after `WriteFileAsync` (`Saves.Managers/RunSaveManager.cs:176`, `:186`) |
| C7 | save importer | run layer R7 owns `Sts2Sim.Core/Run/Save/` + `sts2rec/sts2rec/run_save.py` | diff gate H2a/H2b importer in `Sts2Sim.Diff` | one importer in Core; harness consumes it |
| C8 | diff harness ownership and timing | kernel §6: `diff-v2` agent owns `sim/Sts2Sim.Diff/*`, integration step 5 of W1 | run layer R8 owns `Sts2Sim.Diff/Run/` (W2); P1 plan puts exporter v2 and run-layer diff in W4; diff gate wants G1 before content ratchets | assign H1–H15/X1–X13 to waves; G1 must precede W3 |
| C9 | version handling | P1 plan W1: "GameVersion 开关" | diff gate §2.3 (iii): no per-version rule branches; only seeding and registry seams, ≤ 30 capped branches | follow the diff gate; rename the W1 item |
| C10 | ascension range | P1 plan: "A1–A20" | run layer §6.4, monsters/encounters inventories: A0–A10 | `Entities.Ascension/AscensionManager.cs:8` `maxAscensionAllowed = 10` |
| C11 | prompt families | P1 plan: "Scry、球体等" prompts | kernel §4.3: no orb, scry, retain or stance prompt in v0.111.0 | kernel correct |
| C12 | hook counts | P1/P0 plans: 185 virtuals, 149 dispatchers; diff gate §2.1: virtuals 181 → 183 | kernel, `hooks.json`: 182 hooks (185 public virtuals + abstract `ShouldReceiveCombatHooks`), 148 public dispatchers | use 182 / 148 |
| C13 | combat-history readers | kernel §3, §5: 29 cards, 12 powers, 8 relics = 49 files | subsystems §4: 45 files (4 relics) | 45: only `EmotionChip`, `HistoryCourse`, `PaelsEye`, `RippleBasin` read `History` |
| C14 | affliction appliers | kernel §5: "applied by 11 powers" | subsystems §1.3: 7 powers | 7 (`CardCmd.Afflict`/`AfflictAndPreview` callers) |
| C15 | Doom members | kernel §1, §5: `ShouldDisappearFromDoom`, `OnDieToDoom` as `MonsterDef` rule members | monsters inventory (e.g. `CRUSHER` notes): VFX only | VFX only: read only in `DoomPower` VFX code (`Models.Powers/DoomPower.cs:377`, `:612`); `TorchHeadAmalgam.OnDieToDoom` toggles nodes (`Models.Monsters/TorchHeadAmalgam.cs:463`) |
| C16 | labels | run layer: R1–R8 = W2 agents, K1–K6 = kernel prerequisites | diff gate: R0–R11 = recorder additions, H/X = work items | rename one set (e.g. recorder `REC-n`) before briefs cite them |
| C17 | events batch 1 | events inventory batch 1 = Neow, ancients, TheArchitect **and** all tier-1 events | run layer R5: only Neow, the 7 act-start ancients and TheArchitect are W2; other events W3 | split batch 1 |
| C18 | `tier` meaning | ironclad-colorless and necrobinder-regent: own code only | silent-defect: `tier = max(tier_own, deps, 3 if orbs/enchantments)` | §5.1 |
| C19 | side-turn participants | powers inventory: missing `HookParam.SideTurnParticipants` raises to tier 2 | silent-defect `dependency_powers` and relics: same requirement stays tier 1 | §5.1 |
| C20 | version deltas | classifiers' `version_delta_v107` | diff gate `version_diff_v0_107_1_vs_v0_111_0.content_files` | 21 files flagged by the diff gate are `same` in classifiers; §5.4 |
| C21 | in-combat `card_select` | run layer §3.4: `card_select` → `Prompt` phase | kernel §4.7 and diff gate: `card_select` also appears inside combat | phase depends on a live combat (the diff gate's `in_combat` rule) |

## 4. Concrete gaps

| # | File | Missing | Impact |
| --- | --- | --- | --- |
| G1 | `sim/specs/inventory/` | no classified manifest (tier, kernel_requirements, prompts, batch) for enchantments (24 files), afflictions (10), orbs (7), modifiers (17), acts (5), characters (8) — 71 files; `subsystems.json` has no per-model rows (e.g. `Adroit` absent) | P1 W0 gate "full machine-readable inventory" unmet for 6 kinds; W1 character-mechanics and cards-kernel agents lack per-model lists |
| G2 | `design-combat-kernel.md` §6 phase-0 list | K6 (live `RunRngSet`), run listener source (B5), `ChooseCancel`, canonical tag allocation, `Factories/` owner | W2 cannot start on a frozen W1 contract |
| G3 | `design-combat-kernel.md` §4.6 | turn-setup and per-listener sites reached through auto-play (Hellraiser in the opening draw; Sly discards from `ToolsOfTheTradePower`; B2 chain) are not in the site list or the test plan | deferral ordering untested for nested chains |
| G4 | `design-combat-kernel.md` §4.5 | order of an auto-resolved result: pile order, not the sorted option order (`Commands/CardSelectCmd.cs:3397-3399` vs the sort at `:3403-3405`) | hand order after e.g. Seance/Charge auto-resolution |
| G5 | `cards-*.classified.json`, `powers`, `relics`, `potions`, `events`, `monsters` | `kernel_requirements` are 919 distinct free-text strings, 64 shared by two or more files; no key into `kernel-gaps.json` `cmd_facades`/`hook_virtuals` | cannot compute "content unblocked by W1 feature X" |
| G6 | same files | 12 different `prompts` key sets; type vocabularies differ (kernel `ChoicePromptKind`; relics `DeckTransform`, `RewardsScreen`, `RestSiteOption`, …; events raw Cmd names; necrobinder-regent `Nested`) | prompt test matrix cannot be generated |
| G7 | `cards-*.classified.json` | `port_batch` numbering diverges after 5 (batch 6 = card generation in two slices, orb core in silent-defect; MP-only = 9 vs 99); no cross-kind batch DAG (cards ↔ powers ↔ relics ↔ potions ↔ events) | W3 order not derivable |
| G8 | `cards-ironclad-colorless.classified.json` | indirect prompts of applied powers not recorded (`ENTROPY`, `STRATAGEM`: `prompts: []`) while the other slices record them (`TOOLS_OF_THE_TRADE`, `TYRANNY`, `FOREGONE_CONCLUSION`) | inconsistent prompt-dependency set |
| G9 | `sim/tools/inventory/extract_models.py` | cross-kind `model_id` uniqueness check; per-path base resolution (B6) | silent id errors |
| G10 | all `*.classified.json`, `kernel-gaps.json`, `run-layer-gaps.json`, `corpus-inventory.json`, `subsystems.json` | generators live in git-ignored `tmp/sim-port/*`; `potions`/`relics` classified files have `generated_by: null` | not regenerable after a game update (migration playbook) |
| G11 | `powers.classified.json` (notes) → no W1/W3 work item | P0-ported `VulnerablePower` lacks the `PaperPhrog` step and the pet-owner Cruelty fallback; `WeakPower` lacks `PaperKrane`; `ShrinkPower` uses the `IsTurnParticipant` approximation | must be re-ported when relic runtime and participants land (cards batch 0 lists only cards) |
| G12 | `design-run-layer.md` §3.12, `events.classified.json` batch 3 | "resumable" prompt contract that the kernel does not offer (B1) | events batch 3 blocked on an undefined API |
| G13 | `design-run-layer.md` | `VoteForMapCoordAction` and `VoteToMoveToNextActAction` (the recorder's `vote_for_map_coord` 3,845 and `vote_to_move_to_next_act` 207 records), `UndoEndPlayerTurnAction` (`undo_end_turn`, 0 records, only via `Nodes.Combat/NEndTurnButton.cs:508`) and dev-only `ConsoleCmdGameAction` are not named; the mapping of `vote_to_move_to_next_act` to `ProceedRewards` at a boss rewards screen (`Multiplayer.Game/ActChangeSynchronizer.cs:41`, `:58-70`) is not stated | decision counting differs (diff gate counts it as a map decision) |
| G14 | `design-diff-gate.md` §4–6 | the 17 `CombatHistory` entry types in `events.jsonl` (75,755 `card_drawn`, 54,394 `damage_received`, 48,393 `card_play_started`, …) are only legacy-alignment evidence; no hard per-entry history diff against the sim's `CombatHistory` | the cheapest exact intra-decision oracle and the only direct test of the 45 history readers is unused |
| G15 | `design-diff-gate.md` §1.2, §5 action union | `card_reward_obtained` (2 records, source `hook:RewardSynchronizer.SyncLocal`, e.g. `CARD.DOUBT`) — counted under "reward" in `corpus-inventory.json` but absent from the doc table and bundle v2 union | unmapped record → ineligible segment or crash |
| G16 | `design-diff-gate.md` | `end_turn` from `event:CombatManager.PlayerEndedTurn` (20 records, `human: false`, three QuickSL lineages) and `play_card` from `ActionQueueSynchronizer.RequestEnqueue` (40, `human: false`, the MCP lineage) are not classified | provenance filter |
| G17 | `docs/p1-sim-full-port-plan-2026-11-01.md` | no wave/owner for recorder additions (R0–R11, esp. R1/R2 "before the farm starts"), the MCP coverage farm, and the IL read of decompile losses | anchored v0.111.0 data does not accumulate during W1–W3 |
| G18 | `design-run-layer.md` §3.4 | phase/legal actions for potions during `CardReward` and `Prompt` phases | legal-action closure (F4) |
| G19 | `design-combat-kernel.md` §4.5 | prompt kinds for run scope (deck upgrade/transform/enchant/remove/generic, rewards grid, bundle, card reward with alternatives) | B1 |
| G20 | `events.classified.json` version deltas | set-based fingerprints miss list-membership moves (`Orobas`) | taint filter (§5.4) |

## 5. Classified manifests: consistency

### 5.1 `tier`

Every file uses 1–4 with similar words, but the semantics differ:

- ironclad-colorless: own code only; "unported power dependencies do not raise the tier".
- necrobinder-regent: own code only; separate `tier_with_deps`.
- silent-defect: `tier` includes dependencies; `tier_own` is the file alone (e.g. `ACCURACY` tier 2, tier_own 1).
- relics/potions: relic/potion runtime is the baseline; redundant `AfterCombatEnd` resets are ignored (e.g. `KUNAI` tier 1
  although `AfterCombatEnd` and side-turn participants are missing).
- powers: missing `HookParam.SideTurnParticipants` raises to tier 2; `VulnerablePower`/`WeakPower` are tier 3 though
  ported (relic consults).

Power tiers disagree between classifiers: 21 of 71 between `cards-silent-defect.classified.json`
`dependency_powers` and `powers.classified.json` (mostly the participants question), 11 of 66 for
necrobinder-regent `power_dependencies`, 35 `tier_hint` mismatches in `monsters.classified.json` `deps`. Use
`powers.classified.json` as the single source for power tiers and define `tier` once (own code) plus
`tier_with_deps` everywhere.

### 5.2 `port_batch`

Per-kind numbering is fine; per-slice card numbering is not (G7). A merged W3 plan needs one card batch scheme and an
explicit cross-kind dependency list (power batch k before card batch m, potion before relic `ToyBox`, …).

### 5.3 `kernel_requirements`, `prompts`

G5, G6. Necrobinder-regent's `requirement_catalog` (keys + catalogue) is the only joinable form; adopt it for all
files, with catalogue keys that name `kernel-gaps.json` rows (`CardCmd.Upgrade`, `Hook.AfterCombatEnd`, …).

### 5.4 `version_delta_v107` vs the diff gate

Files the diff gate flags as rule-diverging but classifiers call `same`: cards 6 (`DualWield`, `FiendFire`,
`HeirloomHammer`, `Omnislice`, `SweepingBeam`, `Whirlwind`), monsters 7 (`BowlbugSilk`, `BygoneEffigy`,
`KinFollower`, `MagiKnight`, `Nibbit`, `SlumberingBeetle`, `TestSubject`), encounters 1 (`TwoTailedRatsNormal`),
events 7 (`ColossalFlower`, `DenseVegetation`, `EndlessConveyor`, `Orobas`, `TabletOfTruth`, `Vakuu`,
`WelcomeToWongos`). Adjudicated:

- `Orobas`: real rule change missed by the events classifier. v0.111.0 option pools are {ElectricShrymp, GlassEye}
  and {AlchemicalCoffer, Driftwood, RadiantPearl, SandCastle} (`Models.Events/Orobas.cs:32-41`); v0.107.1 had
  {ElectricShrymp, GlassEye, SandCastle} and {AlchemicalCoffer, Driftwood, RadiantPearl}
  (`v107 Models/Events/Orobas.cs:30-39`). `NextItem` list sizes and relic odds changed.
- `DualWield`: diff-gate false positive; both versions call `CreateClone` (`Models.Cards/DualWield.cs:101`).

The taint filter must use a reconciled list (union, then a triage pass), not either source alone.

## 6. Random verification (15 entries)

| Entry | Classified file | Checked against source | Verdict |
| --- | --- | --- | --- |
| `PROLONG` | cards-ironclad-colorless | ctor (0, Skill, Uncommon, Self), Exhaust, `PowerCmd.Apply<BlockNextTurnPower>(Block)`, upgrade removes Exhaust (`Models.Cards/Prolong.cs:85-109`) | correct |
| `ASTRAL_PULSE` | cards-necrobinder-regent | star cost 3, `DamageVar(6, Move)` +2, 2 hits all enemies, ctor `:93`, `OnPlay` `:99` | correct |
| `ACCURACY` | cards-silent-defect | `PowerVar<AccuracyPower>(4)` +2; `AccuracyPower.ModifyDamageAdditive` only for Shiv-tagged powered attacks | correct (tier is with-deps, §5.1) |
| `DUPLICATION_POWER` | powers | `ModifyCardPlayCount` +1 for the owner's cards, `AfterModifyingCardPlayCount` → `Decrement`, removed when a participant at side-turn end | correct |
| `FLAME_BARRIER_POWER` | powers | retaliates `Amount` Unpowered on powered attacks, removed at the opposing side's turn end | correct |
| `POTION_BELT` | relics | Common, `GainMaxPotionCount(2)` on obtain | correct |
| `GLASS_EYE` | relics | Ancient, 5 `CardReward`s of 3 uniform cards per rarity, `NoRarityModification`; lost array (now recovered, §7) | correct |
| `FYSH_OIL` | potions | Uncommon, CombatOnly, AnyPlayer, Strength 1 + Dexterity 1 | correct |
| `POWER_POTION` | potions | `GetDistinctForCombat(3 Powers, CombatCardGeneration)` → `FromChooseACardScreen(canSkip)` → `SetToFreeThisTurn` → hand | correct |
| `SLUMBERING_BEETLE` | monsters | HP 86/89, Plating 15/18, Rollout 16/18, Slumber 3, graph SNORE → SNORE_NEXT → ROLL_OUT; spawn order Plating then Slumber by `goto` order (`Models.Monsters/SlumberingBeetle.cs:96-127`) | correct |
| `CRUSHER` | monsters | HP 209/219, five-move cycle, A9 values, Weak 2 + Frail 2, block 18 | correct (its Doom note is the evidence for C15) |
| `CUBEX_CONSTRUCT_NORMAL` | encounters | Monster, Overgrowth normal (`Models.Acts/Overgrowth.cs:78`), one monster, no tags, gold 10–20 | correct |
| `LOUSE_PROGENITOR_NORMAL` | encounters | Hive normal (`Models.Acts/Hive.cs:86`), one monster | correct |
| `WAR_HISTORIAN_REPY` | events | `IsAllowed` false (`:787`), LanternKey `ModifyNextEvent` at act index 2, unlock options, 2 potion + 2 relic rewards | correct |
| `TRASH_HEAP` | events | `IsAllowed` HP > 5 (`:239`), DIVE_IN 8 damage + `NextItem` of 5 relics, GRAB 100 gold + 1 of 10 cards, Event1Epoch gate | correct |

Targeted checks outside the sample: `KnowledgeDemon` curse sets have 2 options each (`Models.Monsters/KnowledgeDemon.cs:775-792`,
classifier correct); `Orobas` version delta wrong (§5.4); B6.

## 7. Decompile losses recovered from the assembly

`tmp/sim-port/critic/ilblob/` scans every method of the nine types for `ldtoken <field>` and reads the field's RVA
data (`System.Reflection.Metadata`, no ILSpy). The 9 rule-relevant `LdMemberToken` sites (13 files in all; the other
4 are `AutoSlay`, `HandPosHelper`, two Nodes screens):

| Site | Array | v0.111.0 value | Equals |
| --- | --- | --- | --- |
| `Models.Cards/AscendersBane.cs:21` | `CanonicalKeywords` | Eternal, Unplayable, Ethereal | sim P0 port |
| `Models.Cards/Folly.cs:19` | `CanonicalKeywords` | Unplayable, Eternal, Innate, Ethereal | v0.107.1 |
| `Models.Relics/GlassEye.cs:58` | `CardRarity[5]` | Common, Common, Uncommon, Uncommon, Rare | v0.107.1 |
| `Map/GoldenPathActMap.cs:42` | `MapPointType[16]` | Monster, Unknown, Monster, RestSite, Monster, RestSite, Unknown, Treasure, Unknown, Treasure, Unknown, Shop, Elite, RestSite, Elite, RestSite | `design-run-layer.md` §4.3 [v107] list |
| `Entities.Merchant/MerchantInventory.cs:199` | `CardType[5]` | Attack, Attack, Skill, Skill, Power | run-layer assumption |
| `Entities.Cards/CardKeywordOrder.cs:15` | `CardKeyword[5]` | Ethereal, Sly, Retain, Innate, Unplayable | v0.107.1 |
| `Models.Monsters/KnowledgeDemon.cs:773` | `_disintegrationDamageValues` | 6, 7, 8 | v0.107.1 |
| `Models.Events/ColossalFlower.cs:491`, `:494` | two `int[3]` | 35, 75, 135; 5, 6, 7 | v0.107.1 |
| `Models.Events/TinkerTime.cs:189`, `:196`, `:203` | `RiderEffect[3]` ×3 | Sapping, Violence, Choking; Energized, Wisdom, Chaos; Expertise, Curious, Improvement | v0.107.1 |

Structural match: the probe found `ldtoken` exactly at these methods (`.cctor`, `get_CanonicalKeywords`, `.ctor`,
`ChooseRiderEffect`, `<AfterObtained>d__2.MoveNext`) with the decompile's per-file counts (ColossalFlower 2,
TinkerTime 3). This closes `design-run-layer.md` §9.2 and open question 1, `design-diff-gate.md` open question 1,
events batch 6, the `GLASS_EYE` open question and the tier-4 reason of `ASCENDERS_BANE`/`FOLLY`, pending the
orchestrator's acceptance of `linux-assembly/sts2.dll` as the decompile's source (its build-id is not embedded;
`assembly-comparison.json` shows the mac and linux builds identical).

## 8. Corpus action types

| Kind | Source hook | Sample (20 parts) | Census (1,508 parts) | Diff-gate mapping |
| --- | --- | --: | --: | --- |
| `play_card` | `CardModel.TryManualPlay` (+40 `ActionQueueSynchronizer.RequestEnqueue`) | 733 | 46,330 | `play_card` |
| `end_turn` | `NEndTurnButton.CallReleaseLogic` (+20 `CombatManager.PlayerEndedTurn`) | 126 | 9,672 | `end_turn` |
| `ready_to_begin_enemy_turn` | two sources | 126 | 9,570 | follow-up |
| `player_choice` | `PlayerChoiceSynchronizer.SyncLocalChoice` | 93 | 6,983 | `choose` |
| `reward_taken` | `Reward.SelectUnsynchronized` | 68 | 4,734 | `reward_take` |
| `vote_for_map_coord` / `move_to_map_coord` | `NMapScreen.OnMapPointSelectedLocally` | 60 / 60 | 3,845 / 3,844 | `map_move` / follow-up |
| `use_potion`, `discard_potion` | `PotionModel.EnqueueManualUse`, `NPotionPopup.OnDiscardButtonPressed` | 17, 1 | 1,622, 94 | yes |
| `event_option`, `event_proceed` | `EventSynchronizer.SaveEventOptionToHistory`, `NEventRoom.OptionButtonClicked` | 18, 12 | 1,291, 845 | yes |
| `rest_site_option` | `RestSiteSynchronizer.AfterPlayerOptionChosen` | 15 | 714 | yes |
| `shop_purchase`, `shop_card_removal` | `MerchantEntry` / `MerchantCardRemovalEntry.OnTryPurchaseWrapper` | 6, 2 | 482, 136 | yes |
| `open_chest`, `pick_relic` | `NTreasureRoom`, `TreasureRoomRelicSynchronizer` | 5, 5 | 276, 268 | yes |
| `vote_to_move_to_next_act` | `ActionQueueSynchronizer.RequestEnqueue` | 2 | 207 | `next_act` (G13) |
| `rewards_skipped` | `RewardsSetSynchronizer.SkipLocalRewardsSet` | 2 | 32 | yes |
| `card_reward_obtained` | `RewardSynchronizer.SyncLocal` | 0 | 2 | **none** (G15) |
| `undo_end_turn` | — | 0 | 0 | listed, never observed |

All statuses are `executed` or `committed`. State types in the sample (`monster`, `elite`, `boss`, `hand_select`,
`card_select`, `crystal_sphere`, `map`, `rewards`, `shop`, `rest_site`, `treasure`, `event`, `unknown`) are all
mapped except `unknown`.

## 9. Recommended order before the W1 freeze

1. Decide B1 (one choice runtime for combat and run scope, one action vocabulary with cancel, ordered picks) and B2
   (per-listener deferral: emulate or classify), then write them into `sim/CONTRACTS.md` as the P1 plan requires.
2. Decide B4/B5 ownership in one table (relic/potion instances, run-effects path, live RNG, unlock input name,
   factories, run listener source, canonical tag ranges) and add the missing members to the kernel's phase-0 list.
3. Fix B6 in the extractor (key bases by path or namespace; add a cross-kind uniqueness check) and regenerate.
4. Normalise the classified manifests: one `tier` definition + `tier_with_deps`, one card batch scheme, a
   requirement catalogue keyed to `kernel-gaps.json`, one prompt record schema keyed to the prompt-kind enum;
   classify the 71 files of G1; commit the classifier generators or mark the files as frozen snapshots.
5. Reconcile version deltas (§5.4) before building the taint filter.
6. Accept the recovered arrays (§7) and drop the corresponding v107 placeholders.
7. Correct the P1 plan (C9–C12) and give recorder additions, the MCP farm and the history-event diff (G14) a wave
   and an owner; schedule G1 (anchored combat gate) before the first W3 content batch.

## 10. Open questions

1. B2: emulate the per-listener deferral (needs a continuation for the paused listener) or treat the chain as a
   documented divergence class with an ineligibility reason?
2. B1: is re-execution acceptable at run scope (one `RunState` deep clone per decision with an embedded combat,
   inside the ≤ 1 ms clone budget)?
3. Is `output/server-bootstrap/linux-assembly/sts2.dll` the exact build behind `linux-decompiled/` (needed to accept §7)?
4. `card_reward_obtained`: which game path emits it (a forced curse card such as `CARD.DOUBT`), and is it a decision?
5. Non-human `end_turn` records from `CombatManager.PlayerEndedTurn` in three QuickSL lineages: QuickSL artefact or
   a game auto-end path? (relates to `design-diff-gate.md` open question 3)
6. Do potions stay usable on the card-reward and deck-prompt screens (G18)?
7. Which W1 agent owns `Factories/` and `UnlockState`/`UnlockProfile` after B4?
8. Should the history-event diff (G14) be part of G1, or a separate G1h level?

## Appendix. Reproduction

```
python3 -I tmp/sim-port/critic/check_accounting.py tmp/sim-port/critic/accounting.json
python3 -I tmp/sim-port/critic/check_surfaces.py tmp/sim-port/critic/surfaces.json
python3 -I tmp/sim-port/critic/corpus_sample.py tmp/sim-port/critic/corpus_sample.json
python3 -I tmp/sim-port/critic/corpus_full_actions.py tmp/sim-port/critic/corpus_full_actions.json --events
dotnet build -c Release -o out tmp/sim-port/critic/slug && dotnet tmp/sim-port/critic/slug/out/slug.dll tmp/sim-port/critic/slug/names.tsv
dotnet build -c Release -o out tmp/sim-port/critic/ilblob && dotnet tmp/sim-port/critic/ilblob/out/ilblob.dll output/server-bootstrap/linux-assembly/sts2.dll GlassEye AscendersBane Folly KnowledgeDemon ColossalFlower TinkerTime MerchantInventory GoldenPathActMap CardKeywordOrder
```

(`dotnet build` paths run from inside each probe directory; the `sts2.dll` path is relative to the main repository.)

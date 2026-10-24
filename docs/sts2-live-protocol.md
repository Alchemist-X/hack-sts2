# STS2 直播与决策记录协议

本协议固定记录与核对流程，不是自动出牌策略，也不是已验证的最优策略。2026-09-09 三次战士 A10 记录中，前两局失败、第三局通关；终局记录不能当作待续存档。完整证据和暂停检查点见 [Release](https://github.com/Alchemist-X/hack-sts2/releases/tag/sts2-a10-gpt6-2026-09-09)。

## 本地依赖与测试

构建 MCP 需要本机游戏、.NET 9 和锁文件对应的 STS2MCP 源码提交。`scripts/build_selection_mcp.sh` 通过 `STS2_MCP_SOURCE_REPO` 和 `STS2_GAME_INSTALL_DIR` 指定源码仓库与游戏安装目录，产物只写入隔离构建目录。`headless_provision.sh` 使用此补丁构建；启动窗口和退出步骤见 README。

直播规则检索还需要与 `knowledge/source/v0.107.1/manifest.json` 指纹匹配的本地反编译源码，默认放在 `artifacts/knowledge/decompiled-v0.107.1/`。准备源码后运行 `python3 scripts/export_sts2_rule_evidence.py` 核对规则片段并生成证据。仓库不包含游戏程序集和完整反编译目录；缺少这些来源时，`observe` 会报告问题，`act` 会拒绝执行，不能把关闭校验当作安装步骤。

在仓库根目录运行 `uv run --project sts2rec --locked --group dev pytest -q sts2rec/tests`。通用测试使用合成规则验证依赖检索、来源缺失/变化时拒绝执行，以及超时后禁止重复动作。另两项真实怪物规则集成测试在没有本地反编译目录时明确跳过；有该目录则必须核对原始哈希，不会因内容不匹配而跳过。Python CI 不安装游戏或启动游戏进程。

## 每局记录

1. 模型先阅读角色记忆与上一局失误；用实际局面填写 `config/live-plan.example.json` 的副本。启动脚本会读取并留存计划与记忆哈希，但无法证明模型理解了文件。
2. 创建独立 `output/gameplay-<日期>-<角色>-<编号>/`。运行 `python3 scripts/sts2_live.py start --run-dir <目录> --plan <计划.json> --memory <记忆.md> --port 15701 --mode practice --game-version v0.107.1 --codex-session <本任务rollout.jsonl>`。没有会话文件仍可玩，成本报告必须写未知。
3. `observe --run-dir <目录>`：永久保存公开状态、当前快照可枚举的 legal_actions 及商品/奖励元数据、来源索引。静态规则保存到 knowledge.jsonl，同时将当前相关规则正文、敌人行动状态机和来源返回到 decision_context。牌堆保持无序，保留 ID、升级、附魔、污染及当前文本。MCP返回实际版本、commit和sts2.dll的SHA-256；act在这些值缺失、不匹配或相关本地源码校验失败时停止。其他改变玩法的mod仍需单独核对。
   `start --wiki-facts` 时，observe 另返回 `wiki_facts`（`FactDB.facts_for_state`，最多 4000 字符，来自 knowledge/facts），用于基准测试对比有无 wiki 事实的表现；默认关闭。
4. `act '<一个动作JSON>' '<公开决策摘要>' '<刚观察的hash>' --run-dir <目录>`：动作前写 live.log 和意图；动作后写结果与请求/结算耗时。每个回合开始、抽牌/随机效果后，重新评估计划。升级、附魔、力量和实时状态以当前公开文本为准。
5. `note '<局势更新>' --run-dir <目录>`；`annotate '{"kind":"regret","decision_id":"...","reason":"...","next_rule":"...","counterfactual_tested":false}' --run-dir <目录>`。人工更新 plan.json 后，下个动作会保存计划与哈希；新计划不能追改历史动作。
6. `report --run-dir <目录>`：输出 audit.json，含奖励、怪物、商店、药水与成本。成本用 token_usage_record 的每请求 usage 按 response_id 去重；压缩只数 compacted。滚动日志可能尚未落盘最后一次调用，应在响应完成后补最终快照。不能累加反复播报的累计 token_count，也不能把缓存/推理子项加两遍。
7. 官方退出/续玩和每次 SL 用 `annotate` 的 kind=checkpoint_resume 或 sl，记录原因、前后层/回合、HP、药水、牌组、回滚范围。随后报告并保留 live.log，不删除暂停前的片段。最终归档可用 SHA-256；旧冻结归档不可覆盖。

如果 POST 成功但确认超时，pending-action.json 会阻止追加动作（错误 `pending_unresolved`）。先 observe、核对官方画面，再运行 `reconcile '<实际发生了什么>' --run-dir <目录>`：它给原 decision_id 追加 decision_outcome_recovered（post_state、核对说明），然后才移除 pending-action.json。不能只删除标记或盲重试。游戏拒绝且状态未变时（`action_rejected`），harness 自动清除 pending，无需 reconcile。

## 稳定动作引用（act 请求格式）

observe 的每个 legal_action 带 `ref`，act 优先使用 ref 而不是位置索引。ref 每一步都在最新状态上重新解析并与 legal_actions 校验；位置索引只允许在单步/第一步使用，链式第 2 步起拒绝（索引会随出牌、击杀、购买移位）。

- 卡牌：`{"action":"play_card","card":"Strike+","target":"Inklet@3"}`。卡名大小写、空白、`[markup]`、`++` 均规范化；`Strike` 与 `Strike+` 区分升级档；完全相同的复制品视为同一选择，不同的复制品需 `Strike#2`。也可用卡牌 id。
- 目标：`名称@combat_id`、`cid:N` 或数字。发送给 MCP 的 target 一律是 combat_id（击杀后 entity_id 会重新编号，例如 INKLET_2 变成 INKLET_1）；链式步骤里旧 entity_id 按本次 act 开始时的映射翻译，目标已死则明确报错，绝不改打别的敌人。
- 药水：`{"action":"use_potion","potion":"slot:1"}` 或 potion id，可加 `potion_id` 作守卫。不用英文名子串匹配；是否能在战斗中用看 can_use_in_combat。
- 商店 `item:"card:SHRUG_IT_OFF"`/`"relic:LIZARD_TAIL"`/`"card_removal"`；奖励 `reward:"gold"`/`"potion:WEAK_POTION"`/`"card"`；地图 `node:"col,row"`（可加 `type`）；宝箱 `relic:<id>`；休息/事件用选项 id 或标题。
- 简写：`end`、`proceed`、`skip`、`confirm`、`cancel`。多步：传 JSON 数组，按顺序逐步执行、逐步结算。
- `expect` 守卫：规范化比较 id/名称/描述片段，容忍升级改名；不匹配仍然拒绝。

不可执行的动作放在 `unavailable_actions` 并给原因：药水栏满时的药水奖励（`potion_slots_full`，提示先 `discard_potion` 或 proceed 放弃）、买不起的商品（`cannot_afford`）。非战斗画面（奖励、商店、地图、事件、休息、宝箱）可直接 `discard_potion`。

## 结算与失败语义

act 发送前等待“可行动”帧：战斗需 is_play_phase、queue_idle 且未结束；地图需 next_options。决策 hash 忽略易变的 execution 区块。每个动作需有提交证据才算完成：地图须离开 map（楼层先于房间加载递增），end_turn 须进入新的玩家回合或离开战斗，选择/奖励须界面变化或条目减少，宝箱须遗物被领取。随后再等一个短静默窗口（战斗 1.0 秒、其他 0.6 秒、可靠勾选 0.2 秒）。

所有失败都以退出码 2 输出 JSON：`{"ok":false,"error":<kind>,"message":...,"observation":<最新观察>}`。kind：`stale_hash`（附 diff，未发送任何动作）、`not_settled`、`unresolvable_action`（附 available 与 failed_step）、`selection_incomplete`、`action_rejected`、`action_unresolved`、`pending_unresolved`、`rule_authority_unverified`、`reason_required`、`hash_required`、`bad_request`。

规则来源校验：评测局任何不匹配都停止；练习局只有在 start 或 act 传 `--allow-unverified-authority` 时继续，每条意图记录 `authority_verified:false`。

## 每回合的公开决策摘要

记录：敌人有效意图及机制计数；现有费用/格挡；最保守可行防守与输出；选择方案的逐段伤害/掉血；下一回合关键牌出现率、能否支付；药水用与不用的差值；未知机制及资料来源。无需输出内部逐词推理。

对沙漏需额外跟踪每六张计数、自动出牌、凋萎数量/等级、人工制品、消耗来源是否仍在抽牌堆、下一次洗牌，以及防守后还能活几次凋萎。抽到与能打出是两件事。参考 `sts2rec/sts2rec/draw_odds.py`；它只实现超几何抽样，不是战斗模拟器。

## 多选、变化与快速 SL

新的隔离 MCP 补丁输出 selection_tracking、每张 is_selected、selected_indices/count、min_select/max_select。hand_select 与 card_select 用一次 act 完成：`{"action":"select","cards":["Strike","Strike","Defend"]}`，harness 逐张按 ref 选择（重复名称会选不同复制品，不会取消已选），最后自动确认（意图记录 controller=harness、harness_followup=auto_confirm_selection）。数量少于 min_select 时发送前即拒绝；`"confirm":false` 保持界面打开以便逐张选择；单选即关闭的界面不发确认。存在预览时只确认或取消，不能继续点隐藏网格。

变化预览保留 preview_originals；轮播示例牌不再作为 preview_cards 返回。cycles_random_examples 表示是否有随机轮播，result_committed=false，必须确认后比较真实牌组。缺选择字段的旧 MCP 仍需视觉兜底，不能伪造成功。非预览多选的可靠勾选变化可使用0.2秒短确认；空敌人列表不代表胜利。

宝箱：第一次点击有时被开箱动画吞掉。若 4 秒内未提交且同一遗物仍在，harness 重点一次（steps 中记录 treasure_reclick），不会重复领取。proceed 落到重新打开的 card_reward 时，act 返回 warning，应 `skip`。

快速 SL 操作：先确认无待处理动作 → 用视觉工具定位右上齿轮并点击（也可 Escape 打开暂停）→ 点击“保存并退出” → 主菜单出现后点击“继续游戏” → 核对官方存档恢复的层、房间、HP、药水与牌组。不要点击“放弃”。恢复通常是房间起点，不能承诺恢复到当前回合；遇到不支持保存的界面先观察。用户中途暂停也属于检查点恢复，不能隐藏掉回滚的尝试。

这是已写入记忆的 UI 操作流程，尚无可直接调用的一键 quick_sl MCP 动作。练习局可按用户授权使用；评测局必须明确计入 SL，不能宣称无读档。

## 构建与验证范围

`scripts/build_selection_mcp.sh` 从锁定的 upstream commit 应用 `patches/sts2mcp-selection-state.patch` 和 `patches/sts2mcp-rule-evidence.patch`，在隔离目录构建，再加原有 headless/端口补丁。headless_provision 已接入；现有运行进程不会热替换。不要往人类游戏安装。`scripts/export_sts2_rule_evidence.py` 在本地源码上验证必要片段，补充状态、附魔、污染模型和六条经核对的跨模型规则。

2026-09-09：C# 编译0错误0警告；Python回归覆盖选择确认、预览排除、牌堆信息边界、商店与知识引用、每局日志、超时阻止重复、成本去重、奖励口径与抽样概率。实机“两张变化/三张附魔”完整回归尚待下一次隔离运行启动，不把编译和合成状态测试冒充 UI 通过。

追加验证：22项Python回归通过。独立实验体检查点副本中，版本指纹匹配；震荡波触发激怒与虚弱后，敌人意图22→18，手中与我一战的目标伤害变量6→9，均与事前算式一致。用户要求退出后，原游戏与测试进程均已退出，测试日志在 output/source-of-truth-2026-09-09/smoke-gameplay/live.log。没有继续本回合或开始新的通关挑战。多选完整实机回归仍未完成。

边界：候选集用于展示与记录，目前直播act还没有把提交动作与候选集逐项做强制匹配；最终可执行性由MCP/游戏引擎决定。plan.json的完整性、哈希与动作理由可审计，但策略优劣、伤害推导和药水选择仍由模型负责。数值预览不是完整规则解释器，也不是牌序模拟器。Wiki/Codex目前是已核对的旁证，运行时主要查询本地版本化规则；没有自动联网刷新并自动信任网页的流程。

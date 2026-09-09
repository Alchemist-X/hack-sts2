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
4. `act '<一个动作JSON>' '<公开决策摘要>' '<刚观察的hash>' --run-dir <目录>`：动作前写 live.log 和意图；动作后写结果与请求/结算耗时。每个回合开始、抽牌/随机效果后，重新评估计划。升级、附魔、力量和实时状态以当前公开文本为准。
5. `note '<局势更新>' --run-dir <目录>`；`annotate '{"kind":"regret","decision_id":"...","reason":"...","next_rule":"...","counterfactual_tested":false}' --run-dir <目录>`。人工更新 plan.json 后，下个动作会保存计划与哈希；新计划不能追改历史动作。
6. `report --run-dir <目录>`：输出 audit.json，含奖励、怪物、商店、药水与成本。成本用 token_usage_record 的每请求 usage 按 response_id 去重；压缩只数 compacted。滚动日志可能尚未落盘最后一次调用，应在响应完成后补最终快照。不能累加反复播报的累计 token_count，也不能把缓存/推理子项加两遍。
7. 官方退出/续玩和每次 SL 用 `annotate` 的 kind=checkpoint_resume 或 sl，记录原因、前后层/回合、HP、药水、牌组、回滚范围。随后报告并保留 live.log，不删除暂停前的片段。最终归档可用 SHA-256；旧冻结归档不可覆盖。

如果 POST 成功但确认超时，pending-action.json 会阻止追加动作。先 observe、核对官方画面，给原 decision_id 追加 decision_outcome_recovered（包括 post_state、证据文件路径和恢复原因），确认结果后才移除 pending-action.json。不能只删除标记或盲重试。

## 每回合的公开决策摘要

记录：敌人有效意图及机制计数；现有费用/格挡；最保守可行防守与输出；选择方案的逐段伤害/掉血；下一回合关键牌出现率、能否支付；药水用与不用的差值；未知机制及资料来源。无需输出内部逐词推理。

对沙漏需额外跟踪每六张计数、自动出牌、凋萎数量/等级、人工制品、消耗来源是否仍在抽牌堆、下一次洗牌，以及防守后还能活几次凋萎。抽到与能打出是两件事。参考 `sts2rec/sts2rec/draw_odds.py`；它只实现超几何抽样，不是战斗模拟器。

## 多选、变化与快速 SL

新的隔离 MCP 补丁输出 selection_tracking、每张 is_selected、selected_indices/count、min_select/max_select。相同卡牌 ID 的多张复制品按当前画面索引区分，每次动作后重读。存在预览时只确认或取消，不能继续点隐藏网格。

变化预览保留 preview_originals；轮播示例牌不再作为 preview_cards 返回。cycles_random_examples 表示是否有随机轮播，result_committed=false，必须确认后比较真实牌组。缺选择字段的旧 MCP 仍需视觉兜底，不能伪造成功。非预览多选的可靠勾选变化可使用0.2秒短确认，其余战斗保留2.5秒兜底；空敌人列表不代表胜利。

快速 SL 操作：先确认无待处理动作 → 用视觉工具定位右上齿轮并点击（也可 Escape 打开暂停）→ 点击“保存并退出” → 主菜单出现后点击“继续游戏” → 核对官方存档恢复的层、房间、HP、药水与牌组。不要点击“放弃”。恢复通常是房间起点，不能承诺恢复到当前回合；遇到不支持保存的界面先观察。用户中途暂停也属于检查点恢复，不能隐藏掉回滚的尝试。

这是已写入记忆的 UI 操作流程，尚无可直接调用的一键 quick_sl MCP 动作。练习局可按用户授权使用；评测局必须明确计入 SL，不能宣称无读档。

## 构建与验证范围

`scripts/build_selection_mcp.sh` 从锁定的 upstream commit 应用 `patches/sts2mcp-selection-state.patch` 和 `patches/sts2mcp-rule-evidence.patch`，在隔离目录构建，再加原有 headless/端口补丁。headless_provision 已接入；现有运行进程不会热替换。不要往人类游戏安装。`scripts/export_sts2_rule_evidence.py` 在本地源码上验证必要片段，补充状态、附魔、污染模型和六条经核对的跨模型规则。

2026-09-09：C# 编译0错误0警告；Python回归覆盖选择确认、预览排除、牌堆信息边界、商店与知识引用、每局日志、超时阻止重复、成本去重、奖励口径与抽样概率。实机“两张变化/三张附魔”完整回归尚待下一次隔离运行启动，不把编译和合成状态测试冒充 UI 通过。

追加验证：22项Python回归通过。独立实验体检查点副本中，版本指纹匹配；震荡波触发激怒与虚弱后，敌人意图22→18，手中与我一战的目标伤害变量6→9，均与事前算式一致。用户要求退出后，原游戏与测试进程均已退出，测试日志在 output/source-of-truth-2026-09-09/smoke-gameplay/live.log。没有继续本回合或开始新的通关挑战。多选完整实机回归仍未完成。

边界：候选集用于展示与记录，目前直播act还没有把提交动作与候选集逐项做强制匹配；最终可执行性由MCP/游戏引擎决定。plan.json的完整性、哈希与动作理由可审计，但策略优劣、伤害推导和药水选择仍由模型负责。数值预览不是完整规则解释器，也不是牌序模拟器。Wiki/Codex目前是已核对的旁证，运行时主要查询本地版本化规则；没有自动联网刷新并自动信任网页的流程。

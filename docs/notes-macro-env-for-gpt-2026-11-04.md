# 给 GPT 侧的说明：宏动作环境（macros）——请在同一环境上训练与评测

日期：2026-11-04（v2：休息点 / 商店宏 + 所有选牌提示一步化、无取消；取代 v1 `8d36207`）。分支 `train/w4`
（合入提交见文末），设计细节：`docs/prompt-macros-v2-2026-11-04.md`。

## 一句话

模拟器可以把"进入子菜单 → 选卡 → 确认 / 取消"这类多步 UI 流程改成**只含可选项、不能回退的动作**：休息点直接给
"升级 X 卡"每张一行，商店直接给"删除 X 卡"每张一行（原子执行）；其余所有选牌提示（战斗中的手牌 / 牌堆选择、
三选一、事件里的变形 / 删除 / 升级 / 附魔一张或多张、遗物与药水的选牌）都**不再有取消行**，选满即自动确认。
开启：训练与评测都加 `--macros all`（= `rest,shop,prompt`）；只要 v1 行为用 `--macros rest,shop`。默认关闭。

## 为什么（已核实的数据，docs/diag-2026-11-04/q4-loopbreaker.md）

- 旧策略 u2225 在 dev-128（128 局未见过的开局）上**策略本身**只有 **0.252**；评测端 loop-breaker 介入后 **0.372**，
  差的 0.120 全是补丁的功劳。177 次介入里 **173 次**是同一个循环：休息点 SMITH → 选一张卡 → 取消 → 回到**逐字节相同**
  的休息点画面；其余是商店删卡的同类循环。策略无记忆，贪心会无限重复。
- 训练里这种闭环零奖励、零代价，训练压不住它。新主线加了每个闭环 0.01 的训练期成本后，breaker 开 / 关的差缩到
  **0.003**，但闭环仍在训练期出现。宏环境从结构上消灭它：**没有取消路径就没有闭环**。
- 清点（`PromptSites`）：移植里能弹出选择提示的模型 153 个，但**只有 4 个提示能取消**：休息点 SMITH、COOK、商店删卡、
  奖励删卡（RemoveCard），取消都回到原画面——这就是全部的环来源。其余提示不可取消、选择单调递增，本身不成环。
- 随机策略检查（64 槽、5 角色 A10，`CycleDetector(scope="all")` 统计任何画面 / 任何提示种类的回访环）：
  宏关 600 步 **10 个环**（休息点 6、商店 4）；`--macros all` 2,000 步 **0 个**，2,910 次提示决策、0 SIM_ERROR。
- 训练冒烟（`--macros all`，stream，6 更新，9,216 步）：任何种类 0 环，446 次提示决策里列出的取消行 0，商店删卡宏被选 6 次，
  0 SIM_ERROR、0 MACRO_FALLBACK。

## 候选表里变了什么

| 场景 | 宏关（默认） | `--macros all` |
|---|---|---|
| 休息点升级 | `ChooseRestOption(SMITH)` 一行 → 选卡页 → 确认 / 取消 | 每张可升级牌组卡一行 `ChooseRestOption(A=SMITH, B=d)`，B = 牌组行号（B≥0 即宏行）；一步完成 选项→选中→确认 |
| 商店删卡 | `BuyEntry(删卡)` 一行 → 选卡页 → 确认 / 取消 | 每张可删卡一行 `BuyEntry(A, B=d)`，价格照旧，一步完成 |
| 任何选牌提示 | `PromptSelect` 各行 + `PromptConfirm` + （4 个站点）`PromptCancel` | 无 `PromptCancel`；`PromptSelect` 一行 = 游戏的一次合法选择（`A` = 选项下标），若选后只剩确认则同一步确认 |
| 选 1 张（exactly-1） | 选 + 确认（牌组提示 2 步） | 1 步：每个合法选项一行 |
| 选 k 张（exactly-k，如事件变形 2 张、HIDDEN_DAGGERS 弃 2） | k 次选 + 确认 | k 次选，第 k 次即提交；已选进度在观测里（`prompt_options.picked`、`global_i.prompt_picked`），选择单调，不会回到同一观测 |
| 至多 N 张（up-to-N，如 PURITY 0..3） | 选若干 + 确认 | 选若干 + 一行 **DONE**（`PromptConfirm`）；选满第 N 张自动确认 |
| 可放弃（0 张，MinSelect 0 / CanSkip，如三选一 Skip） | 确认空选 | 0 张时的 `PromptConfirm` 行即显式 **DECLINE**，只在游戏允许时出现 |

- **原子执行、无取消**：宏行在一个 env step 里完成全部点击，中途状态校验不过即 SIM_ERROR（fail closed）；C# 测试证明
  宏一步后的状态哈希与手工点击序列完全相同（HAND_TRICK、PURITY、HIDDEN_DAGGERS、COOK、SMITH 退回）。
- **只列可选项**：行就是游戏自己的合法选择，选项已经过游戏的可选过滤（例：HAND_TRICK 只列手里的技能牌，攻击牌
  不出现；SECRET_TECHNIQUE 只列抽牌堆的技能；牌组提示只列可升级 / 可删除 / 可变形 / 可附魔的牌）。不新增、不泄露。
- **exactly-k 为什么不做成"一行一个组合"**：候选表一行只有一个 `card_id` 列，组合行要加列（ModelSpec 变宽、所有
  checkpoint 失效）或丢信息；顺序选 + 自动提交在不改规格的前提下同样无环。
- **"打开"步骤**：只在选项事先可见且确定时合并（休息点、商店删卡）。事件选项、出牌、用药水在点击前看不到之后的选项
  （先抽牌再选、随机生成的牌），预展开会泄露信息，所以仍是"点开 → 无取消的提示"。

## 不变的东西

- `encoder_version`、`ModelSpec`、`RunActionKind` 与所有 buffer / 词表：宏开 / 关完全相同（`check_spec_compat` 无差异；
  测试 `sim/tools/train/tests/test_prompt_macros.py::test_config_and_spec`）。
- 观测不改：`prompt_can_cancel` 仍描述游戏提示本身（宏开时可能为 1，但候选里没有取消行）。
- 信息对等：宏只暴露玩家在屏幕上能看到、能合法点击的选项。
- 默认路径（不传 `--macros`）逐位不变：合入后 3 更新的 pool / stream 冒烟 `weights_hash` 与 `079979f` 相同
  （pool `6fde2adf17381229`、stream `ffe9ba603d0b52a5`），全部非计时指标相同。

## 现有 checkpoint 怎么续

规格不变，**任何 checkpoint 直接带 `--macros all` 续训或评测**，不需要扩表：

```bash
sts2train train --out RUN_DIR --resume auto --macros all ...       # 续训；config.macros 记入 checkpoint
sts2train eval --checkpoint RUN_DIR/checkpoints/last.pt --seedset silent-a10-dev-v1 --policy greedy --breaker off
                                                                   # 评测默认沿用 checkpoint 的 config.macros；--macros none 关闭
```

已验证：宏关 stream checkpoint（u3）带 `--resume auto --macros all` 续到 u6，`resume_compat` 两侧 encoder_version
`3b73317739c650bd`、`model_inputs identical`、无扩表，rng 恢复正常。

注意：`history_actions` 是候选表下标，**宏开训练 / 录制的对局只能用同样的 macros 回放**（`sts2vec.evaluate` 记录
`limits.macros`）。同一个旧 checkpoint 在宏开环境里，SMITH / 删卡行的意义变成"升级 / 删除这张卡"，续训初期需要适应。

## Colab 包 / NativeAOT

- 包：`sts2train-d21adf4ad8.tar.gz`（7,232,705 字节，sha256 `ef377b832caf02c4f00219b42c3320606d7133553661c596bb2f769bce49fde7`，
  git_rev `d21adf4`；`.so` sha256 `997bb8a0fa6c7b49445192c1acfdb836f2a59bff0e9180f10a39b1cc7b0e7053`，与 owner G 在 raven 上构建的同 key 库逐字节相同），NativeAOT key **`03f89cc37d4f`**（`lib/Sts2Sim.Vec.so`，
  GLIBC_2.34）。v1 包（key `7aaa846a6a35`）的库不认识 `prompt`，会报 `unknown macro "prompt"`。
- 用法同前：`bash run_train.sh RUN_DIR 12 2048 <你的训练参数> --macros all`。
- Python 直接用：`VecEnv(n, macros="all")`；或 `config_json` 里 `"macros": ["prompt"]` / `"all"`。

## 自检：环检查怎么跑

- 训练期（只计数不干预）：metrics / `cycles.jsonl` 里的 `cycles_rest` / `cycles_shop` 应为 0（训练器自带检测器是房间范围）。
- 全范围（任何画面 / 提示种类）：

```python
from sts2train.cycles import CycleDetector
det = CycleDetector(spec, scope="all")   # 只计数：reset 后 observe(obs)；每步 commit(a, obs) → env.step → after_step(obs, done)
```

  参考实现：`sim/tools/train/tests/test_prompt_macros.py::_run`（64 槽 × 2,000 步随机策略，宏开应为 0 环）；
  远端跑：`scripts/test_remote.sh --suites none -- uv run --project sim/tools/train pytest -q -s sim/tools/train/tests/test_prompt_macros.py`。

## 已知限制

- **64 行上限**：开 `prompt` 时，若休息点 / 商店的宏行会让候选超过 64（`PromptMacros.MaxRows` = 训练器 `--max-rows` 默认
  值），退回原始 SMITH / 删卡行（之后是无取消的提示，选中即提交），该观测 status 置 `MACRO_FALLBACK`（= 512）。提示本身
  选项 > 64（极大牌组）时只能顺序处理，训练器照常计入 `cand_over_max_rows`。只开 `rest,shop` 时不加上限（v1 行为）。
- **SmithCount ≠ 1**（一次升级多张的休息点）不展开，仍走原始提示流程（宏开时无取消）。
- **奖励卡界面（非提示的 CardReward 阶段）**的 Skip 回到奖励界面，理论上可成环，未改；随机检查中 0 次。
- **真游戏孪生**（`scripts/live/nn_twin`）：休息点 SMITH / 商店 REMOVE 宏已映射为真实游戏中逐步校验的点击序列（还没在真游戏上
  跑过；REMOVE 与 play server 干跑 0 desync，SMITH 只有单元测试）。**`prompt` 宏在孪生侧尚未映射**：孪生的模拟器会自动确认而
  真游戏不会，首个"选后只剩确认"的提示就会 desync（fail closed：孪生停用，转 fallback）。真游戏评测 `all` 模型前要补这一步。
- combat lookahead 的搜索环境没有宏映射；play server / LLM 通道用 `RunSim.LegalActions()`，不受影响。
- 还没有用 `--macros all` 做过正式训练，只有 6 更新冒烟与续训冒烟。

## 代码位置

- 分支 `train/w4`（未推送），合入提交 `77acd78`；C#：`sim/Sts2Sim.Vec/PromptMacros.cs`、`RoomMacros.cs`、`VecEnv.cs`、
  `Status.cs`；Python：`sim/tools/vec/sts2vec/{pool,schema,env,cli}.py`、`sim/tools/train/sts2train/cycles.py`；孪生：
  `scripts/live/nn_twin/macros.py`。
- 文档：`sim/VEC.md`（"Room macros" + "Prompt macros v2"）、`sim/tools/train/README.md`（同名两节）、
  `docs/prompt-macros-v2-2026-11-04.md`。

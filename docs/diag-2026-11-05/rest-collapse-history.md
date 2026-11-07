# 休息点／商店塌缩溯源：M u3064 与 C1 u764（无宏）对比 A u5064（有宏）

日期 2026-11-05。只读审计，未改仓库代码，未提交。

## 结论

塌缩在宏出现之前就已存在。主线 M u3064（无宏，休息点 SMITH 会弹出可取消的升级提示）在 439 次休息决策里 HEAL 439 次，P(SMITH) 四位小数全为 0.0000；商店删牌 0 次。更早的 C1 u764（sp3-main 由它续训，配置相同，`cycle_cost 0.01`）也一样：HEAL 325/325，删牌 0 次。A u5064 的 `--macros rest,shop` 只是继承了一个已经塌缩的策略，宏本身不是起因。

## 1. 三个检查点并列（silent-a10-dev-v1，128 局，greedy，loop breaker OFF）

| 指标 | C1 u764（sp2-c1，无宏） | M u3064（sp3-main，无宏） | A u5064（w3m-a，宏 rest,shop） |
| --- | --- | --- | --- |
| progress mean | 0.3097 | 0.4042（与官方 eval 一致） | 0.4351 |
| 决策总数 | 35,915 | 42,694 | 47,375 |
| **休息点** 决策 n（同时提供 HEAL 和 SMITH 行） | 325 | 439 | 453 |
| 选 HEAL / SMITH / 其他 | 325 / 0 / 0（100%） | 439 / 0 / 0（100%） | 453 / 0 / 0（100%） |
| P(SMITH) 均值；P>0.05 的占比 | 0.0000；0/325 | 0.0000；0/439 | 0.0000；0/453 |
| P(HEAL) 均值 | 1.0000 | 1.0000 | 1.0000 |
| SMITH 提示的结局（选牌升级 / 取消） | 无 SMITH，不适用 | 无 SMITH，不适用 | 无 SMITH，不适用 |
| 休息时 hp/max 均值；满血仍 HEAL | 0.579；— | 0.601；41 次满血、73 次 ≥90% | 0.599；35 次满血、70 次 ≥90% |
| **商店** 进店次数 / 决策 n | 147 / 356 | 190 / 484 | 223 / 571 |
| 可删牌（且金币够）的决策 n（进店次数） | 176（121） | 159（116） | 203（139） |
| P(remove) 均值；最大；P>0.05 | 0.0010；0.0168；0 | 0.0015；0.0616；1/159 | 0.0001；0.0089；0 |
| 买了删牌 | 0 | 0 | 0 |
| 商店购买分布 | 药水 165，遗物 22，卡 16，无色 3 | 药水 157，卡 123，遗物 9，无色 5 | 卡 176，药水 139，遗物 16，无色 16 |
| **卡牌奖励**：屏幕结局 n（拿牌 + 跳过） | 954 | 1,222 | 1,343 |
| 跳过（ProceedRewards 时还有未拿的牌）占比 | 17（1.8%） | 42（3.4%） | 85（6.3%） |
| 卡牌奖励决策上的 P(Proceed) 均值；>0.05 的占比 | 0.028；14.4% | 0.037；17.6% | 0.058；17.9% |
| 终局牌组：升级牌均值 / 有升级的局数 | 1.05 / 59 | 2.02 / 85 | 2.12 / 94 |
| 终局牌组大小均值 | 22.4 | 25.0 | 26.3 |
| 终局 act 1 / 2 / 3 | 95 / 33 / 0 | 64 / 64 / 0 | 47 / 79 / 2 |

说明：

- 「休息点决策」= 非提示阶段、候选含 `Rest:` 行的决策。三个检查点里，每次休息点访问都只有一次选择，并且 SMITH 行一直可选（M 是 `Rest:SMITH`，A 是每张牌一行 `Rest:SMITH:<card>`）。
- 终局牌组里的升级都不是休息点来的，而是来自 Neow 和事件等 DeckUpgrade 提示，或者遗物（例如开局的 `DeckUpgrade` 提示）。休息点升级三者都是 0。
- 商店里 M 的删牌是普通 BuyEntry 行 `Remove$<price>`，选了会弹出 DeckRemove 提示；A 是宏行 `Remove:<card>$<price>`。M 唯一一次 P(remove)>0.05 是 0.0616，也没有被选中。
- 卡牌奖励的跳过率是低的（1.8%～6.3%），随训练略有上升。这里没有「从不跳过」那种塌缩，反而是牌组一路膨胀到 22～26 张。

## 2. 第 3 问：塌缩是宏之前就有，还是宏切换时才出现

宏之前就有。C1 u764 和 M u3064 都不用宏：SMITH 是一行普通的 ChooseRestOption，选了会打开可取消的 DeckUpgrade 提示，取消后回到休息点再选一次。在这两个检查点上，P(SMITH) 已经精确为 0，HEAL 占 100%，满血也照样 HEAL（M 有 41 次），删牌概率不到 0.2%。所以 u3064 切到 `--macros rest,shop` 的 A 线没有制造这个塌缩，只是在新语义下没有重新探索，把它原样带了过来：A u5064 的 HEAL 453/453，各张牌的 SMITH 宏行合计也是 0.0000。根因应该在宏之前的训练动态里，最早在 C1 的前 764 次更新里就已形成。可能的原因有两条。一是旧语义下 SMITH 和删牌都要多走一层提示：greedy 或采样一旦取消，就形成「休息点→提示→取消→休息点」的回访环，正好被 `cycle_cost 0.01` 惩罚，这让 SMITH 行的回报系统性偏低，而 HEAL 一步就结束，立刻得到 hp 收益。二是 PPO 熵下降后，这个分支再也采不到。本次审计只能确定塌缩的时间早于 u764，不能区分这两条原因。要区分，需要看 W0 u382（sp2-w0，本次没跑，按要求跳过）或更早的检查点，以及 C1/W0 训练日志里 rest 和 shop 的 cycle 计数。

## 3. 复现与核对

- M u3064：与官方 `gs://…/runs/sp3-main/evals/u0003064/greedy-off/` 对比，result/act/floor **128/128 一致**，progress 都是 0.4042，`history_actions` 逐字节一致 117/128（其余 11 局是 CPU 和 CUDA 的近平局分叉，终局楼层和结果都相同）。评估器报告的 UI 回访环为 0（rest 0，shop 0）。
- C1 u764：官方 u764 eval 只有 20 局（progress 0.288），没有 128 局的 greedy-off 可以比，所以没做逐局对照。
- 两个检查点的配置都没有 macros 键：sp2-c1 和 sp3-main 的 `config.json` 都是 `cycle_cost 0.01`、`loop_breaker True`（训练时），encoder 都是 `3b73317739c650bd`。sp3-main 与 sp2-c1 的配置只差 `max_updates`、`eval_at` 和时长，是续训关系。

## 4. 运行目录与命令

raven（`/data/raven/hack-sts2/audit/`，沿用 A dump 的容器配方：`run.sh` 是 python:3.13 容器，`--cpus 8`，nice 10，8 线程）：

| 运行 | 目录 | 检查点 sha256 | decisions / games sha256 前 16 位 |
| --- | --- | --- | --- |
| M u3064 | `m-u3064-dev128/`（日志 `m-u3064-dev128.log`） | `117108692d9fe3ce…`（复制自 `lookahead/m3064/ckpt/`） | `0a074eab0b948989` / `67a00baef5a44ef6` |
| C1 u764 | `c1-u764-dev128/`（日志重定向写错，日志为空；目录完整） | `74a7cbf6e5ed9fd9…` | `e5e9421a608ee561` / `5cd1acb826d6be44` |

```bash
# raven
cd /data/raven/hack-sts2/audit
cp ../lookahead/m3064/ckpt/pinned-0003064.pt ckpt/
nohup ./run.sh src/audit_dump.py --checkpoint ckpt/pinned-0003064.pt --seedset silent-a10-dev-v1 \
  --out m-u3064-dev128 --threads 8 > m-u3064-dev128.log 2>&1 < /dev/null &
# C1：Mac 端 gcloud storage cp gs://gomoku-zero-20260816-ai-sts2train/runs/sp2-c1/checkpoints/pinned-0000764.pt → scp 到 ckpt/
# 在 M 跑完后串行启动（同一时刻只有一个 8 线程任务）
./run.sh src/audit_dump.py --checkpoint ckpt/pinned-0000764.pt --seedset silent-a10-dev-v1 --out c1-u764-dev128 --threads 8

# Mac（stdlib）
scp raven:/data/raven/hack-sts2/audit/m-u3064-dev128/{decisions,games}.jsonl $SCRATCH/audit/m-u3064-dev128/
gcloud storage cp -r gs://gomoku-zero-20260816-ai-sts2train/runs/sp3-main/evals/u0003064/greedy-off $SCRATCH/dl/eval-m/
scp -r raven:/data/raven/hack-sts2/audit/m-u3064-dev128/eval/games $SCRATCH/dl/audit-eval-m/
python3 -I compare_eval.py $SCRATCH/dl/eval-m/greedy-off $SCRATCH/dl/audit-eval-m
python3 -I rest_shop_stats.py $SCRATCH/audit/m-u3064-dev128 M-u3064     # 同样跑 c1-u764-dev128、a-u5064-dev128
```

统计脚本 `rest_shop_stats.py`（只用 stdlib）放在 raven 的 `/data/raven/hack-sts2/audit/src/`，scratchpad 的 `audit-src/` 下也有一份。两份新 dump 的字段格式与 A dump 相同（见 `a-u5064-dev128/README.md`）。

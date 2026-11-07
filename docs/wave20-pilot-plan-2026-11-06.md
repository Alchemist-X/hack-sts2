# wave 20 试点预注册（2026-11-06 22:25 EST）

## 今晚第二个 GPU 槽位的归属（在 u12164 的 S − C3 配对出来之前写定）

并发会话上限只允许两张 A100：槽位 A 跑 C4（对照，C3 u12164 → u13164，已在 sts2a2 上跑）；槽位 B 在 S 约 22:58 结束、sts2b3 停掉后起 sts2b4，二选一：

| 读数（u12164，sts2a2 上的 `runs/w4-c3/evals/pairs/u0012164-S-vs-C3/pair.txt`） | 选择 |
| --- | --- |
| ab-512 配对 S − C3 ≥ +0.010 且 dev-128 配对 ≥ 0（学校在三个段终都保持小幅领先） | **S2**：S u12164 → u13164，S 旗标不变；对照 C4；预注册 `scripts/analysis/prereg/s2-vs-c4.json` |
| 否则 | **W20a**：C3 u12164 → u13164 + `--boss-hp-bonus 0.1`（唯一改动；wave-20 包 sts2train-f3668bf66a）；对照 C4；预注册 `scripts/analysis/prereg/w20a-vs-c4.json` |

理由：第二个槽位给期望信息增益更大的线。学校若在第三个配对点仍只是噪声内的小正数，继续它的边际信息少；boss 血量信号是新的改进轴，且与 C4 同一 checkpoint 分叉、代码在旗标关闭时逐位一致，是最干净的配对。

## W20a 的读法

- 机制指标先于结果指标：训练期 `boss_death_hp_frac_mean_act3` / `_act2`（wave 20 常开诊断）必须在 u12664 前相对 C4 上升；不上升则机制缺席，u12664 停线。
- 结果：dev-128 per-100 五点窗口配对（`scripts/analysis/evalstats.py`）、段终 ab-512 配对（12664 / 13164，只经 `decide.py` 读）、先行指标 act3 到达与第二幕 boss 通过。
- 风险：奖励只看打掉的血量，可能鼓励不防御的"玻璃炮"；若 boss 血量比例上升而 act3 到达与 fresh_win_rate 不升，判为机制错位，不采纳。
- kill：u12464 起任一 per-100 读数窗口差 < −0.03 且 CI 上界 < 0 → 停线（guard 今晚只有 alert 模式，由值班人执行）。
- 一个训练种子对最多得到 REPLICATE；采纳前需第二对种子。

## 不做的

- 不同时开 `--mask-drop-potion`（一线一改）。
- 不在 C4/S2/W20a 上换 dev-512 评测计划（会打断配对序列；下一对新线再换）。

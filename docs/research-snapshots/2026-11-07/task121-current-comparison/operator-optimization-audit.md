# STS2 当前训练算子与性能优化审计

日期：2026-11-07 UTC。性质：既有源码、冻结配置和历史标量证据的只读审计；本文件是审计持久化，不是新实验、部署或训练授权。

结论：当前管线已包含 embedding 反向、候选投影复用、GPU 常驻 rollout、减少同步、pinned 上传及并行预取等优化。部分来自继承 trainer，不能归功于本次审计。所核冻结 trainer 中未见自定义 Triton/CUDA kernel 部署证据。历史 CPU/执行方式迁移有描述性提速证据，但没有当前 H100 上每一项算子的独立加速对照。

## 当前授权边界

- 当前 M 不改配置、不插入新 probe，不运行新 benchmark、GPU 作业或 provider 查询。
- 完成当前 S/M 原定正式 dev256 评测和对比后暂停；下文候选不是执行指令。
- 本次只读取源码、JSON 标量和已有报告，核对文件 SHA，并对已记录阶段时间做加总/比例计算。未读取模型正文、种子正文或凭证，未实例化模型、推进环境或改变训练状态。
- 算子优化主要减少每百万决策成本；吞吐提升不等于游戏能力提升，也不能替代行为诊断和样本效率研究。

## 冻结来源与一致性

仓库根目录：`/Users/Aincrad/.codex/worktrees/sts2-modal-research/hack-sts2`。

以下路径以 `artifacts/modal-research/2026-11-04/` 为根；它们是实际冻结源的本地留存，不以本地 Git HEAD 代表生产。

| 源码 | SHA-256 | 绑定证据 |
|---|---|---|
| `research/task104/formal-preparation/candidate-worker/sts2train/model.py` | `1c0f48bbe45154417f5952d734673aa96874098c57010b765cdfa0b190becb21` | `research/task116/ops/source-proof.json:116` |
| `research/task104/formal-preparation/candidate-worker/sts2train/features.py` | `316e969717284df7aefc1ac459b02b508097db0e2b755476ba42337476529030` | `research/task116/ops/source-proof.json:91` |
| `research/task115/candidate/learning-overlay/ppo.py` | `a516334bf2cc82046a89beccdf926f81cb57b58e99ff3433871dadba0905ce14` | `research/task116/ops/source-proof.json:126`；S115 第二段终态 `inspection.json` 的 `source_overlay` |

三个本地文件均在本次审计中重新计算 SHA，与以上记录一致。模型/输入源码未变，当前 PPO 是实际学习 overlay，不是 task104 基底 PPO。

S115 实际配置：`research/task115/actual/segment1/collected-metadata/config.json:49-57`。S115 第二段和 M116 冻结配置分别在 `research/task115/segment2-actual-binding-preparation/release.bound.json` 与 `research/task116/ops/release.bound.json` 的 `spec.ppo_config`。二者均为 `amp=false`、`tf32=false`、`compile=off`、`grad_ckpt=false`、`epochs=1`、`minibatch=4096`、`transition_coef=0.1`，采样形状 `2048×64`。

下表 `model.py`、`features.py`、`ppo.py` 均指上表已核 SHA 的实际源。

## 已实现、已启用和已测量的区别

| 项目 | 已实现 / 当前启用 | 已有测量及限制 |
|---|---|---|
| 高级索引改 `F.embedding` | 是 / 是。`model.py:567-571,644-647`；避免重复 padding ID 的高级索引反向累加热点。 | `research/task065/candidate/sim/tools/train/README.md:137-145` 记载历史 A100 profile：两个原高级索引反向占 114/244ms，并有 CPU 等价测试记录。这是旧硬件、旧配方的继承证据，不是当前 H100 的独立提速对照；不能宣称当前因此提速 47%。 |
| 候选先投影、再 gather 求和 | 是 / 是。`model.py:262-273,633-648`；同一手牌、敌人、选项和状态投影供多个候选复用。 | 未找到当前自主线单因素加速对照。 |
| GPU 常驻 rollout 与 pinned 异步上传 | 是 / 是。`features.py:318-338` 使用 pinned/non_blocking；`:378-433` 预分配 GPU 存储；`:449-459` 设备侧 minibatch gather。 | 代码路径已核，未找到各项独立收益测量。 |
| 减少主机/设备同步 | 是 / 是。`ppo.py:1881-1899` 每 epoch 一次上传 minibatch 索引；`:1951` 每 update 一次回传统计；`:1136` 关闭 Categorical 参数校验；`model.py:335-336` 使用设备列索引缓存。 | 不能说没有同步；动作仍在 `ppo.py:1748` 回 CPU。历史实现说明见 task065 README:129-135。 |
| SDPA 注意力 | 是 / 是。`model.py:138-162` 使用 `F.scaled_dot_product_attention`，训练和采样保持同一路径。 | 未取得实际 kernel dispatch 证据，不能据此称当前已启用 FlashAttention。 |
| 原生并行环境、后台预取 | 是 / 是。`ppo.py:1879,1973` 使预取与 learner 重叠。 | task103 窗口测量见下一节。预取后台时间和 join 等待不能重复加到总时间。 |
| `torch.compile` / CUDA graphs | 是 / 当前关闭。`ppo.py:944-953,1048-1103` 有编译封装及一致性检查。 | 未找到当前自主线 H100 通过门禁且受控提速的实测。实现存在不代表当前运行已编译。 |
| BF16 / TF32 | 有开关 / 当前关闭。`ppo.py:259-260` 当前 transition 辅助学习契约要求 eager FP32、epochs1。 | `task022/execution-summary.json:49-60`：旧 BF16 首 4096 样本门禁 maxΔlogp=0.0049548149，超过 0.001，0 次 Adam 后拒绝。应说该路径未通过本项目数值一致性验收，不是 BF16 普遍无效。 |
| 激活重算 | 是 / 当前关闭。`model.py:604-606` 为非重入 trunk block checkpoint。 | `research/task046/verification.md:35-44`：旧 L 在原 FP32 4096 首次 learner 前向 OOM、0 optimizer；激活重算只是候选，实际省显存和耗时仍未知。 |
| microbatch 梯度累积 | 未找到已验收执行路径 / 无当前启用证据。 | task046 verification:14 和 task.json:34 仅写候选与保持有效批次、归一化、裁剪/step 语义的要求。4096 learner minibatch 不是 microbatch 梯度累积。 |
| fused Adam | 当前未显式指定。`ppo.py:1391` 为 `torch.optim.Adam(..., eps=1e-5)`，无 `fused=True`。 | 不能称已部署 fused Adam；也未实际测量默认是否选 foreach，以及其耗时。 |

## 最扎实的历史迁移测量

来源：`research/task103/actual-performance-report/report.md:18-33`；验收：`research/task103/root-actual-A-performance-acceptance.json:3`。

同起点、同 20-update 索引窗口，Function32 为 17,084 decisions/s，旧 Sandbox16 为 13,347 decisions/s，迭代计时范围描述性比值 1.280×。

| 20 次更新阶段合计 | Function32 | Sandbox16 |
|---|---:|---:|
| 环境 | 27.872s | 46.221s |
| 推理/输入/store | 34.417s | 31.785s |
| learner | 79.045s | 78.241s |
| 其他 | 12.106s | 40.159s |
| 后台预取，重叠项 | 78.817s | 109.989s |
| prefetch join，嵌套项 | 8.456s | 37.103s |

CPU 请求/affinity、native/Torch 线程、Function/Sandbox、物理 host、隔离种子区间及演化状态都存在变化。因此不是严格 CPU 单因素 A/B，不能宣称 32 核本身带来 28% 提升。也不能把更早相邻窗口导出的 1.90× 选择性当作 CPU 扩容收益。learner 时间基本未变，改进主要表现为环境和暴露的预取等待下降。

## 当前 S115 第二段的阶段时间

来源：`research/task115/segment2-terminal-root-readback/inspection.json:743` 的 `windows.all`，时间字段起于第 855 行。覆盖全部 763 次更新、100,007,936 个新增有效决策。

| 阶段 | 已记录秒数 | 占 iteration 总时间 |
|---|---:|---:|
| 环境推进 `t_env_s` | 3455.137 | 41.11% |
| 推理、输入、采样 `t_infer_s` | 1631.968 | 19.42% |
| learner 更新 `t_update_s` | 3081.545 | 36.66% |
| checkpoint `t_ckpt_s` | 60.305 | 0.72% |
| 未分配残差 | 176.557 | 2.10% |
| iteration 总计 `t_total_s` | 8405.512 | 100% |

这些是 **host elapsed 阶段计时，不是纯 GPU kernel 时间或 GPU 利用率**。infer 包含预处理、采样和传输/同步区间；未独立测量 sync、网络上传和完整 provider 生命周期，不能补 0。prefetch 可能与 learner 重叠，不能再单独叠加。checkpoint 内可能有同步 publication callback，最终循环外保存/上传也可能不在 iteration 计时内。口径见 `research/task119-monitor/architecture-performance/report.md:11-18`。

## 三个候选方向：尚未实施或测量

1. **先裁 padding 再 gather。** `features.py:453` 当前先从固定存储宽度执行 `index_select`，再在 458-459 行裁候选/选项行。候选改动是利用已知 host 行数先裁源张量再 gather，减少无效拷贝和临时张量。非连续读可能抵消收益，必须测量，不能预报倍数。只改变拷贝路径，保持返回 dtype、顺序、mask 和有效内容一致。

2. **候选特征 gather、mask、求和与 pooling 的局部融合。** `model.py:624-655` 以及 B0 公共手牌匹配 `features.py:95-156` 含多次小张量逻辑和 reduction，是值得 profiler 检查的假设热点。先定位 launch 数量、内存流量和反向成本，再选择局部 compile 或小 kernel。保持公开信息证明、歧义时返回 unknown、合法动作 mask 和训练/采样 logp 一致性，不能因速度简化语义。

3. **复用 pinned staging buffers，减少逐步分配和零碎上传。** `features.py:327-338` 每步逐 buffer 执行 pin/upload，可评估持久 staging、按 dtype 打包或双缓冲。必须保持源 buffer 被环境覆盖前已安全复制、异步生命周期与 stream 同步正确；不能以非阻塞调用存在推断实际重叠成功。

尚无 CUDA profiler 证据证明以上三个是排名最高的真实热点。当前已有 SDPA，不优先自行重写 attention；全图 compile、BF16、fused Adam、microbatch 都需要独立验证数值与优化器语义。尤其不能直接打开当前 transition 契约禁止的开关。

本报告不改变当前任务：继续完成既定 S/M 评测对比，然后暂停；不启动上述候选。

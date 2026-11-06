# scripts/dashboard — Silent A10 训练监控页的数据管线

线上页面是 claude.ai 上的一个 **Dashboard artifact**（私有，由所有者分享给协作者；页面本身不在仓库里托管）。
本目录是生成它数据的脚本和页面源码，每 30 分钟刷新一次（研究循环里由 Claude 手动执行）。

## 数据流

```
dash_state.json      手工维护：阶段、一句话结论、plan 表、算力上限、要画的 run 列表
dash_events.jsonl    追加写：时间线事件，每行 {"time", "kind", "event"}（kind 为 ok / bad / info，决定圆点颜色），只追加不改
mirror/<run>/        本机镜像：mirror_loop.sh 从桶里拉的 metrics.jsonl / evals.jsonl / status.json / config.json / train.log
        │
        ▼
python3 -I dash_data.py dash_state.json dash_events.jsonl out <colab 余额>
        │
        ▼
out/{status,plan,timeline,metrics,evals}.json      （行对象数组）
        │
        ▼  作为 artifact 资产上传（Artifact publish，asset: true）
        ▼  Dashboard artifact 的各 dataset 文档（status / plan / timeline / metrics / evals）指向新上传的资产 url
线上页面（index.html，按 data-source / data-field 绑定 dataset）
```

- `status.json`：一行；阶段、当前 run 的最新 update / 环境步数 / 近 20 个 update 的新开局进度均值、熵、KL、吞吐、
  UI 回访环、本批算力（`balance_at_batch_start − 余额`，对 `units_cap`）。
- `plan.json`：`dash_state.json` 的 `plan` 原样输出。
- `timeline.json`：事件按时间倒序。
- `metrics.json`：每个 run 的训练曲线（最近 400 行全保留，之前每 3 行取 1）。
- `evals.jsonl` → `evals.json`：每个评测点的种子集、模式、进度均值与 95% CI、胜局数、平均楼层。

## 命令

```bash
# 本机镜像（另开一个后台进程，间隔 600 s）：scripts/colab/ops/mirror_loop.sh <ops 目录>/mirror 600
cd <ops 目录>                       # 放 dash_state.json / dash_events.jsonl / mirror/ 的目录（不在仓库里）
python3 -I <repo>/scripts/dashboard/dash_data.py dash_state.json dash_events.jsonl out <colab balance>
```

第 4 个参数是 Colab 账户余额（计算单位），省略或给 `-` 时不计算算力消耗。

`dash_state.json` 里 `runs` 的 `dir` 写相对路径 `mirror/<run>`（例：`mirror/w3m-a`），相对于 `$STS2_OPS_DIR`，未设置时相对于
`dash_state.json` 所在目录；绝对路径照用。一个 run 名可以对应多个目录（例：B 的 `w3m-b-hist` + `w3m-b`，桶里被覆盖的那段历史另存一份）。

## 文件

- `dash_data.py` — 上面的生成脚本（stdlib）。
- `index.html` — Dashboard artifact 的页面源码（绑定 dataset 的表格、曲线与时间线）。
- `dash_state.example.json` — 2026-11-05 23:41 EST 的状态快照（目录已改成 `mirror/<run>` 约定），作为格式示例。
- 真实的 `dash_events.jsonl`、镜像数据和 `out/` 不进仓库。

# Recorder 性能观测与按需读取（2026-10-16 工作，2026-11 移植）

跟踪 issue：[Alchemist-X/hack-sts2 #9](https://github.com/Alchemist-X/hack-sts2/issues/9)。

这份说明来自 2026-10 的归档分支 `archive/oct-2026-recorder-decision-capture-and-training`
（7665421）。该分支建在过时的基线上；2026-11 只把仍然有效的部分移植到 recorder 0.3.1
之上，随 recorder 0.4.0 发布。归档里的版本号（"0.3" / "0.3.1"）已被占用，不再使用。

已被 0.3.1 取代、没有移植的部分：

- 归档的 `Hashing.CanonicalUtf8` 与 `RecordState` 重写：0.3.1 的 `Hashing.HashState`
  已经流式写出有序 UTF-8 并只对不重复的状态序列化载荷。归档额外的"完整规范字节 + screen"
  去重比较没有移植，去重仍只比较 SHA-256 截断哈希。
- 归档的 `capture_main_thread_ms` / `record_state_ms`：0.3.1 的 `snapshot_build_ms`
  已由 `Snapshots.TakeSnapshot` 计时整个主线程快照（builder + 哈希 + 信封序列化），
  直方图和按触发点拆分直接挂在这个测量上。
- `scripts/RecorderBench` 与 `scripts/benchmark_recorder_reads.py` 两套基准没有移植；
  归档里 C# 旧路径/新路径的对比数字针对的是被取代的实现，已删除。

## 反射元数据缓存

`FieldReaders` 按（类型，字段名）缓存反射得到的 `FieldInfo`，包含基类字段和"找不到"的结果；
从不缓存字段值、候选列表、牌描述或任何游戏对象状态，每次调用都重新读取当前值，
所以牌堆更新和重掷后读到的是新值。`PassiveStateBuilder.GetInstanceFieldValue` 走这个缓存。
原始游戏对象仍只在主线程读取，后台写盘协议和 seq 分配不变。

## manifest `perf` 新增字段（均为可选，schema_version 不变）

| 字段 | 含义 |
| --- | --- |
| `snapshot_build_ms.p95_upper_bound` / `p99_upper_bound` | 与 `count/avg/max` 同一测量（每次主线程快照的完整耗时，含被去重的快照）的分位数上界；`count/avg/max` 计算方式不变 |
| `capture_by_trigger_ms` | 同一测量按快照触发点（`phase` / `poll` / `action` …）拆分，每项 `{count, avg, max, p95_upper_bound, p99_upper_bound}`；最多 32 个触发点，其余归入 `other` |
| `states_deduplicated` | 因哈希与上一快照相同而未写入的状态数 |
| `writer_queues` | 每个流（states/actions/events）当前待写行数、字节数和历史峰值；不含写线程正在写的一行，峰值在 Flush 后保留 |

分位数来自固定内存直方图（96 个桶，桶宽约 19%），报告的是上界；落在最后一个桶时用实际 max。
耗时不包含整个 Harmony 回调、动画、帧时间或之后的 fsync。保存/结束时仍可能等待队列落盘，
磁盘故障仍会同步回退；没有用丢日志来控制积压。

## 读取方式与缓存失效（sts2rec）

普通 `build_canonical()` 仍保留直接扫描路径，并去掉了每一步审计时重复推导一遍 legal actions。
完整导出仍返回完整对象，内存随结果增长。

可选的 `IndexedCanonicalReader` 与 CLI 参数：

```bash
cd sts2rec
uv run sts2rec canonical /path/to/session \
  --cache-dir ../output/recorder-index \
  --action-seq 380 --action-seq 381 -o ../output/selected-decisions.json
```

首次构建索引会逐条解析、校验并保存完整原始记录；之后的小范围查询按 `(stream, seq)`
只解码所需动作、它引用的状态、下一状态和对应事件区间。输出保留原始 `step_idx`，
只有原局最后一步带 terminal；只选部分动作时，未选动作的事件不会被并入。

- 索引必须放在 session 目录之外，可删除重建，不修改源日志。
- manifest 与实际选用的三个流文件的路径、设备、inode、大小和纳秒 mtime/ctime 参与失效判断，
  plain/gzip 切换也会失效。这是本地文件变化检测，不是内容认证。
- 完成的局复用索引；`incomplete:true` 每次生成临时索引，用完清理。读取中源文件变化会报错，
  调用方应丢弃部分结果重试。
- 先写临时数据库再原子替换。打不开的损坏数据库会重建；读取中发现损坏则报错并删除索引。
  记录引用缺失仍报错；明确的捕获失败（`_decision.status: missing`）仍为 null，
  不能用之后的状态补齐。
- 缓存只存原始记录，不存推导出的候选动作或覆盖审计，改审计逻辑不会读到过期结果。
- 迭代 API 最多保留 8 个解码状态和当前 step；SQLite 页缓存上限约 2 MiB。
  调用方若自己收集全部输出，仍会占用整局内存。

```python
from sts2rec.indexed_reader import IndexedCanonicalReader

with IndexedCanonicalReader(session_dir, cache_dir) as reader:
    for step in reader.steps():
        consume(step)  # 不要 list(...)，才能保持按步读取的内存优势
```

2026-10-16 在 macOS arm64 上用合成数据（每局 200 动作，5 次重复，归档里的基准脚本，未移植）
测得的参考值，机器负载未隔离，不是延迟保证：

| 候选数 | 直接扫描 P50 → 热索引 P50（ms） | 直接扫描峰值 → 热索引峰值（Python MiB） |
| --- | --- | --- |
| 3 | 2.522 → 0.402 | 1.050 → 0.027 |
| 40 | 7.706 → 0.555 | 4.055 → 0.073 |
| 200 | 31.786 → 1.212 | 17.085 → 0.273 |

200 候选整局导出：直接扫描 101 ms，热索引 106 ms，首次建索引并导出 174 ms。
所以索引默认关闭，用于重复的局部查询和按步消费。

## 待做：隔离实机验收

离线测试证明不了 Godot UI 时序和真实帧耗时。仍需在隔离实例（不是玩家正在玩的那份安装）上读取
manifest `perf`：主线程快照的 p95/p99、各触发点的耗时分布、每次操作实际快照次数和写盘积压。

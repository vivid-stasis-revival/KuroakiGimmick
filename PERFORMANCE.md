# Kuroaki/Gimmick 18.0 性能重构实施与实测报告

本地基线为 `ee223188620763d15e70ae9c3aded0faa87ee6d8`（v0.1.5 / 18.0），开始时工作区干净。已直接修改 C# / Rust、构建并测试；没有提交、推送或创建 Release。旧代码通过 `git archive` 导出到工作区外进行比较，没有重置当前仓库。完整 Scene 基线也重新构建了基线版本 Rust 库。

实测环境：macOS 15.7、Apple M4 Pro、SDL_GPU Metal、.NET 8.0.22，Release JIT；另验证 osx-arm64 NativeAOT 发布和启动。GPU 压力基准为 640×360，完整场景逻辑坐标仍是 320×180。使用相同本地资产。原始输出在 [results/m4-pro-2026-10-12](tests/Performance/results/m4-pro-2026-10-12/)，复现方式在 [README](tests/Performance/README.md) 与 [performance-check.sh](scripts/performance-check.sh)。

## HAL30000 真实编辑器回归（后续修复）

用户报告 dist 构建在 HAL30000 的 Editor → Viewer 切换后达到 330.69 GB 应用内存。此前短时合成基准**不足以证明这条交互路径内存可靠，330 GB 根因尚未完整复现/确认**。真实 `Encore.vsc` 为 2,893,693 条记录，可见时间轴段仍有数十万 Note；仅消除全谱扫描没有解决可见数据自身的绘制成本。

新增修复：时间轴每个块由五个矩形变成一个同序 GPU 实例。半透明 UI 淡入时保留五矩形旧路径，避免 RGBA8 中间舍入改变叠加结果。VSC 初次解析结果作为独立源快照缓存，VSM/布局重建复用不可变 Note 记录与 ScoreState；各 Session 的可变集合独立。文件路径/长度/修改时间或 BPM/offset/FixedBpm 配置变化使相应缓存失效。原 GPU 显式三槽缓冲与 fence 背压一起进入新 NativeAOT 构建。

相同 M4 Pro/Metal、1440×940 测试画布、真实 HAL30000、固定 124.72 秒、60 帧、不删 Note、相同场景特效。两列都使用新的 Session 缓存及 GPU 缓冲实现，仅以 `KG_TIMELINE_INSTANCING=off` 切换五矩形兼容路径。可见 Note 共 26,024,460 次（每帧 433,741）。包含冷启动及淡入，不等同于完成 GPU 光栅后的显示 FPS：

| 实测项 | 五矩形兼容路径 | 时间轴实例路径 |
|---|---:|---:|
| 时间轴 Note CPU wall P50 | 275.338 ms | 28.316 ms |
| 时间轴 Note CPU wall P95 | 282.049 ms | 90.585 ms |
| Draw + Submit 平均 | 327.283 ms | 82.794 ms |
| Draw + Submit P50 / P95 | 310.310 / 323.520 ms | 62.920 / 131.360 ms |
| Draw + Submit P99 / max | 1015.460 ms | 691.010 ms |
| 60 帧上传总字节 | 27,935,739,840 | 4,617,823,680 |

旧 18:22 dist 的实际窗口测试在 3 GiB 保护阈值下被中止；另一次 90 秒、6 GiB 保护的 Editor 高密度段测试 RSS 峰值 5,220,106,240 字节，未重现 330 GB，超时由保护脚本停止。21:08 本地 NativeAOT 在真实窗口、用户布局偏好、实体音频流及菜单下完成 300 帧反复切换，固定高密度时间段，RSS 最终 2,663,563,264 字节；短时测试不能推断长时间泄漏已完全修好。

后续 21:17 NativeAOT 运行 1,500 帧、50 次工作区切换并持续跳回高密度播放段，107.457 秒完成；保护脚本 RSS 峰值 2,421,882,880 字节，结束 2,420,064,256 字节，没有持续增长。该次记录到 52 次空队列；审查确认 `ResetAudio` 主动清空并预填的队列也被错误计为 underrun。已修正为只在正常 feeder 轮询时统计，新增 20 次播放中 Seek 的 dummy 回归通过，后续需用最终 NativeAOT 再验证。这项修复只修正统计口径，不通过屏蔽正常播放队列耗尽来改变结果。内存 RSS 与 macOS Force Quit 的应用内存口径不同，仍不能声称已经完整复现并根治 330 GB。

200 Note 普通谱面的 120 帧对照：时间轴 CPU P50 0.2923 → 0.0592 ms；UiDraw P50 1.8536 → 1.6035 ms，P95 2.0909 → 1.8899 ms。含冷启动平均 5.3750 → 5.6069 ms（实例首次 shader/pipeline 初始化影响），普通稳定帧没有测到明显回退。3 秒 UI 停顿的 dummy 音频测试补给 276 次，观测 underrun 为 0。

最终 21:22 NativeAOT 的同一 1,500 帧 / 50 次切换复测在 109.654 秒完成：100 ms 采样 RSS 峰值 2,457,812,992 字节；逐帧 WorkingSet 峰值 2,616,229,888 字节，结束 2,427,994,112 字节。实体音频流（测试音量为 0）补给 8,886 次，正常轮询观测 underrun 为 0。帧记录 wall P95 91.038 ms、P99 282.201 ms、max 963.029 ms；显式 GPU/驱动等待最大 914.077 ms，故不能宣称所有 UI 卡顿消失。该次进程累计分配 26,959,944,872 字节（含初始化及短命对象），仍有分配/GC 优化空间。以上只覆盖约两分钟的真实高密度路径，不能证明任意时长、任意数据均无内存泄漏。

新增像素回归在 1× / 1.25× / 1.5× / 2× / 3× 比例、裁剪、各类型/选中状态/重叠及 1 / .5 / .13 透明度下逐字节一致。Note 与 Timeline shader 的 SPIR-V、MSL、HLSL 转译通过；C# 回归在 Rust auto / verify / off 均通过。早期开发中的 shader varying 缺少 location 和半透明舍入差异均由测试发现并修正。VSC 源缓存的修改失效、BPM/offset 失效和 Session 隔离新增回归通过。

新增 `scripts/workspace-memory-check.py` 对每次真实谱面复现限制 RSS 和时间，只终止其启动的测试进程组。原始日志见 `tests/Performance/results/hal30000-2026-10-12/`。普通谱面仍保留旧路径；当前 HAL 高密度预览粒子约占 34–43 ms，整体编辑器仍有可感知卡顿，未宣称 60 FPS。

## 实际瓶颈与修复

- `Viewer.Drawing` 原来每帧分配密度数组、扫描全部 Mods、逐事件换算时间，并重新统计全部 Note。Session 现在缓存排序事件时刻与 Note 数；分箱只在数量/时长变化时更新，使用分箱边界二分查询。
- 编辑器原来两遍遍历所有 Note、重复换 Beat；VSM 每帧复制全部 Clips，每条可见轨又过滤全谱。现在查询含 crossing Hold 的区间索引，缓存 Beat，保留同刻顺序与 bumper/chip 两遍层级。VSM、折叠文本/图像轨与选择 ID 有缓存；内容 revision 和 BPM/Note 快照使缓存失效。起点排序未变时增量更新树的结束时间/内容；插入、删除、起点变化或 BPM 变化重建相关树。
- `Dirty`、ProxyCount、Proxy 片段计数/PRA 查找和选中片段查找也是全谱热路径。现改为 revision 缓存和 ID/轨道查询。大 VSM 的 `NormalizedText` 移到现有预览加载 worker，先在 UI 线程取得不可变行记录快照。布局变动不再使 VSM 区间索引无条件失效。
- `BpmMap` 的逐分段线性查询在密集 BPM 变化时放大其他开销，改成二分，保留端点、负时间、offset、倒查和 NaN BPM 查询语义。
- Rust 原路径逐帧生成每候选 56 字节的输入，另保留整帧候选/输出。现在缓存 24 字节连续 Note 数据，只传候选索引与逐轨参数，4096 Note 一块，直接写入固定复用输出。没有新增线程调度或 native 逐帧堆分配。保留旧 native batch、C# 数值实现以及 auto/off/verify/required；老 ABI 库缺少新入口时仍可调用旧 batch。
- NoteSkin 的帧选择、纹理/UV 也预计算。纹理无损打包，边界复制 padding，过大图/特殊 UV 留在独立纹理路径。每 sprite 提交一个 64 字节实例，GPU 构造六个顶点并旋转/算 UV，替代 CPU 六个 32 字节顶点。Body/Head/End 顺序、pressed Hold、旋转补正、Alpha、裁剪、Blend 和 Proxy 合成保留；不会按纹理重排半透明物体。
- 密集判定后的指示框、note particles、diamond dust 也存在纹理切换和重复计算。加入同序 Atlas/实例路径；对相同时间、轨道、Kind、Lead 的图元重用计算结果，仍为**每次判定追加每个 sprite**。模板容量只是计算缓存容量，超出后正常逐个生成；不会少画。多页 Atlas 不使用单纹理模板捕获。
- GPU 原精确扩容会反复重建 buffer。现在几何增长，三个持久 vertex/upload 槽，每槽复用前确认其 fence 完成，使用 `cycle=false`。初版自动 cycling 在百万判定时测得高峰内存，因此改为显式槽。CPU staging 分块、GPU 64 MiB / 16384 batch 记录边界可分段提交；后续 pass LOAD 先前结果，不删除或重排图元。资源异常提交与关闭也保护 fence 生命周期。
- Viewer 在主呈现队列达到两帧时继续事件/Update 循环，不再先构造新帧后长时间等交换链。超大逻辑帧的有界 GPU 等待会 pump SDL 事件；音频独立供给。VSync 的短时等待仍可能存在。
- 音频供给从 UI Update 移到专用线程，10 ms 调度，复用静音缓冲；Stream、播放/暂停、Seek、音量、倍速、延迟、路线/变速和关闭用同一 monitor 串行化。Seek 先 Pause/Clear/重补，旧数据不会被 feeder 再写回。音频线程不解码、不读文件、不重建大型谱面。
- 导出改成三个持久 download 槽的异步 fence 轮询，完成后才 map；两帧编码 channel 加一帧 pending 提供背压，后台 FFmpeg 写入使用池化内存。rendered / accepted 计数分开，严格按序、没有丢帧或重绘 pending 帧。取消/错误保留原文件、归还缓冲、停止编码器并清理 partial。

## 实测比较

“记录/提交”包含 CPU 生成、上传、驱动调用及可能的缓冲背压；不是纯 CPU 活跃时间。“完成帧”包含同步 RGBA 读回作为测量终点，不是交互窗口 FPS。表中单位为 ms，P95/P99 基于短样本，常等于最大值；两帧 warmup 不计入表。不要据此推断长时间游戏的 P99。

| 工作负载 | 样本数 | 旧记录/提交均值 → 新 | 旧完成帧均值 → 新 | 旧完成帧 P95/P99 → 新 | 旧完成帧最大 → 新 |
|---|---:|---:|---:|---:|---:|
| 10,000 同刻 NoteSkin | 12 | 12.406 → 1.193 | 14.413 → 3.896 | 22.728 → 5.926 | 22.728 → 5.926 |
| 100,000 同刻 NoteSkin | 12 | 79.716 → 7.464 | 85.747 → 18.878 | 107.565 → 25.040 | 107.565 → 25.040 |
| 1,000,000 同刻 NoteSkin | 6 | 1417.515 → 29.213 | 1508.054 → 124.943 | 1536.360 → 166.161 | 1536.360 → 166.161 |
| 完整 Scene，1,000,000 同刻 | 6 | 1707.571 → 55.467 | 1795.975 → 150.264 | 1871.389 → 175.589 | 1871.389 → 175.589 |
| Viewer，百万均匀 Note + 百万 VSM | 12 | 42.067 → 1.730 | 51.098 → 2.354 | 159.661 → 3.800 | 159.661 → 3.800 |
| 完整 Scene，普通 200 Note | 30 | 0.419 → 0.203 | 2.274 → 2.095 | P95 6.355 → 7.199；P99 11.885 → 11.778 | 11.885 → 11.778 |
| 判定粒子，200 同刻判定 | 8 | 4.413 → 0.492 | 5.427 → 2.579 | 9.534 → 8.137 | 9.534 → 8.137 |

普通 200 Note 的 CPU 阶段更快，完成帧均值相近；GPU 等待尾部受调度影响，P95 并非所有测试都下降。百万完整 Scene 的完成吞吐为约 6.66 帧/秒，仍远低于 60 FPS。

| 同刻 NoteSkin 数 | 旧批次 → 新批次（含 clear） | 新实际 draw calls | 旧上传字节 → 新上传字节 |
|---|---:|---:|---:|
| 10,000 | 8309 → 2 | 1 | 1,920,000 → 640,000 |
| 100,000 | 83078 → 2 | 1 | 19,200,000 → 6,400,000 |
| 1,000,000 | 830770 → 2 | 1 | 192,000,000 → 64,000,000 |

百万完整 Scene：800066 → 67 批次，新的实际 draw calls 为 43，192,010,368 → 64,010,368 上传字节。百万 NoteSkin 的 OS 最大 RSS 旧为 1,759,526,912 字节；新 OS 最大 RSS 为 695,746,560 字节，采样 working set 为 662.6 MiB。旧 .NET 的 `PeakWorkingSet=0` 不可用于比较。该测量中的每帧分配 420,855,387 → 944,217 字节，后者大部分是为测试显式分配的 921,600 字节截图。CPU process time（含 warmup）12.466 → 0.515 秒。

百万同时判定（保留全部粒子），显式 ring 后完成帧均值 915.576 ms，P95/P99/max 946.160 ms，约 1.09 帧/秒。此案例没有跑旧版本的百万粒子全量对照，不能报告旧/新倍率。重构阶段的自动 cycling 版本最大 RSS 3,429,154,816 → 显式 ring 1,354,579,968 字节，完成帧均值 1108.250 → 915.576 ms。大部分逻辑帧时间仍为 GPU/缓冲背压。

另测：百万均匀 Note 完整 Scene 平均完成帧 1.064 ms；百万 VSM 事件 1.091 ms；10,000 crossing Hold 7.479 ms；高频 BPM/Scroll/Velocity + Wave/Driven/Boost/beat-motion 5.588 ms（Rust verify）。这些案例没有对应旧完整 Scene 数字，不能推断倍率。百万数据的 UI 查询微基准，旧全密度扫描 + 两次 Note 扫描平均 57.218 ms，新缓存/区间查询 0.270 ms，查询稳定阶段分配为零；这不是整帧 FPS，也不是旧版 BPM helper 的独立测量。

视频：30 帧、640×360、60 FPS，旧 0.385 秒 / 78.00 export FPS，新为 0.348 秒 / 86.22 export FPS。此前同配置新测试为 0.326 秒 / 92.06 FPS；冷启动 shader/pipeline/JIT 包含在这个很短的导出中，不能当成长视频编码速度。ffprobe 检查为严格 30 帧 / 60 FPS，解码 frame MD5 与旧导出逐帧相同。带音频导出另验证 15 帧 / 30 FPS，音频流存在。代码不再在每帧 Tick 中调用同步 Read/WaitForGPUFences；GPU 下载、CPU copy 与后台编码仍需真实时间。

音频：dummy SDL 设备，故意 3 秒不调用 UI Update，最近一次测试供给 275 次、观察到的空输入队列 underrun 为 0。验证暂停、Seek、倍速、静音、AudioDelay、playspeed/jump route 与 Dispose。此结果不等同于已验证所有实体声卡的听感。

## 验证结果与限制

通过 Release 构建；C# 原有回归与新增区间/BPM/分箱/零查询分配/增量更新回归；Rust 17 tests passed、1 ignored；32,768 native-vs-managed 数值 oracle；百万规模、100,000 Hold 的 10,000,200,000 logical hits 和 staggered Hold/Seek 检查。忽略项为既有高内存手工压力测试，不是本次新增失败。

Metal：旧版本与实例/Atlas 皮肤图逐字节相同，230,400 像素，覆盖四套 changeskin、Chip/Bumper/Mine/Hold、角度/Alpha/Blend/裁剪；判定粒子画面也逐字节相同。关闭新路径的兼容实现同样匹配。Startup、文本 UI、#42/#43 编辑器拖动/撤销/重做/搜索/布局与 Proxy footer（移动、缩放、旋转、透视、Alpha、裁剪）通过。24 帧异步 readback 严格有序、槽复用、目标覆写和 pending disposal 通过；取消/编码器主动退出保留原输出并清理 partial。

SPIR-V / MSL / HLSL shader 转译通过，Metal 实际运行通过。Windows Vulkan、D3D12 字节码/驱动、AMD 硬件、实体音频设备及所有第三方皮肤/Native Gimmick 的完整视觉矩阵**未在当前环境验证**。跨平台布局/绑定共用同一实现，旧回退保留；发布前仍需目标平台测试。

调试时曾发现并修正：新测试的顶层 Note 名称遮蔽导致编译失败、浮点 roundtrip 的测试端点假设错误，以及 `/tmp` 符号链接导致基线 MSBuild manifest 重名。最终已运行的测试没有这些失败。构建仍有原有 `RustTimelineBridge` CS0652、`StarcrashersLate` CS8602，以及 AOT 链接器的 `ld_classic` 弃用警告；没有为消除它们改动不相关实现。

剩余瓶颈：百万透明覆盖/大量判定粒子仍消耗 GPU 光栅和带宽；特殊 Native 叠加不同纹理/Blend 时仍必须保序、可能较多 draw。极端逻辑帧中应用层输入处理仍可能等到该帧结束；SDL 事件泵与音频保持工作，应用提交的数据与 mesh 槽有明确上限，原生驱动的长时间内存稳定性仍需真实交互验证，但没有将整个非线程安全 SceneRenderer 搬到独立渲染宿主，因此不能宣称所有极端场景的 GUI 延迟已完全消除。首次索引构建和编辑内容变更仍有与数据规模相关的工作，稳定普通帧不会重新扫描全谱。Note/BPM 缓存以额外持久内存换取查询速度。

Profiling 默认关闭，启用和统计边界见测试 README。`GpuWait` 单独记录显式等待和 ring 背压，`GpuSubmit` / `GpuRecord` 属于驱动/记录 wall time，无法保证其内部没有驱动阻塞；不能仅根据总 CPU/GPU 占用或把全部 wall time当成 CPU 活跃计算来判断瓶颈。GC pause 和 underrun 统计的观测限制已明确记录。

## 修改文件

- [rust/kg-native-core/src/lib.rs](rust/kg-native-core/src/lib.rs)
- [scripts/performance-check.sh](scripts/performance-check.sh)
- [src/App/Program.cs](src/App/Program.cs)
- [src/Bridge/RustNoteBridge.cs](src/Bridge/RustNoteBridge.cs)
- [src/Core/Audio/Transport.cs](src/Core/Audio/Transport.cs)
- [src/Core/Diagnostics/PerformanceStats.cs](src/Core/Diagnostics/PerformanceStats.cs)
- [src/Core/Editing/EditorDocument.cs](src/Core/Editing/EditorDocument.cs)
- [src/Core/Export/VideoExport.cs](src/Core/Export/VideoExport.cs)
- [src/Core/Projects/Session.cs](src/Core/Projects/Session.cs)
- [src/Core/Timing/BpmMap.cs](src/Core/Timing/BpmMap.cs)
- [src/Core/Timing/EventDensity.cs](src/Core/Timing/EventDensity.cs)
- [src/Core/Timing/IntervalIndex.cs](src/Core/Timing/IntervalIndex.cs)
- [src/Core/Timing/NoteIndex.cs](src/Core/Timing/NoteIndex.cs)
- [src/Core/Timing/NoteMotion.cs](src/Core/Timing/NoteMotion.cs)
- [src/Graphics/Notes/NoteSkin.cs](src/Graphics/Notes/NoteSkin.cs)
- [src/Graphics/Notes/NoteTextureAtlas.cs](src/Graphics/Notes/NoteTextureAtlas.cs)
- [src/Graphics/Scene/SceneRenderer.Judgment.cs](src/Graphics/Scene/SceneRenderer.Judgment.cs)
- [src/Graphics/Scene/SceneRenderer.JudgmentBatch.cs](src/Graphics/Scene/SceneRenderer.JudgmentBatch.cs)
- [src/Graphics/Scene/SceneRenderer.Playfield.cs](src/Graphics/Scene/SceneRenderer.Playfield.cs)
- [src/Graphics/Scene/SceneRenderer.cs](src/Graphics/Scene/SceneRenderer.cs)
- [src/Graphics/SdlGpu/Canvas.Geometry.cs](src/Graphics/SdlGpu/Canvas.Geometry.cs)
- [src/Graphics/SdlGpu/Canvas.Instances.cs](src/Graphics/SdlGpu/Canvas.Instances.cs)
- [src/Graphics/SdlGpu/Canvas.Readback.cs](src/Graphics/SdlGpu/Canvas.Readback.cs)
- [src/Graphics/SdlGpu/Canvas.cs](src/Graphics/SdlGpu/Canvas.cs)
- [src/Graphics/SdlGpu/GpuDevice.Submission.cs](src/Graphics/SdlGpu/GpuDevice.Submission.cs)
- [src/Graphics/SdlGpu/GpuDevice.Transfers.cs](src/Graphics/SdlGpu/GpuDevice.Transfers.cs)
- [src/Graphics/SdlGpu/GpuDevice.cs](src/Graphics/SdlGpu/GpuDevice.cs)
- [src/Graphics/SdlGpu/GpuReadback.cs](src/Graphics/SdlGpu/GpuReadback.cs)
- [src/Graphics/SdlGpu/Shader.cs](src/Graphics/SdlGpu/Shader.cs)
- [src/Native/Sdl.Gpu.Commands.cs](src/Native/Sdl.Gpu.Commands.cs)
- [src/Native/Sdl.cs](src/Native/Sdl.cs)
- [src/UI/Editor/Viewer.Editor.Clipboard.cs](src/UI/Editor/Viewer.Editor.Clipboard.cs)
- [src/UI/Editor/Viewer.Editor.Drawing.cs](src/UI/Editor/Viewer.Editor.Drawing.cs)
- [src/UI/Editor/Viewer.Editor.ImageTimeline.cs](src/UI/Editor/Viewer.Editor.ImageTimeline.cs)
- [src/UI/Editor/Viewer.Editor.PrGrid.cs](src/UI/Editor/Viewer.Editor.PrGrid.cs)
- [src/UI/Editor/Viewer.Editor.Reference.cs](src/UI/Editor/Viewer.Editor.Reference.cs)
- [src/UI/Editor/Viewer.Editor.Select.cs](src/UI/Editor/Viewer.Editor.Select.cs)
- [src/UI/Editor/Viewer.Editor.TextTimeline.cs](src/UI/Editor/Viewer.Editor.TextTimeline.cs)
- [src/UI/Editor/Viewer.Editor.Texts.cs](src/UI/Editor/Viewer.Editor.Texts.cs)
- [src/UI/Editor/Viewer.Editor.Timeline.cs](src/UI/Editor/Viewer.Editor.Timeline.cs)
- [src/UI/Editor/Viewer.Editor.TimelineIndex.cs](src/UI/Editor/Viewer.Editor.TimelineIndex.cs)
- [src/UI/Editor/Viewer.Editor.cs](src/UI/Editor/Viewer.Editor.cs)
- [src/UI/Viewer.Drawing.cs](src/UI/Viewer.Drawing.cs)
- [src/UI/Viewer.Input.cs](src/UI/Viewer.Input.cs)
- [src/UI/Viewer.cs](src/UI/Viewer.cs)
- [tests/Performance/Performance.csproj](tests/Performance/Performance.csproj)
- [tests/Performance/Program.cs](tests/Performance/Program.cs)
- [tests/Performance/README.md](tests/Performance/README.md)
- [tests/Regression/PerformanceRegression.cs](tests/Regression/PerformanceRegression.cs)
- [tests/Regression/Program.cs](tests/Regression/Program.cs)

性能原始结果目录和本报告也为新增文件。未自动提交。

最终再次运行完整 `performance-check.sh` 成功，包括全部压力场景、Rust verify、回退像素比对、粒子像素比对、异步读回、音频、失败/取消和导出探测；输出记录在结果目录 `repro-check.txt` 和 `repro-*` 文本。

本次 HAL 回归另修改/新增：`src/Core/Models/Chart.cs`、`src/Graphics/SdlGpu/TimelineNotePainter.cs`、`src/Graphics/SdlGpu/Canvas.Presentation.cs`、`src/UI/Viewer.WorkspaceMemorySmoke.cs`、`tests/Performance/SwitchMemory.cs`、`scripts/workspace-memory-check.py`、`scripts/publish-aot-common.sh`。发布脚本适配用户将文档移至 docs 的工作区调整；未恢复或覆盖用户文档移动。

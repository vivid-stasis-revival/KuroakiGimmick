> 历史文档：以下内容来自输入源码及其既有修订，不能作为 r19 的编译、运行或画面验收结果。当前状态见 [VALIDATION_R19.md](VALIDATION_R19.md)，当前结构见 [ARCHITECTURE.md](ARCHITECTURE.md)。

# Scarlet Beat / ENCORE：r12

本轮使用用户提供的 xinghongsiwang.zip 和 GimmickExtras_20260908_224948.BWDEiK.zip。没有修改原曲包，也没有把曲包加入查看器发行包。

## 已核实并修改

原 VSM 展开为 13,238 个事件。音乐结束于 60.025011 秒，最后一个跨界事件来自第 19 行 `40:110:1,1,outElastic,5000,0,xoffset,-1`：第 110 拍开始，持续到第 111 拍，即 60.545455 秒。该事件正常保留；音频终点不会自动延长，兼容报告不再把跨曲尾当成问题。报告的 `eventTimeline.pastAudioTail` 保留行号、拍数与结束时间。CLI 导出仍可显式指定更晚的 `--end`。

第 6 行的 `fx_colorise` 没有注册。所提供 Custom Gimmicks v1.12.7 的 `updateMods` 对未注册 ID 直接跳过，所以查看器同样把这个已核实的名字作为空操作，并记录在 `sourceNoOps`；不修改 VSM、不把它冒充成强度参数。第 7、8 行真正的 RGB / intensity 控制继续生效。其它未知名字仍正常报告。

`filter-definitions.json` 中确实包含 `_filter_heathaze` 的原始定义。LBG 使用其中的速度 0.01 / 0.025、色差 0.5、噪声纹理等；谱面把两组 scale 改为 20、amount 改为 240。动态层来自原 InitDistortBG 的深度 700。LBG 与现有 blur / jacket / checker 分层保持一致。

三帧原始 checker PNG 与动态定义已保存在 `Assets/GimmickExtras`，体积约 20 KiB。没有 ASTELLION 歌曲素材。原图逐字节保留。

`encore_text.txt` 大小写匹配现在跨平台一致，本曲 85 条文字都能读取。Monaco 的原字形修复保留。

## 当前仍有 3 条 Notice

1. **房间关联**：曲名不是房间标识，现有包没有证明这首实际使用哪个房间。AUTO 仍明确报告回退行为。
2. **FX_underwater**：拿到的是滤镜默认定义，静态房间可以覆盖它；因此不把默认定义擅自当成本曲的实际房间参数。r12 已支持外部 profile 中的 `FX_underwater`，包括两组 amount 绑定、可见性和 depth。原房间参数到位后能通过现有入口读取。
3. **glow**：本次导出的 `missing` 明确包含 `shader_optimized_glow`。它在这份 macOS 数据中不存在。整屏 glow 仍用近似内核，独立粒子 glow 尚未绘制；合并为一条完整提示，不能以缺一个特定文件名来诊断所有平台。另修复了 `fx_glow` 从 1 淡到 0 时未被旧诊断识别的问题。

## 下一次只需补当前游戏的证据

运行新版 `scripts/assets/Dump_Gimmick_Extras_Mac.sh`。原先的导出器只搜一个 glow 名字、忽略 FX_underwater 图层，范围不够。新版按实际资源名收集：

- 名称包含 glow 的所有 shader（含 GameMaker 内置版本）；
- 所有玩法房间的完整 FX 参数、depth、可见性，及 glow 实例引用；
- 当前游戏中的 cc 初始化 / Begin Step、glow 对象、相关房间创建和 custom 对象代码；
- 棋盘原图与嵌入的动态 FX 默认定义。

游戏文件只读。输出安装在程序旁 `Assets/GimmickExtras`，同时生成一个 ZIP 供核对。把 ZIP 发回即可；这首曲包已经有了，不需再次上传。不要根据歌曲名直接选择 Scarlet Death 房间，那并不能证明自制曲实际使用该房间。

## 验证范围

实际曲包检查使用 `scripts/dev/check-scarlet-beat.py`。测试会比较渲染像素，验证移除空操作不会改变画面、LBG 确实改变背景、原三帧已载入、正常曲尾完整保留，并验证外部 underwater 的真实绘制和可见性。外部 underwater 的受控测试配置仅用于验证程序入口，不声称它就是原房间。

完整原游戏逐帧对照、macOS / Windows 本机 GPU 仍未完成。0 Notice 尚未达到；没有为了凑零而隐藏剩余实现缺口。

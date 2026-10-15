# Mono Full AOT 实验构建（r5）

使用 mono-nx rel-3 自带 Linux x64 → libnx ARM64 AOT 编译器，静态链接
27 个经过 ILLink 裁剪的程序集。运行时显式选择 `MONO_AOT_MODE_FULL`，没有解释器回退。
P/Invoke 保留现有静态 dlshim，不使用会引入缺失原生符号的 `direct-pinvoke`。

## 实机记录

- a1：进入托管代码，在创建 NullReferenceException 时发生 Mono 断言；尚无界面。
- a2：启用官方示例中的 `System.Resources.UseSystemResourceKeys`，完成启动、窗口、
  Vulkan、素材及 UI 初始化，随后 CoreLib 的 type 0 trampoline 池耗尽（默认 4096）。
  重发抓到完整日志；没有帧耗时数据，不能声称提速。
- a3：仅把 CoreLib 的 `ntrampolines` 增至 65536，原生启动日志标识 a3。
  托管程序集仍显示 a2（没有重新发布 IL）；第二次完整上传后用户报告 2162-0002、重启；未连接 nxlink 日志端，启动阶段待查。

日志见 `dist/nxlink-logs/aot-a2-retry-20261011.log`、`aot-a3-live-20261011.log`。a5 加载日志为 `dist/diagnosis/a5-mono-load.log`。
SD 日志是 `/switch/KuroakiGimmick/UserData/mono-aot.log`。

a4 仅用于本机准备：把检查点提前到 metadata/ICU 初始化前，尚未发送。
a5 默认关闭主程序自动 nxlink 日志连接，保留严格 Full AOT 和 65536 调用池；
早期日志写入 `UserData/aot-boot.log`，Mono 日志按行写入 SD 以减少 abort 时丢尾。
小型探针读取到的最新 fatal report 指向系统 bsdsocket（2162-0002），
与 a3 启动的关系未完全确认。a5 已成功进入 UI，加载谱面时因 CoreLib type 3
调用池用尽（512）退出。日志中 playing=False 的阶段曾记录 53–58 FPS，
尚无谱面播放性能数据；不能把此数值当作播放帧率。a6 将该池扩到 8192。
**a6 实机已成功加载并流畅播放 nh，用户反馈平均约 25 FPS；后续补音乐程序收回 59 个 playing=True 采样，帧率范围 13.8–40.3 FPS、中位 28.0；不同谱面时段/界面状态不能当作统一基准。**
音乐已按用户新指令补齐 `/switch/KuroakiGimmick/Songs/nh/music.ogg` 与 `/nh/music.ogg`，两处 SD 读回长度 11435201、CRC32 8ffdb8be 通过；第六版已重发，用户确认听到音乐；随后在 HUD 字体图集 PNG 解码时 NativeMemory.Alloc 抛 OutOfMemoryException。音频关联和解码已通过，播放稳定性未通过。日志在 `dist/nxlink-logs/log-probe-v2-20261011.log`。
如需恢复网络日志，原生 Makefile 支持 `KG_SWITCH_AOT_NXLINK=1`；当前默认 0。

nxlink 接收端本机端口为 28771，Switch NetLoader 为 192.168.2.3:28280。
本地构建的 nxlink 使用 `SO_REUSEADDR`，每次发送前释放旧接收进程并检查本机日志端口。
不要用空 TCP 连接探测 Switch NetLoader，这会触发文件名长度协议错误。

## 构建

在源码根目录执行，SDK 路径使用下列默认布局。Mac 负责 publish/ILLink，
Linux 容器负责 AOT 和原生链接。容器将源码根目录挂载至 `/work`。

```sh
dotnet publish KuroakiGimmick.csproj -c Release -p:KuroakiSwitch=true \
  '-p:InformationalVersion=v0.1.4 / 17.7.1 / Switch r5 Full AOT a2' \
  -p:BaseIntermediateOutputPath="$PWD/dist/switch-obj/" \
  -o dist/switch-aot-r5/input

kg_runtime="$PWD/dist/switch-deps/mono-nx/dotnet_runtime"
dotnet "$kg_runtime/artifacts/bin/Mono.Linker/Debug/net9.0/illink.dll" \
  -x "$kg_runtime/src/mono/System.Private.CoreLib/src/ILLink/ILLink.Descriptors.xml" \
  -x "$kg_runtime/src/mono/System.Private.CoreLib/src/ILLink/ILLink.LinkAttributes.xml" \
  -d dist/switch-aot-r5/input \
  -d "$kg_runtime/artifacts/bin/mono/libnx.arm64.Debug" \
  -d "$kg_runtime/artifacts/bin/runtime/net9.0-libnx-Debug-arm64" \
  --feature System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported false \
  --feature System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeCompiled false \
  --feature System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault false \
  --feature System.Resources.UseSystemResourceKeys true \
  --skip-unresolved false --trim-mode link \
  -a dist/switch-aot-r5/input/KuroakiGimmick.dll -out dist/switch-aot-r5/managed

docker exec kg-switch-build-20261010 bash -lc '
  export PATH=/opt/devkitpro/devkitA64/bin:$PATH
  python3 platform/switch/tools/compile_full_aot.py \
    --managed dist/switch-aot-r5/managed \
    --compiler dist/switch-deps/mono-nx/dotnet_runtime/artifacts/bin/mono/linux.x64.Debug/cross/linux-x64/libnx-arm64/mono-aot-cross \
    --output dist/switch-aot-r5/objects --jobs 3
  python3 platform/switch/tools/prepare_aot_payload.py dist/switch-aot-r5
  bash platform/switch/build-native-aot.sh
'
```

产物：`dist/switch-native-aot/KuroakiGimmick-FullAOT.nro`。
DLL 元数据内嵌于 ROMFS，启动时按 CRC 和长度校验后缓存至独立的
`/switch/KuroakiGimmick/AOT-<hash>`；不替换解释器 DLL 或用户歌曲。
仍需先安装原来的素材和 `/mono/etc/icudt77l.dat`。

编译器报告的 open generic template `gshared failure` 单独计数；其他跳过/失败拒绝打包。
严格 Full AOT 实机运行负责验证动态泛型和调用覆盖；成功链接不能代替这项测试。

## 其他待办

解释器实测谱面约 6–8 FPS（draw 125–159 ms，submit 约 5 ms）。
独立音频探针能出声，但结束时用户看到系统错误，退出清理尚待修复；a6 长时间稳定性和其他谱面尚未覆盖。
原生文件探针确认 SD 的 nh 目录没有 music.ogg；本机文件存在。
音乐补齐 NRO `dist/switch-probe/KG-Install-Music-r5.nro` 已准备，后续已发送并在两处目标通过写入及 SD 读回校验。
它给两份已确认的 nh 补齐提供的 music.ogg（已有则保留），写入前后检查长度与 CRC，
并已通过 nxlink 取回帧耗时日志。音频安装日志为 `dist/nxlink-logs/install-music-r5-20261011.log`。

## LLVM 分支（进行中）

原 SDK 试编译明确拒绝 --aot=llvm（runtime compiled with llvm support required）。
已在 dist/switch-llvm-toolchain/runtime 克隆 libnx 分支 commit 289cdaa5d288809891e3e2fbac82cd6e0cdcfcd7，
安装 Linux x64 .NET SDK 9.0.102 与原生编译依赖，开始构建 MonoAOTEnableLLVM=true 的交叉编译器及
ENABLE_LLVM_RUNTIME=1 的目标运行时。当前尚无成功生成并实机验证的 LLVM NRO。

原生内存诊断 a7 已生成，记录 NativeMemory 失败请求的字节数、errno 及每五秒两类堆用量；
用户要求先继续 LLVM、稍后测内存，因此 a7 尚未发送。
最新音频失败日志 dist/diagnosis/a6-mono-audio-failure.log；LLVM 试编译日志 dist/switch-llvm-trial/trial.log，
构建日志 dist/switch-llvm-trial/llvm-runtime-build.log。

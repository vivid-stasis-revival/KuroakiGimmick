# SDL_GPU 后端

窗口、绘制、特效和导出使用同一套 SDL_GPU 后端。macOS 选择 Metal，Windows 选择 Direct3D 12；不再创建 OpenGL context，也不再加载 OpenGL 函数。当前平台选择仍按 `Native/Sdl.cs` 限定为 macOS / Windows。

运行与验证：

```bash
dotnet run
dotnet run -- --self-test
dotnet run -- --gpu-test
dotnet run -- Samples/ImageGimmicks/ImageGimmicks.sgv.json --smoke-ui --out /tmp/ui.ppm
```

`Native/Sdl.Gpu.cs` 补齐纹理、采样器、着色器、管线、render/copy pass、transfer buffer 和 fence 的 C ABI。`Graphics/GpuDevice.cs` 保存绘制批次，每次提交前统一上传顶点，逐批次保存 uniform、采样器和裁剪状态。显式窗口提交处理交换链不可用的情况；隐藏窗口的截图和导出只提交离屏目标。资源销毁和目标缩放会先提交引用它们的批次。

`Graphics/ShaderCompiler.cs` 保留原 GameMaker GLSL 表达式，仅调整接口与 uniform 声明，经 shaderc 编译成 SPIR-V，再由 SPIRV-Cross 转换。Metal 使用 MSL；Windows 使用 HLSL Shader Model 5.1，再调用系统 `d3dcompiler_47.dll` 生成 DXBC。只向 SDL 声明实际提供的 MSL / DXBC 格式。编译结果在进程内缓存；内置和外部对象特效走相同路径。当前 uniform 表面覆盖已有的 float/int/bool、vec2/3/4 和 sampler2D；不支持的额外声明会明确报错。

编译器原生库通过固定版本的 `Silk.NET.Shaderc.Native`、`Silk.NET.SPIRV.Cross.Native` 2.23.0 随 NuGet 恢复和发布，包含 Mac / Windows x64、ARM64。无需用户安装 shaderc、SPIRV-Cross、Vulkan SDK 或 HLSL 命令行工具。许可证见 `ThirdParty` 和 `THIRD_PARTY_NOTICES.md`。

SDL_GPU 的纹理、裁剪和读回统一使用左上原点。混合保留源 alpha、目标颜色相乘、反向目标颜色等既有行为。视频导出仍向 FFmpeg 写入从 GPU fence 完成后读取的 RGBA 数据。读回按 256 字节行距下载后去除填充。当前 SDL 3.4.16 的 Metal 上传实现忽略 `pixels_per_row`，因此 Metal 上传使用紧密行距，D3D12 上传使用 256 字节行距，避免小尺寸精灵丢行。

## 2026-09-09 本机验证

- `dotnet build`：0 警告、0 错误；`dotnet run` 正常进入 Metal 窗口循环。
- CPU 自测：133 项通过。
- GPU 自测：23 项通过，包含 24 个 ABI 结构的大小、奇数宽纹理上传与读回、连续提交、裁剪与 alpha、uniform / 多纹理状态、目标缩放、16 个内置 shader 的 Metal 管线及 HLSL 转换。macOS ARM64 发布目录也通过同一组测试。
- 示例工程 `--smoke-ui`：快捷键、步进、导出标记通过；截图已检查文字、图标和场景方向。
- `check-original-glow.py`：6 项通过；`check-room-fx.py`：17 项通过；`check-native-gimmicks.py`：14 项通过。
- 示例视频：实际导出 H.264 MP4，320×180、30 fps、15 帧、0.5 秒；FFprobe 核对通过。
- macOS ARM64、Windows x64、Windows ARM64 的 framework-dependent 发布成功，发布目录包含 SDL3、shaderc 和 SPIRV-Cross 原生库。Windows 上的 DXBC 编译与 D3D12 设备运行尚未实机验证；Mac 上的 HLSL 转换及再次编译检查不等同于 Windows 运行验证。

本机 12 组新旧后端对照全部通过，测试帧逐像素一致。详细日志保存在 [本次验证目录](validation/sdl-gpu-20260909)。

`check-gpu-migration.py` 可将迁移前程序集与当前程序集在相同素材下做 12 组场景对照，覆盖音符、透明度、旋转、proxy、灰度、扭曲、posterise、色彩、glow 和原生后处理：

```bash
python3 scripts/check-gpu-migration.py /path/to/baseline/KuroakiGimmick.dll bin/Debug/net8.0/KuroakiGimmick.dll
```

旧测试中的已知基线问题：`check-rendering.py`、`check-font-checker.py`、`check-game-ui.py` 将旧白色音符皮肤的像素写死；当前素材的同一像素已是彩色。`check-gimmicks.py` 的一项断言假定缺少 FX_chroma 层，但当前自动房间配置提供了该层。这四处失败均使用迁移前源码和当前相同素材复现，不作为本次后端迁移新回归；测试断言未被放宽。

参考：[SDL GPU 概览](https://wiki.libsdl.org/SDL3/CategoryGPU)、[shader 资源绑定约定](https://wiki.libsdl.org/SDL3/SDL_CreateGPUShader)、[SDL 3.4.16 Metal 实现](https://github.com/libsdl-org/SDL/blob/release-3.4.16/src/gpu/metal/SDL_gpu_metal.m)、[shaderc](https://github.com/google/shaderc)、[SPIRV-Cross](https://github.com/KhronosGroup/SPIRV-Cross)。

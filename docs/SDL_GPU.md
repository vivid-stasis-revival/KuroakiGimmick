# r19 的 SDL_GPU 后端边界

本次直接保留输入 `KuroakiGimmick-src-SDL_GPU-20260909-133143.zip` 的 GPU 实现，没有退回 OpenGL，也没有另行改选 Vulkan 或其他渲染器。平台选择仍为 macOS Metal / Windows Direct3D 12。

源码位置已移动到 `src/Graphics/SdlGpu/` 与 `src/Native/Sdl.*.cs`。设备提交、资源传输、2D 几何和截图各自分文件；shader 的表示、接口转换和 native 工具调用分文件。详细责任表见 [ARCHITECTURE.md](ARCHITECTURE.md)。

## 保留的实现

- 原 SDL P/Invoke 参数、结构体字段及布局；GPU 设备和窗口创建/销毁时序。
- GLSL 经 shaderc 到 SPIR-V，经 SPIRV-Cross 到 MSL / HLSL 5.1，再由系统 D3DCompiler 生成 DXBC 的原调用链。
- 每批次独立保存 uniform、texture sampler、scissor 和目标状态；8-float 顶点与 16-byte uniform 槽。
- 原 Metal / D3D12 上传行距分支、256-byte 对齐读回和去除填充。
- swapchain acquire/submit 与离屏路径；目标缩放、资源释放前的原 Flush/Submit。
- GPU 自测的原测试内容，以及 NuGet 依赖和 `packages.lock.json`。

`check-input-preservation.py` 对 13 个后端类型的 272 个成员进行 token 对照，忽略注释、空白、partial 外壳和为控制流补全的大括号；literal、运算符、字段表达式和方法体 token 保持。这是局部语法契约，不是完整 C# 编译、生成 IL、ABI 或实际设备验证。

## 真实运行步骤

```bash
bash scripts/verify.sh --gpu
```

使用同一份资源分别编译输入版本与本次版本后：

```bash
python3 scripts/dev/check-gpu-migration.py /path/baseline/KuroakiGimmick.dll bin/Release/net8.0/KuroakiGimmick.dll
python3 scripts/dev/compare-scenes.py /path/baseline/KuroakiGimmick.dll bin/Release/net8.0/KuroakiGimmick.dll \
  /path/ENCORE.vsb --times 0 45 184.11 191.79 --out /tmp/r19-comparison-new
```

第一条是原有 12 组后端场景 harness；第二条实际使用指定谱面在相同秒数比较。GPU 自测通过也不能替代完整曲包的行为验收。当前环境两者均未执行。

原输入的 GPU 验证说明保存在 `history/input-sdl-gpu/SDL_GPU.md`，旧日志仍在 `validation/sdl-gpu-20260909/`。其中的通过数量和逐像素一致只属于输入历史，不是 r19 结果。当前记录见 [VALIDATION_R19.md](VALIDATION_R19.md)。

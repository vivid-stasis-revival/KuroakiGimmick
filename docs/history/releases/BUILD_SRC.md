# 构建 v0.1.2 / 16.2

需要 .NET 8 SDK。macOS：`bash scripts/build-mac.sh`；Windows：`scripts/build-windows.ps1`。
先编译并执行 CPU/IO 测试：`bash scripts/verify-image-objects.sh` 或 `scripts/verify-image-objects.ps1`。
应用版本 0.1.2，Build 16.2。当前交付不含预编译应用，制作环境未执行实际 C# 编译或 UI 验证。

操作见 docs/IMAGE_OBJECTS_16_2.md，示例 Samples/ImageObjects162/demo.sgv.json。

---

## 历史构建说明

# v0.1.2 / 16.1 构建

增量更新后仍从项目根目录运行 `bash scripts/build-mac.sh`；Windows 使用 `scripts/build-windows.ps1`。
使用 `bash scripts/verify-layout-image.sh` 或 `scripts/verify-layout-image.ps1` 先编译再执行新增 CPU/IO 测试。这里的验证脚本依赖 .NET 8 SDK，不会用模拟发布器代替编译。
新版运行目录及应用元信息标记为 v0.1.2 / 16.1。布局与图片导入的操作见 `docs/LAYOUT_IMAGES_16_1.md`。

---

## 通用构建说明

# 当前构建：v0.1.2 / 16.0

macOS：`bash scripts/build-mac.sh`。Windows：`scripts/publish-windows.ps1`。

新增 CPU 测试：`dotnet run -c Release -- --authoring-self-test`。
发布文件名、应用版本及 CLI `--version` 使用 v0.1.2 / 16.0。

以下保留既有构建参数说明。

---

# r20 完整源码构建

本次输入是 `KuroakiGimmick-src-SDL_GPU-20260909-133143.zip`。SDL_GPU 执行实现保留，项目入口仍是根目录 `KuroakiGimmick.csproj`。交付仅包含源码与原有资源，不包含重新编译的程序。

## 本机验证后启动

需要 .NET 8 SDK，不是仅安装 Runtime。解压到独立目录，在项目根目录执行：

```bash
bash scripts/verify-refactor.sh --gpu
dotnet run -c Release --no-build
```

Windows：

```powershell
.\scripts\verify-refactor.ps1 -Gpu
dotnet run -c Release --no-build
```

包装脚本实际依次运行 `restore --locked-mode`、`build -c Release --no-restore`、编译产物的 `--self-test`；加 GPU 参数再运行 `--gpu-test`。任一步失败即停止，不运行旧二进制冒充当前版本。未指定 GPU 参数会明确打印 SKIP。

CPU 测试不创建 GPU；GPU 测试必须在 macOS Metal 或 Windows Direct3D 12 本机运行。无 GPU 的 CI 可以只运行 CPU 测试，但不能据此宣称画面正确。当前 Linux 只适合有 SDK 时的构建/CPU 自测，不支持本项目的交互 GPU 窗口。

## 发布

保留原上传版本的架构/发布参数，只更新输出修订标记为 r20。

```bash
bash scripts/build-mac.sh arm64
bash scripts/build-mac.sh x64
bash scripts/build-mac.sh all

# 在 Mac 或其他有 SDK 的机器交叉发布 Windows 源码：
bash scripts/publish-win.sh x64
bash scripts/publish-win.sh arm64
```

Windows：

```powershell
.\scripts\build-windows.ps1 -Arch x64
.\scripts\build-windows.ps1 -Arch arm64
```

输出在 `dist/`，保留整个 Assets 目录和依赖库。对象行为 JSON 同时内嵌与外置：内嵌只兜底新增定义/目录表缺失，不包含外部曲包素材。发布后必须检查 native 库、实际 shader 编译、播放、重载、导出和资源路径；交叉发布成功不等于在目标 GPU 上运行成功。

FFmpeg 单独提供；首次恢复需联网。当前交付环境没有 SDK，因此以上编译、发布和运行命令均未在此执行成功。具体状态见 `docs/VALIDATION_R19.md`。`SOURCE_CONTENT_SHA256.json` 记录最终源码文件哈希，不含该清单本身；`tests/Fixtures/input-sdl-gpu-contract.json` 是输入后端/资源保留契约，二者用途不同。

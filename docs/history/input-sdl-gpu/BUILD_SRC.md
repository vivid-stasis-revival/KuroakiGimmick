# 完整源码编译说明

本包为 2026-09-09 SDL_GPU 对接后的完整源码快照，包含现有 Assets、Samples、依赖锁定文件、许可证和全部 scripts。Mac 使用 Metal，Windows 使用 Direct3D 12。首次构建需要联网恢复 NuGet；安装 .NET 8 SDK（仅 Runtime 不够）。

请先解压，在包含 KuroakiGimmick.csproj 的目录执行：

```sh
dotnet restore --locked-mode
dotnet run -c Release
```

SDL3、shaderc、SPIRV-Cross 原生依赖自动恢复，无需单独安装。视频导出需要 FFmpeg，可放入 PATH，或用 KUROAKI_FFMPEG 指定路径。

## Mac 编译并打包可分发应用

```sh
bash scripts/build-mac.sh arm64
# Intel Mac：
bash scripts/build-mac.sh x64
# 两种架构：
bash scripts/build-mac.sh all
```

## Windows 编译并打包（PowerShell）

```powershell
.\scripts\build-windows.ps1 -Arch x64
# ARM64：
.\scripts\build-windows.ps1 -Arch arm64
```

也可在 Mac 上交叉构建 Windows 包：

```sh
bash scripts/publish-win.sh x64
bash scripts/publish-win.sh arm64
```

以上发布脚本生成 self-contained 应用及 ZIP，输出到解压目录内的 dist/，不会覆盖旧包。分发时保留完整资源目录。Windows 后端已交叉构建，D3D12 实机验证仍需 Windows 环境。

## 验证

```sh
dotnet run -c Release -- --self-test
dotnet run -c Release -- --gpu-test
```

GPU 自测需要支持对应后端的本机 GPU。已有验证与旧测试的基线问题见 docs/SDL_GPU.md。SOURCE_CONTENT_SHA256.json 记录本源码包所有文件的 SHA-256（不含该清单本身）。

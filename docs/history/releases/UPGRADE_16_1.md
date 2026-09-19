# v0.1.2 / 16.0 → 16.1 升级

这是源码覆盖包，不是已编译程序。基线是本对话最后提供的 16.0 完整源码。
请先退出旧应用并备份本地改动，在包含 KuroakiGimmick.csproj 的项目根目录执行：

```bash
unzip -o ~/Downloads/KuroakiGimmick-v0.1.2-16.0-to-16.1-update.zip
bash scripts/build-mac.sh
```

Windows：覆盖后运行 `scripts/build-windows.ps1`。构建完成后使用 dist 中新生成的 16.1 程序，保留配套 Assets。

LAYOUT 提供 UI 缩放和可拖动分隔条；Editor 的 `+ IMAGE / DROP IMAGE` 与拖入图片入口创建 VSP 实例和图片属性事件。完整说明见 `docs/LAYOUT_IMAGES_16_1.md`。

本包没有字体、原游戏素材或额外原生 DLL。保留 16.0 的导出、E 标记、文档右键添加和窗口移动实现。

验证了源结构、离线数学映射、模拟发布和差异包覆盖。当前环境缺少 .NET SDK，未执行 C# 编译、C# CPU 自测或 macOS/Windows 原生交互。可在本机执行 `bash scripts/verify-layout-image.sh` 或 `scripts/verify-layout-image.ps1` 先构建再跑测试。

单独导出 VSM/config 不包含 VSP 和图片；收集图片请选择 Chart Folder。新图默认居中出现，没有新增画布拖拽手柄；支持静态 PNG/JPEG/BMP/TGA。

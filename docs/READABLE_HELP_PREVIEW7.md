# preview.7：说明卡字体与可读性修复

本次从 preview.6 修改，保留原来的悬停简介和按住 W 约 220ms 的详细说明交互。

## 修改范围

| 项目 | preview.7 |
|---|---|
| 字体 | Noto Sans CJK SC 比例无衬线字体，不是 Noto Sans Mono CJK SC |
| 字重 | 正文 Regular，标题使用同一家族的 Bold |
| 正文 | 17 逻辑单位，29 行距 |
| 简介 | 16 逻辑单位，27 行距 |
| 标题 | 22 逻辑单位，真实粗体 |
| 背景 | 不透明 #15161C，避免游戏画面透过说明 |
| 正文颜色 | #F0F2F7，不再使用旧的低对比度灰色 |
| 排版 | 按字体实际 Advance 自动换行；高度随内容计算；过长时按住 W + 滚轮翻阅 |
| 覆盖范围 | 仅说明卡；Viewer、轨道名、游戏内字体不变 |

字体图集以 64px em 生成，字形具有自己的轴承和 Advance。实际绘制与测量均使用 size/EmSize；不再使用旧图集的 size/32 约定。PNG 的透明度保存抗锯齿覆盖，缩小时仍使用项目现有 mip/线性采样路径。

两个字重各含 845 个字形，覆盖当前说明卡源码和可打印 ASCII。这不是完整 Unicode 字库。自定义名称含有图集外字符时会打印 U+xxxx 缺字日志，并显示同字体的问号，不静默拼接另一字体。

## 覆盖与构建（已有 preview.6）

先退出正在运行的旧程序，在项目根目录覆盖：

```bash
cd ~/KuroakiGimmick-v0.1.1
unzip -o ~/Downloads/KuroakiGimmick-v0.1.1-preview.6-to-preview.7-readable-font-hotfix.zip
./scripts/publish-mac.sh
```

启动本次 dist 中新生成的 editor-preview.7 应用，不要继续打开旧版本。发行目录中的 Assets 必须跟随应用一起保留。

正常加载日志：

```text
[font] Readable help: Noto Sans CJK SC Regular / Bold; 845 glyphs; 64px em (editor-preview.7)
```

若出现 `Readable help unavailable`，说明卡底部也会提示字体资源缺失。检查以下四个文件是否同时在源码和发布 Assets/Fonts 目录：

```text
editor-help-sans.png
editor-help-sans.json
editor-help-sans-bold.png
editor-help-sans-bold.json
```

系统不需要安装新字体，也没有新增 NuGet/native 依赖。此包仅附生成的位图和度量，不附 TTF/TTC/OTF 字体文件。维护者重新生成图集时可使用 `scripts/dev/build-editor-help-atlas.py`，其 Pillow/fontTools 依赖不影响普通 .NET 构建。

## 验证边界

已执行离线字体图集、源码结构、原资源保留与热修覆盖验证。另使用新图集/度量绘制了 1×/2× 排版示意图，检查文字未截断；这些是 Pillow 离线示意，不是应用运行截图，也不是 SDL_GPU 输出证明。

当前环境没有可用的 .NET SDK。C# 编译、C# CPU 测试和 macOS/Windows 的实际渲染、DPI 与滚轮交互未执行。随包 C# `HelpTypographySelfTest` 需在本机运行后才可算通过。

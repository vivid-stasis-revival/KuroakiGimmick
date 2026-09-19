# KuroakiGimmick v0.1.2 / 16.2

图片对象编辑：按实例分组已有 img* 事件，IMAGES 列表、独立本地 IMAGE CANVAS、初始/定时姿态、移动缩放旋转、成组起终点动画、路径连接与资源替换。保留 16.1 主题、固定说明卡层级、布局、保存与导出功能。

[操作与范围](docs/IMAGE_OBJECTS_16_2.md) · [实际验证](docs/VALIDATION_16_2.json)

构建：`bash scripts/build-mac.sh`；Windows：`scripts/build-windows.ps1`。
真实 C# CPU/IO 测试：`bash scripts/verify-image-objects.sh`。示例：`Samples/ImageObjects162/demo.sgv.json`。

交付为源码，当前制作环境未完成 C# 编译/自测及原生图形验证。图片画布明确采用本地坐标，不模拟 Proxy 和后处理的反向变换；SCENE 与游戏/视频渲染保持原语义。

---

## 历史版本记录

# KuroakiGimmick v0.1.2 / 16.1

新增工作区布局、UI 缩放和 Editor 静态图片拖入。保留 16.0 的导出、时间标记、文档右键添加和 UI 配色。

- Viewer/Editor 顶部 LAYOUT：缩放 UI、调整侧栏、预览与时间轴、轨道名称栏。
- Editor 拖入 PNG/JPEG/BMP/TGA：创建 VSP 图片实例和定时 VSM 参数，支持撤销、伴随资源保存与 Chart Folder 导出。
- [操作与范围](docs/LAYOUT_IMAGES_16_1.md)。本包为源码升级，不包含预编译程序；当前环境未执行 C# 编译和原生交互验证。
- 本机运行 `bash scripts/verify-layout-image.sh` 构建并执行 CPU/IO 自测；发布使用 `bash scripts/build-mac.sh` 或 `scripts/build-windows.ps1`。

---

## 16.0 及以前版本说明

# KuroakiGimmick v0.1.2 / 16.0

新增三种 Chart EXPORT、任意 gimmick 添加、E 时间标记、F1 右键添加，并提升界面对比度。字体、原游戏图像、SDL_GPU 场景渲染与 ExtCustomGimmick 原件不变。

[16.0 操作说明与导出边界](docs/EXPORT_AUTHORING_16.md)

```bash
bash scripts/build-mac.sh
# 有 .NET 8 SDK 时运行新增的 CPU 回归测试：
dotnet run -c Release -- --authoring-self-test
```

当前交付为源码，不是已验收的应用二进制。C# 编译与原生交互未在制作环境执行；实际执行过的离线检查见 `docs/VALIDATION_16.json`。

下面保留旧版本说明，其中版本、验证状态和旧版限制属于历史记录，以本节及 16.0 操作说明为准。

---

# KuroakiGimmick v0.1.1 · Editor Preview 9

## 文档阅读界面与 UI 过渡

F1 改为原生离线文档阅读界面：章节导航、搜索、排版正文、可点击参数表、可复制代码块、本页目录及浏览历史。说明卡和 F1 去掉自动附加的编辑器说明、原文标签与整篇源文档模式。既有参数、模板、单位和适用条件保留。

公共按钮、工作区、对话框、说明卡、面板和滚动加入短时过渡；`Viewer UI → SETTINGS → UI transitions` 可以关闭。精确拍点、播放游标、直接拖动、歌曲求值和视频导出不使用这些缓动。

详细说明见 [Preview 9 操作说明](docs/DOCS_UI_PREVIEW9.md) 与 [验证记录](docs/VALIDATION_V0.1.1.md)。字体与游戏素材保持 preview.8 的原始字节。当前包未完成 C# 编译与原生实机验证。

基于 v0.1.0-15.3 的源码试用版：保留 Viewer，增加 Viewer/Editor 切换、多轨道事件编辑、音符时间参考、撤销重做、安全保存副本，以及 ExtCustomGimmick 窗口编舞适配。

**本包尚未完成 C# 编译和 macOS/Windows 实机验证。** 制作环境未提供可用的 .NET SDK；包中的静态检查结果不能替代运行测试。先用示例测试，再编辑重要歌曲的副本。

快速试用：macOS 运行 `bash Try_Editor_Mac.command`；Windows PowerShell 运行 `./Try_Editor_Windows.ps1`。需要 .NET 8 SDK，脚本先构建再打开示例，构建失败立即停止。

- [编辑器操作与保存规则](docs/EDITOR_V0.1.1.md)
- [窗口移动、内容绑定与限制](docs/WINDOW_MOVEMENT_V0.1.1.md)
- [实际验证记录](docs/VALIDATION_V0.1.1.md)

示例：`Samples/EditorDemo/demo.sgv.json` 和 `proxy.sgv.json`。窗口扩展原包保存在 `Integrations/ExtCustomGimmick/`，不是直接载入 SDL_GPU 的原生插件。

---

## 原 Viewer 文档（保留）

# KuroakiGimmick v0.1.0 · r20 Windows UI antialiasing

> r20 基于 r19 的 SDL_GPU 数据化重构，专门修复 Windows 低/中 DPI 下 UI 位图字体缩小时的锯齿。游戏预览的 NEAREST 采样保持不变。详见 `docs/WINDOWS_UI_ANTIALIASING.md`。


C# + SDL3 GPU 的 gimmick 预览器。读取 `.vsb` / `.vsc` 谱面、`.vsm` 演出、`.vsp` 图片声明与 `.sgv.json` 工程，在同一条场景管线中预览和导出视频。

**本包是基于 `KuroakiGimmick-src-SDL_GPU-20260909-133143.zip` 的完整重构源码候选，不是已经编译验收的运行包。** 当前交付环境没有 .NET SDK，也没有可运行该后端的 Metal / Direct3D 12 设备；已执行的源码、数据与脚本检查见 [本次验证](docs/VALIDATION_R19.md)。原输入包的运行记录只保留为历史资料。

## 开始使用

在新目录解压，不与旧版源文件或 `bin/obj` 混合。安装 .NET 8 SDK，首次构建需要恢复锁定的 NuGet 依赖。

```bash
bash scripts/verify-refactor.sh
# 在支持后端的本机额外执行 GPU 自测：
bash scripts/verify-refactor.sh --gpu
# 验证成功后启动：
dotnet run -c Release --no-build
```

Windows PowerShell：

```powershell
.\scripts\verify-refactor.ps1 -Gpu
dotnet run -c Release --no-build
```

保留根目录 `KuroakiGimmick.csproj` 与原依赖版本、锁文件。后端沿用本次输入的选择：macOS 使用 Metal，Windows 使用 Direct3D 12；当前没有 Linux GPU 后端。SDL3、shaderc、SPIRV-Cross 随依赖恢复。Ogg/Vorbis 预览内置解码，其他音频格式和视频导出使用 FFmpeg。

编译与发布参数见 [BUILD_SRC.md](BUILD_SRC.md)。这次没有生成 macOS 或 Windows 可执行运行包。

## 本次结构

```text
src/
├── App/                    CLI 与启动入口
├── Core/
│   ├── Models/             谱面、事件、工程模型
│   ├── Parsing/            VSB / VSC / VSM 与扩展编号解码
│   ├── Definitions/        通用对象定义、表达式、约束校验
│   ├── Assets/             元数据、资源发现、兼容迁移
│   ├── Timing/             BPM、随机访问时间轴、回调、粒子
│   ├── Projects/           会话装配、工程路径、兼容报告
│   ├── Audio/              解码与播放时钟
│   └── Export/             固定帧视频导出
├── Graphics/
│   ├── SdlGpu/             GPU 批次、提交、传输、shader 编译
│   ├── Primitives/         Rect、Color、BlendFactor
│   ├── Scene/              合成、轨道、资源、普通粒子
│   ├── Effects/            通用对象与公共特效
│   ├── Notes/              音符皮肤
│   └── Text/               字体与游戏 HUD
├── Native/                 SDL / shader 工具 ABI、Host
└── UI/                     输入、布局、设置、绘制
Assets/Catalog/             原版关联表与旧配置转换模板
Assets/Gimmicks/            对象行为数据
Samples/ReusableObject/     不依赖曲名的最小对象示例
tests/                     CPU/GPU 自测与输入保留契约
```

大类按职责拆成 `partial` 文件，但没有增加空项目或改变 GPU 执行路径。原命名空间尽量保留；目录分层不等于破坏外部调用方。设计边界见 [ARCHITECTURE.md](docs/ARCHITECTURE.md)。

## 不再为每首歌添加专用加载器

对象名只参与定义查找。加载、时间轴、shader 和绘制代码不识别 ASTELLION 名称，也不存在 `Astellion = AstellionLoad` 一类别名分派。

```text
manifest 数据 → 通用解析/验证 → 资源 profile
             → 时间轴/绘制调度 → 通用对象渲染 → SDL_GPU
```

BPM、VSB 扩展 mod 表、默认参数、回调绘制、逐帧表达式、粒子和 shader 绑定都属于配置。ASTELLION 的旧 `astellion/manifest.json` 继续作为曲包资源描述读取；名称保留在 JSON 路径中，而不是专用 C# 类中。Scarlet 旧 manifest 缺省的颜色控制字段按兼容语义处理，显式关闭仍然有效。

已有能力范围内可以只新增对象定义；真正的新绘制能力仍需要扩展通用组件，不声称能够执行任意 GML 或完整模拟 GameMaker。格式、优先级、单位和示例见 [OBJECT_DEFINITIONS.md](docs/OBJECT_DEFINITIONS.md)。

```bash
dotnet run -c Release --no-build -- Samples/ReusableObject/ENCORE.vsc
```

## 打开谱面与保存工程

`OPEN CHART / VSM` 接受谱面、VSM 或工程；拖入歌曲目录会按原关联规则选择谱面及同名 VSM/VSP。`+ FILES / RESOURCES` 附加图片声明、音频、曲绘、FX 配置。原始曲包不会被重写。

同难度 VSM/VSP 优先，缺失时可使用 GLOBAL 文件。VSC 音符时间以毫秒计，VSM 演出以拍计。图片路径相对谱面或声明文件的目录，保存工程会转换为相对路径。按 `R` 事务式重载，保留可用会话直到新会话构建成功。

自带的音符、bumper、轨道、字体图集和 shader 资源沿用输入包。需要额外游戏资源的曲包仍须提供对应素材；没有将 ASTELLION 歌曲与剧情音频打包进应用。

## 命令行与诊断

```bash
dotnet run -c Release --no-build -- --inspect /path/ENCORE.vsb --time 184.11
dotnet run -c Release --no-build -- --snapshot /path/ENCORE.vsb \
  --scene --time 184.11 --out frame.ppm --report frame.json --strict

dotnet run -c Release --no-build -- --render /path/project.sgv.json \
  --start 30 --end 45 --fps 60 --width 1920 --out clip.mp4 --strict
```

`--gimmick-definition /path/gimmick-object.json` 可以显式指定对象定义；`--profile auto|core|<manifest-alias>` 控制自动加载、基础模式或配置声明的别名。`--room`、`--fx-profile`、`--audio`、`--bpm`、`--offset`、`--render-width` 等原有参数保留，完整列表用 `--help` 查看。

报告采用 `reportSchemaVersion = 2`，对象信息集中在 `nativeGimmick`。记录定义来源、实际 Assets 根目录、迁移记录、资源错误、颜色控制及逐时刻 FX 状态。CPU `--inspect` 不能证明 shader 可以在 GPU 上编译；截图加 `--report` 才包含本帧绘制后诊断。`--strict` 遇到错误诊断返回非零退出码，不把“截图文件存在”当作验收通过。

## 预览与视频

预览和导出共用场景合成，逻辑空间为 320×180。RenderWidth 控制离屏像素尺寸，不能生成原图不存在的细节。导出继续使用原速音频和固定帧时钟，不继承预览倍速或监听音量。IN / OUT 以秒计。

FFmpeg 可在 PATH 中提供，或通过 `KUROAKI_FFMPEG` 指定。使用界面 EXPORT 或 CLI 输出 H.264/AAC MP4；已有目标文件不应被覆盖，取消时清理未完成输出。分发前须在目标系统实际验证编码器和 GPU。

空格播放/暂停；方向键逐帧；Shift + 方向键逐秒；Home/End 到两端；I/U 设置导出范围；N/P/M 切换音符/后处理/静音；F/ESC 全屏或退出；Ctrl/⌘ + O/S/E 打开、保存、导出。

## 回归检查

源码保留检查可以在没有 .NET 的环境运行，但不是编译或 GPU 验证：

```bash
python3 scripts/check-source.py
python3 scripts/check-input-preservation.py --baseline /path/KuroakiGimmick-src-SDL_GPU-20260909-133143.zip
```

构建新旧两版后按相同时间比较，不能用两张不同秒数的截图断定颜色或轨道等价：

```bash
python3 scripts/compare-scenes.py \
  /path/baseline/KuroakiGimmick.dll bin/Release/net8.0/KuroakiGimmick.dll \
  /path/ScarletDeath/ENCORE.vsb --times 184.11 191.79 --out /tmp/scarlet-r19-new
```

输出目录必须不存在。脚本真正调用两个程序生成同帧图像、候选帧诊断与差异图；失败返回非零。默认阈值不是逐像素一致标准，严格零差异用 `--max-error 0 --mean-error 0`。

旧功能说明与原始 README 保留在 [历史资料](docs/history/README.md)，不得把旧版日志当作 r19 通过记录。

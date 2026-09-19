# v0.1.2 / 16.2

- 新增图片对象分组、原始参数展开、IMAGES 列表与 IMAGE/EVENT Inspector。
- 新增本地图片画布：移动、八点缩放、旋转、微调、比例锁定和坐标映射。
- 初始姿态、指定时间姿态、运动起终点和完整路径偏移分别编辑。
- 成对 XY/缩放事件可以统一拖动、改时长、复制、删除、撤销。
- MOVE、SCALE、ROTATE、FADE IN/OUT、POP IN 片段与 CONTINUE 多段路径。
- 识别已有 VSP 图片；资源替换保留其他实例、逻辑尺寸和已有动画。
- 拖放初始坐标、保存/重开/Chart Folder 资源引用继续使用标准 VSM/VSP。
- 保留复杂动态/重复/独立轴事件，拒绝不明确的覆盖和逆变换。
- 集成 16.1 的主题与说明卡最终层级修复，未改变字体和游戏素材。

---

# v0.1.2 / 16.1

- 增加 Viewer/Editor 工作区缩放、侧栏和预览/时间轴分隔条、轨道名称栏宽度设置。
- 使用同一逻辑坐标映射处理 DPI、UI 绘制和鼠标输入。
- 增加 Editor 静态图片拖入/选择，生成 VSP 实例和定时图片参数。
- 将 VSP 与 VSM 纳入同一撤销状态；保存私有导入资源并更新 Chart Folder 导出。
- 补充布局/图片 CPU/IO 自测入口和升级包校验。

# v0.1.2 / 16.0

- 新增 EXPORT：VSM、VSM + cgmk config、Chart Folder。导出包含未保存编辑，保护源文件和已有输出。
- 新增可搜索的添加表单，允许未收录的原始 gimmick 名称；支持图片/字幕模板实例化与显式对象切换。
- 新增 E 时间标记、选择添加位置、重命名和撤销重做，随编辑工程保存。
- F1 导航条目、参数表和条目标题支持右键添加至当前谱面。
- 提高 Viewer、Editor、文档和时间轴的文字/边线对比度，保留已有字体及游戏渲染。
- 发布脚本统一版本号；不再强制要求中文原文文件名，也不再枚举失效的旧版操作说明。

---

## v0.1.1 editor-preview.8

- 导入用户提供的 VSM / Custom Gimmick 原始文档，生成带来源和行号的只读索引。
- 悬停/长按 W 优先使用文档说明；支持图片、字幕、lane 和 twirl 动态名称模板。
- 新增 F1 / VSM DOCS 离线手册：分类、全文搜索、章节、带行号原文、复制源行和独立滚动。
- 保留文档空白和冲突，以单独提示显示；不根据手册自动修改执行规则或注册别名。
- 扩充 preview.7 同一家族 Regular/Bold 位图字符覆盖；不改变字体与字号。
- 添加独立文档/字符覆盖检查及未执行的 C# CPU 自测。未完成 C# 编译或平台验证。

# v0.1.1 · editor-preview.7

- 更换 gimmick 悬停/长按 W 说明卡的字体：Noto Sans CJK SC 比例黑体，不再使用 Mono 等宽版本。正文、英文标识符、数字和中文使用同一家族；标题使用其真实 Bold 字重。
- 正文字号由 11 改为 17，标题为 22；独立 64px em 图集使用显式 Advance/OffsetX/OffsetY，不再通过旧 32px 格子缩小。
- 说明卡改为不透明深色背景、高对比度正文；长句按实际字宽自动换行，卡片按内容伸展并限制在窗口内。
- 过长说明可按住 W 使用滚轮翻阅；松开 W 收起。移除五行截断，原始帮助文本和 VSM 语义不变。
- 仅影响编辑器说明卡。Viewer、轨道原始名字、游戏内字体、Note/gimmick/window 渲染未重写。
- 发布脚本检查四项新字体资源是否齐全；启动日志标明比例字体和 editor-preview.7。
- 验证：离线字体/资源/源码检查和排版示意图已执行；未执行 C# 编译或 SDL_GPU/macOS/Windows 实机验证。

---

# v0.1.1 · editor-preview.6

- preview.6：gimmick hover / 长按 W 说明卡改为整卡统一使用 Noto Sans Mono CJK SC 栅格字形；中文、英文 identifier、数字和标点不再逐字符混用 DejaVu/CJK 两套字体。
- `cjk-editor` 图集新增完整 printable ASCII；Viewer 与左侧原始轨道名仍保留旧 Kuroaki 字体，不改变原 UI 密度。

- Editor 时间轴左侧改为直接显示 VSM / WindowMovement 的原始 identifier；GLOBAL / Proxy / Window 目标改成独立小标，不再拼进轨道名。
- 鼠标悬停轨道名显示简要说明；持续按住 `W` 约 220ms 显示详细说明卡，包含作用、默认值、组合关系和注意事项。
- `scrollspeed`、`velocity`、`noterot` 等常用 Note/SV 轨即使源 VSM 尚无事件也固定出现；存在 proxy 时同时提供 `prx/pry/prrz/przm/pra` 空轨。
- 新建 `scrollspeed / velocity / noterot` 片段使用可见的示例目标值，不再生成 from==to 的无效果片段。
- 修复 `SceneRenderer.WindowSources.cs` 中 `Canvas.Quad` 的 shader 参数误占 `angle` 参数位导致的 `CS1503`。
- 清理 `Viewer.Drawing.cs` 的 `uiTarget` 可空引用编译警告。
- 保留 Viewer，增加 Editor 工作区切换按钮和 Tab 切换。
- 增加按 proxy/mod、窗口/op 分组的多轨时间轴、音频波形、只读音符参考、拍点与音符吸附。
- 增加 VSM 片段添加、选择、移动、右端调整时长、属性编辑、复制、删除、撤销/重做。
- 源文本编辑保留未修改内容、未知行、mpf、重复范围、动态值、BOM 与换行；不改写导入的谱面。
- 保存生成 .sgv.json、.editor.vsm、.editor_cgmk_config.json；检测输出冲突并保留恢复资料。
- 按上传的 ExtCustomGimmick 移植 WindowMovement 五类事件、五个移动预设及普通 ProxyWindowBinding。
- 增加虚拟桌面、可显式开启的 SDL_GPU 多原生窗口后端；宿主编辑器保持原位。
- 原 ExtCustomGimmick 源码/原生库随包保留；不将 GameMaker DLL 当作 SDL_GPU 插件加载。
- 增加两个合成节拍器示例、CPU 自测源码、离线结构检查和试用启动脚本。
- 状态：源码试用版。静态检查已执行；未编译、未启动 GPU、未验证目标平台窗口行为。

---

# r20 · Windows UI antialiasing

- UI 字体 atlas 使用 premultiplied-alpha-aware mip chain 与线性 mip 采样。
- 文本起点按当前物理像素网格对齐，保留 fractional glyph advance。
- UI 最终合成 target 改用 LINEAR，避免 swapchain 尺寸取整时整张 UI 被 NEAREST 重采样。
- Windows app manifest 显式声明 PerMonitorV2 DPI awareness。
- 320×180 游戏预览继续使用原 NEAREST 行为。

# r19 · SDL_GPU data-driven refactor

本修订以 2026-09-09 13:31:43 的 SDL_GPU 源码为输入。整理全部应用源码目录、GPU partial 职责和注释；保留新后端执行 token、原依赖和原有贴图/shader 资源。

演出改为通用对象定义、调度与渲染。补上旧配置颜色缺省值、大小写结构字段、逐帧模板兼容和内嵌目录兜底；保留侧边/栏动画寿命和同深度先后关系。CLI 覆盖配置完成后只装配一次会话。

新增输入保留检查、通用对象回归、真实本机编译/CPU/GPU 验证脚本及同谱同秒图像对照工具。当前交付只执行了静态检查；没有本次编译或 GPU 通过声明。详见 `docs/VALIDATION_R19.md`。

---

## 以下为输入包原有变更记录

## r16 - restore complete bumper frames
- 撤销 r15 对 compact v1 普通 / timing bumper 的中央 45×7 UV 裁剪。`obj_note_rendering` 使用 `draw_sprite_ext` 绘制整个 sprite frame，因此 107×7 图中独立的 L / M / R 标记是原版帧的一部分，不是垃圾 padding。
- compact v1 现在严格使用 manifest 的 sprite `width / height / origin`：普通 / timing bumper 为 107×7、origin (53,3)，UV 为整张纹理；`sp_note_bumper_mine_normal` 仍保持其自己的 45×7。
- 保留 r15.1 的 `GetTexture(...)` 编译修复；raw-source / NotesExact v2 的 TargetX/Y 与 UV 逻辑不变。

## r15 - compact bumper crop fix
- 修复 r14 把 compact v1 的 107×7 padded bumper 整张当作可见逻辑区域，导致 L/M/R 外侧碎片和错误宽度一起显示。
- 当前 dump 的普通 / timing bumper 中央 45×7 区域与旧版 raw source 缩放结果一致；现在以 `sp_note_bumper_mine_normal` 的 45×7 footprint 为准，从 107×7 纹理做 UV 中央裁剪，不重新编码 PNG。
- NoteSkinFrame 新增独立 UV crop；纹理物理尺寸校验仍按原 dump 的 107×7 执行，旋转、origin、lane position 和 nearest sampling 保持不变。

## r14 - note skin dump compatibility
- Note renderer now reads dump manifest geometry instead of forcing every bumper to the legacy 45x7 size.
- Supports `vividstasis-default-notes-v1`, raw-source v2, exact-lane v2, and legacy vsnotes.
- Exact v2 uses TargetX/TargetY plus sprite origin when full dump metadata is present.
- `R` reloads the note skin transactionally, so a broken replacement does not destroy the active skin.

# 共享原生对象配置

- 按 `!obj` / VSB 对象名加载共享定义；精灵回调、二维 tween、GUI 覆盖层和 post shader 绑定由通用模块处理。VSB extra mod ID 按对象定义中的注册顺序解码。
- 为 `obj_scarletdeath_gimmick` 提供原始对象配置和素材：heartattack1–6、cover2、三种 post 模式、普通粒子和 depth 301 棋盘均可被同类型谱面复用。
- 房间 FX 补齐实际 Glow / underwater / posterise 参数；背景滤镜按配置 depth 插入，posterise 使用原始 rounding 公式并处于正确图层顺序。
- 验证改名、换目录的实际谱面，以及全新对象名和回调名的合成配置，防止歌曲特例；缺失资源和未知 mod 继续报告。

# ASTELLION 外部游玩素材接入

- 从曲包 `astellion/manifest.json` 读取专属素材，验证所有精灵帧的相对路径和尺寸，不复制到公共 Assets。
- 接入原 underwater 背景、parttimer/rainbow 碎屑、sides 镜像与扩散粒子、astbars、房间深度及首次 sides 的九秒淡入。
- 使用曲包原始 shader 对完整演出画面执行后处理，再绘制 wflash；缺失资源逐项报告，保留粒子随机行为限制。
- 游玩测试直接从歌曲零点启动；剧情模式保持独立。四难度真实谱面和 56 项加载/像素检查通过。

# src-r13-original-glow

- 实际 Scarlet Beat ENCORE 达到 0 Report；保留正常曲尾与已核实空操作记录。
- 原 Mac Disk Glow：36 次采样、动态 pass 半径与 MAX 合成；实现 depth 500 背景 glow 与 -1700 整屏 glow。
- 普通房间 Create 的 underwater、chroma 显示与背景 contrast；按真实 song name 选择 custom 房间。
- macOS / Windows publish 脚本，支持 x64 / arm64，素材统一放在程序旁 Assets；失败保留日志。

# v0.1.0 / r12

- Scarlet Beat 实际曲包适配：原动态 LBG heat haze 参数与三帧 checker 直接读取；不附带曲包或 ASTELLION 歌曲素材。
- 曲尾后的正常事件保留为时间信息，不再提示不兼容；裸 fx_colorise 按已核实的原跳过规则处理，保留可审计的空操作记录。
- FX_underwater 新增原房间 profile 入口，支持 amount / depth / 可见性；实际房间设置仍待补取。
- 文本文件匹配兼容 macOS / Windows / Linux 的大小写差异。
- glow 诊断识别从非零淡到零的事件；导出器扩展为当前平台的实际 glow shader、完整玩法 FX 与相关代码。

# v0.1.0 / r11

- 修复 Monaco 中文下端截断：AscenderOffset 用于源图集采样，歌词与底部曲名均保留完整笔画；原字体图集未改动。
- 新增 angelstar_checker_alpha / mode / set 的原代码状态与分层实现；缺少三帧原图时明确提示，不生成替代贴图。
- 增加紧凑的 macOS 原始棋盘 / glow / 动态 FX 定义导出脚本。棋盘原图可放在程序旁 Assets/GimmickExtras 即时读取。
- 修正原字体已经可用时仍出现的 Fusion Pixel 提示；fx_colorise 无后缀事件明确标注为当前源代码未注册。
- 原粒子 glow、动态 LBG 默认参数及这首自制曲的实际事件文件仍需补充，未声称全曲兼容完成。

# v0.1.0 / r10

- PAUSE / Escape、标题/作者/难度等固定 HUD 在轨道副本之后合成，移动或旋转轨道不再覆盖它们。uialpha / hom 仍控制原 UI。
- 高优先级 gimmick 图片仍可盖 UI，保留自身的 proxy 变换。
- SETTINGS 增加 Text Font：Default / Monaco，使用两套真实游戏字形并即时切换，保存到默认设置与工程；图片字样不变。
- 公共 UI 仍位于程序旁 Assets/GameUI；不增加 ASTELLION 素材。

# v0.1.0 / r9

- 项目 / 窗口 / logo 改为 KuroakiGimmick / Kuroaki/Gimmick / Kuroaki。
- 原玩法 HUD、歌曲资料、字体指标、隐藏控制及只读公共 UI 导出脚本；缺图明确报告。
- 撤回默认 INTEGER / 播放标签误改；轨道底部给标题留白，删除 proxy 固定假轨道续接。
- VSP 图片按原 depth 插入，可覆盖轨道、判定区、note 和 HUD。
- pressed hold 不再遗留判定线下起始帽；旋转 note 原像素补偿。
- 独立 SETTINGS：Top/Bottom、320×180 到 4K 实际渲染、试听音量、音频与视觉延迟、保存默认值。

# Changelog

## src-r8-room-fx

- 修复轨道底部误裁 15 像素；proxy 原位底栏跟随 hom/uialpha；仅 note 裁在 y=165。
- 预览默认按物理像素 INTEGER 放大并对齐 Retina；PAUSED/PLAYING 移出演出画面。

- 接入原 Mac 游戏的 7 份静态房间 FX 配置，新增 ROOM FX 选择、项目保存与 --room 参数。
- 区分 Plaudite heat haze / 其它 underwater chroma、Angelstar colourise / Scarlet Death colour balance；保留可见性和 depth。
- 修正 neutral underwater / black colour balance 的 0/0 退化情况。
- 按实际 codepatches.json 补上 notealpind0..6，普通音符、bumper 与 hold 共用逐轨透明度。
- 自制歌曲无法确认房间时明确标记 AUTO fallback；继续保留外部 .fx.json 覆盖。

## src-r7-more-gimmicks

- 接入左右 slow particle burst，4 秒淡出、全局 motion power 与原粒子纹理。
- 绘制 ditortedBG_alp / RGB 曲绘背景，接原 large blur 和通用噪声。
- 接入原 red / hue / colourise、四组 twist、sin/cos/tan、逐轨 note 位移/boost、prsy / rotdir。
- 支持外部 room FX profile，chroma/contrast 与背景 heat haze 按原配置执行，缺资源明确提示。附只读 UTMT 导出脚本。
- 保留任意非空 VSM !key:value 元数据，修正 !0 的错误分类。
- 保持白闪、原轨道、曲绘按位置乘色与 note 裁剪；未恢复 ASTELLION 歌曲素材。

## src-r6-jacket-multiply

- 补上遗漏的曲绘乘色层，读取本地 jacket 并按粒子当前位置逐像素相乘。
- 接入 custom / plaudite 曲绘选择，缺少内置封面时明确提示外部资源名。
- 接入 df_whitebg、pburstspeed 和 plaudite_pburst 即时分支；不再把爆发当普通参数而没有绘制。
- 更新两首歌的兼容审计与源码构建标记，仍不附带 ASTELLION 歌曲素材。

## 0.1.0

- 轨道换为原始 sp_laneOverlay / sp_holdnote_overlay PNG，恢复完整轨道与 y=144 判定线，灰色判定区使用 holdoverlayalpha；移除占位色条。
- 粒子换为 pt_diamonddust 的四帧原图（9×9、原点 4,4）；按 Custom Gimmicks 原代码每秒生成 30 对粒子，保留 2 秒淡出与永久出界销毁。独立粒子 glow 尚缺 shader_optimized_glow 和对应游玩房间配置，明确报告缺失。
- 移除音符结束后的 0.16 秒残留；长条与旋转音符在轨道局部坐标裁剪，代理变换和导出保持一致。
- 修正透明轨道/音符离屏合成时重复乘 alpha 导致的发暗。

- 修正 GML 数值前缀兼容：327.6.6 按 327.6 读取，保留事件并提示，不再整行丢弃。

- 移除 BUNDLED / ASTELLION 与图片 DEMO 入口；启动为空白工作区。
- 支持 VSC，打开 VSM 自动关联谱面、VSP、多份独立文本文件及音乐；修正 Weavers 的 174 BPM。
- 移除预览的红色占位网格/节拍脉冲，轨道透明度改用 bgalph。
- 加入 Custom Gimmicks 旧式/新版歌词、位置、缩放、旋转、透明度、颜色和对齐。
- 使用 custom shader 处理横向噪声等效果；发光、水下效果提供已标注的近似实现；确定性粒子和颜色 slash。

- 音符皮肤更新：接入 vsnotes 的 12 张 PNG，保留原色；hold 身体按时长拉伸，头尾固定；两类地雷与带判定 bumper 使用独立图片。

- C# / SDL3 原生黑红界面，文件拖入和系统文件对话框。
- VSB / VSM，BPM / offset、时间轴、逐帧、变速、相对路径工程。
- 外部 ASTELLION 谱面读取，Ogg/Vorbis 播放；不再捆绑 ASTELLION 的音频、贴图或谱面。
- Custom Gimmicks v1.12.7 的 VSP 图片与 13 类 img* 控制。
- MP4 固定帧视频导出，带原音频，支持区间、取消和文件保护。
- 兼容诊断与独立 CLI。

## 0.1.1 planned

- 真实窗口位移及相关预览控制。当前版本没有调用窗口移动 API。

## src-r15.1-note-bumper-crop-compile-fix

- 修复 `Graphics/NoteSkin.cs` 中实例方法 `Texture(NoteSkinFrame)` 与 `Texture` 类型同名，导致 `Texture.Load(...)` 被 C# 解析为方法组并触发 CS0119 的编译错误。
- 将私有取纹理方法重命名为 `GetTexture(...)`；NoteSkin 几何、107×7 bumper UV crop 和 manifest 行为不变。

### v0.1.2 / 16.1 theme hotfix
- Layout divider grips no longer draw over or receive input through the track help card while it is visible or fading.
- Added UI themes in Settings: Nekomiya (current 16.1 palette, default), Scarlet (legacy red palette), and Kuroaki (higher-contrast red/black palette).
- Theme selection is stored in device settings and applies immediately to Viewer, Editor, timeline, docs, help cards and layout controls.

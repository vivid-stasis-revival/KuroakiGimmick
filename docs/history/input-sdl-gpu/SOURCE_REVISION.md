# 当前源码快照：SDL_GPU / 2026-09-09

本包包含完成 SDL_GPU 对接后的源码。运行与编译见 [BUILD_SRC.md](BUILD_SRC.md)，后端验证见 [docs/SDL_GPU.md](docs/SDL_GPU.md)。下文保留此前修订历史；此前的 r16 包名不代表本快照。

# 源码修订 src-r16-complete-bumper-frame

项目 KuroakiGimmick，窗口标题 Kuroaki/Gimmick，构建版本 v0.1.0 / src-r16-complete-bumper-frame。独立源码目录 KuroakiGimmick-src-r16；请在新目录构建，避免运行旧包。

构建：dotnet run -c Release；发布：bash scripts/publish-mac.sh / bash scripts/publish-win.sh；Windows 本机：scripts/publish-windows.ps1。架构与输出说明见 docs/PUBLISH.md。

新增共享原生对象配置：`Core/NativeGimmickProfile.cs` / `Graphics/NativeGimmickRenderer.cs` 按对象名加载精灵回调、GUI 覆盖和 shader 绑定，不按歌曲名或路径分支。对象配置、VSB mod 注册顺序、房间 FX 和验证说明见 docs/NATIVE_GIMMICKS.md。

Core/GameUiAssets.cs 与 Graphics/GameUiRenderer.cs 读取原 UI sprite/font 资源并按 cc Draw_0 绘制；Core/SongMetadata.cs 读取曲目、作者和等级；scripts/Dump_Game_UI_Mac.sh 内嵌只读 UTMT 导出。Assets/GameUI 已包含本次成功导出的原 UI 精灵与三套字体，原字节哈希已记录；无需再导出。无模拟原 UI 贴图。

Core/ViewerSettings.cs、UI/Viewer.cs 实现设置、持久化和校准，Graphics 的所有场景/FX 目标支持 320×180 到 3840×2160。NoteSkin.cs 修正 pressed hold 与旋转补偿，SceneRenderer.cs 恢复底部标题留白，并按 VSP depth 插入图片。已有原房间 FX 与 ASTELLION 外部谱面解码保留。没有 ASTELLION 歌曲素材。

SOURCE_CONTENT_SHA256.json 是包内文件 SHA-256（不含自身）。验证与限制见 docs/VALIDATION.md 和 docs/GAME_UI_R9.md。

r10 将固定 HUD 与移动轨道分开合成，保留高优先级 VSP 图片覆盖；Text Font 设置映射原游戏 Default / Monaco 资源并持久化。

r11 修复 Monaco CJK 图集采样偏移；Core/Checkerboard.cs 与 Graphics/CheckerboardRenderer.cs 实现原棋盘状态与分层，scripts/Dump_Gimmick_Extras_Mac.sh 负责补取缺失原图。资源需求和未完成项见 docs/GIMMICK_EXTRAS_R11.md。

r12 读取原动态 LBG 定义，保留正常曲尾，记录已核实空操作，并支持 FX_underwater 原图层输入；本曲三个剩余 Notice 和缺失资源见 docs/SCARLET_BEAT_R12.md。

r13 根据当前 Mac 游戏原码实现背景 / 整屏 Disk Glow 与动态房间参数；本曲 0 Report 与限制见 docs/SCARLET_BEAT_R13.md。真实源文件和当前二进制同步构建。
r14：NoteSkin 改为 manifest 驱动，支持 compact v1、raw-source v2 与 NotesExact v2；完整 v2 dump 可放到 Assets/NoteSkinFull，按 R 可事务式重载。
r15：修复 compact v1 的 107×7 padded bumper。以同组 bumper mine 的 45×7 footprint 为准，按 sprite origin 从纹理中央做 UV crop，保留物理 107×7 文件用于校验，不再显示外侧 L/M/R 碎片。

r15.1：仅修复 NoteSkin 的 C# 名称遮蔽编译错误。私有方法 `Texture(...)` 改名为 `GetTexture(...)`，不改变 r15 的 bumper crop、UV、origin 或资源格式逻辑。


r16：撤销 r15 的 45×7 中央裁剪。当前 compact dump 的 107×7 普通 / timing bumper 是完整 GameMaker frame，L/M/R 侧标记属于原 sprite；按 manifest width/height/origin 整帧绘制。

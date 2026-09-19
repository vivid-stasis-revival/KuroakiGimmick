> 历史版本文档。r17 架构以 [OBJECT_PROFILES.md](OBJECT_PROFILES.md) 为准；本次实际验证以 [REFACTOR_VALIDATION.md](REFACTOR_VALIDATION.md) 为准。

# ASTELLION 外部游玩模式

将完整歌曲目录拖入查看器，或打开其中任意 VSB。目录自动优先选 `ENCORE.vsb`，关联 `music.ogg`，effect BPM 为 174。`OPENING`、`MIDDLE`、`FINALE`、`ENCORE` 共用歌曲目录中的 `astellion/manifest.json`；应用公共 `Assets` 和发布包不收录该曲的图片、谱面或音频。

## 素材与效果

支持 manifest v1、profile `astellion`、room `scene_gameplay_fl_astellion`。精灵使用 manifest 声明的原点和独立 PNG 帧，所有已声明帧均检查相对路径、图像尺寸及总解码预算。拒绝绝对路径、`..`、跨出曲包的软链接和尺寸不符的图片；其他有效素材仍可加载。

| 项目 | 行为 |
|---|---|
| `sp_ast_u_bg` / depth 800 | 使用 `background.parameters` 和曲包 `_filter_underwater` GLSL；speed 随歌曲秒数推进，两个 amount 分别读取 `fxdist1`、`fxdist2` |
| `pt_diamonddust` / depth 600 | 按 `parttimer` 累积 60 Hz 歌曲时间生成；`rainbow` 控制出生饱和度；旧版运动、两秒淡出和出界销毁 |
| `sp_overlaynew` / depth 300 | 原始第 0 帧，alpha 由 `bgalph` 控制 |
| `sides` / depth 260 | 从 (150,90)/(170,90) 向两侧以 800 px/s 移动的镜像 `sp_sidebar4`，alpha 0.4；越过 -160/480 销毁 |
| `sp_ast_particle` / depth 260 | sides 同时在左右区域生成，125 ms 放大、125 ms 淡出；回调发生在 [100,132.4] 秒时不生成 |
| `astbars` / depth 260 | 值 1..4 对应 y=125/95/65/35；同一横条使用最新回调，从 0.5 在 100 ms 内淡出 |
| 轨道 / depth 200 | 沿用原始轨道和音符绘制；proxy 变换保留 |
| `sp_holdnote_overlay` / depth 100 | 原始第 0 帧，alpha 由 `bgalph` 控制；位于轨道上方、音符下方，固定在房间坐标 |
| 首次 sides | 专属房间初始 `uialpha`/`bgalph` 为 0，第一次 sides 触发九秒 EaseOutCubic 淡入；`holdoverlayalpha` 跟随 `bgalph` |
| `shader_unraveling_main` | 在背景、装饰、轨道、音符和 HUD 合成后执行，绑定 gray、barrel+barrel2、hdistort、fish、vig、abx、aby；aberControl 按 effect beat 更新色差 |
| `wflash` | 位于上述后处理之后的白色覆盖 |

shader 直接读取 `astellion/shaders/underwater/GLSL_Fragment.frag` 与 `astellion/shaders/post/GLSL_Fragment.frag`。仅在内存中适配为 OpenGL 3.3；原文件不改写。underwater 两个 amount 都为零时保留现有除零保护。

当前收到的测试包没有附带 README 中提到的噪声 PNG。manifest 的 `_filter_underwater_noise_sprite` 符号先查找曲包同名 PNG、`sprites/<符号>/0000.png`、`noise/<符号>.png`；缺失时使用应用已有的原始 `Assets/GameFX/_filter_underwater_noise_sprite.png`。报告记录具体路径和 `noiseSource`，不会生成替代噪声。其他 sampler 路径必须是曲包内的相对路径。

## 报告与范围

`--inspect` 和 SAVE REPORT 增加 `astellion` 字段，包含 manifest 路径、已加载精灵、各资源来源、逐项错误和粒子/回调数量。缺少 manifest、图片、参数、噪声或 shader 时报告对应项，shader 编译错误也进入实时 REPORT。

按提供的 IMPLEMENTATION.md 保留“随机序列/公共粒子对象未逐版确认”提示。固定种子让前后拖动和视频导出一致，但不声称与某次原版游玩随机排布相同。轨道装饰使用默认第 0 帧，没有玩法输入、按键亮起或判定反馈。`story/` 的剧情、幕帘和语音属于独立模式，本次不启用；音乐和 VSB 从歌曲零点开始。

## 验证

```bash
dotnet build -c Release
python3 scripts/dev/check-astellion.py bin/Release/net8.0/KuroakiGimmick.dll ~/Downloads/ASTELLION_Test_Chart_Pack
```

真实四种难度均保留 1259 个 mod、796 次 sides 和 128 次 astbars，加载全部 7 组专属精灵；没有资源错误，仅保留上述粒子限制。原始曲包全部文件的 SHA-256 在验证前后一致。

56 项 ASTELLION 加载/实际 GPU 像素检查通过，覆盖路径与软链接边界、尺寸不符、缺失资源、非法回调、生成密度、镜像位置、粒子生灭与禁用区间、横条、九秒淡入、深度、proxy 模式的完整背景、两个 underwater amount、整屏后处理、aberControl 和重复定位。另通过 128 项应用自测、36 项通用 GPU 检查及 33 项 UI/设置/图层检查。实测 GPU 为 Apple M4 Pro；未与原游戏录制做逐帧逐像素比对。

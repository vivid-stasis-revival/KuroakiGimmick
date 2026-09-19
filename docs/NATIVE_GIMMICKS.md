> 历史版本文档。r17 架构以 [OBJECT_PROFILES.md](OBJECT_PROFILES.md) 为准；本次实际验证以 [REFACTOR_VALIDATION.md](REFACTOR_VALIDATION.md) 为准。

# 共享原生对象配置

资源加载以 VSM 的 `!obj` 或 VSB 的对象名为入口：`Assets/Gimmicks/<对象名>/manifest.json`。歌曲标题、目录、谱面文件名不参与选择，谱面的拍数和 BPM 表决定实际回调时间。更换歌曲或重新安排事件，不需要改动 C# 代码。

`NativeGimmickProfile` 负责校验配置、图片尺寸、相对路径、shader 与参数；`Timeline` 根据回调定义调度事件；`NativeGimmickRenderer` 统一绘制 GUI 精灵与后处理。资源缺失、非法配置和 shader 编译失败进入报告，未知 mod 继续按名称报告。

## 配置能力

- `extraMods`：原始 extra mod 的注册顺序，VSB 的 129 对应首项。重复注册的名称仍占据其原始位置。
- `defaults`：该对象全局参数的初值，保留通用参数和 proxy 的默认行为。
- `callbacks`：mod 名对应一组精灵绘制定义，回调按照谱面事件时间触发；每项包含位置、缩放、角度、深度和存活时间。
- `tweens`：对 x、y、scaleX、scaleY、angle、alpha 定义延迟、持续时间、from/to 和 easing，支持向后定位时重建状态。
- `overlays`：持续的 GUI 覆盖层，`alphaMod` 绑定透明度。GUI 精灵按 depth 排序，在房间 FX 与整屏后处理之后绘制。
- `postModes` / `postModeMod`：按 mod 值选择 shader。uniform 可绑定常量、歌曲秒数、多个 mod 的和、比例、sin/cos 和幅度 mod；支持 float/vec2/vec3/vec4 及图片 sampler。
- `ambientParticles`、`burstAfterJacket`、`checkerboard`、`checkerMode`：复用普通碎屑、曲绘乘色之后的侧向爆发，以及三个已有棋盘绘制位置。
- `roomPreset`：对象对应的默认房间。用户明确指定 ROOM FX 或外部 `.fx.json` 时仍由用户选择优先。
- `perFrameFunctions`：声明使用已实现的函数，当前可复用 `aberControl`。

新增对象可通过配置复用以上能力；尚无实现的行为仍需要扩展渲染器，配置不会执行任意 GML。

## 当前提供的对象配置

`obj_scarletdeath_gimmick` 的精灵、房间参数与 GML 来自本机原游戏的只读 UTMT 导出，输入游戏没有保存或改写。该配置为所有声明同一对象的谱面提供：

| 能力 | 原始行为 |
|---|---|
| `heartattack1–6` | 六张原始精灵及原点，分别在 (31,94)、(72,92)、(127,92)、(172,92)、(210,90)、(274,91) 创建；GUI depth -5000 |
| 回调动画 | Y 缩放在 333.333 ms 内以 EaseOutExpo 从 1.25 降到 1；400 ms 后用 500 ms 淡出；总寿命 900 ms |
| `cover2` | `sp_cover4` 从 (0,0) 以 0.25 倍绘制，透明度绑定 mod；位于 post shader 之后 |
| 棋盘 | 原始 32×18 格、三帧素材，固定 depth 301；更新和透明度仍由谱面控制 |
| 背景碎屑 | 默认每两个 60 Hz 歌曲时间步生成两颗普通粒子，使用原始生成速度、两秒寿命和越界规则 |
| 侧向爆发 | 位于曲绘乘色之后，保留原 4 秒 lifetime、方向与全局运动参数 |
| 房间效果 | 背景 contrast depth 700、glow depth 600、整屏 FX_glow depth -1800、posterise depth -2100；包含原始可见性和参数 |
| 后处理 | 配置三种原始 shader 模式和对应 uniform/sampler 绑定，复用通用绑定模块 |

共享资源位于 `Assets/Gimmicks`，未向测试曲包写入文件。图片和 shader 的 SHA-256、输入游戏哈希记录在对象目录的 `SOURCE_SHA256.json`；原始 GML 和完整房间层数据位于源码 `docs/reference/native-gimmicks`。

普通粒子和棋盘采用固定种子、歌曲时间步长，便于随机跳转与导出；不等于原游戏某次运行的随机排布或暂停时的墙钟行为。报告中的 `nativeGimmick.randomness` 保留这一说明。查看器仍不实现游玩判定、原游戏剧情或任意对象脚本。

## 导出与验证

`scripts/assets/Dump_Native_Gimmick.csx` 接受已有对象定义，从原游戏只读导出该定义列出的精灵、shader 和参考代码：

```bash
KUROAKI_GIMMICK_DEFINITION=/path/to/object/manifest.json \
KUROAKI_GIMMICK_OUTPUT=/path/to/new-empty-output \
UndertaleModCli load /path/to/game.ios --scripts scripts/assets/Dump_Native_Gimmick.csx
```

输出目录必须为空；命令不传保存游戏数据的参数。输出配置保留原有通用绘制定义，并更新实际图片尺寸、原点和帧路径。

```bash
python3 scripts/dev/check-native-gimmicks.py bin/Release/net8.0/KuroakiGimmick.dll --song /path/to/test-song
```

22 项对象复用检查覆盖了原报告中的功能、歌曲/目录/文件名变化、全新对象和回调名、VSB 注册顺序、tween、GUI/post 顺序、资源错误和原始 posterise rounding。另通过 128 项自测、36 项通用渲染、33 项 UI/图层、17 项房间 FX 和 6 项独立 GPU/CPU Glow 检查。

本次原报告的 1354 个 mod 全部保留；六个文字回调、128 次棋盘刷新、13834 颗普通粒子和 2720 颗侧向爆发均正常调度，原 5 条兼容提示已解决。测试曲包文件保持原字节，不用改名或迁移到专门目录。

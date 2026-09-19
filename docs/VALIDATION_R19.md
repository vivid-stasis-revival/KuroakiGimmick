# r19 当次验证记录

## 状态

本次交付为 **源码候选**。当前容器没有 .NET SDK / C# 编译器，也没有该应用可用的 macOS Metal 或 Windows Direct3D 12 环境。没有完成真实编译、C# 自测、窗口启动、GPU 像素比较、音视频导出或发布。

以下 PASS 仅对应实际执行的源码、数据与脚本检查，不能推出 shader 可以编译、程序可以启动或演出逐像素正确。

| 检查 | 状态与边界 |
|---|---|
| 输入 GPU 实现保留 | PASS：13 个类型、272 个成员的归一化 token 契约；忽略注释/空白/partial 外壳和控制流显式大括号，不是 IL 或 ABI 验证 |
| 输入资源与锁文件 | PASS：145 个原 Assets 文件逐字节相同；只更新原 Scarlet manifest 元数据，新增 10 个 JSON；锁文件不变 |
| C# 源码组织 | PASS：108 个应用文件 + 5 个自测文件的词法括号、类型/文件布局和编译项检查；不是 C# 编译 |
| 对象定义静态检查 | PASS：9 个内置/示例对象定义的声明引用、已随包提供的 shader/精灵及参数范围 |
| 旧演出契约对照 | PASS：30 项源码/数据对照，包含 ASTELLION 扩展编号顺序、174 BPM、9 秒淡入、回调尾部/栏排序与 Scarlet 元数据/滤镜资源 |
| 实际外部 ASTELLION 曲包 | PASS：7 组精灵、15 个 PNG 帧头尺寸、2 个 shader 文件、38 个 uniform 分量与资源参数核对；未执行 C# loader 或 shader 编译 |
| Python / Bash 语法 | PASS：由 `check-source.py` 记录实际检查数量；PowerShell 未执行 |
| 本机验证脚本的缺 SDK 分支 | 正确返回 127，并明确说明没有执行 build/runtime tests；这不是构建 PASS |
| `dotnet restore/build` | 未执行成功，缺少 SDK |
| `--self-test` | 未执行。原有 CPU 自测、通用对象/旧配置自测和新增 14 项 refactor 检查已随源码提供 |
| `--gpu-test` | 未执行。保留输入版本的 GPU 自测内容 |
| 同谱同秒图像比较 | 未执行。`compare-scenes.py` 会真实调用两份 DLL，不能在这里用静态数据代替 |
| macOS / Windows publish | 未执行。本次只有源码 ZIP，没有新的可执行包 |

## 本次输入

源码：`KuroakiGimmick-src-SDL_GPU-20260909-133143.zip`  
SHA-256：`99b362dd91de968b9bed878c786227276db05f0a30abdb4e7b3090a5ab217de9`

外部资源核对：先前提供的 `ASTELLION_Test_Chart_Pack.zip`  
SHA-256：`0258cb74e696ea0dba22a2461ade12064325c83ecf0c412d212490acef0de997`

该曲包只用于只读检查，没有塞入本次应用源码包。它不是用户截图中的 Scarlet Death 曲包，也不作为 Scarlet Death 的实际运行证据。

## 复现静态检查

```bash
python3 scripts/dev/check-source.py
python3 scripts/dev/check-input-preservation.py --baseline /path/KuroakiGimmick-src-SDL_GPU-20260909-133143.zip
# 参数是解压后的旧源码根目录，不是 ZIP：
python3 scripts/dev/check-legacy-contracts.py /path/input-source-root
python3 scripts/dev/check-external-pack.py Assets/Gimmicks/obj_astellion_gimmick/manifest.json /path/ASTELLION_Test_Chart_Pack.zip
```

未传 `--baseline` 时，保留检查使用 `tests/Fixtures/input-sdl-gpu-contract.json` 中记录的输入指纹。fixture 是从输入 ZIP 生成，不是拿重构后的结果给自己生成预期。

## 必须在本机完成的验收

macOS：

```bash
bash scripts/verify.sh --gpu
dotnet run -c Release --no-build
```

Windows：

```powershell
.\scripts\verify.ps1 -Gpu
dotnet run -c Release --no-build
```

验证脚本先恢复锁定依赖，再编译，再运行当前编译产物，不会在 build 失败后拿旧 DLL 继续测。GPU 测试成功仍需打开完整曲包，验证暂停/跳转/倒拖/重载和导出。

分别独立构建本次输入和重构版，保留它们各自的 Assets。对两版使用同一份曲包、同样渲染宽度/对齐和秒数：

```bash
python3 scripts/dev/compare-scenes.py /path/baseline/KuroakiGimmick.dll bin/Release/net8.0/KuroakiGimmick.dll \
  /path/ScarletDeath/ENCORE.vsb --times 184.11 191.79 --out /tmp/scarlet-r19-new
```

输出成对帧、差异图、候选帧的绘制后报告和实际子进程日志。默认误差阈值为通道最大 4、平均 0.1；逐像素零差异需要显式设置 `--max-error 0 --mean-error 0`。任何一帧失败、缺资源或 shader 报错都会非零退出。

ASTELLION 应覆盖第一次 sides 的九秒淡入、100～132.4 秒特殊回调抑制区间、astbars 密集段、倒拖和尾部。Scarlet 应检查 red 开/关两个状态和 GUI cover，不能只凭 REPORT 0 验收。

## 记录边界

本次日志在 `docs/validation/r19/`，执行命令、退出码和环境信息在 `summary.json`。旧 `docs/validation/sdl-gpu-20260909/`、旧 `.log` 与旧报告只属于输入历史；没有重标日期冒充本次运行记录。测试代码的存在也不是 PASS。

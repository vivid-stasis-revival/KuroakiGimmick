# WindowMovement integration · v0.1.1 preview

## 输入来源与实现边界

`Integrations/ExtCustomGimmick/` 是用户上传的扩展包解压内容，移除了 Finder 元数据，业务源码与原 DLL/dylib 不做改写。逐文件 SHA-256 在 `Integrations/ExtCustomGimmick.source-sha256.json`。

预览器按照其中 WindowMovement / ProxyWindowBinding 的字段与逻辑建立 C# 适配器。原扩展的原生库服务 GameMaker 渲染上下文，预览器不将它强行载入 SDL_GPU。真实预览窗口使用自己的 SDL3 后端，共享 SDL_GPU 设备和分别取得的窗口交换链；游戏端仍需要正确安装原 ExtCustomGimmick。

源代码翻译、源配置能读写、SDL_GPU 窗口后端已编写，不代表已与 GameMaker/macOS/Windows 实测逐项一致。制作环境无 .NET SDK，未执行该后端。

## 配置发现

优先使用工程的 `WindowMotion` 路径，CLI 可用 `--window-motion <file>`。没有指定时，在歌曲目录找对应难度的 `*_cgmk_config.json`，然后找 `cgmk_config.json` 或 `window_movement.json`。

内联键：

```text
ECG_WINDOW_MOVEMENT_EVENTS
ECG_WINDOW_MOVEMENT_WINDOW_COUNT
ECG_WINDOW_MOVEMENT_SETTINGS
ECG_PROXY_WINDOW_BINDINGS
```

也读取 `format: ExtCustomGimmick.WindowMovement` 的独立事件 JSON 和旧 `ECG_WINDOW_MOVEMENT_FILE` 引用。编辑保存输出内联 cgmk 配置。未知配置键及事件字段保留，不等于全部已模拟。

## 当前事件与预设

| 事件 | 时间轴/属性 |
|---|---|
| NewWindowDance | 位置、预设、轴启用、角度、振幅、速度、频率、缓动、reference、easeDur |
| WindowResize | 尺寸、轴启用、pivot、anchor、缓动和 dur |
| HideWindow | show 布尔值 |
| ReorderWindows | order 数组，前后关系 |
| SetWindowContent | room 内容来源 ID，与窗口编号分开 |

移动预设：Move、Sway、Wrap、Ellipse、ShakePer。JSON 时间字段是秒，所有新事件包含所提供 GML 直接访问的默认字段。右侧显示的字段名保留原扩展名称，不把 angle 等字段擅自换成未经确认的单位。

窗口移动索引 w=0 表示主窗口；w=1 对应原生 custom id=100，w=2 对应 101。Proxy binding 的 window 字段本身已是原生 ID，例如 100，不再二次转换。默认 proxy source 为 1000+i，主画面 source 为 0。

普通 proxy 绑定读取谱面现有 pr* 变换、裁剪、透明度及标题/边框。它引用累积 playfield 的独立裁剪内容，不把每个窗口都硬换成同一张 Final。无法映射的 source 显示不可用，不静默替换为主画面。

## 两种预览

虚拟桌面默认逻辑布局 1920×1080。在 Viewer 左下或 Editor 中央预览区切换 DESKTOP。它只在应用内部绘制窗口矩形与内容。

LIVE 模式根据宿主所在显示器的 SDL bounds 创建真实辅助窗口。宿主编辑器不隐藏、不移动；关闭任一预览窗口或按 Esc 关闭全部辅助窗口。主窗口编号 0 也由辅助窗口表示。预览关闭、加载新工程和退出时释放辅助窗口。

输出尺寸、坐标有安全限幅，防止编辑错误瞬间创建不可控的超大窗口；不把限制写回源文件。最多预览 64 个窗口。操作系统可能限制位置、层叠、边框或窗口大小，仍需平台实测，尤其是 macOS Retina 的显示坐标与原游戏物理像素布局差异。

跳转只重建内部窗口状态，再输出目标时刻姿态，不沿着经过的事件逐次移动 OS 窗口。随机移动使用源移植的确定性计算，不能据此声称所有游戏随机效果已经逐版本还原。

## 仍未实现/仍需验证

普通 proxy 以外的 proxyMode00、暂停/FCAC/结算合并生命周期没有完整模拟。相关配置原样保留，报告明确提示。混合 WindowMovement 与 ProxyBinding 对同一 window id 的写入，当前由 proxy binding 最后覆盖，需要按目标扩展实机验证其时序；两个演示工程故意避免这种冲突。

尚未覆盖任意 GameMaker surface/room 对象。SetWindowContent 目前只支持主最终画面和已登记的普通 proxy 来源。原游戏窗口捕获、跨显示器组合、平台焦点策略、多个交换链的帧率与剪裁精确性未完成平台验证。

原 GameMaker DLL/dylib 是输入包原件，不是本次重新编译产物。请不要把 Integrations/raw 中的库复制到应用根目录替换 SDL3。播放场景的视频导出不包含整个桌面窗口布局。

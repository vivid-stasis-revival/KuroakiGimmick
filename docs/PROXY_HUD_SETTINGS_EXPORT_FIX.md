# Proxy HUD、Editor 设置与导出目录修复

基础 `obj_base_gimmick` 改为先组装完整场景，再进行 proxy 采样，与已有 Custom 路径共用合成入口。依据本机 `vs6201/CodeEntries/gml_Object_obj_base_gimmick_Draw_64.gml`：原版先把 `application_surface` 复制到 `aft`，绘制固定侧边与底栏，最后按 proxy 顺序采样。PAUSE、Escape、计分和谱面图片都属于采样源；`prcl/prcr` 决定能否采到它们，`prx` 移动结果，`pra` 控制结果透明度。移动后的 proxy 可以覆盖固定 HUD。特殊原生对象的独立绘制路径保持原有行为。

设置覆盖层移到 Viewer / Editor 共用的帧收尾入口，避免 Editor 提前返回后只设置了打开状态却没有画面。UI 自测通过左侧真实设置按钮打开面板，再点击 Note Alignment 验证面板消费输入；同时修正旧字体测试使用的过时按钮坐标。

导出位置保存在用户 `settings.json` 的 `ExportDirectories`，按 VSM、带配置 VSM、曲包、视频、信息卡分别记忆父目录。取消系统对话框不更新；选定目标后持久化，重启仍可复用。目录不存在时回退到原来的 Documents/KuroakiGimmick/Exports。此偏好不进入谱面工程。

截图对应原曲未提供。本次按原版源码、用户给出的 proxy 参数类型及生成的像素测试验证，不能替代该原曲在目标游戏里的逐帧验收。参数名 `prcl` 末尾是小写 L，不是数字 1。

验证：Release 构建 0 警告 / 0 错误；基础自测 371 项、创作导出 48 项、UI/设置/图层像素检查 34 项、渲染像素检查 38 项通过。`--gpu-test` 在本机 Metal 通过，包含 Custom proxy、文字/剧情及原生场景回归。未执行 Windows 实机运行。

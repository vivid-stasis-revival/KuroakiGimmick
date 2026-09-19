# 输入到 r19 的源码定位

本表按类型名定位文件，不声称移动前后的整个类型行为相同。GPU 成员另有 token 契约，对象演出是结构性数据化。

| 输入文件 | 类型 | 当前文件 / 处理方式 |
|---|---|---|
| `Core/AstellionAssets.cs` | `AstellionAssets` | 原专用实现移除；行为定义转入 JSON，执行统一使用 NativeGimmickProfile / NativeGimmickRenderer |
| `Core/Audio.cs` | `AudioData` | `src/Core/Audio/AudioData.cs` |
| `Core/Audio.cs` | `Transport` | `src/Core/Audio/Transport.cs` |
| `Core/Audio.cs` | `Ffmpeg` | `src/Core/Audio/Ffmpeg.cs` |
| `Core/Checkerboard.cs` | `Checkerboard` | `src/Core/Assets/Checkerboard.cs` |
| `Core/CustomImages.cs` | `CustomImages` | `src/Core/Assets/CustomImages.cs` |
| `Core/CustomText.cs` | `CustomText` | `src/Core/Assets/CustomText.cs` |
| `Core/Easings.cs` | `Easings` | `src/Core/Timing/Easings.cs` |
| `Core/GameFxProfile.cs` | `GameFxProfile` | `src/Core/Assets/GameFxProfile.cs` |
| `Core/GameUiAssets.cs` | `GameUiAssets` | `src/Core/Assets/GameUiAssets.cs` |
| `Core/GpuSelfTest.cs` | `GpuSelfTest` | `tests/SelfTests/GpuSelfTest.cs` |
| `Core/JacketAssets.cs` | `JacketAssets` | `src/Core/Assets/JacketAssets.cs` |
| `Core/Models.cs` | `Diagnostic` | `src/Core/Models/Diagnostic.cs` |
| `Core/Models.cs` | `Note` | `src/Core/Models/Note.cs` |
| `Core/Models.cs` | `ModEvent` | `src/Core/Models/ModEvent.cs` |
| `Core/Models.cs` | `FrameEvent` | `src/Core/Models/FrameEvent.cs` |
| `Core/Models.cs` | `Chart` | `src/Core/Models/Chart.cs` |
| `Core/Models.cs` | `ViewerProject` | `src/Core/Models/ViewerProject.cs` |
| `Core/Models.cs` | `Paths` | `src/Core/Projects/Paths.cs` |
| `Core/Models.cs` | `ModCatalog` | `src/Core/Parsing/ModCatalog.cs` |
| `Core/NativeGimmickProfile.cs` | `NativeGimmickProfile` | `src/Core/Assets/NativeGimmickProfile.cs` |
| `Core/NoteMotion.cs` | `NoteMotion` | `src/Core/Timing/NoteMotion.cs` |
| `Core/NoteSkinProfile.cs` | `NoteSkinFrame` | `src/Core/Assets/NoteSkinFrame.cs` |
| `Core/NoteSkinProfile.cs` | `NoteSkinProfile` | `src/Core/Assets/NoteSkinProfile.cs` |
| `Core/SelfTest.cs` | `SelfTest` | `tests/SelfTests/SelfTest.cs` |
| `Core/Session.cs` | `Session` | `src/Core/Projects/Session.ProjectFiles.cs`、`src/Core/Projects/Session.Report.cs`、`src/Core/Projects/Session.cs` |
| `Core/SongFiles.cs` | `SongFiles` | `src/Core/Projects/SongFiles.cs` |
| `Core/SongMetadata.cs` | `SongMetadata` | `src/Core/Projects/SongMetadata.cs` |
| `Core/Timeline.cs` | `BpmMap` | `src/Core/Timing/BpmMap.cs` |
| `Core/Timeline.cs` | `Timeline` | `src/Core/Timing/Timeline.Evaluation.cs`、`src/Core/Timing/Timeline.ObjectBehavior.cs`、`src/Core/Timing/Timeline.Particles.cs`、`src/Core/Timing/Timeline.cs` |
| `Core/VideoExport.cs` | `ExportOptions` | `src/Core/Export/ExportOptions.cs` |
| `Core/VideoExport.cs` | `VideoExport` | `src/Core/Export/VideoExport.cs` |
| `Core/ViewerSettings.cs` | `ViewerSettings` | `src/Core/Projects/ViewerSettings.cs` |
| `Core/VsbReader.cs` | `VsbReader` | `src/Core/Parsing/VsbReader.cs` |
| `Core/VscReader.cs` | `VscReader` | `src/Core/Parsing/VscReader.cs` |
| `Core/VsmReader.cs` | `VsmReader` | `src/Core/Parsing/VsmReader.cs` |
| `Graphics/AstellionRenderer.cs` | `AstellionRenderer` | 原专用实现移除；行为定义转入 JSON，执行统一使用 NativeGimmickProfile / NativeGimmickRenderer |
| `Graphics/Canvas.cs` | `Color` | `src/Graphics/Primitives/Color.cs` |
| `Graphics/Canvas.cs` | `Rect` | `src/Graphics/Primitives/Rect.cs` |
| `Graphics/Canvas.cs` | `Canvas` | `src/Graphics/SdlGpu/Canvas.Geometry.cs`、`src/Graphics/SdlGpu/Canvas.Readback.cs`、`src/Graphics/SdlGpu/Canvas.cs` |
| `Graphics/CheckerboardRenderer.cs` | `CheckerboardRenderer` | `src/Graphics/Effects/CheckerboardRenderer.cs` |
| `Graphics/CustomEffects.cs` | `CustomEffects` | `src/Graphics/Effects/CustomEffects.cs` |
| `Graphics/Fonts.cs` | `Fonts` | `src/Graphics/Text/Fonts.cs` |
| `Graphics/GameUiRenderer.cs` | `GameUiRenderer` | `src/Graphics/Text/GameUiRenderer.cs` |
| `Graphics/GpuDevice.cs` | `BlendFactor` | `src/Graphics/Primitives/BlendFactor.cs` |
| `Graphics/GpuDevice.cs` | `GpuDevice` | `src/Graphics/SdlGpu/GpuDevice.Submission.cs`、`src/Graphics/SdlGpu/GpuDevice.Transfers.cs`、`src/Graphics/SdlGpu/GpuDevice.cs` |
| `Graphics/NativeGimmickRenderer.cs` | `NativeGimmickRenderer` | `src/Graphics/Effects/NativeGimmickRenderer.Shaders.cs`、`src/Graphics/Effects/NativeGimmickRenderer.cs` |
| `Graphics/NoteSkin.cs` | `NoteSkin` | `src/Graphics/Notes/NoteSkin.cs` |
| `Graphics/SceneFont.cs` | `SceneFont` | `src/Graphics/Text/SceneFont.cs` |
| `Graphics/SceneRenderer.cs` | `SceneRenderer` | `src/Graphics/Scene/SceneRenderer.Particles.cs`、`src/Graphics/Scene/SceneRenderer.Playfield.cs`、`src/Graphics/Scene/SceneRenderer.Resources.cs`、`src/Graphics/Scene/SceneRenderer.cs` |
| `Graphics/Shader.cs` | `Shader` | `src/Graphics/SdlGpu/Shader.cs` |
| `Graphics/ShaderCompiler.cs` | `ShaderCompiler` | `src/Graphics/SdlGpu/ShaderCompiler.Toolchain.cs`、`src/Graphics/SdlGpu/ShaderCompiler.Translation.cs`、`src/Graphics/SdlGpu/ShaderCompiler.cs` |
| `Graphics/Texture.cs` | `Texture` | `src/Graphics/SdlGpu/Texture.cs` |
| `Graphics/Texture.cs` | `Target` | `src/Graphics/SdlGpu/Target.cs` |
| `Native/Host.cs` | `Host` | `src/Native/Host.cs` |
| `Native/Sdl.Gpu.cs` | `Sdl` | `src/Native/Sdl.Audio.cs`、`src/Native/Sdl.Gpu.Commands.cs`、`src/Native/Sdl.Gpu.cs`、`src/Native/Sdl.Input.cs`、`src/Native/Sdl.PlatformGpu.cs`、`src/Native/Sdl.cs` |
| `Native/Sdl.cs` | `Sdl` | `src/Native/Sdl.Audio.cs`、`src/Native/Sdl.Gpu.Commands.cs`、`src/Native/Sdl.Gpu.cs`、`src/Native/Sdl.Input.cs`、`src/Native/Sdl.PlatformGpu.cs`、`src/Native/Sdl.cs` |
| `Native/ShaderTools.cs` | `ShaderTools` | `src/Native/ShaderTools.cs` |
| `Program.cs` | `Program` | `src/App/Program.cs` |
| `UI/Viewer.cs` | `Viewer` | `src/UI/Viewer.Drawing.cs`、`src/UI/Viewer.Input.cs`、`src/UI/Viewer.Settings.cs`、`src/UI/Viewer.SmokeTests.cs`、`src/UI/Viewer.cs` |

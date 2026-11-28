# External resource installation

CI packages contain the application, public application branding, fonts and notices. Private game assets and the compiled built-in shader bytecode (`KenomaShaders`) are not downloaded, committed, embedded in CI packages, or uploaded. A program package alone cannot open the editor without a matching resource pack.

Desktop packs should include `Assets/KenomaShaders` so the Startup shader prewarm can use the baked stages. Without it, the application falls back to compiling `Assets/Shaders` GLSL on first use; behavior is functionally similar but the startup timing guarantee does not apply.

Desktop CI Release archives (Windows x64, macOS ARM64, Linux x64) include a checksum-verified GPL-licensed FFmpeg CLI beside the executable, including `libx264` for MP4 output. No separate FFmpeg install is required for these archives. Private game assets remain external. Local builds may still use `KUROAKI_FFMPEG` or the system `PATH`.

- Windows / macOS / Linux: place your complete `Assets` directory beside the executable. Linux uses Vulkan; driver/device validation is still required.
- Android: on first launch choose your Android resource ZIP using the system file picker. The app validates and installs it to its private `Documents/Assets` directory. Existing resources are preserved under `Assets-previous-*` when replaced. Cancelling closes the initial setup. No storage-wide permission is required.
- iOS / iPadOS: the IPA is unsigned and requires the user's signing/installation method. Before creating the editor, the app opens the system Files picker. Select the iOS resource ZIP (containing `Assets/KenomaShaders`); the app copies and installs it in its Documents directory. Cancel exits startup. The app also exposes Documents via Finder file sharing.

Desktop, Android and iOS share one portable `Assets/KenomaShaders` pack. The stages are SPIR-V; the runtime converts them to MSL/DXIL for the active backend. `MobileShaders` and `SwitchShaders` are legacy paths and are no longer generated. Shader caches are tied to shader source; regenerate after shader changes. Custom songs may need their shaders baked as well.

## Definition compatibility

The 18.1.x resource packs use v2 object definitions. Use an application containing the 18.1.1 object system with those packs; older 18.0.1-based builds (including Nightly ab7e4bb) reject them. Changing the manifest version number does not migrate the behavior configuration. Rebuild the portable shader pack when updating shader sources.

## Prepare a private pack on a desktop

Install the .NET 10 SDK and the project's desktop shader compiler dependencies. From the repository:

```sh
python3 scripts/prepare_resource_pack.py desktop --assets /private/Assets --output /private/Kuroaki-Desktop-Assets.zip
python3 scripts/prepare_resource_pack.py android --assets /private/Assets --output /private/Kuroaki-Android-Assets.zip
python3 scripts/prepare_resource_pack.py ios --assets /private/Assets --output /private/Kuroaki-iOS-Assets.zip
```

These commands work locally and never upload the result. Add `--shader-root /private/Songs` for custom song shader sources. Keep these ZIPs private and outside Git. The supplied resource files are not permission to redistribute them.

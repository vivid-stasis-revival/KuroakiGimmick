# External resource installation

CI packages contain the application, public application branding, fonts/notices and built-in shader code. Game assets are private: they are neither downloaded by Actions nor committed, embedded, or uploaded with builds. A program package alone cannot open the editor without a matching resource pack.

- Windows / macOS / Linux: place your complete `Assets` directory beside the executable. Linux uses Vulkan; driver/device validation is still required.
- Switch: extract your Switch resource pack to `sdmc:/switch/KuroakiGimmick/Assets`. Copy the package's `mono` directory to the SD root too; it contains the required ICU runtime data. Launch the NRO in full application mode. The resource pack must include `SwitchShaders`.
- Android: on first launch choose your Android resource ZIP using the system file picker. The app validates and installs it to its private `Documents/Assets` directory. Existing resources are preserved under `Assets-previous-*` when replaced. Cancelling closes the initial setup. No storage-wide permission is required.
- iOS / iPadOS: the IPA is unsigned and requires the user's signing/installation method. In Files, copy the iOS resource pack's `Assets` folder into **On My iPad/iPhone → Kuroaki/Gimmick**, then tap Retry. The app also exposes Documents via Finder file sharing.

Android and iOS packs must include `MobileShaders` baked for the correct platform (SPIR-V for Android, MSL for iOS). Copying a raw desktop `Assets.zip` is insufficient. Shader caches are tied to shader source; regenerate after shader changes. Custom songs may need their shaders baked as well.

## Prepare a private pack on a desktop

Install .NET SDK/runtime 9 and the project's desktop shader compiler dependencies. From the repository:

```sh
python3 scripts/prepare_resource_pack.py android --assets /private/Assets --output /private/Kuroaki-Android-Assets.zip
python3 scripts/prepare_resource_pack.py ios --assets /private/Assets --output /private/Kuroaki-iOS-Assets.zip
python3 scripts/prepare_resource_pack.py switch --assets /private/Assets --output /private/Kuroaki-Switch-Assets.zip
```

These commands work locally and never upload the result. Add `--shader-root /private/Songs` for custom song shader sources. Keep these ZIPs private and outside Git. The supplied resource files are not permission to redistribute them.

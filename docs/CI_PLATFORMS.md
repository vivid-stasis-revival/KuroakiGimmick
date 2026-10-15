# Six-platform builds

`.github/workflows/nativeaot.yml` builds Windows x64, macOS arm64 and Linux x64 NativeAOT executables, Android arm64 signed APK, iOS arm64 unsigned IPA, and Switch NRO. Relevant pushes and manual runs trigger builds. Each platform produces a ZIP in Actions artifacts; a release is published only on the default branch when all six builds succeed. Feature branches produce Actions artifacts without publishing releases. Switch uses NroForge on a GitHub-hosted Ubuntu x64 runner; no self-hosted runner or repository readiness variable is required.

## Android signing

Repository Actions Secrets:

- `ANDROID_KEYSTORE_BASE64`
- `ANDROID_KEYSTORE_PASSWORD`
- `ANDROID_KEY_ALIAS`
- `ANDROID_KEY_PASSWORD`

The key is decoded only to runner temporary storage (mode 0600), removed in an `always()` step, and never uploaded. `apksigner verify` checks the resulting APK. Passwords are supplied using .NET Android's `env:` signing properties. This is a .NET Android project, not Gradle: `Kuroaki.Android.csproj` contains the effective signing configuration. There is no unused Gradle SigningConfig file. Do not enable shell tracing or publish diagnostic signing logs.

## Mobile toolchains

The handoff source targets .NET 10 mobile. CI pins SDK 10.0.101 / workload set 10.0.101.1, Xcode 26.2 for iOS, Android SDK 36 / NDK 29.0.14206865 and Java 21. SDK/runtime 9 is installed for the host shader baker and AOT audit. SDL 3.4.18 is downloaded from its official GitHub release with a SHA-256 check. Public built-in shaders are baked during build. Private resources are excluded using `--external-assets` and `KuroakiExternalAssets=true`.

Signing property reference: https://learn.microsoft.com/en-us/dotnet/android/building-apps/build-properties#androidsigningkeypass

The current Mac's Xcode 16.4 / iOS SDK 18.5 cannot validate this handoff's iOS 26.2 workload. A successful generic C# build is not evidence of an iOS or Android platform build. Hardware gesture tests remain separate from compile/packaging checks.

## Switch with NroForge

CI checks out [BennettNotFound/NroForge](https://github.com/BennettNotFound/NroForge) at audited commit `e41f2e68b212737da81ddc97fe5c7b881bb125e8` and invokes its local composite Action on `ubuntu-24.04`. Its Docker build uses `devkitpro/devkita64:20260219` and .NET SDK 9.0.102; the repository includes the matching Mono/LLVM/SDL3/Mesa SDK with hash verification. No manual handoff `dist/`, `KG_SWITCH_TOOLCHAIN_ROOT`, `KG_SWITCH_RUNNER_READY`, or `kg-switch` runner is needed by CI.

`scripts/ci/prepare_switch.sh` publishes the current application with `KuroakiSwitch=true` and `KuroakiExternalAssets=true`, then creates a profile using the version in the project file. NroForge links that output against its matching libnx BCL and performs LLVM PIC Full AOT, native linking and NRO validation. The historical scripts under `platform/switch` remain available for the original handoff environment; CI uses NroForge instead.

The Action receives no `assets-directory`. `scripts/ci/package_switch.sh` verifies the NRO hash and packages the SD layout, ICU data, build metadata, application notices and NroForge's third-party licenses. It creates an empty `/switch/KuroakiGimmick/Assets` directory for the user resource pack. Private game assets and prebaked private shaders remain outside CI. NroForge's own Hello NRO CI passed at the pinned revision; full K/G build results must be checked separately, and neither establishes hardware compatibility.

See [EXTERNAL_ASSETS.md](EXTERNAL_ASSETS.md) for separate private resource installation. No game resource download URL or public asset host is required.

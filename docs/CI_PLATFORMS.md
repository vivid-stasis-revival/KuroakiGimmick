# Six-platform builds

`.github/workflows/nativeaot.yml` builds Windows x64, macOS arm64 and Linux x64 NativeAOT executables, Android arm64 signed APK, iOS arm64 unsigned IPA, and Switch NRO. Relevant pushes and manual runs trigger builds. Each platform produces a ZIP in Actions artifacts; a release is published only when all six builds succeed. With the Switch runner unconfigured, five platform builds can run, Switch is explicitly reported pending, and the release job is skipped.

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

## Switch prerequisite

The uploaded handoff omitted `dist/`. Supply the full matching toolchain to a dedicated **Linux x64** runner with labels `self-hosted`, `Linux`, `X64`, `kg-switch`. An ARM64 Mac alone cannot execute its Linux x64 AOT compiler. Runner provisioning/registration is not performed by this change.

Install the matching devkitPro/libnx environment, export `DEVKITPRO`, and provide Python 3 with Pillow, rsync, zip, cmake, make and pkg-config. Set repository variable `KG_SWITCH_TOOLCHAIN_ROOT` to the full handoff root containing:

```
dist/handoff-switch-runtime
dist/switch-llvm-toolchain/runtime
dist/switch-deps/mono-nx
dist/switch-deps/SDL3-Switch
```

After validating this prepared runner, set `KG_SWITCH_RUNNER_READY=true`. The build copies required toolchain directories to an isolated clean checkout and rebuilds the current C# source, linked assemblies, full AOT objects and SDL. It never uses the handoff's old prebuilt application DLLs. Use a disposable runner restricted to trusted repository code; the workflow does not run on fork pull requests. The complete toolchain is still needed to validate this build path.

See [EXTERNAL_ASSETS.md](EXTERNAL_ASSETS.md) for separate private resource installation. No game resource download URL or public asset host is required.

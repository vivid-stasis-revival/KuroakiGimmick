#!/usr/bin/env python3
"""Build the SDL3/C# mobile ports without changing the desktop/Switch profiles."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tarfile
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[2]
SDL_VERSION = "3.4.18"
SDL_SHA256 = "9c75cf16330322c217dedd2e0609f1124f1b54b8633e763467b4684d0f4334a3"

def run(*args, **kwargs):
    subprocess.run([str(a) for a in args], cwd=ROOT, check=True, **kwargs)

def sdl_source():
    deps = ROOT / "dist/mobile-deps"
    archive = deps / f"SDL3-{SDL_VERSION}.tar.gz"
    deps.mkdir(parents=True, exist_ok=True)
    if not archive.exists():
        urllib.request.urlretrieve(f"https://github.com/libsdl-org/SDL/releases/download/release-{SDL_VERSION}/{archive.name}", archive)
    if hashlib.sha256(archive.read_bytes()).hexdigest() != SDL_SHA256:
        raise RuntimeError("SDL source archive checksum mismatch")
    source = deps / f"SDL3-{SDL_VERSION}"
    if not source.exists():
        with tarfile.open(archive) as tar:
            tar.extractall(deps, filter="data")
    metal = source / "src/gpu/metal/SDL_gpu_metal.m"
    if "Altered for K/G mobile" not in metal.read_text():
        subprocess.run(["patch", "-p1", "-i", str(ROOT / "platform/mobile/patches/SDL3-ios-simulator-metal.patch")], cwd=source, check=True)
    return source

def native(platform, source, jobs, sdk, ndk, java, simulator):
    rid = "android-arm64" if platform == "android" else ("iossimulator-arm64" if simulator else "ios-arm64")
    target = ROOT / "dist/mobile-native" / rid
    args = ["cmake", "-S", source, "-B", target, "-G", "Ninja", "-DCMAKE_BUILD_TYPE=Release",
            "-DSDL_TESTS=OFF", "-DSDL_TEST_LIBRARY=OFF", "-DSDL_EXAMPLES=OFF"]
    if platform == "android":
        args += [f"-DCMAKE_TOOLCHAIN_FILE={ndk}/build/cmake/android.toolchain.cmake", "-DANDROID_ABI=arm64-v8a",
                 "-DANDROID_PLATFORM=android-26", "-DSDL_SHARED=ON", "-DSDL_STATIC=OFF"]
        jar_root = ROOT / "dist/mobile-native/android"
        classes = jar_root / "java"
        if classes.exists(): shutil.rmtree(classes)
        classes.mkdir(parents=True)
        run(java / "bin/javac", "-encoding", "UTF-8", "-source", "11", "-target", "11", "-classpath",
            sdk / "platforms/android-36/android.jar", "-d", classes,
            *sorted((source / "android-project/app/src/main/java/org/libsdl/app").glob("*.java")))
        run(java / "bin/jar", "cf", jar_root / "SDL3.jar", "-C", classes, ".")
    else:
        args += ["-DCMAKE_SYSTEM_NAME=iOS", "-DCMAKE_OSX_ARCHITECTURES=arm64", "-DCMAKE_OSX_DEPLOYMENT_TARGET=15.0",
                 f"-DCMAKE_OSX_SYSROOT={'iphonesimulator' if simulator else 'iphoneos'}", "-DSDL_SHARED=OFF", "-DSDL_STATIC=ON"]
    run(*args)
    run("cmake", "--build", target, "-j", jobs)
    return rid

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("platform", choices=["android", "ios", "both"])
    parser.add_argument("--assets", type=Path, default=ROOT / "Assets")
    parser.add_argument("--external-assets", action="store_true", help="Bundle public app files only; user installs game resources")
    parser.add_argument("--shader-root", type=Path, action="append", default=[])
    parser.add_argument("--output", type=Path, required=True, help="New directory for the installable artifacts")
    parser.add_argument("--dotnet", default=str(ROOT / "dist/mobile-dotnet/dotnet") if (ROOT / "dist/mobile-dotnet/dotnet").exists() else "dotnet")
    parser.add_argument("--baker-dotnet", default="dotnet")
    candidates = [Path(os.environ["ANDROID_HOME"])] if os.environ.get("ANDROID_HOME") else []
    candidates.append(Path.home() / "Library/Android/sdk")
    default_sdk = next((p for p in candidates if (p / "platforms/android-36/android.jar").exists()), candidates[-1])
    parser.add_argument("--android-sdk", type=Path, default=default_sdk)
    parser.add_argument("--ndk", type=Path)
    parser.add_argument("--java", type=Path, default=Path(os.environ.get("JAVA_HOME", "/Library/Java/JavaVirtualMachines/jdk-21.jdk/Contents/Home")))
    parser.add_argument("--jobs", type=int, default=8)
    parser.add_argument("--simulator", action="store_true", help="Build iOS simulator .app instead of device IPA")
    args = parser.parse_args()
    args.assets = args.assets.resolve()
    args.output = args.output.resolve()
    if args.output.exists(): parser.error("Output directory must not already exist")
    if args.external_assets:
        import sys
        sys.path.insert(0, str(ROOT / "scripts/ci"))
        from stage_public_assets import stage
        args.assets = args.output.parent / (args.output.name + "-public-assets")
        stage(args.assets)
        if args.shader_root: parser.error("External-assets CI must not embed custom shader roots")
    for required in (["App/Kuroaki.png"] if args.external_assets else ["App/Kuroaki.png", "Brand/kuroaki.png", "Shaders", "GameCommon"]):
        if not (args.assets / required).exists(): parser.error(f"Complete Assets required; missing {required}")
    if args.simulator and args.platform != "ios": parser.error("--simulator requires platform ios")
    if args.platform in ("android", "both"):
        if not (args.android_sdk / "platforms/android-36/android.jar").exists(): parser.error("Android SDK 36 not found; pass --android-sdk")
        if not ((args.ndk or args.android_sdk / "ndk/29.0.14206865") / "build/cmake/android.toolchain.cmake").exists(): parser.error("Android NDK not found; pass --ndk")
    args.output.mkdir(parents=True)
    source = sdl_source()
    baker = ROOT / "dist/mobile-baker"
    run(args.baker_dotnet, "build", ROOT / "KuroakiGimmick.csproj", "-c", "Release", "-p:KuroakiShaderBaker=true",
        "-p:KuroakiExternalAssets=true", "-p:BaseIntermediateOutputPath=dist/mobile-baker-obj/", "-p:NuGetLockFilePath=dist/mobile-baker.lock.json", "-o", baker)
    platforms = ["android", "ios"] if args.platform == "both" else [args.platform]
    artifacts = []
    for platform in platforms:
        assets = ROOT / "dist/mobile-assets" / platform
        if assets.exists(): shutil.rmtree(assets)
        shutil.copytree(args.assets, assets, ignore=shutil.ignore_patterns("SwitchShaders", "MobileShaders"))
        # Copy the selected complete Assets next to the host baker so it scans the same shaders.
        if (baker / "Assets").exists(): shutil.rmtree(baker / "Assets")
        shutil.copytree(args.assets, baker / "Assets", ignore=shutil.ignore_patterns("SwitchShaders", "MobileShaders"))
        run(args.baker_dotnet, baker / "KuroakiGimmick.dll", "--bake-mobile-shaders", platform, assets / "MobileShaders",
            *[p.resolve() for p in args.shader_root])
        digest = hashlib.sha256()
        for file in sorted(assets.rglob("*")):
            if file.is_file():
                digest.update(str(file.relative_to(assets)).encode())
                digest.update(hashlib.sha256(file.read_bytes()).digest())
        (assets / "mobile-assets.version").write_text(digest.hexdigest())
        ndk = args.ndk or args.android_sdk / "ndk/29.0.14206865"
        rid = native(platform, source, args.jobs, args.android_sdk, ndk, args.java, args.simulator)
        project = ROOT / f"platform/mobile/{platform}/Kuroaki.{'Android' if platform == 'android' else 'iOS'}.csproj"
        if platform == "ios":
            # The SDK can retain framework AOT objects after Microsoft.iOS.dll
            # is relinked with a new MVID. Always compile a consistent device set.
            for name in ("obj", "bin"):
                directory = project.parent / name
                if directory.exists(): shutil.rmtree(directory)
        if platform == "ios":
            icon = project.parent / "Assets.xcassets/AppIcon.appiconset/icon.png"
            run("sips", "-s", "format", "png", "-z", "1024", "1024", args.assets / "App/Kuroaki.png", "--out", icon)
        build_args = [args.dotnet, "build", project, "-c", "Release"]
        if args.external_assets: build_args.append("-p:KuroakiExternalAssets=true")
        if platform == "android":
            build_args += ["-t:SignAndroidPackage", f"-p:AndroidSdkDirectory={args.android_sdk}", f"-p:JavaSdkDirectory={args.java}"]
        else:
            build_args += [f"-p:RuntimeIdentifier={rid}", "-p:EnableCodeSigning=false"]
            if args.simulator: build_args += ["-p:EnableCodeSigning=true", "-p:CodesignKey=-"]
        run(*build_args)
        build_dir = project.parent / "bin/Release" / f"net10.0-{'android' if platform == 'android' else 'ios'}" / rid
        if platform == "android":
            packages = list(build_dir.glob("*-Signed.apk"))
            if len(packages) != 1: raise RuntimeError(f"Expected one signed APK in {build_dir}")
            output = args.output / "KuroakiGimmick-Android-arm64-r1.apk"
            shutil.copy2(packages[0], output)
        elif args.simulator:
            output = args.output / "KuroakiGimmick.app"
            shutil.copytree(build_dir / "KuroakiGimmick.app", output)
        else:
            output = args.output / "KuroakiGimmick-iOS-arm64-r1-unsigned.ipa"
            app = build_dir / "KuroakiGimmick.app"
            run(args.baker_dotnet, "run", "--project", ROOT / "platform/mobile/tools/AotAudit/AotAudit.csproj", "-c", "Release", "--", app)
            if (app / "embedded.mobileprovision").exists() or (app / "_CodeSignature").exists():
                raise RuntimeError("Expected unsigned iOS application")
            with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED) as archive:
                for file in sorted(app.rglob("*")):
                    if file.is_file(): archive.write(file, Path("Payload/KuroakiGimmick.app") / file.relative_to(app))
        if output.is_file():
            artifacts.append({"file": output.name, "bytes": output.stat().st_size, "sha256": hashlib.sha256(output.read_bytes()).hexdigest()})
    (args.output / "BUILD-INFO.json").write_text(json.dumps({"port": "Mobile experimental r1", "sdl": SDL_VERSION,
        "artifacts": artifacts, "external_assets": args.external_assets, "physical_device_tested": False}, indent=2) + "\n")
    shutil.copytree(ROOT / "ThirdParty", args.output / "ThirdParty")
    shutil.copy2(ROOT / "LICENSE", args.output / "LICENSE")
    shutil.copy2(ROOT / "THIRD_PARTY_NOTICES.md", args.output / "THIRD_PARTY_NOTICES.md")
    shutil.copy2(ROOT / "platform/mobile/README.md", args.output / "README.md")
    shutil.copy2(ROOT / "platform/mobile/VALIDATION.md", args.output / "VALIDATION.md")
    print(f"Mobile artifacts: {args.output}")

if __name__ == "__main__": main()

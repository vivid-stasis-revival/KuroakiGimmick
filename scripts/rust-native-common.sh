#!/bin/bash
# Build and stage the Rust Optimization Engine for the *target* RID.
# Shared by managed and NativeAOT publishers; Bash 3.2 compatible (macOS).
# Fail closed: never produce a release archive without the correct native library.

KG_RUST_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

kg_rust_publish() {
    local rid="$1" output="$2" host target library source installed
    command -v cargo >/dev/null 2>&1 || { echo 'Rust publishing requires cargo (install the Rust toolchain).' >&2; return 1; }
    command -v rustc >/dev/null 2>&1 || { echo 'Rust publishing requires rustc.' >&2; return 1; }
    command -v rustup >/dev/null 2>&1 || { echo 'Rust publishing requires rustup to install the target toolchain.' >&2; return 1; }
    host="$(rustc -vV | sed -n 's/^host: //p')"
    [ -n "$host" ] || { echo 'Unable to determine rustc host target.' >&2; return 1; }

    case "$rid" in
        osx-arm64) target=aarch64-apple-darwin; library=libkg_native_core.dylib ;;
        osx-x64) target=x86_64-apple-darwin; library=libkg_native_core.dylib ;;
        win-x64)
            library=kg_native_core.dll
            case "$host" in
                x86_64-pc-windows-*) target="$host" ;;
                *) target=x86_64-pc-windows-gnu ;;
            esac
            ;;
        win-arm64) target=aarch64-pc-windows-msvc; library=kg_native_core.dll ;;
        *) echo "Unsupported Rust publishing RID: $rid" >&2; return 2 ;;
    esac

    case "$target" in
        *apple-darwin)
            [ "$(uname -s)" = Darwin ] || { echo "Rust target $target requires a macOS build host." >&2; return 2; }
            ;;
        aarch64-pc-windows-msvc)
            case "$(uname -s)" in
                MINGW*|MSYS*|CYGWIN*) ;;
                *) echo 'Windows ARM64 Rust DLL requires a Windows host with MSVC ARM64 build tools.' >&2; return 2 ;;
            esac
            ;;
        x86_64-pc-windows-gnu)
            case "$(uname -s)" in
                MINGW*|MSYS*|CYGWIN*) ;;
                *)
                    command -v x86_64-w64-mingw32-gcc >/dev/null 2>&1 || {
                        echo 'Windows x64 cross-build requires x86_64-w64-mingw32-gcc (mingw-w64).' >&2
                        return 2
                    }
                    export CARGO_TARGET_X86_64_PC_WINDOWS_GNU_LINKER="${CARGO_TARGET_X86_64_PC_WINDOWS_GNU_LINKER:-x86_64-w64-mingw32-gcc}"
                    ;;
            esac
            ;;
    esac

    installed="$(rustup target list --installed)"
    if ! printf '%s\n' "$installed" | grep -Fxq "$target"; then
        echo "Installing Rust target: $target"
        rustup target add "$target" || { echo "Failed to install Rust target $target." >&2; return 1; }
    fi

    echo "Building Rust Optimization Engine for $rid ($target)"
    cargo build --manifest-path "$KG_RUST_ROOT/rust/kg-native-core/Cargo.toml" --release --target "$target" \
        --target-dir "$KG_RUST_ROOT/rust/kg-native-core/target" || return 1
    source="$KG_RUST_ROOT/rust/kg-native-core/target/$target/release/$library"
    [ -s "$source" ] || { echo "Missing Rust native artifact: $source" >&2; return 1; }
    mkdir -p "$output"
    cp "$source" "$output/$library" || return 1
    [ -s "$output/$library" ] || { echo "Rust native artifact not staged: $output/$library" >&2; return 1; }
    echo "Staged Rust Optimization Engine: $output/$library"
}

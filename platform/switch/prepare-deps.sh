#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
source "$SCRIPT_DIR/versions.env"
DEPS_DIR=${KG_SWITCH_DEPS:-"$SCRIPT_DIR/../../dist/switch-deps"}
mkdir -p "$DEPS_DIR"
checkout() {
    local name=$1 url=$2 sha=$3 path="$DEPS_DIR/$1"
    if [[ ! -d "$path/.git" ]]; then
        git clone --no-checkout "$url" "$path"
        git -C "$path" checkout --detach "$sha"
    fi
    if [[ $(git -C "$path" rev-parse HEAD) != "$sha" ]]; then
        printf 'Dependency %s is not at the checked commit %s. Use a fresh KG_SWITCH_DEPS directory.\n' "$name" "$sha" >&2
        exit 1
    fi
}
apply_once() {
    local repo=$1 patch=$2
    if git -C "$repo" apply --reverse --check "$patch" 2>/dev/null; then return; fi
    git -C "$repo" apply --check "$patch"
    git -C "$repo" apply "$patch"
}
checkout mono-nx https://github.com/exelix11/mono-nx.git "$MONO_NX_SHA"
checkout SDL3-Switch https://github.com/SnepOMatic/SDL3-Switch.git "$SDL3_SWITCH_SHA"
apply_once "$DEPS_DIR/mono-nx" "$SCRIPT_DIR/patches/mono-nx-kg.patch"
apply_once "$DEPS_DIR/SDL3-Switch" "$SCRIPT_DIR/patches/sdl3-switch-vulkan.patch"
apply_once "$DEPS_DIR/SDL3-Switch" "$SCRIPT_DIR/patches/sdl3-switch-gpu-diagnostics.patch"
apply_once "$DEPS_DIR/SDL3-Switch" "$SCRIPT_DIR/patches/sdl3-switch-audio.patch"
printf 'Dependencies prepared in %s\n' "$DEPS_DIR"
printf 'Install or build the checked mesa-switch SDK separately; no proprietary SDK is used.\n'

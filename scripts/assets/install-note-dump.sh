#!/bin/bash
# Install a VIVIDSTASIS note dump into a Kuroaki/Gimmick source/publish tree.
# Prefers a full v2 NoteSkinFull/NotesExact dump; otherwise installs a compact Notes pack.
set -euo pipefail

fail(){ printf 'error: %s\n' "$*" >&2; exit 1; }
[[ $# -ge 1 && $# -le 2 ]] || fail 'usage: install-note-dump.sh DUMP.zip|DIR [KUROAKI_ROOT]'
input=$1
root=${2:-"$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"}
[[ -e "$input" ]] || fail "not found: $input"
mkdir -p "$root/Assets"
work=''
cleanup(){ [[ -n "$work" && -d "$work" ]] && rm -rf "$work"; }
trap cleanup EXIT

if [[ -f "$input" ]]; then
  command -v unzip >/dev/null || fail 'unzip is required for ZIP input'
  work=$(mktemp -d "${TMPDIR:-/tmp}/kuroaki-notes.XXXXXX")
  unzip -q "$input" -d "$work"
  source_root=$work
else
  source_root=$(cd "$input" && pwd)
fi

# Whole forensic dump. Copying the whole folder keeps metadata/frames.json and
# metadata/sprites.json, which are required to place raw Source crops exactly.
exact_manifest=$(find "$source_root" -type f -path '*/NoteSkinFull/NotesExact/manifest.json' -print -quit 2>/dev/null || true)
if [[ -n "$exact_manifest" ]]; then
  full_root=$(dirname "$(dirname "$exact_manifest")")
  dest="$root/Assets/NoteSkinFull"
  if [[ -e "$dest" ]]; then mv "$dest" "$dest.previous.$(date +%Y%m%d_%H%M%S)"; fi
  cp -R "$full_root" "$dest"
  printf 'Installed exact v2 note skin: %s\n' "$dest"
  printf 'Press R in Kuroaki/Gimmick to reload.\n'
  exit 0
fi

# Direct NotesExact folder. Metadata is copied too when it is next to it.
exact_manifest=$(find "$source_root" -type f -path '*/NotesExact/manifest.json' -print -quit 2>/dev/null || true)
if [[ -n "$exact_manifest" ]]; then
  exact_root=$(dirname "$exact_manifest")
  parent=$(dirname "$exact_root")
  dest="$root/Assets/NotesExact"
  if [[ -e "$dest" ]]; then mv "$dest" "$dest.previous.$(date +%Y%m%d_%H%M%S)"; fi
  cp -R "$exact_root" "$dest"
  if [[ -d "$parent/metadata" ]]; then
    mkdir -p "$root/Assets/metadata"
    cp -R "$parent/metadata/." "$root/Assets/metadata/"
  fi
  printf 'Installed exact-lane note skin: %s\n' "$dest"
  printf 'Press R in Kuroaki/Gimmick to reload.\n'
  exit 0
fi

# Compact v1 / legacy v2 / old vsnotes directory.
manifest=$(find "$source_root" -type f -name manifest.json -print 2>/dev/null | while IFS= read -r f; do
  d=$(dirname "$f")
  [[ -f "$d/chip.png" && -f "$d/bumper_L.png" && -f "$d/hold_body_L.png" ]] && { printf '%s\n' "$f"; break; }
done)
if [[ -z "$manifest" ]]; then
  # Old pack can have no manifest at all.
  candidate=$(find "$source_root" -type f -name chip.png -print -quit 2>/dev/null || true)
  [[ -n "$candidate" ]] || fail 'no supported note dump found'
  compact_root=$(dirname "$candidate")
else
  compact_root=$(dirname "$manifest")
fi
dest="$root/Assets/Notes"
if [[ -e "$dest" ]]; then mv "$dest" "$dest.previous.$(date +%Y%m%d_%H%M%S)"; fi
mkdir -p "$dest"
for name in chip.png chip_mine.png hold_head.png hold_body_L.png hold_body_R.png bumper_L.png bumper_M.png bumper_R.png bumper_mine.png judge_bumper_L.png judge_bumper_M.png judge_bumper_R.png; do
  [[ -f "$compact_root/$name" ]] || fail "compact pack is missing $name"
  cp "$compact_root/$name" "$dest/$name"
done
[[ -f "$compact_root/manifest.json" ]] && cp "$compact_root/manifest.json" "$dest/manifest.json"
printf 'Installed compact/legacy note skin: %s\n' "$dest"
printf 'Press R in Kuroaki/Gimmick to reload.\n'

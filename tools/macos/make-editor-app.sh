#!/bin/bash
# Refreshes a Dock-ready app bundle from an editor build output, in place so a pinned Dock icon keeps working.
# Usage: make-editor-app.sh <build-output> <app> [signing-identity]
set -euo pipefail

out=${1%/}
app=$2
identity=${3:-}
repo_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
[[ -x $out/Voltage.Editor ]] || { echo "make-editor-app: no Voltage.Editor in $out" >&2; exit 1; }

contents="$app/Contents"
mkdir -p "$contents/MacOS" "$contents/Resources"
rsync -a --delete "$out/" "$contents/MacOS/"

work=$(mktemp -d)
trap 'rm -rf -- "$work"' EXIT

icon_source="$repo_dir/Voltage.Editor/Icon.ico"
icns="$contents/Resources/Voltage.icns"
if [[ ! -f $icns || $icon_source -nt $icns ]]; then
    mkdir "$work/Voltage.iconset"
    sips -s format png "$icon_source" --out "$work/icon.png" >/dev/null
    for size in 16 32 128; do
        sips -z "$size" "$size" "$work/icon.png" --out "$work/Voltage.iconset/icon_${size}x${size}.png" >/dev/null
        sips -z $((size * 2)) $((size * 2)) "$work/icon.png" --out "$work/Voltage.iconset/icon_${size}x${size}@2x.png" >/dev/null
    done
    cp "$work/icon.png" "$work/Voltage.iconset/icon_256x256.png"
    iconutil -c icns "$work/Voltage.iconset" -o "$icns"
fi

# A separate identifier keeps this build's Documents permission apart from an installed release.
version=$(sed -n 's/.*Engine = "\(.*\)";.*/\1/p' "$repo_dir/Voltage.Engine/VoltageVersion.cs")
plist="$work/Info.plist"
sed "s/__VERSION__/${version:-0.0.0}/g" "$repo_dir/tools/macos/Info.plist" > "$plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleIdentifier io.github.voltageengine.editor.dev" "$plist"
/usr/libexec/PlistBuddy -c "Delete :CFBundleIconFile" "$plist" 2>/dev/null || true
/usr/libexec/PlistBuddy -c "Add :CFBundleIconFile string Voltage" "$plist"
# The editor takes projects from its arguments, not Finder's open-document events.
/usr/libexec/PlistBuddy -c "Delete :CFBundleDocumentTypes" "$plist" 2>/dev/null || true
if ! cmp -s "$plist" "$contents/Info.plist"; then
    cp "$plist" "$contents/Info.plist"
fi

# macOS ties folder permissions to the signature, so only a stable identity keeps them across rebuilds.
if [[ -n $identity ]]; then
    codesign --force --deep --sign "$identity" "$app"
fi

# Finder dates an .app by its bundle folder, which the in-place refresh above never touches, so without this the
# app keeps showing the day it was first made. Touching it also tells Launch Services to re-read Info.plist.
touch "$app"

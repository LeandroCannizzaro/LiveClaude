#!/usr/bin/env bash
#
# Builds the macOS packages: a LiveClaude.app bundle inside a .dmg, plus a plain tar.gz.
#
# The bundle path is what a LaunchAgent registers, and an .app is replaced in place on update, so the
# registration survives — the same property /opt/liveclaude gives on Linux and the stable copy gives
# on Windows.
#
# Usage: build-macos-packages.sh <version> <rid> <output-directory>
#   e.g. build-macos-packages.sh 1.1.0 osx-arm64 artifacts/packages

set -euo pipefail

VERSION="${1:?version required}"
RID="${2:?runtime identifier required, e.g. osx-arm64}"
OUTPUT="${3:?output directory required}"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

case "$RID" in
  osx-x64|osx-arm64) ;;
  *) echo "unsupported runtime identifier: $RID" >&2; exit 2 ;;
esac

mkdir -p "$OUTPUT"

echo "==> publishing $RID"
PUBLISH="$STAGE/publish"
dotnet publish "$ROOT/src/LiveClaude.App/LiveClaude.App.csproj" \
  -c Release -r "$RID" --self-contained true \
  -p:Version="$VERSION" \
  -o "$PUBLISH"

dotnet publish "$ROOT/src/LiveClaude.Service/LiveClaude.Service.csproj" \
  -c Release -r "$RID" --self-contained true \
  -p:Version="$VERSION" \
  -o "$PUBLISH"

chmod +x "$PUBLISH/LiveClaude" "$PUBLISH/LiveClaude.Service"

echo "==> tar.gz"
tar -czf "$OUTPUT/LiveClaude-$VERSION-$RID.tar.gz" -C "$PUBLISH" .

# ---------------------------------------------------------------- app bundle

echo "==> app bundle"
APP="$STAGE/LiveClaude.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

cp -r "$PUBLISH/." "$APP/Contents/MacOS/"

# LSUIElement is deliberately absent: this is a real windowed application, not an agent. The
# supervisor it registers is the background part, and launchd owns that.
cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
  <dict>
    <key>CFBundleName</key>
    <string>LiveClaude</string>
    <key>CFBundleDisplayName</key>
    <string>LiveClaude</string>
    <key>CFBundleIdentifier</key>
    <string>com.leandrocannizzaro.liveclaude</string>
    <key>CFBundleVersion</key>
    <string>$VERSION</string>
    <key>CFBundleShortVersionString</key>
    <string>$VERSION</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleExecutable</key>
    <string>LiveClaude</string>
    <key>CFBundleIconFile</key>
    <string>liveclaude.icns</string>
    <key>LSMinimumSystemVersion</key>
    <string>12.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSHumanReadableCopyright</key>
    <string>Copyright (c) Leandro Cannizzaro. MIT licensed.</string>
  </dict>
</plist>
PLIST

# iconutil wants a .iconset directory; sips resizes the PNG the app already ships.
if command -v iconutil >/dev/null 2>&1 && command -v sips >/dev/null 2>&1; then
  ICONSET="$STAGE/liveclaude.iconset"
  mkdir -p "$ICONSET"
  SOURCE="$ROOT/src/LiveClaude.App/Assets/liveclaude.png"

  for size in 16 32 128 256 512; do
    sips -z $size $size "$SOURCE" --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
    sips -z $((size * 2)) $((size * 2)) "$SOURCE" --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
  done

  iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/liveclaude.icns"
else
  echo "    (iconutil/sips unavailable — bundle ships without an icon)"
fi

# ---------------------------------------------------------------- dmg

echo "==> dmg"
DMG_ROOT="$STAGE/dmg"
mkdir -p "$DMG_ROOT"
cp -R "$APP" "$DMG_ROOT/"
ln -s /Applications "$DMG_ROOT/Applications"

# Unsigned, so Gatekeeper asks the first time. Notarisation needs a Developer ID account and is
# tracked separately; documenting the right-click-Open route is the honest interim answer.
cat > "$DMG_ROOT/READ ME FIRST.txt" <<'README'
LiveClaude is not signed with an Apple Developer ID yet, so macOS will refuse to
open it on the first try.

To run it: drag LiveClaude to Applications, then right-click (or Control-click)
the app and choose Open. Confirm once. Every later launch works normally.

If you prefer the terminal:
    xattr -dr com.apple.quarantine /Applications/LiveClaude.app
README

hdiutil create \
  -volname "LiveClaude $VERSION" \
  -srcfolder "$DMG_ROOT" \
  -ov -format UDZO \
  "$OUTPUT/LiveClaude-$VERSION-$RID.dmg"

echo "==> done: $(ls "$OUTPUT")"

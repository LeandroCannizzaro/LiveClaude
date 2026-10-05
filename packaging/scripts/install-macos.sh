#!/usr/bin/env sh
#
# Installs LiveClaude on macOS.
#
#   curl -fsSL https://leandrocannizzaro.github.io/LiveClaude/install-macos.sh | sh
#
# Downloads the .dmg for this Mac from the latest GitHub release, copies LiveClaude.app to
# /Applications, and removes the quarantine flag — which is the whole reason this script is worth
# having. The app is not notarised yet, so a plain download refuses to open until somebody knows to
# right-click it; doing it here means the first launch simply works.
#
# Nothing here needs a .NET runtime: the app is self-contained.

set -eu

REPO="LeandroCannizzaro/LiveClaude"
API="https://api.github.com/repos/$REPO/releases/latest"
APP="LiveClaude.app"

say() { printf '%s\n' "$*"; }
die() { printf 'error: %s\n' "$*" >&2; exit 1; }

[ "$(uname -s)" = "Darwin" ] || die "This is the macOS installer. On Linux use https://leandrocannizzaro.github.io/LiveClaude/install.sh"

case "$(uname -m)" in
    arm64)  RID=osx-arm64 ;;
    x86_64) RID=osx-x64 ;;
    *) die "Unsupported architecture: $(uname -m)." ;;
esac

say "LiveClaude installer — $RID"

command -v curl >/dev/null 2>&1 || die "curl is required."

say "Looking up the latest release…"
RELEASE_JSON=$(curl -fsSL "$API") || die "could not reach the GitHub API."

VERSION=$(printf '%s' "$RELEASE_JSON" | sed -n 's/.*"tag_name"[[:space:]]*:[[:space:]]*"v\{0,1\}\([^"]*\)".*/\1/p' | head -1)
[ -n "$VERSION" ] || die "could not read the version from the GitHub API response."

URL=$(printf '%s' "$RELEASE_JSON" \
    | tr ',' '\n' \
    | sed -n 's/.*"browser_download_url"[[:space:]]*:[[:space:]]*"\([^"]*'"$RID"'\.dmg\)".*/\1/p' \
    | head -1)
[ -n "$URL" ] || die "no .dmg for $RID in release $VERSION."

say "Latest release: $VERSION"

TMP=$(mktemp -d)
MOUNT="$TMP/mnt"
# Detach before removing, or the mount point stays busy and the temp directory lingers.
cleanup() {
    [ -d "$MOUNT" ] && hdiutil detach "$MOUNT" -quiet 2>/dev/null || true
    rm -rf "$TMP"
}
trap cleanup EXIT INT TERM

say "Downloading $(basename "$URL")…"
curl -fsSL "$URL" -o "$TMP/LiveClaude.dmg"

say "Mounting…"
mkdir -p "$MOUNT"
hdiutil attach "$TMP/LiveClaude.dmg" -mountpoint "$MOUNT" -nobrowse -quiet

[ -d "$MOUNT/$APP" ] || die "the disk image does not contain $APP."

# /Applications needs root unless the user owns it, which on a single-user Mac they usually do.
if [ -w /Applications ]; then
    SUDO=""
else
    SUDO="sudo"
    say "Installing to /Applications; sudo will ask for your password."
fi

if [ -d "/Applications/$APP" ]; then
    say "Replacing the existing /Applications/$APP…"
    $SUDO rm -rf "/Applications/$APP"
fi

say "Copying to /Applications…"
$SUDO cp -R "$MOUNT/$APP" /Applications/

# The point of the script. Without this, Gatekeeper refuses the first launch because the app is not
# signed with a Developer ID, and the error looks like a broken download rather than a policy.
say "Clearing the quarantine flag…"
$SUDO xattr -dr com.apple.quarantine "/Applications/$APP" 2>/dev/null || \
    say "  (could not clear it — if the first launch is refused, right-click the app and choose Open)"

say ""
say "LiveClaude $VERSION is installed in /Applications."
say ""
say "  open -a LiveClaude                                          open the app"
say "  /Applications/$APP/Contents/MacOS/LiveClaude.Service status   see what is registered"
say ""
say "Autostart is a LaunchAgent, installed from the app under 'Service & startup'."
say ""

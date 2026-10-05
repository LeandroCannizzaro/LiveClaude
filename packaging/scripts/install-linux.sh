#!/usr/bin/env sh
#
# Installs LiveClaude on Linux.
#
#   curl -fsSL https://leandrocannizzaro.github.io/LiveClaude/install.sh | sh
#
# Picks the right package for this machine — .deb, .rpm, or a plain tarball when neither package
# manager is present — downloads it from the latest GitHub release and installs it. Nothing here
# needs a .NET runtime: every package is self-contained.
#
# POSIX sh on purpose: this is the one script that has to run before anything is installed, on a
# distribution we know nothing about.

set -eu

REPO="LeandroCannizzaro/LiveClaude"
API="https://api.github.com/repos/$REPO/releases/latest"

say()  { printf '%s\n' "$*"; }
die()  { printf 'error: %s\n' "$*" >&2; exit 1; }

need() {
    command -v "$1" >/dev/null 2>&1 || die "$1 is required but not installed."
}

need uname

# ---------------------------------------------------------------- what are we on

case "$(uname -s)" in
    Linux) ;;
    Darwin) die "This is the Linux installer. On macOS download the .dmg from https://github.com/$REPO/releases/latest" ;;
    *) die "Unsupported system: $(uname -s)" ;;
esac

case "$(uname -m)" in
    x86_64|amd64)  RID=linux-x64;   DEB_ARCH=amd64; RPM_ARCH=x86_64 ;;
    aarch64|arm64) RID=linux-arm64; DEB_ARCH=arm64; RPM_ARCH=aarch64 ;;
    *) die "Unsupported architecture: $(uname -m). LiveClaude ships x86_64 and arm64." ;;
esac

say "LiveClaude installer — $RID"

# ---------------------------------------------------------------- how to download

if command -v curl >/dev/null 2>&1; then
    fetch() { curl -fsSL "$1" -o "$2"; }
    read_url() { curl -fsSL "$1"; }
elif command -v wget >/dev/null 2>&1; then
    fetch() { wget -qO "$2" "$1"; }
    read_url() { wget -qO- "$1"; }
else
    die "neither curl nor wget is available."
fi

# ---------------------------------------------------------------- which release

say "Looking up the latest release…"
RELEASE_JSON=$(read_url "$API") || die "could not reach the GitHub API."

# Parsed with sed rather than jq: jq is not installed by default on most distributions, and asking
# for it before the first install would defeat the point of a one-liner.
VERSION=$(printf '%s' "$RELEASE_JSON" | sed -n 's/.*"tag_name"[[:space:]]*:[[:space:]]*"v\{0,1\}\([^"]*\)".*/\1/p' | head -1)
[ -n "$VERSION" ] || die "could not read the version from the GitHub API response."

say "Latest release: $VERSION"

asset_url() {
    printf '%s' "$RELEASE_JSON" \
        | tr ',' '\n' \
        | sed -n 's/.*"browser_download_url"[[:space:]]*:[[:space:]]*"\([^"]*'"$1"'\)".*/\1/p' \
        | head -1
}

# ---------------------------------------------------------------- privilege

if [ "$(id -u)" -eq 0 ]; then
    SUDO=""
elif command -v sudo >/dev/null 2>&1; then
    SUDO="sudo"
    say "Installing system-wide; sudo will ask for your password."
else
    die "this needs root, and sudo is not available. Re-run as root, or use the tarball from the releases page."
fi

TMP=$(mktemp -d)
# shellcheck disable=SC2064
trap "rm -rf '$TMP'" EXIT INT TERM

# ---------------------------------------------------------------- install

if command -v apt-get >/dev/null 2>&1 || command -v dpkg >/dev/null 2>&1; then
    URL=$(asset_url "_${DEB_ARCH}\.deb")
    [ -n "$URL" ] || die "no .deb for $DEB_ARCH in release $VERSION."

    say "Downloading $(basename "$URL")…"
    fetch "$URL" "$TMP/liveclaude.deb"

    say "Installing…"
    # apt resolves dependencies and is happy with a local path; dpkg is the fallback.
    if command -v apt-get >/dev/null 2>&1; then
        $SUDO apt-get install -y "$TMP/liveclaude.deb"
    else
        $SUDO dpkg -i "$TMP/liveclaude.deb"
    fi

elif command -v dnf >/dev/null 2>&1 || command -v zypper >/dev/null 2>&1 || command -v rpm >/dev/null 2>&1; then
    URL=$(asset_url "\.${RPM_ARCH}\.rpm")
    [ -n "$URL" ] || die "no .rpm for $RPM_ARCH in release $VERSION."

    say "Downloading $(basename "$URL")…"
    fetch "$URL" "$TMP/liveclaude.rpm"

    say "Installing…"
    if command -v dnf >/dev/null 2>&1; then
        $SUDO dnf install -y "$TMP/liveclaude.rpm"
    elif command -v zypper >/dev/null 2>&1; then
        $SUDO zypper --non-interactive install --allow-unsigned-rpm "$TMP/liveclaude.rpm"
    else
        $SUDO rpm -Uvh "$TMP/liveclaude.rpm"
    fi

else
    # No package manager we recognise: unpack the tarball where the packages would have put it, so
    # the autostart unit points at the same path either way.
    URL=$(asset_url "$RID\.tar\.gz")
    [ -n "$URL" ] || die "no tarball for $RID in release $VERSION."

    say "No supported package manager found — installing the tarball to /opt/liveclaude."
    fetch "$URL" "$TMP/liveclaude.tar.gz"

    $SUDO rm -rf /opt/liveclaude
    $SUDO mkdir -p /opt/liveclaude
    $SUDO tar -xzf "$TMP/liveclaude.tar.gz" -C /opt/liveclaude
    $SUDO chmod +x /opt/liveclaude/LiveClaude /opt/liveclaude/LiveClaude.Service
    $SUDO ln -sf /opt/liveclaude/LiveClaude /usr/local/bin/liveclaude
    $SUDO ln -sf /opt/liveclaude/LiveClaude.Service /usr/local/bin/liveclaude-supervisor
fi

# ---------------------------------------------------------------- what now

say ""
say "LiveClaude $VERSION is installed."
say ""
say "  liveclaude                       open the app"
say "  liveclaude-supervisor status     see what is registered"
say ""
say "Autostart is a systemd user unit, installed from the app under 'Service & startup',"
say "or with:"
say ""
say "  liveclaude-supervisor install-autostart"
say ""

if ! command -v systemctl >/dev/null 2>&1; then
    say "Note: systemctl was not found. Autostart needs systemd; the app itself runs regardless."
fi

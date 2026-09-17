#!/usr/bin/env bash
#
# Builds the Linux packages: a self-contained tar.gz, a .deb and an .rpm.
#
# Everything installs to /opt/liveclaude, which is what makes the autostart registration survive an
# update: a systemd unit points at an absolute path, and a versioned directory would break it on
# every upgrade. The launcher symlink in /usr/bin and the .desktop entry both point there too.
#
# Usage: build-linux-packages.sh <version> <rid> <output-directory>
#   e.g. build-linux-packages.sh 1.1.0 linux-x64 artifacts/packages

set -euo pipefail

VERSION="${1:?version required}"
RID="${2:?runtime identifier required, e.g. linux-x64}"
OUTPUT="${3:?output directory required}"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

case "$RID" in
  linux-x64)   ARCH_DEB=amd64; ARCH_RPM=x86_64 ;;
  linux-arm64) ARCH_DEB=arm64; ARCH_RPM=aarch64 ;;
  *) echo "unsupported runtime identifier: $RID" >&2; exit 2 ;;
esac

mkdir -p "$OUTPUT"

echo "==> publishing $RID"
PUBLISH="$STAGE/publish"
dotnet publish "$ROOT/src/LiveClaude.App/LiveClaude.App.csproj" \
  -c Release -r "$RID" --self-contained true \
  -p:Version="$VERSION" \
  -o "$PUBLISH"

# The supervisor executable is published separately: the app hosts it too, but registering the
# dedicated one keeps a headless install from carrying the whole UI into memory.
dotnet publish "$ROOT/src/LiveClaude.Service/LiveClaude.Service.csproj" \
  -c Release -r "$RID" --self-contained true \
  -p:Version="$VERSION" \
  -o "$PUBLISH"

chmod +x "$PUBLISH/LiveClaude" "$PUBLISH/LiveClaude.Service"

echo "==> tar.gz"
tar -czf "$OUTPUT/LiveClaude-$VERSION-$RID.tar.gz" -C "$PUBLISH" .

# ---------------------------------------------------------------- shared package tree

build_tree() {
  local tree="$1"

  mkdir -p "$tree/opt/liveclaude" "$tree/usr/bin" \
           "$tree/usr/share/applications" "$tree/usr/share/icons/hicolor/256x256/apps"

  cp -r "$PUBLISH/." "$tree/opt/liveclaude/"
  ln -sf /opt/liveclaude/LiveClaude "$tree/usr/bin/liveclaude"
  ln -sf /opt/liveclaude/LiveClaude.Service "$tree/usr/bin/liveclaude-supervisor"

  cp "$ROOT/src/LiveClaude.App/Assets/liveclaude.png" \
     "$tree/usr/share/icons/hicolor/256x256/apps/liveclaude.png"

  cat > "$tree/usr/share/applications/liveclaude.desktop" <<'DESKTOP'
[Desktop Entry]
Type=Application
Name=LiveClaude
GenericName=Claude Code Remote Control supervisor
Comment=Keeps Claude Code Remote Control servers running
Exec=/opt/liveclaude/LiveClaude
Icon=liveclaude
Terminal=false
Categories=Development;Utility;
Keywords=claude;remote control;supervisor;
StartupWMClass=LiveClaude
DESKTOP
}

# ---------------------------------------------------------------- deb

echo "==> deb"
DEB="$STAGE/deb"
build_tree "$DEB"
mkdir -p "$DEB/DEBIAN"

# Installed-Size is in kilobytes and apt shows it before downloading.
SIZE=$(du -ks "$DEB/opt" | cut -f1)

cat > "$DEB/DEBIAN/control" <<CONTROL
Package: liveclaude
Version: $VERSION
Section: devel
Priority: optional
Architecture: $ARCH_DEB
Maintainer: Leandro Cannizzaro <noreply@github.com>
Installed-Size: $SIZE
Homepage: https://github.com/LeandroCannizzaro/LiveClaude
Description: Supervisor for Claude Code Remote Control servers
 Keeps one "claude remote-control" server per project directory alive: restarted
 after a crash, at sign-in and after a reboot, hosted by a systemd unit. Includes
 a desktop application to create and watch sessions, an embedded terminal for the
 flows that need one, and cleanup of leftover bridge environments.
 .
 The build is self-contained; no .NET runtime is required.
CONTROL

# Removing the package must not leave a unit behind pointing at files that are gone.
cat > "$DEB/DEBIAN/prerm" <<'PRERM'
#!/bin/sh
set -e
if [ "$1" = "remove" ] || [ "$1" = "purge" ]; then
    systemctl stop liveclaude.service >/dev/null 2>&1 || true
    systemctl disable liveclaude.service >/dev/null 2>&1 || true
fi
exit 0
PRERM
chmod 755 "$DEB/DEBIAN/prerm"

dpkg-deb --build --root-owner-group "$DEB" "$OUTPUT/liveclaude_${VERSION}_${ARCH_DEB}.deb"

# ---------------------------------------------------------------- rpm

if ! command -v rpmbuild >/dev/null 2>&1; then
  echo "==> rpm skipped (rpmbuild not installed)"
  exit 0
fi

echo "==> rpm"
RPM="$STAGE/rpm"
mkdir -p "$RPM"/{BUILD,RPMS,SOURCES,SPECS,SRPMS}
build_tree "$RPM/BUILDROOT-tree"

cat > "$RPM/SPECS/liveclaude.spec" <<SPEC
Name:           liveclaude
Version:        $VERSION
Release:        1
Summary:        Supervisor for Claude Code Remote Control servers
License:        MIT
URL:            https://github.com/LeandroCannizzaro/LiveClaude
BuildArch:      $ARCH_RPM
# The build is self-contained, and the binaries are not built here — stripping and
# dependency extraction would both be wrong.
AutoReqProv:    no
%global __os_install_post %{nil}
%global debug_package %{nil}

%description
Keeps one "claude remote-control" server per project directory alive: restarted
after a crash, at sign-in and after a reboot, hosted by a systemd unit. Includes
a desktop application, an embedded terminal, and cleanup of leftover bridge
environments. No .NET runtime is required.

%install
cp -r $RPM/BUILDROOT-tree/. %{buildroot}/

%files
/opt/liveclaude
/usr/bin/liveclaude
/usr/bin/liveclaude-supervisor
/usr/share/applications/liveclaude.desktop
/usr/share/icons/hicolor/256x256/apps/liveclaude.png

%preun
if [ \$1 -eq 0 ]; then
    systemctl stop liveclaude.service >/dev/null 2>&1 || true
    systemctl disable liveclaude.service >/dev/null 2>&1 || true
fi
exit 0
SPEC

# --target is required, not decorative: the runner is x86_64, and without being told the target
# explicitly rpmbuild refuses a spec whose BuildArch is aarch64 with "No compatible architectures
# found for build". Nothing here is compiled by rpmbuild — the binaries are already built — so
# naming the architecture is all it takes.
rpmbuild --target "$ARCH_RPM" --define "_topdir $RPM" -bb "$RPM/SPECS/liveclaude.spec"
find "$RPM/RPMS" -name '*.rpm' -exec cp {} "$OUTPUT/" \;

echo "==> done: $(ls "$OUTPUT")"

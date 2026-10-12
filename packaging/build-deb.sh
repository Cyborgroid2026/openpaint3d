#!/bin/sh
# Build the Rust engine and the self-contained C# app, then package a .deb.
set -eu
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION=0.2.0
ARCH=amd64
PKG=openpaint3d_${VERSION}_${ARCH}
STAGE="$ROOT/build/$PKG"

cargo build --release --manifest-path "$ROOT/engine/Cargo.toml"
cargo test --release --manifest-path "$ROOT/engine/Cargo.toml"

# headless UI walkthrough: drives the real window and must pass before packaging
dotnet build "$ROOT/uitest/UiTest.csproj" -c Release
dotnet "$ROOT/uitest/bin/Release/net10.0/UiTest.dll" "$ROOT/build-uitest"

rm -rf "$ROOT/build" && mkdir -p "$STAGE/DEBIAN" "$STAGE/opt/openpaint3d" "$STAGE/usr/bin" \
    "$STAGE/usr/share/applications" "$STAGE/usr/share/icons/hicolor/256x256/apps"
dotnet publish "$ROOT/app/OpenPaint3D.csproj" -c Release -r linux-x64 --self-contained true \
    -p:DebugType=none -o "$STAGE/opt/openpaint3d"

"$STAGE/opt/openpaint3d/openpaint3d" --export-icon "$STAGE/opt/openpaint3d/openpaint3d.png"
cp "$STAGE/opt/openpaint3d/openpaint3d.png" "$STAGE/usr/share/icons/hicolor/256x256/apps/openpaint3d.png"
ln -s ../../opt/openpaint3d/openpaint3d "$STAGE/usr/bin/openpaint3d"

cat > "$STAGE/usr/share/applications/openpaint3d.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=OpenPaint 3D
GenericName=Paint Program
GenericName[ko]=그림판
Comment=Paint 3D style painting app (C# + Rust)
Comment[ko]=Paint 3D 스타일 그림판 (C# + Rust)
Exec=openpaint3d %f
Icon=openpaint3d
Terminal=false
Categories=Graphics;2DGraphics;RasterGraphics;
MimeType=image/png;image/jpeg;image/bmp;image/gif;image/webp;
StartupWMClass=openpaint3d
DESKTOP

SIZE=$(du -sk "$STAGE" | cut -f1)
cat > "$STAGE/DEBIAN/control" <<CONTROL
Package: openpaint3d
Version: $VERSION
Section: graphics
Priority: optional
Architecture: $ARCH
Installed-Size: $SIZE
Depends: libc6, libgcc-s1, libstdc++6, libx11-6, libice6, libsm6, libfontconfig1
Maintainer: a71143055 <a71143055@gmail.com>
Description: Paint 3D style painting app for Ubuntu
 OpenPaint 3D is a clean-room painting application whose layout follows
 Microsoft Paint 3D (brushes, 2D shapes, 3D shapes, stickers, text,
 effects, canvas, select). UI in C# (Avalonia), raster engine in Rust.
 Contains no Microsoft code or assets.
CONTROL

chmod -R u+rwX,go+rX,go-w "$STAGE"
mkdir -p "$ROOT/dist"
dpkg-deb --root-owner-group --build "$STAGE" "$ROOT/dist/$PKG.deb"
echo "built: $ROOT/dist/$PKG.deb"

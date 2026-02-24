#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP_PROJECT="$ROOT_DIR/BacpacGUI.App/BacpacGUI.App.csproj"
ICON_SRC="$ROOT_DIR/BacpacGUI.App/Assets/app-icon-512.png"
ARTIFACTS_DIR="$ROOT_DIR/artifacts/macos"
APP_EXECUTABLE_NAME="BacpacGUI.App"
APP_DISPLAY_NAME="Bacpac GUI"
BUNDLE_ID="com.internal.bacpacgui"
VERSION="1.0.0"

mkdir -p "$ARTIFACTS_DIR"

create_icns() {
  local output_icns="$1"
  local tmp_dir
  tmp_dir="$(mktemp -d)"
  local iconset_dir="$tmp_dir/app.iconset"
  mkdir -p "$iconset_dir"

  magick "$ICON_SRC" -resize 16x16   "$iconset_dir/icon_16x16.png"
  magick "$ICON_SRC" -resize 32x32   "$iconset_dir/icon_16x16@2x.png"
  magick "$ICON_SRC" -resize 32x32   "$iconset_dir/icon_32x32.png"
  magick "$ICON_SRC" -resize 64x64   "$iconset_dir/icon_32x32@2x.png"
  magick "$ICON_SRC" -resize 128x128 "$iconset_dir/icon_128x128.png"
  magick "$ICON_SRC" -resize 256x256 "$iconset_dir/icon_128x128@2x.png"
  magick "$ICON_SRC" -resize 256x256 "$iconset_dir/icon_256x256.png"
  magick "$ICON_SRC" -resize 512x512 "$iconset_dir/icon_256x256@2x.png"
  magick "$ICON_SRC" -resize 512x512 "$iconset_dir/icon_512x512.png"
  magick "$ICON_SRC" -resize 1024x1024 "$iconset_dir/icon_512x512@2x.png"

  iconutil -c icns "$iconset_dir" -o "$output_icns" || {
    echo "Warning: iconutil failed, using PNG icon fallback."
    cp "$ICON_SRC" "$output_icns"
  }
  rm -rf "$tmp_dir"
}

package_rid() {
  local rid="$1"
  local app_bundle_name
  local zip_name

  if [[ "$rid" == "osx-arm64" ]]; then
    app_bundle_name="Bacpac GUI (Apple Silicon).app"
    zip_name="BacpacGUI-macOS-AppleSilicon.zip"
  else
    app_bundle_name="Bacpac GUI (Intel).app"
    zip_name="BacpacGUI-macOS-Intel.zip"
  fi

  local publish_dir="$ROOT_DIR/BacpacGUI.App/bin/Release/net10.0/$rid/publish"
  local rid_out_dir="$ARTIFACTS_DIR/$rid"
  local app_bundle_path="$rid_out_dir/$app_bundle_name"
  local macos_dir="$app_bundle_path/Contents/MacOS"
  local resources_dir="$app_bundle_path/Contents/Resources"
  local plist_path="$app_bundle_path/Contents/Info.plist"
  local zip_path="$ARTIFACTS_DIR/$zip_name"

  rm -rf "$rid_out_dir" "$zip_path"
  mkdir -p "$macos_dir" "$resources_dir"

  echo "Publishing $rid..."
  if ! dotnet publish "$APP_PROJECT" \
    -c Release \
    -r "$rid" \
    --self-contained true \
    -p:PublishSingleFile=false \
    -p:UseAppHost=true \
    -p:PublishTrimmed=false \
    -p:UsedAvaloniaProducts=; then
    echo "Warning: publish failed for $rid. Skipping package for this RID."
    return 1
  fi

  cp -R "$publish_dir/." "$macos_dir/"
  chmod +x "$macos_dir/$APP_EXECUTABLE_NAME"

  create_icns "$resources_dir/app-icon.icns"

  cat > "$plist_path" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>
  <string>$APP_DISPLAY_NAME</string>
  <key>CFBundleDisplayName</key>
  <string>$APP_DISPLAY_NAME</string>
  <key>CFBundleIdentifier</key>
  <string>$BUNDLE_ID.$rid</string>
  <key>CFBundleVersion</key>
  <string>$VERSION</string>
  <key>CFBundleShortVersionString</key>
  <string>$VERSION</string>
  <key>CFBundleExecutable</key>
  <string>$APP_EXECUTABLE_NAME</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleIconFile</key>
  <string>app-icon.icns</string>
  <key>LSMinimumSystemVersion</key>
  <string>12.0</string>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
EOF

  ditto -c -k --sequesterRsrc --keepParent "$app_bundle_path" "$zip_path"
  echo "Created $zip_path"
  return 0
}

created_count=0
if package_rid "osx-arm64"; then
  created_count=$((created_count + 1))
fi
if package_rid "osx-x64"; then
  created_count=$((created_count + 1))
fi

echo
if [[ "$created_count" -eq 0 ]]; then
  echo "No macOS packages were created."
  exit 1
fi

echo "Done. Share the generated file(s) in:"
echo "  $ARTIFACTS_DIR"

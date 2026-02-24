#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP_PROJECT="$ROOT_DIR/BacpacGUI.Desktop/BacpacGUI.Desktop.csproj"
ARTIFACTS_DIR="$ROOT_DIR/artifacts/linux"
RID="linux-x64"
APP_DIR_NAME="BacpacGUI-Linux-x64"
ARCHIVE_NAME="BacpacGUI-Linux-x64.tar.gz"
APP_EXECUTABLE_NAME="BacpacGUI.Desktop"

mkdir -p "$ARTIFACTS_DIR"

publish_dir="$ROOT_DIR/BacpacGUI.Desktop/bin/Release/net10.0/$RID/publish"
rid_out_dir="$ARTIFACTS_DIR/$RID"
app_dir="$rid_out_dir/$APP_DIR_NAME"
archive_path="$ARTIFACTS_DIR/$ARCHIVE_NAME"

rm -rf "$rid_out_dir" "$archive_path"
mkdir -p "$app_dir"

echo "Publishing $RID..."
dotnet publish "$APP_PROJECT" \
  -c Release \
  -r "$RID" \
  --self-contained true \
  -p:PublishSingleFile=false \
  -p:UseAppHost=true \
  -p:PublishTrimmed=false \
  -p:UsedAvaloniaProducts=

cp -R "$publish_dir/." "$app_dir/"
chmod +x "$app_dir/$APP_EXECUTABLE_NAME"

(
  cd "$rid_out_dir"
  tar -czf "$archive_path" "$APP_DIR_NAME"
)

echo "Created $archive_path"
echo "Done. Share the generated file(s) in:"
echo "  $ARTIFACTS_DIR"

# Bacpac GUI

Cross-platform desktop utility for SQL Server `.bacpac` backup and restore.

## Releases

Download prebuilt artifacts here:

- https://github.com/brooklynDev/Bacpac-GUI/releases

## Quick Start

Prerequisite: .NET SDK 10+

```bash
make build
make run
```

## Build Artifacts

```bash
make publish
```

Outputs:

- `artifacts/macos/BacpacGUI-macOS-AppleSilicon.zip`
- `artifacts/macos/BacpacGUI-macOS-Intel.zip`
- `artifacts/windows/BacpacGUI-Windows-x64.zip`

## Notes

- Uses in-process DacFx (no `sqlpackage` dependency).
- Packaging uses `PublishSingleFile=false` for DacFx compatibility.
- SQL connections are created with `Encrypt=true` and `TrustServerCertificate=true`.

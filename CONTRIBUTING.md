# Contributing

Use Windows and .NET SDK 10. Run the isolated suite before proposing changes:

```powershell
dotnet run --project Tests/Burnout3.PlaylistStudio.Tests.csproj -c Release
./build.ps1
```

Open an issue with application version, Takedawn version, expected behaviour and a small reproduction. Remove account tokens, personal paths and private playlist data from logs/screenshots. Never upload game files, saves, copyrighted audio or lyrics as test fixtures.

Use synthetic HTTP/process fixtures for automated tests. Real network services can become unavailable and should not be required by CI. Keep process arguments out of shell command strings, preserve tool hash checks, and test install/config changes with temporary directories and `--config`, never personal settings.

Interface changes should include screenshots of the affected state at normal and high Windows DPI. Keep keyboard navigation, cancellation, honest partial-import messages, and readable disabled controls.

This project is MIT licensed. Changes to third-party tooling must preserve applicable upstream notices and must not bundle binaries without checking their distribution requirements.

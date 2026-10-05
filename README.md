# TgMini — portable mini-Telegram (C# .NET 9)

Portable desktop Telegram client: `TgMini.exe` + setup driver that fills
API ID / API Hash / phone on first run and saves `config.toml`.
No hardcoded accounts — every user enters their own credentials.

> Full build + binaries live in the local `deliverable/` folder (not in git).
> See `DELIVERABLE-NOTES.txt` for the original Russian manual.

## Features
- Portable `TgMini.exe` (no installer)
- `TgMiniSetupDriver` — first-run wizard (API ID / Hash / phone)
- `TgMini.Core` — shared logic
- `tests/TgMini.Smoke` + `tools/BridgeTest`, `tools/Doc`

## Stack
![C#](https://img.shields.io/badge/C%23-239120?style=flat&logo=csharp&logoColor=white)
`.NET 9` · `WPF/Win32` · `SQLite`

## Quickstart
```powershell
dotnet build TgMini.sln -c Release
dotnet test tests/TgMini.Smoke/TgMini.Smoke.csproj
```

Requires [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)
(`dotnet --list-runtimes` should show `Microsoft.WindowsDesktop.App 9.x`).

## Layout
| Path | What |
|---|---|
| `src/TgMini` | App |
| `src/TgMini.Core` | Shared core |
| `src/TgMiniSetupDriver` | First-run driver |
| `tests/TgMini.Smoke` | Smoke tests |
| `tools/` | BridgeTest, Doc |

## License
MIT

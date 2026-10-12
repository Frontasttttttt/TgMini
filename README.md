# TgMini [![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4)](https://dotnet.microsoft.com/) [![C#](https://img.shields.io/badge/C%23-13.0-239120)](https://docs.microsoft.com/dotnet/csharp/) [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A portable, lightweight Telegram desktop client for Windows built with .NET 9 and WPF. Features instant QR code authentication and background resource optimization.

## 📥 Download

**[⬇️ Download TgMini v1.0.3 (Windows x64, Standalone)](https://github.com/frontys/TgMini/releases/latest)**

Download the single-file executable `TgMini.exe` (or the portable ZIP archive) and run it directly. No installation or separate .NET runtime required.

---

## 🌟 Key Features

- **Instant QR Code Login:** Log in via phone camera scan without manual phone number typing or API key configuration on my.telegram.org.
- **Folder Synchronization:** Full support for Telegram chat folders (All, Personal, Work, Unread) with fast reactive switching and avatar caching.
- **Rich Message View:** Chat bubbles with author coloring, replies, and pinned message indicators.
- **Media Previews & Downloads:** In-app inline previews for photos, GIFs, videos, and round video messages with instant save/open.
- **Desktop Overlay & Tray:** Non-intrusive notification overlay for gaming/work, quiet mode (DND), and native Win32 system tray minimizing.
- **Global Hotkeys:** Win+H, Win+J, and Win+M for quick toggle, minimize, and mute actions.
- **SQLite Message Archive:** Missed messages cached in a local WAL-mode SQLite database.
- **Single-File Standalone Binary:** Self-contained single-executable deployment with zero external dependencies.

---

## 📱 QR Code Login

1. Run `TgMini.exe` — the QR login window appears automatically.
2. In your official Telegram mobile app, open **Settings** → **Devices** → **Link Desktop Device**.
3. Point your camera at the screen to scan the QR code and authorize.

---

## 🛠️ Building from Source

```bash
dotnet restore TgMini.sln
dotnet build TgMini.sln --configuration Release
dotnet publish src/TgMini/TgMini.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist
```

**Requirements:** [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

---

## 📂 Project Architecture

| Component | Description |
| :--- | :--- |
| `src/TgMini` | WPF application layer (`MainWindow`, `NotificationWindow`, `QrWindow`, `TrayHelper`) |
| `src/TgMini.Core` | Core logic: `WTelegramBridge`, `AppConfig`, `FolderMatcher`, `MediaCache`, SQLite |
| `tests/TgMini.Smoke` | Automated integration and smoke tests |
| `tools/TgMini.BridgeTest` | Telegram API MTProto bridge verification tools |
| `TgMini.sln` | Visual Studio / .NET solution |

---

## 📄 License

Distributed under the terms of the [MIT License](LICENSE).

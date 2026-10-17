# TgMini [![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4)](https://dotnet.microsoft.com/) [![C#](https://img.shields.io/badge/C%23-13.0-239120)](https://docs.microsoft.com/dotnet/csharp/) [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A portable, ultra-lightweight Telegram desktop client for Windows and Linux built with .NET 9, WPF, and Avalonia UI. Features instant QR code authentication, cloud 2FA protection, and low-footprint background resource optimization.

## 📥 Download

**[⬇️ Download Latest TgMini Release (Windows & Linux)](https://github.com/frontys/TgMini/releases/latest)**

Download the single-file executable `TgMini.exe` for Windows or the Linux standalone binary package (`TgMini-v1.0.6-linux-x64.tar.gz`). No installation or separate .NET runtime required.

---

## 🌟 Key Features

- **Cross-Platform:** Native WPF client for Windows x64 and high-performance Avalonia UI 11 client for Linux (Fedora, Ubuntu, Debian, Arch).
- **Instant QR Code & 2FA Login:** Log in via phone camera scan with built-in modal 2FA verification dialog and flood-wait protection.
- **Folder Synchronization:** Full support for Telegram chat folders (All, Personal, Work, Unread) with fast reactive switching and avatar caching.
- **Rich Message View:** Chat bubbles with author coloring, replies, and pinned message indicators.
- **Media Previews & Downloads:** In-app inline previews for photos, GIFs, videos, and round video messages with instant save/open.
- **Ultra-low RAM Footprint:** File-backed uncompressed single-file mmap, bounded LRU image caching with explicit Skia disposal, and background non-blocking working set trimming (`malloc_trim`).
- **Desktop Overlay & Tray:** Non-intrusive notification overlay for gaming/work, quiet mode (DND), and native system tray minimizing with unread badges.
- **Global Hotkeys:** Win+H, Win+J, and Win+M for quick toggle, minimize, and mute actions.
- **SQLite Message Archive:** Missed messages cached in a local WAL-mode SQLite database.
- **Single-File Standalone Binary:** Self-contained single-executable deployment with zero external dependencies.

---

## 📱 QR Code Login

1. Run `TgMini` (`TgMini.exe` on Windows or `./TgMini` on Linux) — the QR login window appears automatically.
2. In your official Telegram mobile app, open **Settings** → **Devices** → **Link Desktop Device**.
3. Point your camera at the screen to scan the QR code and authorize.

---

## 🛠️ Building from Source

```bash
dotnet restore TgMini.sln
dotnet build TgMini.sln --configuration Release

# Windows Standalone
dotnet publish src/TgMini/TgMini.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist_win

# Linux Standalone
dotnet publish src/TgMini.Desktop/TgMini.Desktop.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o dist_linux
```

**Requirements:** [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

---

## 📂 Project Architecture

| Component | Description |
| :--- | :--- |
| `src/TgMini` | Windows WPF desktop application (`MainWindow`, `NotificationWindow`, `QrWindow`, `TrayHelper`) |
| `src/TgMini.Desktop` | Cross-platform Linux Avalonia UI 11 desktop application (`MainWindow`, `VerificationWindow`, `QrWindow`) |
| `src/TgMini.Core` | Shared core library: `WTelegramBridge`, `AppConfig`, `FolderMatcher`, `MediaCache`, SQLite |
| `tests/TgMini.Smoke` | Automated integration and smoke tests |
| `tools/TgMini.BridgeTest` | Telegram API MTProto bridge verification tools |
| `TgMini.sln` | Unified .NET solution |

---

## 👥 Contributors & Credits

- **Lead Developer:** [@frontys](https://github.com/frontys)
- **QA & Debugger:** [@surf1k](https://github.com/surf1k) (ym1co) — Linux testing, 2FA lifecycle diagnostics, memory profiling & font rendering bugfixes.

---

## 📄 License

Distributed under the terms of the [MIT License](LICENSE).

# TgMini [![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4)](https://dotnet.microsoft.com/) [![C#](https://img.shields.io/badge/C%23-13.0-239120)](https://docs.microsoft.com/dotnet/csharp/) [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Портативный мини-клиент Telegram для Windows с мгновенным входом по QR-коду и оптимизацией для работы в фоне.

## 📥 Скачать

**[⬇️ Скачать TgMini v1.0.2 (Windows x64, portable)](https://github.com/frontys/TgMini/releases/latest)**

Распакуйте архив в любую папку и запустите `TgMini.exe` — ничего устанавливать не нужно, .NET встроен в сборку. Внутри архива есть файл `ИНСТРУКЦИЯ.txt`.

## Возможности
- Вход исключительно по QR-коду без ручного ввода телефона и создания приложений на my.telegram.org.
- Синхронизация папок чатов Telegram (Все, Личные, Учеба, Непрочитанные) с быстрым переключением.
- Чаты с пузырьками сообщений, цветными именами авторов и цитированием ответов.
- Загрузка медиафайлов (фото, гиф, видео, голосовые, документы) через диалог сохранения.
- Логирование пропущенных сообщений в локальную базу данных SQLite.
- Тихий режим для игр, DND и системный трей с автозапуском.
- Глобальные горячие клавиши Win+H, Win+J и Win+M для быстрого управления.
- Портативная архитектура без установщика.

## Вход по QR
1. Запустите приложение — окно QR откроется сразу.
2. Откройте официальный клиент Telegram: **Настройки** → **Устройства** → **Подключить устройство**.
3. Отсканируйте QR-код с экрана монитора камерой телефона.

## Сборка
```bash
dotnet restore TgMini.sln
dotnet build TgMini.sln --configuration Release
dotnet publish src/TgMini/TgMini.csproj -c Release -o dist
```

Требуется [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0).

## Структура проекта
| Path | Что внутри |
| :--- | :--- |
| `src/TgMini` | WPF-приложение (MainWindow, SetupWindow, QrWindow, VerificationWindow) |
| `src/TgMini.Core` | Ядро: WTelegramBridge, AppConfig, FolderMatcher, MediaCache, SQLite |
| `tests/TgMini.Smoke` | Смоук-тесты (SMOKE_OK) |
| `tools/TgMini.BridgeTest` | Тестирование Telegram API-моста |
| `tools/TgMini.Doc` | Документация и вспомогательные скрипты |
| `TgMini.sln` | Главный файл решения |

## Лицензия
Распространяется под условиями лицензии [MIT](LICENSE).

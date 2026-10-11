// Доменные модели TgMini — зеркало структур tg.py / mini.py из Python-прототипа.
namespace TgMini.Core.Models;

/// <summary>Тип медиа вложения. Ср. Bridge._describe_media в tg.py.</summary>
public enum MediaKind { Text, Photo, Sticker, Gif, Video, Voice, Audio, File, RoundVideo }

/// <summary>Тип чата. Ср. kind в tg.py (user/bot/group/channel).</summary>
public enum ChatKind { User, Bot, Group, Channel, Other }

/// <summary>Диалог из левого списка. Ср. dict из Bridge.get_dialogs().</summary>
public sealed record DialogInfo(
    long Id,
    string Name,
    string LastMessage,
    DateTime? Date,
    int Unread,
    bool IsGroup,
    ChatKind Kind,
    bool Muted,
    bool Archived,
    bool IsPinned = false,
    int PinnedIndex = 0);

/// <summary>Одно сообщение ленты. Ср. dict из Bridge.get_history() / info из _handle().</summary>
public sealed record MessageInfo(
    int Id,
    long ChatId,
    bool Out,
    string Text,
    DateTime? Date,
    long SenderId,
    string Author,
    int ReplyTo,
    MediaKind Kind,
    string MediaLabel,
    string ThumbPath);

/// <summary>Папка Telegram. Ср. dict из Bridge.get_folders().</summary>
public sealed record ChatFolder(
    int Id,
    string Title,
    IReadOnlyList<long> Peers,
    IReadOnlyList<long> Exclude,
    IReadOnlyList<string> Types,
    bool ExcludeRead,
    bool ExcludeMuted,
    bool ExcludeArchived);

/// <summary>Входящее событие от клиента. Ср. info в Bridge._handle().</summary>
public sealed record IncomingMessage(
    long ChatId,
    int MsgId,
    bool Out,
    long SenderId,
    string Name,
    string Text,
    bool IsGroup,
    bool Mentioned,
    DateTime Date,
    MediaKind Kind,
    string MediaLabel,
    string Author);

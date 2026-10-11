// Каркас MTProto-моста (аналог Bridge из tg.py).
// Следующий шаг: подключить WTelegramClient (NuGet) и реализовать методы
// через Dialogs / Messages / GetDialogFiltersRequest — сигнатуры уже зафиксированы,
// UI пишется против интерфейса IBridge и не поменяется.
using TgMini.Core.Models;

namespace TgMini.Core;

public interface IBridge : IDisposable
{
    event Action<IncomingMessage>? Message;
    Task<bool> ConnectAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DialogInfo>> GetDialogsAsync(int limit = 200, CancellationToken ct = default);
    Task<IReadOnlyList<ChatFolder>> GetFoldersAsync(CancellationToken ct = default);
    Task<IReadOnlyList<MessageInfo>> GetHistoryAsync(long peerId, int limit = 50, int offsetId = 0, CancellationToken ct = default);
    Task<IReadOnlyList<MessageInfo>> GetContextAsync(long peerId, int aroundId, int before = 10, int after = 10, CancellationToken ct = default);
    Task<Dictionary<long, string>> ResolveAuthorsAsync(IEnumerable<long> senderIds, CancellationToken ct = default);
    Task<string> GetReplyTextAsync(long chatId, int replyId, CancellationToken ct = default);
    Task<string> EnsureThumbAsync(long chatId, int msgId, CancellationToken ct = default);
    Task<string> DownloadToAsync(long chatId, int msgId, string destPath, IProgress<(long Sent, long Total)>? progress = null, CancellationToken ct = default);
    Task<bool> SendAsync(long peerId, string text, int replyTo = 0, CancellationToken ct = default);
    Task<bool> SendFileAsync(long peerId, string path, string caption = "", int replyTo = 0, IProgress<(long Sent, long Total)>? progress = null, CancellationToken ct = default);
    Task MarkReadAsync(long peerId, CancellationToken ct = default);
    Task<string?> EnsureAvatarAsync(long peerId, CancellationToken ct = default);
    Task<bool> ToggleChatFolderAsync(int folderId, long peerId, CancellationToken ct = default);
    Task<bool> DeleteMessagesAsync(long peerId, int[] msgIds, CancellationToken ct = default);
    Task<bool> EditMessageAsync(long peerId, int msgId, string newText, CancellationToken ct = default);

    /// <summary>
    /// QR-вход: показывает QR до тех пор, пока пользователь не отсканирует его
    /// официальным приложением (Настройки → Устройства → Подключить). Код из SMS/чата не нужен.
    /// onQr вызывается каждый раз, когда Telegram обновляет картинку (живёт ~30 сек).
    /// Возвращает true после успешной авторизации. Бросает OperationCanceledException при отмене.
    /// </summary>
    Task<bool> LoginQrAsync(Action<byte[]> onQr, CancellationToken ct = default);
}

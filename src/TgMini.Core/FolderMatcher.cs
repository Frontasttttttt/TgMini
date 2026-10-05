// Фильтрация диалогов по папкам Telegram — порт mini.match_folder из Python-прототипа.
// Причина клиентской фильтрации: сервер GetDialogsRequest(folder_id=N) для
// кастомных папок отвечает "The folder you tried to use was not valid".
using TgMini.Core.Models;

namespace TgMini.Core;

public static class FolderMatcher
{
    /// <summary>
    /// Порт mini.match_folder: peers/exclude -> union по типам -> exclude-флаги.
    /// </summary>
    public static bool Match(
        long dialogId, string kind, int unread, bool muted, bool archived,
        ChatFolder? folder)
    {
        if (folder is null) return true;
        var peers = new HashSet<long>(folder.Peers);
        var excl = new HashSet<long>(folder.Exclude);
        var types = new HashSet<string>(folder.Types, StringComparer.OrdinalIgnoreCase);
        if (excl.Contains(dialogId)) return false;
        if (peers.Count > 0 || types.Count > 0)
        {
            var ok = peers.Contains(dialogId);
            if (!ok && types.Contains(kind)) ok = true;
            if (!ok) return false;
        }
        if (folder.ExcludeRead && unread == 0) return false;
        if (folder.ExcludeMuted && muted) return false;
        if (folder.ExcludeArchived && archived) return false;
        return true;
    }
}

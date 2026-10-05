// SQLite-лог пропущенных — порт missed_store.py.
using Microsoft.Data.Sqlite;

namespace TgMini.Core;

public sealed class MissedStore : IDisposable
{
    private readonly SqliteConnection _db;

    public record Row(long Id, DateTime Ts, string Title, string Text,
        string Chat, bool IsMention, bool HiddenQuiet, bool Seen);

    public MissedStore(string? path = null)
    {
        path ??= Path.Combine(AppContext.BaseDirectory, "missed.db");
        _db = new SqliteConnection($"Data Source={path}");
        _db.Open();
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS msgs(
              id INTEGER PRIMARY KEY AUTOINCREMENT, ts REAL, title TEXT, text TEXT,
              chat TEXT DEFAULT '', sender TEXT DEFAULT '',
              is_mention INTEGER DEFAULT 0, hidden_quiet INTEGER DEFAULT 0, seen INTEGER DEFAULT 0)
            """;
        cmd.ExecuteNonQuery();
    }

    public void Log(string title, string text, string chat = "", string sender = "",
        bool isMention = false, bool hiddenQuiet = false)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "INSERT INTO msgs(ts,title,text,chat,sender,is_mention,hidden_quiet,seen) VALUES($ts,$t,$x,$c,$s,$m,$h,0)";
        cmd.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("$t", title);
        cmd.Parameters.AddWithValue("$x", text);
        cmd.Parameters.AddWithValue("$c", chat);
        cmd.Parameters.AddWithValue("$s", sender);
        cmd.Parameters.AddWithValue("$m", isMention ? 1 : 0);
        cmd.Parameters.AddWithValue("$h", hiddenQuiet ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    public List<Row> Recent(int limit = 150)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT id,ts,title,text,chat,is_mention,hidden_quiet,seen FROM msgs ORDER BY id DESC LIMIT $l";
        cmd.Parameters.AddWithValue("$l", limit);
        using var r = cmd.ExecuteReader();
        var out_ = new List<Row>();
        while (r.Read())
            out_.Add(new Row(r.GetInt64(0),
                DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(1)).LocalDateTime,
                r.GetString(2), r.GetString(3), r.GetString(4),
                r.GetInt64(5) == 1, r.GetInt64(6) == 1, r.GetInt64(7) == 1));
        return out_;
    }

    public int UnseenCount()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM msgs WHERE seen=0";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void MarkSeen()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "UPDATE msgs SET seen=1";
        cmd.ExecuteNonQuery();
    }

    public void Clear()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "DELETE FROM msgs";
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _db.Dispose();
}

using System.Globalization;

namespace TgMini.Core;

public static class TimeFormatter
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    public static string FormatDialogTime(DateTime? dt)
    {
        if (!dt.HasValue) return "";
        var local = dt.Value.ToLocalTime();
        var now = DateTime.Now;

        if (local.Date == now.Date)
            return local.ToString("HH:mm");

        if (local.Date == now.Date.AddDays(-1))
            return "Вчера";

        var days = (now.Date - local.Date).TotalDays;
        if (days > 0 && days < 7)
            return local.ToString("ddd", RuCulture);

        return local.ToString("dd.MM", RuCulture);
    }

    public static string FormatDateSeparator(DateTime? dt)
    {
        if (!dt.HasValue) return "";
        var local = dt.Value.ToLocalTime().Date;
        var now = DateTime.Today;

        if (local == now)
            return "Сегодня";

        if (local == now.AddDays(-1))
            return "Вчера";

        if (local.Year == now.Year)
            return local.ToString("d MMMM", RuCulture);

        return local.ToString("d MMMM yyyy", RuCulture);
    }
}

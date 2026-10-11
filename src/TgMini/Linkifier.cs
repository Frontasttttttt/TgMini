using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace TgMini;

public static class Linkifier
{
    private static readonly Regex UrlRegex = new(
        @"(https?://[^\s]+|www\.[^\s]+|t\.me/[^\s]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached(
            "Text",
            typeof(string),
            typeof(Linkifier),
            new PropertyMetadata("", OnTextChanged));

    public static string GetText(DependencyObject obj) => (string)obj.GetValue(TextProperty);
    public static void SetText(DependencyObject obj, string value) => obj.SetValue(TextProperty, value);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;
        var text = e.NewValue as string ?? "";
        tb.Inlines.Clear();
        if (string.IsNullOrEmpty(text)) return;

        var lastIdx = 0;
        foreach (Match match in UrlRegex.Matches(text))
        {
            if (match.Index > lastIdx)
            {
                tb.Inlines.Add(new Run(text[lastIdx..match.Index]));
            }

            var url = match.Value;
            var targetUri = url;
            if (!targetUri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !targetUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                targetUri = "https://" + targetUri;
            }

            var link = new Hyperlink(new Run(url))
            {
                Foreground = (Brush)new BrushConverter().ConvertFrom("#2EA6FF")!,
                TextDecorations = null,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            link.Click += (_, _) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(targetUri) { UseShellExecute = true });
                }
                catch { /* ignore */ }
            };
            tb.Inlines.Add(link);

            lastIdx = match.Index + match.Length;
        }

        if (lastIdx < text.Length)
        {
            tb.Inlines.Add(new Run(text[lastIdx..]));
        }
    }
}

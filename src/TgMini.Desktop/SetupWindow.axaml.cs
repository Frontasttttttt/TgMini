using Avalonia.Controls;
using Avalonia.Interactivity;
using TgMini.Core;

namespace TgMini.Desktop;

public partial class SetupWindow : Window
{
    private readonly AppConfig _cfg;

    public SetupWindow() : this(AppConfig.Load()) { }

    public SetupWindow(AppConfig cfg)
    {
        InitializeComponent();
        _cfg = cfg;
        ApiIdBox.Text = cfg.Telegram.ApiId == 0 ? "" : cfg.Telegram.ApiId.ToString();
        ApiHashBox.Text = cfg.Telegram.ApiHash;
        PhoneBox.Text = cfg.Telegram.Phone;
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (!int.TryParse(ApiIdBox.Text?.Trim(), out var apiId) || apiId == 0)
        {
            ShowError("Введите корректный API ID.");
            return;
        }

        var apiHash = ApiHashBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(apiHash) || apiHash.Length < 10)
        {
            ShowError("Введите корректный API Hash.");
            return;
        }

        var phone = PhoneBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(phone) || !phone.StartsWith('+'))
        {
            ShowError("Номер должен начинаться с +.");
            return;
        }

        _cfg.Telegram.ApiId = apiId;
        _cfg.Telegram.ApiHash = apiHash;
        _cfg.Telegram.Phone = phone;

        AppConfig.Save(_cfg);
        Close(true);
    }

    private void QrButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void ShowError(string msg)
    {
        ErrorLabel.Text = msg;
        ErrorLabel.IsVisible = true;
    }
}

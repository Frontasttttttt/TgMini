using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace TgMini.Desktop;

public partial class VerificationWindow : Window
{
    public string Value => ValueBox.Text?.Trim() ?? "";

    public VerificationWindow() : this("verification_code", null) { }

    public VerificationWindow(string what, string? error = null)
    {
        InitializeComponent();
        PromptText.Text = what switch
        {
            "password" => "Включена двухэтапная аутентификация (2FA) — введите пароль Telegram:",
            "first_name" => "Как вас зовут (first name) для регистрации?",
            "last_name" => "Фамилия (можно оставить пустой):",
            "email" => "Email для кода подтверждения (можно оставить пустой):",
            _ => "Введите код подтверждения Telegram:"
        };

        if (!string.IsNullOrEmpty(error))
        {
            ErrorText.Text = error;
            ErrorText.IsVisible = true;
        }

        if (what == "password")
        {
            ValueBox.PasswordChar = '•';
        }

        Loaded += (_, _) => ValueBox.Focus();
    }

    private void ValueBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Ok_Click(sender, e);
        }
    }

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}

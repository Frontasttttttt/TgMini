using System.Windows;

namespace TgMini;

/// <summary>Диалог ввода кода/пароля при первой авторизации (Prompt моста).</summary>
public partial class VerificationWindow : Window
{
    public string Value => ValueBox.Text.Trim();

    public VerificationWindow(string what)
    {
        InitializeComponent();
        PromptText.Text = what switch
        {
            "password" => "Включена двухэтапная аутентификация — введите пароль Telegram:",
            "first_name" => "Как вас зовут (first name) для регистрации?",
            "last_name" => "Фамилия (можно оставить пустой):",
            "email" => "Email для кода подтверждения (можно оставить пустой):",
            _ => "Введите код, который Telegram прислал в приложение:"
        };
        Loaded += (_, _) => ValueBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}

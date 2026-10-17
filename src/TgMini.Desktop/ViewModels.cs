using System.ComponentModel;
using System.IO;

namespace TgMini.Desktop;

public sealed class DialogRow : INotifyPropertyChanged
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string Initials { get; init; } = "";
    public string AvatarColor { get; init; } = "#2EA6FF";

    private string _preview = "";
    public string Preview
    {
        get => _preview;
        set
        {
            if (_preview != value)
            {
                _preview = value;
                OnPropertyChanged(nameof(Preview));
            }
        }
    }

    private int _unread;
    public int Unread
    {
        get => _unread;
        set
        {
            if (_unread != value)
            {
                _unread = value;
                OnPropertyChanged(nameof(Unread));
                OnPropertyChanged(nameof(NameWeight));
                OnPropertyChanged(nameof(BadgeVisible));
            }
        }
    }

    private bool _isPinned;
    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (_isPinned != value)
            {
                _isPinned = value;
                OnPropertyChanged(nameof(IsPinned));
                OnPropertyChanged(nameof(PinnedVisible));
            }
        }
    }

    public bool PinnedVisible => IsPinned;
    public string NameWeight => Unread > 0 ? "Bold" : "Normal";
    public bool BadgeVisible => Unread > 0;

    private string _avatarPath = "";
    public string AvatarPath
    {
        get => _avatarPath;
        set
        {
            if (_avatarPath != value)
            {
                _avatarPath = value;
                OnPropertyChanged(nameof(AvatarPath));
                OnPropertyChanged(nameof(AvatarVisible));
            }
        }
    }
    public bool AvatarVisible => !string.IsNullOrEmpty(_avatarPath) && File.Exists(_avatarPath);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class MsgRow : INotifyPropertyChanged
{
    public int Id { get; init; }
    public string Text { get; init; } = "";
    public string Author { get; init; } = "";
    public string AuthorColor { get; init; } = "#8B949E";
    public bool AuthorVisible => Author.Length > 0;
    public string Quote { get; init; } = "";
    public bool QuoteVisible => Quote.Length > 0;
    public string MediaHint { get; init; } = "";
    public bool MediaVisible => MediaHint.Length > 0;
    public string Bubble { get; init; } = "#1A2230";
    public string Align { get; init; } = "Left";
    public string Time { get; init; } = "";
    public bool Out { get; init; }
    public bool CanEdit => Out;
    public bool HasMedia { get; init; }
    public bool MediaActionsVisible => HasMedia;

    private string _imagePath = "";
    public string ImagePath
    {
        get => _imagePath;
        set
        {
            if (_imagePath != value)
            {
                _imagePath = value;
                OnPropertyChanged(nameof(ImagePath));
                OnPropertyChanged(nameof(ImageVisible));
            }
        }
    }
    public bool ImageVisible => !string.IsNullOrEmpty(_imagePath) && File.Exists(_imagePath);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

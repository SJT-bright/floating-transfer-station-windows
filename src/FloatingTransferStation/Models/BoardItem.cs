using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace FloatingTransferStation.Models;

public sealed class BoardItem : INotifyPropertyChanged
{
    private bool _isPinned;
    private bool _startsNormalRegion;
    private string? _name;
    private string? _text;

    public event PropertyChangedEventHandler? PropertyChanged;

    public required Guid Id { get; init; }
    public required BoardItemKind Kind { get; init; }
    public required BoardCategory Category { get; set; }
    public required int Order { get; set; }
    public required DateTimeOffset CreatedAt { get; init; }
    public string? Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }
    public string? ImageRelativePath { get; init; }
    public long ArrivalSequence { get; set; }
    public string? FileRelativePath { get; init; }
    public string? FileName { get; init; }
    public long? FileSize { get; init; }

    public string? Name
    {
        get => _name;
        set
        {
            var next = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (_name == next) { return; }
            SetProperty(ref _name, next);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayTitle)));
        }
    }

    [JsonIgnore]
    public string? FileAbsolutePath { get; set; }

    [JsonIgnore]
    public string DisplayTitle => Name ?? "添加名称";

    public bool IsPinned
    {
        get => _isPinned;
        set => SetProperty(ref _isPinned, value);
    }

    [JsonIgnore]
    public bool StartsNormalRegion
    {
        get => _startsNormalRegion;
        internal set => SetProperty(ref _startsNormalRegion, value);
    }

    [JsonIgnore]
    public string? ImageAbsolutePath { get; set; }

    public static BoardItem CreateText(string text, Guid id, DateTimeOffset createdAt) => new()
    {
        Id = id,
        Kind = BoardItemKind.Text,
        Category = BoardCategory.Inbox,
        Order = 0,
        CreatedAt = createdAt,
        Text = text
    };

    public static BoardItem CreateImage(
        Guid id,
        string relativePath,
        string absolutePath,
        DateTimeOffset createdAt) => new()
        {
            Id = id,
            Kind = BoardItemKind.Image,
            Category = BoardCategory.Inbox,
            Order = 0,
            CreatedAt = createdAt,
            ImageRelativePath = relativePath,
            ImageAbsolutePath = absolutePath
        };

    public BoardItem CloneForSnapshot() => new()
    {
        Id = Id,
        Kind = Kind,
        Category = Category,
        Order = Order,
        CreatedAt = CreatedAt,
        Text = Text,
        Name = Name,
        ArrivalSequence = ArrivalSequence,
        FileRelativePath = FileRelativePath,
        FileAbsolutePath = FileAbsolutePath,
        FileName = FileName,
        FileSize = FileSize,
        ImageRelativePath = ImageRelativePath,
        IsPinned = IsPinned,
        ImageAbsolutePath = ImageAbsolutePath
    };

    private void SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

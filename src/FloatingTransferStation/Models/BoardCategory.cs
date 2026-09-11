using System.Globalization;

namespace FloatingTransferStation.Models;

public enum BoardCategory
{
    CustomerOriginal = 0,
    Reference = 1,
    Prompt = 2,
    Inbox = 3
}

public static class BoardCategoryCatalog
{
    public const int MaxDisplayNameLength = 6;

    public static IReadOnlyList<BoardCategory> Ordered { get; } =
        Array.AsReadOnly(new[]
        {
            BoardCategory.CustomerOriginal,
            BoardCategory.Reference,
            BoardCategory.Prompt,
            BoardCategory.Inbox
        });

    public static IReadOnlyDictionary<BoardCategory, string> DefaultNames { get; } =
        new Dictionary<BoardCategory, string>
        {
            [BoardCategory.CustomerOriginal] = "人物资产",
            [BoardCategory.Reference] = "场景",
            [BoardCategory.Prompt] = "提示词",
            [BoardCategory.Inbox] = "待分类"
        };

    public static string DisplayName(BoardCategory category) => category switch
    {
        BoardCategory.CustomerOriginal => "人物资产",
        BoardCategory.Reference => "场景",
        BoardCategory.Prompt => "提示词",
        BoardCategory.Inbox => "待分类",
        _ when (int)category >= 1000 => "自定义",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null)
    };

    public static bool IsDefined(BoardCategory category) =>
        category is >= BoardCategory.CustomerOriginal and <= BoardCategory.Inbox || (int)category >= 1000;

    public static bool IsValidDisplayName(string? name) =>
        name is not null &&
        StringInfo.ParseCombiningCharacters(name).Length <= MaxDisplayNameLength;

    public static string LimitDisplayName(string? name)
    {
        var value = name ?? string.Empty;
        var elementIndexes = StringInfo.ParseCombiningCharacters(value);
        return elementIndexes.Length <= MaxDisplayNameLength
            ? value
            : value[..elementIndexes[MaxDisplayNameLength]];
    }
}

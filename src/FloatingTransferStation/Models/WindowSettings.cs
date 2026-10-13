namespace FloatingTransferStation.Models;

public sealed record WindowSettings(
    double PanelWidth,
    double WindowHeight,
    double Top,
    Dictionary<BoardCategory, string>? CategoryNames = null,
    double WindowOpacity = 0.88)
{
    public const double TabWidth = 58;
    public const double MinPanelWidth = 280;
    public const double MaxPanelWidth = 640;
    public const double MinWindowHeight = 360;
    public const double MinWindowOpacity = 0.35;
    public const double MaxWindowOpacity = 1.0;
    public const double DefaultWindowOpacity = 0.88;

    public bool TextHoverExpansionEnabled { get; init; } = true;
    public bool CardActionsInContextMenuOnly { get; init; } = true;
    public bool CardCopyPinButtonsEnabled { get; init; } = true;
    public bool CardHoverLiftEnabled { get; init; } = true;
    public bool CopySuccessSoundEnabled { get; init; } = true;
    public int? InboxItemLimit { get; init; }
    public double TextBrightness { get; init; }
    public double TextOpacity { get; init; } = 1;
    public double BackgroundBrightness { get; init; } = 1;
    public double BackgroundOpacity { get; init; } = 0.45;
    public double GlassIntensity { get; init; } = 0.5;
    public bool FreeFloating { get; init; }
    public bool DockLeft { get; init; }
    public double? WindowX { get; init; }
    public HashSet<BoardCategory> DeletedCategoryIds { get; init; } = [];

    public static WindowSettings Default { get; } = new(360, 640, 80);

    public string CategoryName(BoardCategory category)
    {
        if (!BoardCategoryCatalog.IsDefined(category))
        {
            throw new ArgumentOutOfRangeException(nameof(category), category, null);
        }

        return CategoryNames is not null &&
               CategoryNames.TryGetValue(category, out var name) &&
               BoardCategoryCatalog.IsValidDisplayName(name)
            ? name
            : BoardCategoryCatalog.DisplayName(category);
    }

    public WindowSettings WithCategoryName(BoardCategory category, string name)
    {
        if (!BoardCategoryCatalog.IsDefined(category))
        {
            throw new ArgumentOutOfRangeException(nameof(category), category, null);
        }

        if (!BoardCategoryCatalog.IsValidDisplayName(name))
        {
            throw new ArgumentException(
                $"Category names must contain at most {BoardCategoryCatalog.MaxDisplayNameLength} characters.",
                nameof(name));
        }

        var names = BoardCategoryCatalog.Ordered
            .Concat(CategoryNames?.Keys.AsEnumerable() ?? Enumerable.Empty<BoardCategory>())
            .Where(BoardCategoryCatalog.IsDefined)
            .Distinct().ToDictionary(
            current => current,
            CategoryName);
        names[category] = name;
        return this with { CategoryNames = names };
    }

    public WindowSettings ResetToDefault(double workAreaWidth, double workAreaHeight) =>
        (this with
        {
            PanelWidth = Default.PanelWidth,
            WindowHeight = Default.WindowHeight,
            Top = Default.Top,
            WindowOpacity = Default.WindowOpacity,
            FreeFloating = false,
            DockLeft = false,
            WindowX = null,
            TextBrightness = Default.TextBrightness,
            TextOpacity = Default.TextOpacity,
            BackgroundBrightness = Default.BackgroundBrightness,
            BackgroundOpacity = Default.BackgroundOpacity,
            GlassIntensity = Default.GlassIntensity
        })
            .Normalize(workAreaWidth, workAreaHeight);

    public WindowSettings Normalize(double workAreaWidth, double workAreaHeight)
    {
        var maxPanelWidth = Math.Max(0, Math.Min(MaxPanelWidth, workAreaWidth - TabWidth));
        var minPanelWidth = Math.Min(MinPanelWidth, maxPanelWidth);
        var panelWidth = Math.Clamp(PanelWidth, minPanelWidth, maxPanelWidth);
        var minHeight = Math.Min(MinWindowHeight, workAreaHeight);
        var height = Math.Clamp(WindowHeight, minHeight, workAreaHeight);
        var top = Math.Clamp(Top, 0, Math.Max(0, workAreaHeight - height));
        return this with
        {
            PanelWidth = panelWidth,
            WindowHeight = height,
            Top = top,
            WindowOpacity = NormalizeOpacity(WindowOpacity),
            InboxItemLimit = InboxItemLimit is > 0 ? InboxItemLimit : null,
            TextBrightness = NormalizeUnit(TextBrightness, 0),
            TextOpacity = NormalizeUnit(TextOpacity, 1),
            BackgroundBrightness = NormalizeUnit(BackgroundBrightness, 1),
            BackgroundOpacity = NormalizeUnit(BackgroundOpacity, 0.45),
            GlassIntensity = NormalizeUnit(GlassIntensity, 0.5),
            WindowX = WindowX is { } x && double.IsFinite(x) ? x : null
        };
    }

    private static double NormalizeOpacity(double opacity) =>
        double.IsFinite(opacity)
            ? opacity == 0
                ? DefaultWindowOpacity
                : Math.Clamp(opacity, MinWindowOpacity, MaxWindowOpacity)
            : DefaultWindowOpacity;

    private static double NormalizeUnit(double value, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 1) : fallback;
}

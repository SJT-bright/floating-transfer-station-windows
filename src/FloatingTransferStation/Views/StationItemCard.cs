using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using FloatingTransferStation.Models;

namespace FloatingTransferStation.Views;

public sealed class StationItemCard : UserControl
{
    private readonly Grid _layout = new();
    private readonly Border _glass = new() { CornerRadius = new CornerRadius(12) };
    private readonly Button _title = new() { HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private readonly TextBlock _titleText = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBox _editor = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, Visibility = Visibility.Collapsed };
    private readonly Button _arrow = new() { Width = 22, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Visibility = Visibility.Collapsed };
    private readonly TextBox _text = new()
    {
        IsReadOnly = true,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 12,
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        MaxHeight = 36,
        MinHeight = 18
    };
    private readonly System.Windows.Controls.Image _image = new() { MaxHeight = 160, Stretch = Stretch.Uniform };
    private readonly TextBlock _fileText = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _missing = new() { Text = "图片文件已缺失，请重新导入原文件", FontSize = 12, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly Button _select;
    private readonly Button _pin;
    private readonly Button _copy;
    private readonly StackPanel _others = new() { Orientation = Orientation.Horizontal };
    private readonly ContextMenu _menu = new();
    private readonly DispatcherTimer _expand = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _collapse = new() { Interval = TimeSpan.FromMilliseconds(380) };
    private readonly TranslateTransform _lift = new();
    private MainWindow? _owner;
    private BoardItem? _item;
    private Guid _previewGeneration;
    private Guid _editGeneration;
    private bool _expanded;
    private bool _editing;
    private bool _pending;
    private bool _reading;

    public StationItemCard()
    {
        Background = Brushes.Transparent;
        _layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 4; i++) { _layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); }
        var titleHost = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 4, 0) };
        DockPanel.SetDock(_arrow, Dock.Left);
        titleHost.Children.Add(_arrow);
        var naming = new Grid();
        _title.Content = _titleText;
        naming.Children.Add(_title);
        naming.Children.Add(_editor);
        titleHost.Children.Add(naming);
        _layout.Children.Add(titleHost);
        _select = ActionButton("选择", "ToggleSelection", "□");
        _select.SetResourceReference(StyleProperty, "SelectionButtonStyle");
        _select.Opacity = 1;
        _select.IsHitTestVisible = true;
        _pin = ActionButton("置顶或取消置顶", "TogglePin", "⌖");
        _pin.SetResourceReference(StyleProperty, "PinButtonStyle");
        var pinPath = new System.Windows.Shapes.Path { Data = Geometry.Parse("M8,3 L16,3 L14,8 L17,11 L12,11 L12,18 L10,20 L10,11 L5,11 L8,8 Z"), Stretch = Stretch.Uniform };
        pinPath.SetBinding(System.Windows.Shapes.Shape.FillProperty, new System.Windows.Data.Binding("Foreground") { Source = _pin });
        _pin.Content = new Viewbox { Width = 16, Height = 16, Child = pinPath };
        _pin.Opacity = 1;
        _pin.IsHitTestVisible = true;
        _copy = ActionButton("复制", "CopyItem", "⧉");
        for (var i = 0; i < 3; i++)
        {
            var button = new[] { _select, _pin, _copy }[i];
            Grid.SetColumn(button, i + 1);
            _layout.Children.Add(button);
        }
        Grid.SetColumn(_others, 4);
        _layout.Children.Add(_others);
        var body = new StackPanel { Margin = new Thickness(0, 5, 0, 0) };
        body.Children.Add(_text);
        body.Children.Add(_image);
        body.Children.Add(_fileText);
        body.Children.Add(_missing);
        Grid.SetRow(body, 1);
        Grid.SetColumnSpan(body, 5);
        _layout.Children.Add(body);
        _glass.Child = _layout;
        _glass.RenderTransform = _lift;
        Content = _glass;
        ContextMenu = _menu;
        _text.ContextMenu = _menu;
        _editor.ContextMenu = ChineseEditorMenu(_editor);
        _title.Click += (_, _) => BeginNaming();
        _arrow.Click += (_, _) => { _expand.Stop(); _expanded = !_expanded; RefreshBody(); };
        _title.MouseEnter += (_, _) => StartHover();
        _title.MouseLeave += (_, _) => _expand.Stop();
        MouseEnter += (_, _) => { _collapse.Stop(); UpdateLift(); };
        MouseLeave += (_, _) =>
        {
            _expand.Stop();
            UpdateLift();
            if (CanAutoExpand()) { _collapse.Start(); }
        };
        _expand.Tick += (_, _) =>
        {
            _expand.Stop();
            if (_title.IsMouseOver && CanAutoExpand()) { _expanded = true; RefreshBody(); }
        };
        _collapse.Tick += (_, _) =>
        {
            _collapse.Stop();
            if (!IsMouseOver && CanAutoExpand()) { _expanded = false; RefreshBody(); }
        };
        _menu.Opened += (_, _) => { _expand.Stop(); _collapse.Stop(); BuildMenu(); UpdateHold(); UpdateLift(); };
        _menu.Closed += (_, _) => { UpdateHold(); UpdateLift(); };
        _editor.KeyDown += async (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Return) { e.Handled = true; await FinishNamingAsync(); }
            else if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; EndNaming(); }
        };
        _editor.LostKeyboardFocus += async (_, _) => { if (_editing && !_editor.IsKeyboardFocusWithin) { await FinishNamingAsync(); } };
        Loaded += (_, _) => { _owner = FindOwner(this); RefreshSettings(); RefreshItem(); };
        Unloaded += (_, _) =>
        {
            _expand.Stop(); _collapse.Stop(); _previewGeneration = Guid.NewGuid();
            if (!_pending && !_reading && _item is { } item) { _owner?.SetCardInteractionHeld(item.Id, false); }
        };
        DataContextChanged += (_, _) =>
        {
            if (_item is not null) { _item.PropertyChanged -= ItemChanged; _owner?.SetCardInteractionHeld(_item.Id, false); }
            _item = DataContext as BoardItem;
            _editGeneration = Guid.NewGuid();
            _expanded = false; _editing = false; _pending = false; _reading = false;
            if (_item is not null) { _item.PropertyChanged += ItemChanged; }
            RefreshItem();
        };
    }

    public void BeginNaming()
    {
        if (_item is null || _editing || _pending) { return; }
        _expand.Stop(); _collapse.Stop(); _editing = true;
        _editGeneration = Guid.NewGuid();
        _editor.Text = _item.Name ?? string.Empty;
        _editor.Visibility = Visibility.Visible;
        _title.Visibility = Visibility.Collapsed;
        UpdateHold(); UpdateLift();
        Dispatcher.BeginInvoke(() => { _editor.Focus(); _editor.SelectAll(); }, DispatcherPriority.Input);
    }

    public void RefreshSettings()
    {
        if (_owner is null) { return; }
        var settings = _owner.CurrentSettings;
        var shade = (byte)Math.Round(settings.TextBrightness * 255);
        var textBrush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(settings.TextOpacity * 255), shade, shade, shade));
        Foreground = textBrush; _titleText.Foreground = textBrush; _text.Foreground = textBrush; _fileText.Foreground = textBrush; _missing.Foreground = textBrush;
        _select.Foreground = _copy.Foreground = textBrush;
        _pin.Foreground = _item?.IsPinned == true ? (Brush)FindResource("AccentBrush") : textBrush;
        _pin.Visibility = _copy.Visibility = settings.CardCopyPinButtonsEnabled ? Visibility.Visible : Visibility.Collapsed;
        _others.Visibility = settings.CardActionsInContextMenuOnly ? Visibility.Collapsed : Visibility.Visible;
        _others.Children.Clear();
        if (_item?.Kind == BoardItemKind.Text) { _others.Children.Add(InlineAction("查看全文", "▤", ReadFullText)); }
        _others.Children.Add(InlineAction("移动到分类", "▱", () => { BuildMenu(); _menu.IsOpen = true; }));
        _others.Children.Add(InlineAction("删除", "×", () => RunCommand("DeleteItem")));
        var background = (byte)Math.Round(settings.BackgroundBrightness * 255);
        var alpha = (byte)Math.Round(settings.BackgroundOpacity * 130);
        _glass.Background = new LinearGradientBrush(Color.FromArgb(alpha, background, background, background),
            Color.FromArgb((byte)Math.Min(255, alpha + settings.GlassIntensity * 28), background, background, background), 35);
        _glass.BorderThickness = new Thickness(settings.GlassIntensity > 0 ? 1 : 0);
        _glass.BorderBrush = new SolidColorBrush(Color.FromArgb((byte)(settings.GlassIntensity * 120), 255, 255, 255));
        if (!settings.TextHoverExpansionEnabled) { _expand.Stop(); _collapse.Stop(); }
        UpdateLift();
    }

    private void RefreshItem()
    {
        if (_item is null) { return; }
        foreach (var button in new[] { _select, _pin, _copy }) { button.Tag = _item; }
        _titleText.Text = _item.DisplayTitle;
        _title.ToolTip = "点击名称文字改名；在卡片空白或正文处拖动；右键显示全部操作";
        _text.Text = _item.Text ?? string.Empty;
        _fileText.Text = _item.FileName ?? "文件副本已缺失";
        if (_item.Kind == BoardItemKind.File && !File.Exists(_item.FileAbsolutePath)) { _fileText.Text += "\n文件副本已缺失，请重新导入"; }
        _image.Source = null;
        _missing.Visibility = Visibility.Collapsed;
        var generation = _previewGeneration = Guid.NewGuid();
        if (_item.Kind == BoardItemKind.Image && _item.ImageAbsolutePath is { } path)
        {
            _ = LoadPreviewAsync(path, generation);
        }
        RefreshSettings(); RefreshBody();
    }

    private async Task LoadPreviewAsync(string path, Guid generation)
    {
        var preview = await Task.Run(() => new Converters.ImageThumbnailConverter().Convert(path, typeof(ImageSource), 512, System.Globalization.CultureInfo.InvariantCulture));
        if (generation != _previewGeneration) { return; }
        _image.Source = preview as ImageSource;
        _missing.Visibility = preview is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BoardItem.IsPinned)) { RefreshSettings(); }
        if (e.PropertyName is nameof(BoardItem.Name) or nameof(BoardItem.DisplayTitle))
        {
            _titleText.Text = _item?.DisplayTitle ?? "添加名称";
            RefreshBody();
            _owner?.RefreshSearch();
        }
    }

    private void RefreshBody()
    {
        if (_item is null) { return; }
        var text = _item.Kind == BoardItemKind.Text;
        var named = !string.IsNullOrWhiteSpace(_item.Name);
        _text.MaxHeight = Math.Ceiling(_text.FontSize * _text.FontFamily.LineSpacing) * 2 + _text.Padding.Top + _text.Padding.Bottom;
        _arrow.Visibility = text && named && !_editing ? Visibility.Visible : Visibility.Collapsed;
        _arrow.Content = _expanded ? "⌄" : "›";
        _text.Visibility = text && (!named || _expanded) ? Visibility.Visible : Visibility.Collapsed;
        _image.Visibility = _item.Kind == BoardItemKind.Image ? Visibility.Visible : Visibility.Collapsed;
        _fileText.Visibility = _item.Kind == BoardItemKind.File ? Visibility.Visible : Visibility.Collapsed;
        if (_text.Visibility == Visibility.Visible && SystemParameters.ClientAreaAnimation)
        {
            _text.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)));
        }
    }

    private bool CanAutoExpand() => _owner?.CurrentSettings.TextHoverExpansionEnabled == true && _item?.Kind == BoardItemKind.Text &&
        !string.IsNullOrWhiteSpace(_item.Name) && !_editing && !_pending && !_reading && !_menu.IsOpen && !_owner.IsCardDragInProgress;

    private void StartHover()
    {
        if (!_expanded && CanAutoExpand() && !_expand.IsEnabled) { _expand.Start(); }
    }

    private async Task FinishNamingAsync()
    {
        if (!_editing || _pending || _owner is null || _item is null) { return; }
        _pending = true; UpdateHold();
        var generation = _editGeneration;
        var item = _item;
        try { await _owner.RenameItemAsync(item, _editor.Text); }
        finally
        {
            if (_editGeneration == generation && ReferenceEquals(_item, item)) { _pending = false; EndNaming(); }
            else { _owner.SetCardInteractionHeld(item.Id, false); }
        }
    }

    private void EndNaming()
    {
        _editing = false; _editor.Visibility = Visibility.Collapsed; _title.Visibility = Visibility.Visible;
        RefreshBody(); UpdateHold(); UpdateLift();
    }

    private void UpdateHold()
    {
        if (_item is { } item) { _owner?.SetCardInteractionHeld(item.Id, _editing || _pending || _reading || _menu.IsOpen); }
    }

    private void UpdateLift()
    {
        var lifted = IsMouseOver && _owner?.CurrentSettings.CardHoverLiftEnabled == true && !_editing && !_pending && !_reading && !_menu.IsOpen;
        var y = lifted && SystemParameters.ClientAreaAnimation ? -2 : 0;
        _lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, TimeSpan.FromMilliseconds(160)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        _glass.Effect = lifted ? new DropShadowEffect { BlurRadius = 12, ShadowDepth = 4, Opacity = 0.22 } : null;
    }

    private void BuildMenu()
    {
        if (_item is null || _owner is null) { return; }
        _menu.Items.Clear();
        AddMenu("重命名", BeginNaming);
        AddMenu(_item.IsPinned ? "取消置顶" : "置顶", () => RunCommand("TogglePin"));
        AddMenu("复制", () => RunCommand("CopyItem"));
        if (_item.Kind == BoardItemKind.Text)
        {
            AddMenu("查看全文", ReadFullText);
            AddMenu(_expanded ? "收起正文" : "展开正文", () => { _expanded = !_expanded; RefreshBody(); });
        }
        if (_item.Kind != BoardItemKind.File)
        {
            var move = new MenuItem { Header = "移动到" };
            foreach (var category in _owner.AvailableCategories.Where(category => category != _item.Category && category != BoardCategory.Files))
            {
                var target = category;
                var child = new MenuItem { Header = _owner.CurrentSettings.CategoryName(target) };
                child.Click += (_, _) => { if (_item is not null) { _owner.MoveItemToCategory(_item, target); } };
                move.Items.Add(child);
            }
            _menu.Items.Add(move);
        }
        else { AddMenu("在资源管理器中显示", () => RunCommand("ShowFile")); }
        AddMenu("删除", () => RunCommand("DeleteItem"));
    }

    private void AddMenu(string title, Action action)
    {
        var item = new MenuItem { Header = title };
        item.Click += (_, _) => action();
        _menu.Items.Add(item);
    }

    private void RunCommand(string command)
    {
        if (_owner is not null && _item is not null) { _owner.RunItemCommand(_item, command); }
    }

    private void ReadFullText()
    {
        if (_item is null || _owner is null || _reading) { return; }
        var text = new TextBox
        {
            Text = _item.Text ?? string.Empty,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontSize = 12,
            Margin = new Thickness(12)
        };
        text.ContextMenu = ChineseEditorMenu(text);
        var reader = new Window { Title = _item.Name ?? "查看全文", Width = 560, Height = 420, Owner = _owner, Content = text };
        _reading = true; _expand.Stop(); _collapse.Stop(); UpdateHold();
        reader.Closed += (_, _) => { _reading = false; UpdateHold(); };
        reader.Show();
    }

    private static Button ActionButton(string label, string command, string icon)
    {
        var button = new Button { Content = icon, CommandParameter = command, ToolTip = label, MinWidth = 26, Height = 26, FontSize = 17 };
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        button.SetResourceReference(StyleProperty, "HeaderActionButtonStyle");
        return button;
    }

    private static Button InlineAction(string label, string icon, Action action)
    {
        var button = ActionButton(label, string.Empty, icon);
        button.Click += (_, e) => { e.Handled = true; action(); };
        return button;
    }

    internal static ContextMenu ChineseEditorMenu(TextBox editor)
    {
        var menu = new ContextMenu();
        foreach (var (title, action) in new (string, Action)[] { ("剪切", editor.Cut), ("复制", editor.Copy), ("粘贴", editor.Paste), ("全选", editor.SelectAll) })
        {
            var item = new MenuItem { Header = title };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        return menu;
    }

    private static MainWindow? FindOwner(DependencyObject element)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is MainWindow owner) { return owner; }
        }
        return null;
    }
}

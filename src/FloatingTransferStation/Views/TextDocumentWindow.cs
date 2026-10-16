using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FloatingTransferStation.Models;
using FloatingTransferStation.Services;

namespace FloatingTransferStation.Views;

public sealed class TextDocumentWindow : Window
{
    private readonly BoardItem _item;
    private readonly Func<string, string, Task<BoardTextEditResult>> _save;
    private readonly Button _edit = new() { Content = "编辑正文", MinWidth = 72 };
    internal Button SaveButton { get; } = new() { Content = "保存", MinWidth = 64 };
    internal Button CancelButton { get; } = new() { Content = "取消", MinWidth = 64 };
    internal TextBox BodyEditor { get; } = new()
    {
        AcceptsReturn = true,
        AcceptsTab = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontSize = 14,
        IsUndoEnabled = true,
        Padding = new Thickness(8)
    };
    internal TextBlock ErrorLabel { get; } = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private string _original;
    private bool _editing;
    private bool _saving;

    public TextDocumentWindow(Window owner, BoardItem item,
        Func<string, string, Task<BoardTextEditResult>> save, bool editing)
    {
        Owner = owner; _item = item; _save = save; _original = item.Text ?? string.Empty;
        Width = 560; Height = 440; MinWidth = 360; MinHeight = 280;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Title = item.Name ?? "文字全文";
        var root = new DockPanel { Margin = new Thickness(14) };
        var header = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 10) };
        foreach (var button in new[] { _edit, CancelButton, SaveButton }) { button.Margin = new Thickness(6, 0, 0, 0); header.Children.Add(button); }
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var hint = new TextBlock { Text = "只修改正文，名称、分类、置顶和排序保持不变。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(hint, Dock.Top); root.Children.Add(hint);
        DockPanel.SetDock(ErrorLabel, Dock.Bottom); root.Children.Add(ErrorLabel);
        root.Children.Add(BodyEditor); Content = root;
        BodyEditor.ContextMenu = StationItemCard.ChineseEditorMenu(BodyEditor);
        AutomationProperties.SetAutomationId(BodyEditor, "station-body-editor");
        AutomationProperties.SetName(BodyEditor, "正文");
        AutomationProperties.SetName(SaveButton, "保存正文");
        BodyEditor.TextChanged += (_, _) => UpdateSaveState();
        _edit.Click += (_, _) => { _original = _item.Text ?? string.Empty; BodyEditor.Text = _original; SetEditing(true); };
        SaveButton.Click += async (_, _) => await SaveAsync();
        CancelButton.Click += (_, _) => Close();
        PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; await SaveAsync(); }
            else if (e.Key == Key.Escape && !_saving) { e.Handled = true; Close(); }
        };
        Closing += (_, e) => { if (_saving) { e.Cancel = true; } };
        BodyEditor.Text = _original;
        SetEditing(editing);
        Loaded += (_, _) => { if (_editing) { BodyEditor.Focus(); } };
    }

    private void SetEditing(bool editing)
    {
        _editing = editing;
        BodyEditor.IsReadOnly = !editing;
        _edit.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
        SaveButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Content = editing ? "取消" : "关闭";
        Title = editing ? $"编辑正文 — {_item.Name ?? "未命名"}" : _item.Name ?? "文字全文";
        UpdateSaveState();
        if (editing) { BodyEditor.Focus(); }
    }

    private void UpdateSaveState() => SaveButton.IsEnabled = _editing && !_saving &&
        !string.IsNullOrWhiteSpace(BodyEditor.Text) && BodyEditor.Text != _original;

    internal async Task SaveAsync()
    {
        if (!SaveButton.IsEnabled) { return; }
        _saving = true; UpdateSaveState(); CancelButton.IsEnabled = false; BodyEditor.IsReadOnly = true;
        try
        {
            var result = await _save(BodyEditor.Text, _original);
            if (result is BoardTextEditResult.Saved or BoardTextEditResult.Unchanged) { _saving = false; Close(); return; }
            ErrorLabel.Text = result switch
            {
                BoardTextEditResult.Conflict => "正文已被其他操作修改，请复制保留草稿后重新打开。",
                BoardTextEditResult.Invalid => "记录已不存在或正文为空，未保存修改。",
                _ => "正文未保存，原文和草稿已保留，请重试。"
            };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException)
        { ErrorLabel.Text = "正文未保存，原文和草稿已保留，请重试。"; }
        finally { _saving = false; CancelButton.IsEnabled = true; BodyEditor.IsReadOnly = !_editing; UpdateSaveState(); }
    }
}

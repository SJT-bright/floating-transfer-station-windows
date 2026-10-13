using System.Collections.Specialized;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FloatingTransferStation.Models;
using FloatingTransferStation.Services;

namespace FloatingTransferStation.Views;

public partial class MainWindow
{
    private readonly HashSet<Guid> _cardInteractions = [];
    private readonly System.Media.SoundPlayer _successPlayer = new();
    private StationSettingsWindow? _stationSettings;
    public WindowSettings CurrentSettings => _settings;
    public IReadOnlyList<BoardCategory> AvailableCategories => _viewModel.Categories.Select(category => category.Category).ToArray();
    public bool IsCardDragInProgress => _panelState.IsDragInProgress;

    public void SetCardInteractionHeld(Guid id, bool held)
    {
        if (held)
        {
            _cardInteractions.Add(id);
            StopPanelContentAnimation();
            _collapseTimer.Stop();
        }
        else { _cardInteractions.Remove(id); if (_cardInteractions.Count == 0) { ReconcileSurfaceAfterCategoryNameEdit(); } }
    }

    public Task<bool> RenameItemAsync(BoardItem item, string name)
    {
        if (_isClosing) { return Task.FromResult(false); }
        var task = _mutations.RenameAsync(item.Id, name);
        TrackPendingOperation(task);
        return task;
    }

    public void RunItemCommand(BoardItem item, string command)
    {
        if (_isClosing) { return; }
        TrackPendingOperation(ExecuteItemCommandAsync(item, command));
    }

    private async Task ExecuteItemCommandAsync(BoardItem item, string command)
    {
        switch (command)
        {
            case "CopyItem": await CopyItemAsync(item); break;
            case "TogglePin": await _mutations.SetPinnedAsync([item.Id], !item.IsPinned); break;
            case "DeleteItem": await _mutations.DeleteAsync(item.Id); break;
            case "ShowFile":
                if (item.FileAbsolutePath is { } path && File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                }
                break;
        }
        RefreshSearch();
    }

    public void MoveItemToCategory(BoardItem item, BoardCategory category) =>
        TrackPendingOperation(MoveItemSafelyAsync(item, category));

    private async Task MoveItemSafelyAsync(BoardItem item, BoardCategory category)
    {
        await _mutations.MoveManyToCategoryTopAsync([item.Id], category);
        RefreshSearch();
    }

    private async Task CopyItemAsync(BoardItem item)
    {
        try
        {
            if (item.Kind == BoardItemKind.File)
            {
                var path = await Task.Run(() => new FileAssetStore(_store.DataDirectory).EnsureExport(item));
                Clipboard.SetFileDropList(new StringCollection { path });
            }
            else { Clipboard.SetDataObject(_dragPayload.Build(item), copy: true); }
            _clipboardCapture.IgnoreSequence(NativeMethods.GetClipboardSequenceNumber());
            ShowStatus("复制成功");
            PlaySuccessSound();
        }
        catch (Exception e) when (e is IOException or System.Runtime.InteropServices.ExternalException)
        {
            ShowStatus("复制失败，请重试。");
        }
    }

    private void PlaySuccessSound()
    {
        if (!_settings.CopySuccessSoundEnabled) { return; }
        Dispatcher.BeginInvoke(() =>
        {
            try { _successPlayer.Play(); }
            catch (InvalidOperationException) { }
        });
    }

    private void InitializeStationFeatures()
    {
        _board.InboxItemLimit = _settings.InboxItemLimit;
        _board.CaptureSucceeded = PlaySuccessSound;
        ApplyStationAppearance();
        Loaded += (_, _) => ApplyStationAppearance();
        var sound = Application.GetResourceStream(new Uri("/悬浮中转站;component/Resources/GlassTap.wav", UriKind.Relative));
        if (sound is not null)
        {
            var bytes = new MemoryStream();
            sound.Stream.CopyTo(bytes);
            bytes.Position = 0;
            _successPlayer.Stream = bytes;
        }
        foreach (var category in _viewModel.Categories) { category.Items.CollectionChanged += (_, _) => RefreshSearch(); }
        _viewModel.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(_viewModel.ActivePanel))
            {
                SearchBox.Text = string.Empty;
                RefreshSearch();
            }
        };
        SearchBox.ContextMenu = StationItemCard.ChineseEditorMenu(SearchBox);
        HeaderSurface.ContextMenu = new ContextMenu();
        var restart = new MenuItem { Header = "重新启动悬浮中转站" };
        restart.Click += (_, _) => RequestRestart();
        HeaderSurface.ContextMenu.Items.Add(restart);
        var quit = new MenuItem { Header = "退出悬浮中转站" };
        quit.Click += (_, _) => Close(); HeaderSurface.ContextMenu.Items.Add(quit);
        _viewModel.Categories.CollectionChanged += (_, change) =>
        {
            _viewModel.RefreshCategoryList();
            if (change.NewItems is not null)
            {
                foreach (ViewModels.CategoryViewModel category in change.NewItems) { category.Items.CollectionChanged += (_, _) => RefreshSearch(); }
            }
        };
    }

    public void RefreshSearch()
    {
        if (!Dispatcher.CheckAccess())
        {
            if (!Dispatcher.HasShutdownStarted) { Dispatcher.BeginInvoke(RefreshSearch); }
            return;
        }
        if (SearchBox is null) { return; }
        var query = SearchBox.Text.Trim();
        if (query.Length == 0)
        {
            if (_viewModel.ActivePanel is { } panel) { BoardList.ItemsSource = panel.Items; }
            return;
        }
        BoardList.ItemsSource = _board.Categories.SelectMany(_board.Items)
            .Where(item => item.Name?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true)
            .OrderByDescending(item => item.IsPinned).ThenBy(item => item.Order).ToArray();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (BoardList is not null) { BoardList.UnselectAll(); }
        RefreshSearch();
    }

    private void StationSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_stationSettings is not null) { _stationSettings.Activate(); return; }
        _stationSettings = new StationSettingsWindow(this);
        SetCardInteractionHeld(Guid.Empty, true);
        _stationSettings.Closed += (_, _) => { _stationSettings = null; SetCardInteractionHeld(Guid.Empty, false); };
        _stationSettings.Show();
    }

    public async Task<bool> ApplyStationSettingsAsync(Func<WindowSettings, WindowSettings> update)
    {
        await _settingsSaveGate.WaitAsync();
        try
        {
            var work = CurrentWorkArea();
            var next = update(_settings).Normalize(work.Width, work.Height);
            await _store.SaveSettingsAsync(next);
            _settings = next; _board.InboxItemLimit = next.InboxItemLimit;
            ApplyStationAppearance();
            foreach (var card in Descendants<StationItemCard>(BoardList)) { card.RefreshSettings(); }
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ShowStatus("设置未保存，请重试；原设置保留。"); return false;
        }
        finally { _settingsSaveGate.Release(); }
    }

    public void RequestRestart()
    {
        if (Environment.ProcessPath is not { } path) { return; }
        Process.Start(new ProcessStartInfo(path, $"--restart --target-pid {Environment.ProcessId}") { UseShellExecute = false });
    }

    private void ApplyStationAppearance()
    {
        var shade = (byte)(_settings.BackgroundBrightness * 255);
        var background = new SolidColorBrush(Color.FromArgb((byte)(_settings.BackgroundOpacity * 255), shade, shade, shade));
        WindowShell.Background = background; CategoryRail.Background = background;
        var foreground = (byte)(_settings.TextBrightness * 255);
        var text = new SolidColorBrush(Color.FromArgb((byte)(_settings.TextOpacity * 255), foreground, foreground, foreground));
        foreach (var label in Descendants<TextBlock>(WindowShell).Where(label => label.Text != "◎")) { label.Foreground = text; }
        SearchBox.Foreground = text;
    }

    private async void DeleteCategoryMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement { DataContext: ViewModels.CategoryViewModel panel } } }) { return; }
        if (panel.Category is BoardCategory.Inbox or BoardCategory.Files) { ShowStatus("待分类与文件中转站是固定入口，不能删除。"); return; }
        var hold = Guid.NewGuid();
        SetCardInteractionHeld(hold, true);
        try
        {
            if (MessageBox.Show(this, $"删除“{panel.DisplayName}”？其中的内容会完整移回待分类，不删除素材。", "删除分类", MessageBoxButton.OKCancel) != MessageBoxResult.OK) { return; }
            await _settingsSaveGate.WaitAsync();
            try
            {
                var next = _settings with { DeletedCategoryIds = _settings.DeletedCategoryIds.Append(panel.Category).ToHashSet() };
                if (!await _mutations.DeleteCategoryAsync(panel.Category, next)) { return; }
                _settings = next;
                _viewModel.Categories.Remove(panel);
                _viewModel.RefreshCategoryList();
                _panelState.Switch(BoardCategory.Inbox); _viewModel.Activate(BoardCategory.Inbox);
                RefreshSearch(); ShowStatus("分类已删除，内容已移回待分类。");
            }
            finally { _settingsSaveGate.Release(); }
        }
        finally { SetCardInteractionHeld(hold, false); }
    }

    private async void ImportFilesButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Title = "导入文件中转站", Multiselect = true };
        var hold = Guid.NewGuid();
        SetCardInteractionHeld(hold, true);
        try
        {
            if (picker.ShowDialog(this) != true) { return; }
            if (await _mutations.ImportFilesAsync(picker.FileNames)) { ShowStatus($"已导入 {picker.FileNames.Length} 个文件"); PlaySuccessSound(); }
            RefreshSearch();
        }
        finally { SetCardInteractionHeld(hold, false); }
    }
}

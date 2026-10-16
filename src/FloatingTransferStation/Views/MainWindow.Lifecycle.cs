using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FloatingTransferStation.Models;
using FloatingTransferStation.Services;
using FloatingTransferStation.ViewModels;

namespace FloatingTransferStation.Views;

public partial class MainWindow : Window
{

    private void WidthThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var work = CurrentWorkArea();
        _settings = (_settings with { PanelWidth = _settings.PanelWidth + (_settings.DockLeft ? e.HorizontalChange : -e.HorizontalChange) })
            .Normalize(work.Width, work.Height);
        Width = _settings.PanelWidth + WindowSettings.TabWidth;
        DockRight();
    }

    private void HeightThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var work = CurrentWorkArea();
        _settings = (_settings with { WindowHeight = _settings.WindowHeight + e.VerticalChange })
            .Normalize(work.Width, work.Height);
        Height = _settings.WindowHeight;
        DockRight();
    }

    private void HeaderThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var work = CurrentWorkArea();
        StopPanelContentAnimation();
        var x = Math.Clamp(Left + e.HorizontalChange, work.Left, Math.Max(work.Left, work.Right - Width));
        var leftDock = Math.Abs(x - work.Left) <= 20;
        var rightDock = Math.Abs(work.Right - Width - x) <= 20;
        _settings = (_settings with
        {
            Top = _settings.Top + e.VerticalChange,
            WindowX = x,
            DockLeft = leftDock,
            FreeFloating = !leftDock && !rightDock
        })
            .Normalize(work.Width, work.Height);
        Top = work.Top + _settings.Top;
        Left = leftDock ? work.Left : rightDock ? work.Right - Width : x;
        UpdateDockLayout();
    }

    private async void ResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        try
        {
            await SaveSettingsAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowStatus("窗口尺寸暂未保存。");
        }
    }

    private void DockRight()
    {
        var work = CurrentWorkArea();
        if (!_settings.FreeFloating) { Left = _settings.DockLeft ? work.Left : work.Right - ActualWidth; }
    }

    private static WorkArea CurrentWorkArea()
    {
        var area = SystemParameters.WorkArea;
        return new WorkArea(area.Left, area.Top, area.Width, area.Height);
    }

    private void ApplyPlacement(WindowPlacement placement)
    {
        UpdateDockLayout();
        var expandsHorizontally = placement.Width > ActualWidth;
        var expandsVertically = placement.Height > ActualHeight;
        if (expandsHorizontally)
        {
            Width = placement.Width;
        }

        if (expandsVertically)
        {
            Height = placement.Height;
        }

        Left = placement.Left;
        Top = placement.Top;
        if (!expandsHorizontally)
        {
            Width = placement.Width;
        }

        if (!expandsVertically)
        {
            Height = placement.Height;
        }
    }

    private async Task SaveSettingsAsync()
    {
        await _settingsSaveGate.WaitAsync();
        try
        {
            await _store.SaveSettingsAsync(_settings);
        }
        finally
        {
            _settingsSaveGate.Release();
        }
    }

    private void UpdateDockLayout()
    {
        if (WindowColumns is null) { return; }
        WindowColumns.ColumnDefinitions[0].Width = _settings.DockLeft ? new GridLength(WindowSettings.TabWidth) : new GridLength(1, GridUnitType.Star);
        WindowColumns.ColumnDefinitions[1].Width = _settings.DockLeft ? new GridLength(1, GridUnitType.Star) : new GridLength(WindowSettings.TabWidth);
        Grid.SetColumn(PanelContentHost, _settings.DockLeft ? 1 : 0);
        Grid.SetColumn(CategoryRail, _settings.DockLeft ? 0 : 1);
        Grid.SetColumn(CollapsedCategoryHandle, _settings.DockLeft ? 0 : 1);
        WindowShell.CornerRadius = ShellOutline.CornerRadius = _settings.DockLeft ? new CornerRadius(0, 12, 12, 0) : new CornerRadius(12, 0, 0, 12);
        ShellOutline.BorderThickness = _settings.DockLeft ? new Thickness(0, 1, 1, 1) : new Thickness(1, 1, 0, 1);
        HeaderSurface.CornerRadius = _settings.DockLeft ? new CornerRadius(0, 11, 0, 0) : new CornerRadius(11, 0, 0, 0);
        WidthResizeThumb.HorizontalAlignment = _settings.DockLeft ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        WindowShell.Clip = WindowShellClip.Create(Width, Height, 12, _settings.DockLeft);
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            StopClipboardListening();
            return;
        }

        e.Cancel = true;
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        ResetManualCollapseGate();
        _expandIntentTimer.Stop();
        _collapseTimer.Stop();
        StopPanelContentAnimation();
        IsEnabled = false;
        var operationCancellation = _windowOperationCancellation;
        try
        {
            operationCancellation.Cancel();
            await DrainPendingOperationsAsync();
            await _mutations.SaveForShutdownAsync(() => _store.SaveSettingsAsync(_settings));
            operationCancellation.Dispose();
            _allowClose = true;
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(Close));
        }
        catch (Exception)
        {
            operationCancellation.Dispose();
            _windowOperationCancellation = new CancellationTokenSource();
            _isClosing = false;
            IsEnabled = true;
            ShowStatus("退出前保存失败，悬浮中转站暂未关闭。");
        }
    }

    private async Task DrainPendingOperationsAsync()
    {
        Task[] operations;
        lock (_pendingOperationsLock)
        {
            operations = [.. _pendingOperations];
        }

        await Task.WhenAll(operations);
    }

    private void StopClipboardListening()
    {
        if (_windowSource is null)
        {
            return;
        }

        NativeMethods.RemoveClipboardFormatListener(_windowSource.Handle);
        _windowSource.RemoveHook(WndProc);
        _windowSource = null;
    }
}

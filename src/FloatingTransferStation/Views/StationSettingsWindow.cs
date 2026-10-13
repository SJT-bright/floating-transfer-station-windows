using System.Windows;
using System.Windows.Controls;
using FloatingTransferStation.Models;

namespace FloatingTransferStation.Views;

public sealed class StationSettingsWindow : Window
{
    private readonly MainWindow _owner;
    private readonly List<(CheckBox Control, Func<WindowSettings, bool> Read, Func<WindowSettings, bool, WindowSettings> Write)> _options = [];
    private readonly List<(Slider Control, Func<WindowSettings, double> Read, Func<WindowSettings, double, WindowSettings> Write)> _sliders = [];
    private bool _updating;

    public StationSettingsWindow(MainWindow owner)
    {
        _owner = owner; Owner = owner; Title = "悬浮中转站设置与交互 DIY"; Width = 360; Height = 600; Topmost = true;
        var panel = new StackPanel { Margin = new Thickness(16) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "交互 DIY", FontSize = 16, FontWeight = FontWeights.SemiBold });
        Option(panel, "文字悬停满一秒自动展开", s => s.TextHoverExpansionEnabled, (s, v) => s with { TextHoverExpansionEnabled = v });
        Option(panel, "显示每条复制与置顶按钮", s => s.CardCopyPinButtonsEnabled, (s, v) => s with { CardCopyPinButtonsEnabled = v });
        Option(panel, "其他操作仅放在右键菜单", s => s.CardActionsInContextMenuOnly, (s, v) => s with { CardActionsInContextMenuOnly = v });
        Option(panel, "卡片悬停浮起与阴影", s => s.CardHoverLiftEnabled, (s, v) => s with { CardHoverLiftEnabled = v });
        Option(panel, "复制、保存成功时播放玻璃轻点", s => s.CopySuccessSoundEnabled, (s, v) => s with { CopySuccessSoundEnabled = v });
        panel.Children.Add(new TextBlock { Text = "关闭快捷按钮时，右键菜单仍然保留全部操作。多选框始终可用。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12) });
        Slider(panel, "文字深浅（黑—白）", s => s.TextBrightness, (s, v) => s with { TextBrightness = v });
        Slider(panel, "文字不透明度", s => s.TextOpacity, (s, v) => s with { TextOpacity = v });
        Slider(panel, "背景深浅（黑—白）", s => s.BackgroundBrightness, (s, v) => s with { BackgroundBrightness = v });
        Slider(panel, "背景不透明度", s => s.BackgroundOpacity, (s, v) => s with { BackgroundOpacity = v });
        Slider(panel, "清透玻璃亮边强度（不叠加磨砂）", s => s.GlassIntensity, (s, v) => s with { GlassIntensity = v });
        panel.Children.Add(new TextBlock { Text = "待分类数量上限（仅未置顶内容）", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
        var capacity = new TextBox { Text = owner.CurrentSettings.InboxItemLimit?.ToString() ?? "无限", Margin = new Thickness(0, 4, 0, 4) };
        capacity.ContextMenu = StationItemCard.ChineseEditorMenu(capacity);
        panel.Children.Add(capacity);
        var saveCapacity = new Button { Content = "保存数量上限" };
        panel.Children.Add(saveCapacity);
        saveCapacity.Click += async (_, _) =>
        {
            var value = capacity.Text.Trim();
            int? limit = null;
            if (value != "无限" && (!int.TryParse(value, out var parsed) || parsed <= 0))
            {
                MessageBox.Show(this, "请输入正整数，或填写“无限”。", "数量上限"); return;
            }
            if (value != "无限") { limit = int.Parse(value); }
            await owner.ApplyStationSettingsAsync(s => s with { InboxItemLimit = limit });
        };
        panel.Children.Add(new TextBlock { Text = "更改上限不会立即删除内容；下次新增时淘汰最早未置顶项，置顶内容保留。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 12) });
        var restart = new Button { Content = "重新启动悬浮中转站", Margin = new Thickness(0, 4, 0, 4) };
        restart.Click += (_, _) => owner.RequestRestart(); panel.Children.Add(restart);
        Refresh();
    }

    private void Option(Panel panel, string label, Func<WindowSettings, bool> read, Func<WindowSettings, bool, WindowSettings> write)
    {
        var box = new CheckBox { Content = label, Margin = new Thickness(0, 8, 0, 0) };
        _options.Add((box, read, write)); panel.Children.Add(box);
        box.Click += async (_, _) =>
        {
            if (_updating) { return; }
            await _owner.ApplyStationSettingsAsync(s => write(s, box.IsChecked == true)); Refresh();
        };
    }

    private void Slider(Panel panel, string label, Func<WindowSettings, double> read, Func<WindowSettings, double, WindowSettings> write)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 0) });
        var slider = new Slider { Minimum = 0, Maximum = 1, TickFrequency = 0.01, Margin = new Thickness(0, 4, 0, 0) };
        _sliders.Add((slider, read, write)); panel.Children.Add(slider);
        slider.ValueChanged += async (_, _) => { if (!_updating) { var value = slider.Value; await _owner.ApplyStationSettingsAsync(s => write(s, value)); } };
    }

    private void Refresh()
    {
        _updating = true;
        foreach (var option in _options) { option.Control.IsChecked = option.Read(_owner.CurrentSettings); }
        foreach (var slider in _sliders) { slider.Control.Value = slider.Read(_owner.CurrentSettings); }
        _updating = false;
    }
}

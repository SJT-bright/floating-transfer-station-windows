using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace FloatingTransferStation.Views;

public static class StationHoverFeedback
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(StationHoverFeedback), new PropertyMetadata(false, Changed));
    public static bool GetEnabled(DependencyObject value) => (bool)value.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject value, bool enabled) => value.SetValue(EnabledProperty, enabled);
    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement element) { return; }
        if (args.NewValue is true) { element.MouseEnter += Enter; element.MouseLeave += Leave; }
        else { element.MouseEnter -= Enter; element.MouseLeave -= Leave; }
    }
    private static void Enter(object sender, System.Windows.Input.MouseEventArgs args) => Animate((FrameworkElement)sender, true);
    private static void Leave(object sender, System.Windows.Input.MouseEventArgs args) => Animate((FrameworkElement)sender, false);
    private static void Animate(FrameworkElement element, bool hovered)
    {
        if (!element.IsEnabled) { return; }
        FrameworkElement? decoration = element is Control control ? control.Template?.FindName("ActionSurface", control) as FrameworkElement
            : (element as Border)?.Child as FrameworkElement;
        if (decoration is null) { return; }
        if (decoration.RenderTransform is not TranslateTransform transform)
        {
            decoration.RenderTransform = transform = new TranslateTransform();
        }
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(hovered && SystemParameters.ClientAreaAnimation ? -2 : 0,
            TimeSpan.FromMilliseconds(160))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        decoration.Effect = hovered ? new DropShadowEffect { BlurRadius = 8, ShadowDepth = 3, Opacity = 0.18 } : null;
    }
}

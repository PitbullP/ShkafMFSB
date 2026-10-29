using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ShkafMFSB;

public enum ToastType { Success, Error, Info }

public partial class ToastHost : UserControl
{
    private const int MaxToasts = 3;
    private const int ToastLifetimeMs = 3000;

    public ToastHost() { InitializeComponent(); }

    public void Show(string message, ToastType type)
    {
        var bg = type switch
        {
            ToastType.Success => Color.FromRgb(22, 101, 52),
            ToastType.Error => Color.FromRgb(127, 29, 29),
            _ => Color.FromRgb(30, 64, 175)
        };

        var border = new Border
        {
            Background = new SolidColorBrush(bg),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16, 10, 16, 10),
            Margin = new Thickness(0, 0, 0, 8),
            MaxWidth = 400,
            Opacity = 0,
            Child = new TextBlock
            {
                Text = message,
                Foreground = Brushes.White,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap
            }
        };

        Stack.Children.Add(border);
        while (Stack.Children.Count > MaxToasts)
            Stack.Children.RemoveAt(0);

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
        border.BeginAnimation(UIElement.OpacityProperty, fadeIn);

        _ = FadeOutAndRemoveAsync(border);
    }

    private async Task FadeOutAndRemoveAsync(Border border)
    {
        await Task.Delay(ToastLifetimeMs);
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400));
        fadeOut.Completed += (_, _) => Stack.Children.Remove(border);
        border.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }
}

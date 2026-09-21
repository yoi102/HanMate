using HanMate.App.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Button = Microsoft.UI.Xaml.Controls.Button;
using Thickness = Microsoft.UI.Xaml.Thickness;

namespace HanMate.App.Platforms.Windows;

/// <summary>A plain text card with a native focusable activation surface, not button chrome.</summary>
public sealed class PinyinExampleHandler() : ViewHandler<PinyinExampleView, Button>(Mapper)
{
    private static readonly IPropertyMapper<PinyinExampleView, PinyinExampleHandler> Mapper =
        new PropertyMapper<PinyinExampleView, PinyinExampleHandler>(ViewMapper)
        {
            [nameof(PinyinExampleView.Content)] = (handler, view) =>
                handler.PlatformView.Content = view.Content?.ToPlatform(handler.MauiContext!)
        };
    private bool _held;
    protected override Button CreatePlatformView() => new()
    {
        Padding = new Thickness(0), BorderThickness = new Thickness(0),
        HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
        Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
        IsTabStop = true, UseSystemFocusVisuals = true
    };
    protected override void ConnectHandler(Button view)
    {
        base.ConnectHandler(view);
        view.Click += Click; view.RightTapped += RightTapped; view.Holding += Holding;
        view.KeyDown += KeyDown; view.PointerPressed += PointerPressed;
    }
    protected override void DisconnectHandler(Button view)
    {
        view.Click -= Click; view.RightTapped -= RightTapped; view.Holding -= Holding;
        view.KeyDown -= KeyDown; view.PointerPressed -= PointerPressed;
        base.DisconnectHandler(view);
    }
    private void Click(object sender, RoutedEventArgs e)
    { if (!_held) VirtualView.ReadExample(); _held = false; }
    private void PointerPressed(object sender, PointerRoutedEventArgs e) => _held = false;
    private void RightTapped(object sender, RightTappedRoutedEventArgs e)
    { e.Handled = true; VirtualView.DefineExample(); }
    private void Holding(object sender, HoldingRoutedEventArgs e)
    {
        if (e.HoldingState != Microsoft.UI.Input.HoldingState.Started) return;
        _held = true; e.Handled = true; VirtualView.DefineExample();
    }
    private void KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == global::Windows.System.VirtualKey.F10) { e.Handled = true; VirtualView.DefineExample(); }
    }
}

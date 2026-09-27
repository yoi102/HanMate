using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Window = Microsoft.Maui.Controls.Window;

namespace HanMate.App.Platforms.Windows;

/// <summary>Routes a right mouse click through the page's normal back guard.</summary>
internal sealed class MouseBackNavigation : IDisposable
{
    private readonly Window _window;
    private UIElement? _surface;
    private bool _navigating;

    public MouseBackNavigation(Window window)
    {
        _window = window;
        window.Created += WindowCreated;
        window.Activated += WindowCreated;
        window.Destroying += WindowDestroying;
    }

    private void WindowCreated(object? sender, EventArgs e)
    {
        if (_surface is not null) return;
        if (_window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow ||
            nativeWindow.Content is not UIElement surface) return;
        _surface = surface;
        surface.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(PointerPressed), true);
        surface.AddHandler(UIElement.RightTappedEvent, new RightTappedEventHandler(RightTapped), true);
    }

    private void PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_surface is null) return;
        var point = e.GetCurrentPoint(_surface);
        if (e.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse ||
            !point.Properties.IsRightButtonPressed || point.Properties.IsLeftButtonPressed) return;
        e.Handled = true;
        if (_navigating) return;
        _navigating = true;
        if (!_surface.DispatcherQueue.TryEnqueue(async () => await GoBackAsync())) _navigating = false;
    }

    private void RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (e.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse) e.Handled = true;
    }

    private async Task GoBackAsync()
    {
        try
        {
            if (_window.Page is not Shell shell) return;
            var modal = shell.Navigation.ModalStack.LastOrDefault();
            var page = modal is NavigationPage navigationPage ? navigationPage.CurrentPage : modal ?? shell.CurrentPage;
            if (page is null || page.SendBackButtonPressed()) return;
            if (modal is not null)
            {
                if (page.Navigation.NavigationStack.Count > 1) await page.Navigation.PopAsync();
                else await shell.Navigation.PopModalAsync();
            }
            else if (page.Navigation.NavigationStack.Count > 1) await page.Navigation.PopAsync();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Mouse back navigation failed: {exception}");
        }
        finally { _navigating = false; }
    }

    private void WindowDestroying(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        _window.Created -= WindowCreated;
        _window.Activated -= WindowCreated;
        _window.Destroying -= WindowDestroying;
        if (_surface is null) return;
        _surface.RemoveHandler(UIElement.PointerPressedEvent, new PointerEventHandler(PointerPressed));
        _surface.RemoveHandler(UIElement.RightTappedEvent, new RightTappedEventHandler(RightTapped));
        _surface = null;
    }
}

using Microsoft.Maui.Controls.Handlers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace HanMate.App.Platforms.Windows;

/// <summary>Keep native tab selection, adding reselect and mouse/touch hold to return home.</summary>
public sealed class RootReturningShellItemHandler : ShellItemHandler
{
    private DispatcherTimer? _holdTimer;
    private Updates.AppUpdateAvailability? _updates;
    private ShellSection? _pressedSection;
    private uint? _pointerId;
    private global::Windows.Foundation.Point _pressPosition;
    private bool _held;

    protected override void ConnectHandler(FrameworkElement platformView)
    {
        base.ConnectHandler(platformView);
        if (platformView is not NavigationView view) return;
        if (VirtualView.Parent is AppShell shell)
        {
            _updates = shell.UpdateAvailability;
            _updates.Changed += UpdateAvailabilityChanged;
        }
        view.Loaded += ViewLoaded;
        ApplyUpdateBadge();
        view.ItemInvoked += ItemInvoked;
        view.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(PointerPressed), true);
        view.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(PointerEnded), true);
        view.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(PointerEnded), true);
        view.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(PointerEnded), true);
        view.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(PointerMoved), true);
        view.Unloaded += Unloaded;
        view.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(KeyDown), true);
        _holdTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _holdTimer.Tick += HoldElapsed;
    }

    protected override void DisconnectHandler(FrameworkElement platformView)
    {
        if (platformView is NavigationView view)
        {
            view.ItemInvoked -= ItemInvoked;
            view.Loaded -= ViewLoaded;
            view.RemoveHandler(UIElement.PointerPressedEvent, new PointerEventHandler(PointerPressed));
            view.RemoveHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(PointerEnded));
            view.RemoveHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(PointerEnded));
            view.RemoveHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(PointerEnded));
            view.RemoveHandler(UIElement.PointerMovedEvent, new PointerEventHandler(PointerMoved));
            view.Unloaded -= Unloaded;
            view.RemoveHandler(UIElement.KeyDownEvent, new KeyEventHandler(KeyDown));
        }
        CancelHold();
        if (_holdTimer is not null) _holdTimer.Tick -= HoldElapsed;
        _holdTimer = null;
        if (_updates is not null) _updates.Changed -= UpdateAvailabilityChanged;
        _updates = null;
        base.DisconnectHandler(platformView);
    }

    private void ViewLoaded(object sender, RoutedEventArgs e) => ApplyUpdateBadge();
    private void UpdateAvailabilityChanged(object? sender, EventArgs e) =>
        PlatformView.DispatcherQueue.TryEnqueue(ApplyUpdateBadge);
    private void ApplyUpdateBadge()
    {
        if (_updates is null || PlatformView is not NavigationView view ||
            view.MenuItemsSource is not System.Collections.IEnumerable models) return;
        var settingsModel = models.Cast<object>().LastOrDefault();
        if (settingsModel is null || view.ContainerFromMenuItem(settingsModel) is not NavigationViewItem settingsItem) return;
        if (_updates.HasUpdate)
            settingsItem.InfoBadge ??= new InfoBadge
            { Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red) };
        else settingsItem.InfoBadge = null;
    }

    private ShellSection? SectionFor(NavigationViewItemBase? container)
    {
        if (container is null || PlatformView is not NavigationView view ||
            view.MenuItemsSource is not System.Collections.IEnumerable menuItems) return null;
        var invoked = view.MenuItemFromContainer(container);
        var sections = ((IShellItemController)VirtualView).GetItems();
        // MAUI builds this top-level menu in visible ShellSection order. Use public
        // container APIs instead of depending on its internal view-model type.
        var index = 0;
        foreach (var model in menuItems)
        {
            if (ReferenceEquals(model, invoked)) return index < sections.Count ? sections[index] : null;
            index++;
        }
        return null;
    }

    private ShellSection? SectionAt(object source)
    {
        for (var node = source as DependencyObject; node is not null && node != PlatformView;
             node = VisualTreeHelper.GetParent(node))
            if (node is NavigationViewItemBase item) return SectionFor(item);
        return null;
    }

    private void ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (_held) { _held = false; return; }
        // ItemInvoked precedes SelectionChanged. Compare with Shell's existing
        // section so an ordinary switch keeps that tab's previous page.
        if (SectionFor(args.InvokedItemContainer) is { } section &&
            VirtualView.CurrentItem == section && VirtualView.Parent is AppShell shell)
            QueueReturn(shell, section);
    }

    private void PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        CancelHold();
        _held = false;
        var point = e.GetCurrentPoint(PlatformView);
        if (!point.Properties.IsLeftButtonPressed || SectionAt(e.OriginalSource) is not { } section) return;
        _pressedSection = section;
        _pointerId = e.Pointer.PointerId;
        _pressPosition = point.Position;
        _holdTimer?.Start();
    }

    private void PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_pointerId != e.Pointer.PointerId) return;
        var point = e.GetCurrentPoint(PlatformView);
        if (!point.IsInContact || Math.Abs(point.Position.X - _pressPosition.X) > 10 ||
            Math.Abs(point.Position.Y - _pressPosition.Y) > 10) CancelHold();
    }

    private void PointerEnded(object sender, PointerRoutedEventArgs e)
    { if (_pointerId == e.Pointer.PointerId) CancelHold(); }

    private void HoldElapsed(object? sender, object e)
    {
        var section = _pressedSection;
        CancelHold();
        if (section is null || !PlatformView.IsLoaded || VirtualView.Parent is not AppShell shell) return;
        _held = true;
        QueueReturn(shell, section);
    }

    private void QueueReturn(AppShell shell, ShellSection section)
    {
        // Finish the native input callback before Shell changes its view hierarchy.
        PlatformView.DispatcherQueue.TryEnqueue(() =>
        {
            if (PlatformView?.IsLoaded == true && section.Parent == VirtualView)
                _ = shell.ReturnToTabRootAsync(section);
        });
    }

    private void Unloaded(object sender, RoutedEventArgs e) { CancelHold(); _held = false; }
    private void KeyDown(object sender, KeyRoutedEventArgs e) { CancelHold(); _held = false; }
    private void CancelHold()
    {
        _holdTimer?.Stop();
        _pressedSection = null;
        _pointerId = null;
    }
}

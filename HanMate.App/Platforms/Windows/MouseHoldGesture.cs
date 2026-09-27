using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace HanMate.App.Platforms.Windows;

/// <summary>Recognizes a stationary left-mouse hold; WinUI Holding only covers touch and pen reliably.</summary>
internal sealed class MouseHoldGesture : IDisposable
{
    private readonly UIElement _view;
    private readonly Action _pressed;
    private readonly Action _held;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(550) };
    private uint? _pointerId;
    private global::Windows.Foundation.Point _start;

    public MouseHoldGesture(UIElement view, Action pressed, Action held)
    {
        _view = view;
        _pressed = pressed;
        _held = held;
        _timer.Tick += HoldElapsed;
        view.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(PointerPressed), true);
        view.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(PointerMoved), true);
        view.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(PointerEnded), true);
        view.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(PointerEnded), true);
        view.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(PointerEnded), true);
        view.PointerExited += PointerExited;
    }

    private void PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Cancel();
        var point = e.GetCurrentPoint(_view);
        if (e.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse ||
            !point.Properties.IsLeftButtonPressed || point.Properties.IsRightButtonPressed) return;
        _pressed();
        _pointerId = e.Pointer.PointerId;
        _start = point.Position;
        _timer.Start();
    }

    private void PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_pointerId != e.Pointer.PointerId) return;
        var point = e.GetCurrentPoint(_view);
        if (!point.Properties.IsLeftButtonPressed || Math.Abs(point.Position.X - _start.X) > 10 ||
            Math.Abs(point.Position.Y - _start.Y) > 10) Cancel();
    }

    private void PointerEnded(object sender, PointerRoutedEventArgs e)
    { if (_pointerId == e.Pointer.PointerId) Cancel(); }

    private void PointerExited(object sender, PointerRoutedEventArgs e)
    { if (_pointerId == e.Pointer.PointerId) Cancel(); }

    private void HoldElapsed(object? sender, object e)
    {
        if (_pointerId is null) return;
        Cancel();
        _held();
    }

    private void Cancel() { _timer.Stop(); _pointerId = null; }

    public void Dispose()
    {
        Cancel();
        _timer.Tick -= HoldElapsed;
        _view.RemoveHandler(UIElement.PointerPressedEvent, new PointerEventHandler(PointerPressed));
        _view.RemoveHandler(UIElement.PointerMovedEvent, new PointerEventHandler(PointerMoved));
        _view.RemoveHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(PointerEnded));
        _view.RemoveHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(PointerEnded));
        _view.RemoveHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(PointerEnded));
        _view.PointerExited -= PointerExited;
    }
}

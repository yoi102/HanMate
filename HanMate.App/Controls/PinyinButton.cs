namespace HanMate.App.Controls;

/// <summary>Accessible click remains available; long press cancels after a drag and suppresses the trailing click.</summary>
public sealed class PinyinButton : Button
{
    public event EventHandler? ShowExamples;
    private bool _suppressClick;
    public bool ConsumeClick() { var result = !_suppressClick; _suppressClick = false; return result; }

    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
#if ANDROID
        CancelHold();
        if (args.OldHandler?.PlatformView is Android.Views.View oldView) oldView.Touch -= OnTouch;
#elif WINDOWS
        if (args.OldHandler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement oldView)
        { oldView.RightTapped -= OnRightTapped; oldView.Holding -= OnHolding; oldView.KeyDown -= OnKeyDown; oldView.PointerPressed -= OnPointerPressed; }
#elif IOS || MACCATALYST
        if (args.OldHandler?.PlatformView is UIKit.UIControl oldControl) oldControl.TouchDown -= OnTouchDown;
        if (_recognizer is not null && args.OldHandler?.PlatformView is UIKit.UIView oldView) { oldView.RemoveGestureRecognizer(_recognizer); _recognizer.Dispose(); _recognizer = null; }
#endif
        base.OnHandlerChanging(args);
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
#if ANDROID
        if (Handler?.PlatformView is Android.Views.View view) view.Touch += OnTouch;
#elif WINDOWS
        if (Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement view)
        { view.RightTapped += OnRightTapped; view.Holding += OnHolding; view.KeyDown += OnKeyDown; view.PointerPressed += OnPointerPressed; }
#elif IOS || MACCATALYST
        if (Handler?.PlatformView is UIKit.UIView view)
        {
            if (view is UIKit.UIControl control) control.TouchDown += OnTouchDown;
            _recognizer = new UIKit.UILongPressGestureRecognizer(r => { if (r.State == UIKit.UIGestureRecognizerState.Began) { _suppressClick = true; ShowExamples?.Invoke(this, EventArgs.Empty); } }) { MinimumPressDuration = .5, AllowableMovement = 12, CancelsTouchesInView = true };
            view.AddGestureRecognizer(_recognizer);
        }
#endif
    }

#if ANDROID
    private CancellationTokenSource? _hold;
    private float _x, _y;
    private void CancelHold() { _hold?.Cancel(); _hold?.Dispose(); _hold = null; }
    private void OnTouch(object? sender, Android.Views.View.TouchEventArgs e)
    {
        if (e.Event is not { } motion) return;
        e.Handled = false;
        switch (motion.ActionMasked)
        {
            case Android.Views.MotionEventActions.Down:
                CancelHold(); _suppressClick = false; _x = motion.GetX(); _y = motion.GetY();
                _hold = new(); _ = HoldAsync(_hold.Token); break;
            case Android.Views.MotionEventActions.Move:
                var slop = 14 * (Android.App.Application.Context.Resources?.DisplayMetrics?.Density ?? 1);
                if (Math.Abs(motion.GetX() - _x) > slop || Math.Abs(motion.GetY() - _y) > slop) CancelHold();
                break;
            case Android.Views.MotionEventActions.Up:
                e.Handled = _suppressClick; CancelHold(); break;
            case Android.Views.MotionEventActions.Cancel:
                CancelHold(); break;
        }
    }
    private async Task HoldAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(500, token);
            if (token.IsCancellationRequested) return;
            _suppressClick = true;
            ShowExamples?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) { }
    }
#elif WINDOWS
    private void OnRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e) { e.Handled = true; ShowExamples?.Invoke(this, EventArgs.Empty); }
    private void OnPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => _suppressClick = false;
    private void OnHolding(object sender, Microsoft.UI.Xaml.Input.HoldingRoutedEventArgs e)
    { if (e.HoldingState == Microsoft.UI.Input.HoldingState.Started) { e.Handled = true; _suppressClick = true; ShowExamples?.Invoke(this, EventArgs.Empty); } }
    private void OnKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    { if (e.Key == Windows.System.VirtualKey.F10) { e.Handled = true; ShowExamples?.Invoke(this, EventArgs.Empty); } }
#elif IOS || MACCATALYST
    private UIKit.UILongPressGestureRecognizer? _recognizer;
    private void OnTouchDown(object? sender, EventArgs e) => _suppressClick = false;
#endif
}

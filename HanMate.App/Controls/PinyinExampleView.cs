namespace HanMate.App.Controls;

/// <summary>A text card: tap reads, long press opens the dictionary. Scrolling cancels a hold.</summary>
public sealed class PinyinExampleView : ContentView
{
    public event EventHandler? Read;
    public event EventHandler? Define;
    internal void ReadExample() => Read?.Invoke(this, EventArgs.Empty);
    internal void DefineExample() => Define?.Invoke(this, EventArgs.Empty);
    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
#if ANDROID
        if (args.OldHandler?.PlatformView is Android.Views.View old) { old.Click -= OnClick; old.LongClick -= OnLongClick; }
#elif IOS || MACCATALYST
        if (args.OldHandler?.PlatformView is UIKit.UIView old)
        { if (_tap is not null) old.RemoveGestureRecognizer(_tap); if (_hold is not null) old.RemoveGestureRecognizer(_hold); }
        _tap?.Dispose(); _hold?.Dispose(); _tap = null; _hold = null;
#endif
        base.OnHandlerChanging(args);
    }
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
#if ANDROID
        if (Handler?.PlatformView is Android.Views.View view)
        { view.Clickable = view.LongClickable = view.Focusable = true; view.Click += OnClick; view.LongClick += OnLongClick; }
#elif IOS || MACCATALYST
        if (Handler?.PlatformView is UIKit.UIView view)
        {
            _hold = new UIKit.UILongPressGestureRecognizer(r => { if (r.State == UIKit.UIGestureRecognizerState.Began) Define?.Invoke(this, EventArgs.Empty); }) { MinimumPressDuration = .5 };
            _tap = new UIKit.UITapGestureRecognizer(() => Read?.Invoke(this, EventArgs.Empty)); _tap.RequireGestureRecognizerToFail(_hold);
            view.AddGestureRecognizer(_tap); view.AddGestureRecognizer(_hold);
        }
#endif
    }
#if ANDROID
    private void OnClick(object? sender, EventArgs e) => Read?.Invoke(this, EventArgs.Empty);
    private void OnLongClick(object? sender, Android.Views.View.LongClickEventArgs e) { e.Handled = true; Define?.Invoke(this, EventArgs.Empty); }
#elif IOS || MACCATALYST
    private UIKit.UITapGestureRecognizer? _tap;
    private UIKit.UILongPressGestureRecognizer? _hold;
#endif
}

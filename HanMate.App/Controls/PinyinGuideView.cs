using HanMate.App.Localization;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Platform;

namespace HanMate.App.Controls;

/// <summary>A window-wide spotlight over the actual control; the hole passes gestures through.</summary>
public sealed class PinyinGuideView : AbsoluteLayout, IDisposable
{
    private readonly VisualElement _target;
    private readonly LocalizationService _language;
    private readonly string _sample;
    private readonly BoxView[] _shade = new BoxView[4];
    private readonly Label _progress = new() { FontSize = 14, TextColor = Color.FromArgb("#DCD8E5") };
    private readonly Label _title = new() { FontSize = 24, FontAttributes = FontAttributes.Bold, TextColor = Colors.White };
    private readonly Label _description = new() { FontSize = 18, TextColor = Colors.White };
    private readonly Label _desktop = new() { FontSize = 14, TextColor = Color.FromArgb("#DCD8E5") };
    private readonly Button _skip = new() { BackgroundColor = Colors.Transparent, TextColor = Colors.White,
        BorderColor = Color.FromArgb("#BFFFFFFF"), BorderWidth = 1, CornerRadius = 22, MinimumHeightRequest = 44,
        Padding = new Thickness(18, 6), HorizontalOptions = LayoutOptions.Start, AutomationId = "Pinyin.Guide.Skip" };
    private readonly VerticalStackLayout _copy;
    private readonly ScrollView _copyScroll;
    private readonly Border _hint;
    private readonly Border _ring = new() { BackgroundColor = Colors.Transparent, Stroke = Colors.White, StrokeThickness = 2,
        StrokeShape = new RoundRectangle { CornerRadius = 10 }, InputTransparent = true };
    private readonly ArrowDrawing _arrow = new();
    private readonly GraphicsView _arrows;
    private readonly IDispatcherTimer _timer;
    private Action? _detach;
    private Func<Rect>? _bounds;
    private Rect _lastTarget;
    private Size _lastSize;
    private int _step;
    private bool _disposed;
    internal Rect Hole { get; private set; }

    public PinyinGuideView(VisualElement target, LocalizationService language, string sample, int step, Action skip)
    {
        _target = target; _language = language; _sample = sample; _step = step;
        AutomationId = "Pinyin.Guide";
        InputTransparent = true; CascadeInputTransparent = false;
        for (var i = 0; i < _shade.Length; i++)
        {
            var shade = new BoxView { Color = Color.FromArgb("#66000000"), BackgroundColor = Colors.Transparent, InputTransparent = false };
            shade.GestureRecognizers.Add(new TapGestureRecognizer());
            _shade[i] = shade; Children.Add(shade);
        }
        Children.Add(_ring);
        _arrows = new GraphicsView { Drawable = _arrow, InputTransparent = true };
        Children.Add(_arrows);
        _skip.Clicked += (_, _) => skip();
        _copy = new VerticalStackLayout { Padding = 18, Spacing = 12, Children = { _progress, _title, _description, _desktop, _skip } };
        _copyScroll = new ScrollView { Content = _copy, InputTransparent = false };
        _hint = new Border { Content = _copyScroll, BackgroundColor = Color.FromArgb("#F0252630"), StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 16 }, InputTransparent = false };
        Children.Add(_hint);
        SemanticProperties.SetHeadingLevel(_title, SemanticHeadingLevel.Level1);
        _timer = Dispatcher.CreateTimer(); _timer.Interval = TimeSpan.FromMilliseconds(100);
        _timer.Tick += OnTick; SizeChanged += OnSizeChanged;
        RefreshCopy();
    }

    public void SetStep(int step) { _step = step; RefreshCopy(); }
    public void RefreshCopy()
    {
        string T(string key) => _language["Pinyin.Guide." + key];
        var prefix = _step switch { 1 => "Tap", 2 => "Hold", 3 => "ExampleTap", _ => "ExampleHold" };
        _progress.Text = string.Format(T("Step"), _step);
        _title.Text = T(prefix + "Title");
        _description.Text = string.Format(T(prefix + "Body"), _sample);
        _desktop.Text = T("Desktop");
        _desktop.IsVisible = (_step == 2 || _step == 4) && DeviceInfo.Platform == DevicePlatform.WinUI;
        _skip.Text = T("Skip");
        Dispatcher.Dispatch(() => UpdatePosition(true));
    }

    public void Show(ContentPage page, Grid fallback)
    {
        var context = page.Handler?.MauiContext ?? throw new InvalidOperationException("Page is not ready.");
#if ANDROID
        if (Platform.CurrentActivity?.Window?.DecorView is Android.Views.ViewGroup decor &&
            _target.Handler?.PlatformView is Android.Views.View target)
        {
            var density = decor.Resources?.DisplayMetrics?.Density ?? 1;
            var host = new SpotlightHost(decor.Context!, this, density);
            host.AddView(this.ToPlatform(context), new Android.Widget.FrameLayout.LayoutParams(-1, -1));
            decor.AddView(host, new Android.Views.ViewGroup.LayoutParams(-1, -1));
            _bounds = () =>
            {
                int[] origin = new int[2], position = new int[2];
                host.GetLocationOnScreen(origin); target.GetLocationOnScreen(position);
                return new Rect((position[0] - origin[0]) / density, (position[1] - origin[1]) / density,
                    target.Width / density, target.Height / density);
            };
            var excluded = new List<(Android.Views.View View, Android.Views.ImportantForAccessibility Value)>();
            Android.Views.View current = target;
            while (current.Parent is Android.Views.ViewGroup parent)
            {
                for (var i = 0; i < parent.ChildCount; i++)
                {
                    var sibling = parent.GetChildAt(i)!;
                    if (sibling == current || sibling == host) continue;
                    excluded.Add((sibling, sibling.ImportantForAccessibility));
                    sibling.ImportantForAccessibility = Android.Views.ImportantForAccessibility.NoHideDescendants;
                }
                if (parent == decor) break;
                current = parent;
            }
            _detach = () =>
            {
                foreach (var entry in excluded) entry.View.ImportantForAccessibility = entry.Value;
                decor.RemoveView(host); host.RemoveAllViews(); host.Dispose();
            };
        }
        else
#elif WINDOWS
        if (page.Window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window window &&
            window.Content is Microsoft.UI.Xaml.FrameworkElement original &&
            _target.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement target)
        {
            var panel = original as Microsoft.UI.Xaml.Controls.Panel;
            var wrapped = panel is null;
            if (panel is null)
            {
                window.Content = null;
                panel = new Microsoft.UI.Xaml.Controls.Grid();
                panel.Children.Add(original); window.Content = panel;
            }
            var platform = this.ToPlatform(context);
            Microsoft.UI.Xaml.Controls.Canvas.SetZIndex(platform, int.MaxValue);
            if (panel is Microsoft.UI.Xaml.Controls.Grid grid)
            {
                Microsoft.UI.Xaml.Controls.Grid.SetRowSpan(platform, Math.Max(1, grid.RowDefinitions.Count));
                Microsoft.UI.Xaml.Controls.Grid.SetColumnSpan(platform, Math.Max(1, grid.ColumnDefinitions.Count));
            }
            panel.Children.Add(platform);
            _bounds = () =>
            {
                var point = target.TransformToVisual(platform).TransformPoint(new global::Windows.Foundation.Point());
                return new Rect(point.X, point.Y, target.ActualWidth, target.ActualHeight);
            };
            void ArrangeOverlay(object? sender, Microsoft.UI.Xaml.SizeChangedEventArgs args)
            {
                ((IView)this).Measure(panel.ActualWidth, panel.ActualHeight);
                ((IView)this).Arrange(new Rect(0, 0, panel.ActualWidth, panel.ActualHeight));
            }
            panel.SizeChanged += ArrangeOverlay;
            Dispatcher.Dispatch(() => ArrangeOverlay(null, null!));
            _detach = () =>
            {
                panel.SizeChanged -= ArrangeOverlay; panel.Children.Remove(platform);
                if (wrapped) { window.Content = null; panel.Children.Remove(original); window.Content = original; }
            };
        }
        else
#endif
        {
            fallback.Add(this);
            _bounds = () => RelativeBounds(_target, fallback);
            _detach = () => fallback.Remove(this);
        }
        _timer.Start(); UpdatePosition(true);
    }

    private static void Place(BindableObject view, Rect bounds) => AbsoluteLayout.SetLayoutBounds(view, bounds);
    private static Rect RelativeBounds(VisualElement target, VisualElement root)
    {
        var x = 0d; var y = 0d;
        for (Element? current = target; current is VisualElement view && current != root; current = current.Parent)
        {
            x += view.X + view.TranslationX; y += view.Y + view.TranslationY;
            if (current.Parent is ScrollView scroll) { x -= scroll.ScrollX; y -= scroll.ScrollY; }
        }
        return new Rect(x, y, target.Width, target.Height);
    }
    private void OnTick(object? sender, EventArgs e) => UpdatePosition(false);
    private void OnSizeChanged(object? sender, EventArgs e) => UpdatePosition(true);
    private void UpdatePosition(bool force)
    {
        if (_disposed || _bounds is null || Width <= 0 || Height <= 0) return;
        var target = _bounds();
        if (!force && target == _lastTarget && _lastSize == new Size(Width, Height)) return;
        _lastTarget = target; _lastSize = new(Width, Height);
        if (target.Width <= 0 || target.Height <= 0)
        {
            Hole = Rect.Zero; Place(_shade[0], new Rect(0, 0, Width, Height)); return;
        }
        var left = Math.Clamp(target.Left - 5, 0, Width); var top = Math.Clamp(target.Top - 5, 0, Height);
        var right = Math.Clamp(target.Right + 5, left, Width); var bottom = Math.Clamp(target.Bottom + 5, top, Height);
        Hole = new Rect(left, top, right - left, bottom - top);
        Place(_shade[0], new Rect(0, 0, Width, top));
        Place(_shade[1], new Rect(0, bottom, Width, Height - bottom));
        Place(_shade[2], new Rect(0, top, left, bottom - top));
        Place(_shade[3], new Rect(right, top, Width - right, bottom - top));
        Place(_ring, Hole);
        var width = Math.Min(360, Math.Max(1, Width - 48));
        var wanted = ((IView)_copy).Measure(width, double.PositiveInfinity).Height;
        var below = Height - bottom >= top;
        var available = Math.Max(1, (below ? Height - bottom : top) - 88);
        var height = Math.Min(wanted, available);
        var x = Math.Clamp(left, 24, Math.Max(24, Width - width - 24));
        var y = below ? bottom + 64 : top - 64 - height;
        Place(_hint, new Rect(x, y, width, height));
        Place(_arrows, new Rect(0, 0, Width, Height));
        _arrow.Tip = new Point(Hole.Center.X, below ? bottom + 6 : top - 6);
        _arrow.Start = new Point(x + 28, below ? y - 12 : y + height + 12);
        _arrow.Below = below; _arrows.Invalidate();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _timer.Stop(); _timer.Tick -= OnTick; SizeChanged -= OnSizeChanged;
        _detach?.Invoke(); _detach = null; _bounds = null; Handler?.DisconnectHandler();
    }

    private sealed class ArrowDrawing : IDrawable
    {
        public Point Tip { get; set; }
        public Point Start { get; set; }
        public bool Below { get; set; }
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            if (Tip == Point.Zero) return;
            canvas.StrokeColor = Colors.White; canvas.StrokeSize = 2.5f; canvas.StrokeLineCap = LineCap.Round;
            var sign = Below ? 1 : -1;
            var path = new PathF(); path.MoveTo((float)Start.X, (float)Start.Y);
            path.CurveTo((float)Start.X, (float)(Start.Y - 22 * sign), (float)Tip.X, (float)(Tip.Y + 28 * sign), (float)Tip.X, (float)Tip.Y);
            canvas.DrawPath(path);
            canvas.DrawLine((float)Tip.X, (float)Tip.Y, (float)Tip.X - 7, (float)(Tip.Y + 10 * sign));
            canvas.DrawLine((float)Tip.X, (float)Tip.Y, (float)Tip.X + 7, (float)(Tip.Y + 10 * sign));
        }
    }
}

#if ANDROID
internal sealed class SpotlightHost(Android.Content.Context context, PinyinGuideView guide, float density) : Android.Widget.FrameLayout(context)
{
    private bool _passThrough;
    protected override void OnLayout(bool changed, int left, int top, int right, int bottom)
    {
        base.OnLayout(changed, left, top, right, bottom);
        // This layout is attached to DecorView rather than a MAUI parent. Supply
        // its managed frame explicitly so the spotlight can measure and position.
        ((IView)guide).Measure((right - left) / density, (bottom - top) / density);
        ((IView)guide).Arrange(new Rect(0, 0, (right - left) / density, (bottom - top) / density));
    }
    public override bool DispatchTouchEvent(Android.Views.MotionEvent? e)
    {
        // Let DecorView route a gesture in the cutout to the existing control below.
        if (e?.ActionMasked == Android.Views.MotionEventActions.Down)
            _passThrough = guide.Hole.Contains(e.GetX() / density, e.GetY() / density);
        if (_passThrough) return false;
        base.DispatchTouchEvent(e); return true;
    }
}
#endif

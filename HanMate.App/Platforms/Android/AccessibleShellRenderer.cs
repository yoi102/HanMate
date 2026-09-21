using Android.Content;
using Android.Views;
using Android.Widget;
using Google.Android.Material.BottomNavigation;
using Microsoft.Maui.Controls.Handlers.Compatibility;
using Microsoft.Maui.Controls.Platform.Compatibility;

namespace HanMate.App.Platforms.Android;

/// <summary>Keep the five native tabs readable at the user's system font size.</summary>
public sealed class AccessibleShellRenderer(Context context) : ShellRenderer(context)
{
    protected override IShellItemRenderer CreateShellItemRenderer(ShellItem shellItem)
        => new RootReturningItemRenderer(this);

    private sealed class RootReturningItemRenderer(IShellContext context) : ShellItemRenderer(context)
    {
        protected override void OnTabReselected(ShellSection shellSection)
        {
            if (ShellContext.Shell is AppShell shell) _ = shell.ReturnToTabRootAsync(shellSection);
        }
    }

    protected override IShellBottomNavViewAppearanceTracker CreateBottomNavViewAppearanceTracker(ShellItem shellItem)
        => new WrappingTabs(this, shellItem);

    private sealed class WrappingTabs : ShellBottomNavViewAppearanceTracker
    {
        private readonly IShellContext _context;
        private readonly ShellItem _item;
        public WrappingTabs(IShellContext context, ShellItem item) : base(context, item)
        { _context = context; _item = item; }

        private BottomNavigationView? _view;
        private ViewTreeObserver? _observer;
        private readonly HashSet<global::Android.Views.View> _tabs = [];
        public override void SetAppearance(BottomNavigationView bottomView, IShellAppearanceElement appearance)
        { base.SetAppearance(bottomView, appearance); Attach(bottomView); }
        public override void ResetAppearance(BottomNavigationView bottomView)
        { base.ResetAppearance(bottomView); Attach(bottomView); }

        private void Attach(BottomNavigationView view)
        {
            if (_view != view)
            {
                if (_observer?.IsAlive == true) _observer.GlobalLayout -= GlobalLayout;
                DetachTabs();
                _view = view; _observer = view.ViewTreeObserver;
                if (_observer is not null) _observer.GlobalLayout += GlobalLayout;
            }
            WrapLabels();
        }
        private void GlobalLayout(object? sender, EventArgs e) => WrapLabels();
        private void WrapLabels()
        {
            if (_view is not { Width: > 0 } view) return;
            var density = view.Resources?.DisplayMetrics?.Density ?? 1;
            var width = Math.Max(1, view.Width / Math.Max(1, view.Menu.Size()) - 16 * density);
            var height = 56 * density;
            foreach (var label in Labels(view))
            {
                // Material scales a second label around a single-line baseline when
                // selected. That transform clips wrapped text. Keep one unscaled
                // label; the native checked state still supplies its selected color.
                if (view.Resources?.GetResourceEntryName(label.Id) == "navigation_bar_item_large_label_view")
                { if (label.Visibility != ViewStates.Gone) label.Visibility = ViewStates.Gone; continue; }
                if (label.Visibility != ViewStates.Visible) label.Visibility = ViewStates.Visible;
                if (label.ScaleX != 1) label.ScaleX = 1;
                if (label.ScaleY != 1) label.ScaleY = 1;
                if (label.MaxLines != 3)
                {
                    label.SetSingleLine(false); label.SetMaxLines(3); label.Ellipsize = null;
                    // Material's single-line text appearance may carry a line height
                    // that no longer fits the scaled font's ascenders/descenders.
                    label.SetIncludeFontPadding(true); label.SetLineSpacing(0, 1);
                }
                label.Gravity = GravityFlags.Center;
                if (label.LayoutParameters is { } labelLayout && labelLayout.Width != ViewGroup.LayoutParams.MatchParent)
                { labelLayout.Width = ViewGroup.LayoutParams.MatchParent; label.LayoutParameters = labelLayout; }
                var lines = Math.Clamp((int)Math.Ceiling((label.Paint?.MeasureText(label.Text ?? "") ?? 0) / width), 1, 3);
                var labelHeight = (int)Math.Ceiling(Math.Max(label.LineHeight, label.TextSize * 1.5) * lines);
                if (label.LayoutParameters is { } textLayout && textLayout.Height != labelHeight)
                { textLayout.Height = labelHeight; label.LayoutParameters = textLayout; }
                // Material's baseline container was designed for one line. Give it
                // the complete wrapped label height, including its own padding.
                if (label.Parent is ViewGroup labels && labels.LayoutParameters is FrameLayout.LayoutParams labelsLayout)
                {
                    var groupHeight = labelHeight + labels.PaddingTop + labels.PaddingBottom;
                    if (labelsLayout.Height != groupHeight || labelsLayout.Width != ViewGroup.LayoutParams.MatchParent || labelsLayout.Gravity != GravityFlags.Center)
                    {
                        labelsLayout.Height = groupHeight; labelsLayout.Gravity = GravityFlags.Center;
                        labelsLayout.Width = ViewGroup.LayoutParams.MatchParent;
                        labels.LayoutParameters = labelsLayout;
                    }
                    height = Math.Max(height, groupHeight + 16 * density);
                }
            }
            var contentPixels = (int)Math.Ceiling(height);
            // The system gesture inset is padding on BottomNavigationView, not
            // part of the tab's touch target or the wrapped label's available height.
            var pixels = contentPixels + view.PaddingTop + view.PaddingBottom;
            if (view.MinimumHeight != pixels) view.SetMinimumHeight(pixels);
            if (view.GetChildAt(0) is ViewGroup menu)
            {
                var tabs = Enumerable.Range(0, menu.ChildCount).Select(menu.GetChildAt)
                    .OfType<global::Android.Views.View>().ToHashSet();
                foreach (var old in _tabs.Except(tabs).ToArray())
                { old.LongClick -= TabLongClick; _tabs.Remove(old); }
                foreach (var tab in tabs)
                    if (_tabs.Add(tab)) tab.LongClick += TabLongClick;
                if (menu.MinimumHeight != contentPixels) menu.SetMinimumHeight(contentPixels);
                for (var i = 0; i < menu.ChildCount; i++)
                    if (menu.GetChildAt(i) is { } tab && tab.MinimumHeight != contentPixels) tab.SetMinimumHeight(contentPixels);
            }
            if (view.LayoutParameters is { } layout && layout.Height != pixels)
            { layout.Height = pixels; view.LayoutParameters = layout; }
        }
        private void TabLongClick(object? sender, global::Android.Views.View.LongClickEventArgs e)
        {
            if (sender is not global::Android.Views.View tab || _context.Shell is not AppShell shell) return;
            var sections = ((IShellItemController)_item).GetItems();
            // MAUI assigns menu IDs from the visible ShellSection list, not the label text.
            if (tab.Id < 0 || tab.Id >= sections.Count) return;
            e.Handled = true;
            _ = shell.ReturnToTabRootAsync(sections[tab.Id]);
        }
        private void DetachTabs()
        {
            foreach (var tab in _tabs) tab.LongClick -= TabLongClick;
            _tabs.Clear();
        }
        private static IEnumerable<TextView> Labels(ViewGroup group)
        {
            for (var i = 0; i < group.ChildCount; i++)
            {
                var child = group.GetChildAt(i);
                if (child is TextView label) yield return label;
                else if (child is ViewGroup nested) foreach (var descendant in Labels(nested)) yield return descendant;
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_observer?.IsAlive == true) _observer.GlobalLayout -= GlobalLayout;
                _observer = null; _view = null;
                DetachTabs();
            }
            base.Dispose(disposing);
        }
    }
}

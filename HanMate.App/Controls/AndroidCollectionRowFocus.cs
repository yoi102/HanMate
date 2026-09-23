#if ANDROID
namespace HanMate.App.Controls;

internal static class AndroidCollectionRowFocus
{
    public static void RemoveUnnamedItemFocus(VisualElement item)
    {
        if (item.Handler?.PlatformView is not Android.Views.View nativeView) return;

        // MAUI's CollectionView makes the native row itself focusable. The
        // labelled buttons inside it remain reachable when only that row is
        // removed from the keyboard and accessibility focus sequence.
        var row = nativeView;
        while (row.Parent is Android.Views.View parent && parent is not AndroidX.RecyclerView.Widget.RecyclerView)
            row = parent;
        if (row.Parent is not AndroidX.RecyclerView.Widget.RecyclerView) return;

        row.Focusable = false;
        row.FocusableInTouchMode = false;
        row.Clickable = false;
        row.ImportantForAccessibility = Android.Views.ImportantForAccessibility.No;
    }
}
#endif

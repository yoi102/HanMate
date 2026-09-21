using HanMate.App.Localization;
using HanMate.Infrastructure.Packages;

namespace HanMate.App.Pages;

/// <summary>Bounded review of every entry. Leaving without Continue cancels the pending update.</summary>
public sealed class ResourceUpdateReviewPage : ContentPage
{
    private readonly TaskCompletionSource<bool> _decision = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ResourceUpdatePreview _preview;
    private readonly LocalizationService _language;
    private readonly VerticalStackLayout _rows = new() { Spacing = 12 };
    private readonly Button _previous = new(), _next = new(), _apply = new();
    private int _offset;
    private bool _continuing;
    public Task<bool> Decision => _decision.Task;

    public ResourceUpdateReviewPage(ResourceUpdatePreview preview, LocalizationService language)
    {
        _preview = preview; _language = language; Title = T("UpdateReview");
        _previous.Text = T("Previous"); _next.Text = T("Next"); _apply.Text = T("UpdateContinue");
        _previous.Clicked += (_, _) => { _offset = Math.Max(0, _offset - 20); Render(); };
        _next.Clicked += (_, _) => { _offset += 20; Render(); };
        _apply.AutomationId = "Resource.UpdateContinue";
        _apply.Clicked += async (_, _) =>
        {
            if (_continuing) return; _continuing = true; _apply.IsEnabled = false;
            try { await Navigation.PopAsync(); _decision.TrySetResult(true); }
            catch { _continuing = false; _decision.TrySetResult(false); }
        };
        var grid = new Grid { Padding = 20, RowSpacing = 12, RowDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) } };
        grid.Add(new Label { Text = T("UpdatePreservation") + (preview.Reinstall ? "\n" + T("ReinstallDisabled") : "") });
        grid.Add(new ScrollView { Content = _rows }, 0, 1);
        grid.Add(new VerticalStackLayout { Spacing = 8, Children = { new HorizontalStackLayout { Spacing = 8, Children = { _previous, _next } }, _apply } }, 0, 2);
        Content = grid; Render();
    }
    protected override void OnDisappearing()
    { base.OnDisappearing(); if (!_continuing) _decision.TrySetResult(false); }
    private string T(string key) => _language["Install." + key];
    private void Render()
    {
        _rows.Clear();
        foreach (var entry in _preview.Entries.Skip(_offset).Take(20))
            _rows.Add(new Label { Text = T("Change." + entry.Change) + " · " + entry.Title + (entry.Retain ? "\n" + T("RetainOld") : "") +
                (entry.AudioDrafts + entry.TeachingExamples > 0 ? "\n" + string.Format(T("UpdateReferences"), entry.AudioDrafts, entry.TeachingExamples) : ""), FontSize = 18, LineBreakMode = LineBreakMode.WordWrap });
        _previous.IsEnabled = _offset > 0; _next.IsEnabled = _offset + 20 < _preview.Entries.Count;
    }
}

using HanMate.App.Localization;
using HanMate.Infrastructure.Packages;

namespace HanMate.App.Pages;

public sealed class ContentAudioSelectionPage : ContentPage
{
    private readonly IReadOnlyList<ShareAudioRow> _tracks;
    private readonly HashSet<Guid> _selection;
    private readonly LocalizationService _language;
    private readonly Action<IReadOnlyCollection<Guid>> _save;
    private int _offset;
    private string T(string key) => _language["Transfer." + key];
    public ContentAudioSelectionPage(IReadOnlyList<ShareAudioRow> tracks, IEnumerable<Guid> selected, LocalizationService language, Action<IReadOnlyCollection<Guid>> save)
    { _tracks = tracks; _selection = selected.ToHashSet(); _language = language; _save = save; Title = T("ChooseAudio"); Render(); }
    private void Render()
    {
        var body = new VerticalStackLayout { Padding = 16, Spacing = 10 };
        body.Add(new Label { Text = T("AudioHint") });
        var count = new Label();
        void UpdateCount() => count.Text = string.Format(T("AudioPage"), _offset / 10 + 1, Math.Max(1, (_tracks.Count + 9) / 10), _selection.Count);
        UpdateCount(); body.Add(count);
        var save = new Button { Text = T("AudioApply"), AutomationId = "Transfer.AudioApply" };
        save.Clicked += async (_, _) => { save.IsEnabled = false; _save(_selection.ToArray()); await Navigation.PopAsync(); }; body.Add(save);
        var clear = new Button { Text = T("AudioNone") }; clear.Clicked += (_, _) => { _selection.Clear(); Render(); }; body.Add(clear);
        foreach (var track in _tracks.Skip(_offset).Take(10))
        {
            var check = new CheckBox { IsChecked = _selection.Contains(track.Id), IsEnabled = track.CanShare };
            check.CheckedChanged += (_, e) => { if (e.Value) _selection.Add(track.Id); else _selection.Remove(track.Id); UpdateCount(); };
            var boundaries = System.Globalization.StringInfo.ParseCombiningCharacters(track.TargetText);
            var target = boundaries.Length <= 80 ? track.TargetText : track.TargetText[..boundaries[80]] + "…";
            var text = new Label { Text = track.ContentTitle + " · " + track.Label + "\n" + target + "\n"
                + string.Format(T("AudioDetails"), track.DurationMs / 1000.0, track.ByteLength / 1024.0)
                + " · " + T(!track.CanShare ? "AudioRestricted" : track.NeedsReview ? "AudioReview" : "AudioReady")
                + (track.Preferred ? " · " + T("AudioPreferred") : "") };
            var grid = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) } }; grid.Add(check); grid.Add(text, 1); body.Add(grid);
        }
        var previous = new Button { Text = T("Previous"), IsEnabled = _offset > 0 };
        previous.Clicked += (_, _) => { _offset -= 10; Render(); }; body.Add(previous);
        var next = new Button { Text = T("Next"), IsEnabled = _offset + 10 < _tracks.Count };
        next.Clicked += (_, _) => { _offset += 10; Render(); }; body.Add(next);
        Content = new ScrollView { Content = body };
    }
}

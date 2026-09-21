using System.Text.Json;
using HanMate.App.Audio;
using HanMate.App.Localization;
using HanMate.Core.Content;
using HanMate.Core.Reading;
using HanMate.Core.Pinyin;
using HanMate.Core.Search;
using HanMate.Infrastructure.Database;
using ReadingPage = HanMate.App.Pages.ReadingPage;

namespace HanMate.App.Controls;

public sealed class HandwritingSearchView : ContentView
{
    private static readonly Lazy<Task<HandwritingRecognizer>> Model = new(async () =>
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        await using var stream = await FileSystem.OpenAppPackageFileAsync("Handwriting/templates.bin");
        var model = await Task.Run(() =>
        {
            using var buffered = new BufferedStream(stream, 65536);
            return HandwritingRecognizer.LoadPrepared(buffered);
        });
        SpeechDiagnostics.Write($"handwriting model-load ms={timer.ElapsedMilliseconds}");
        return model;
    });
    private readonly LocalizationService _language;
    private readonly OfflineSearchStore _store;
    private readonly HandwritingPad _pad = new();
    private readonly VerticalStackLayout _matches = new() { Spacing = 10 };
    private readonly Label _status = new() { AutomationId = "Handwriting.Status", FontSize = 14, IsVisible = false };
    private readonly ImageButton _undo = ActionIcon("Handwriting.Undo", "ink_undo.png");
    private readonly ImageButton _clear = ActionIcon("Handwriting.Clear", "ink_clear.png");
    private static ImageButton ActionIcon(string id, string source) => new()
    {
        AutomationId = id, Source = source, WidthRequest = 44, HeightRequest = 44,
        Padding = 11, CornerRadius = 12, BackgroundColor = Color.FromArgb("#EEE6FA"),
        HorizontalOptions = LayoutOptions.Center
    };
    private CancellationTokenSource? _operation;
    private int _generation;
    private bool _active, _opening;
    private CandidateRow[] _rows = [];
    private readonly Dictionary<string, Lazy<Task<CandidateRow>>> _pendingRows = [];
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CandidateRow> _rowCache = new();
    private bool _preparing;
    private sealed record Entry(ContentDocument Document, bool Installed);
    private sealed record CandidateRow(string Character, Entry? Exact, IReadOnlyList<Entry> Examples, long Epoch);

    public HandwritingSearchView(LocalizationService language, OfflineSearchStore store)
    {
        _language = language; _store = store;
        var upper = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Star) }, RowSpacing = 8 };
        upper.Add(_status); upper.Add(new ScrollView { AutomationId = "Handwriting.Results", Content = _matches }, 0, 1);
        var actions = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Auto) }, ColumnSpacing = 20, HorizontalOptions = LayoutOptions.Center };
        actions.Add(_undo); actions.Add(_clear, 1);
        var lower = new Grid { RowDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, RowSpacing = 4 };
        lower.Add(_pad); lower.Add(actions, 0, 1);
        var split = new Grid { RowDefinitions = { new(new GridLength(2, GridUnitType.Star)), new(new GridLength(3, GridUnitType.Star)) }, RowSpacing = 6 };
        split.Add(upper); split.Add(lower, 0, 1); Content = split;
        _pad.StrokeStarted += (_, _) => { CancelRecognition(); _matches.Clear(); _rows = []; _status.IsVisible = false; };
        _pad.StrokesChanged += async (_, _) => await RecognizeAsync();
        _undo.Clicked += (_, _) => _pad.Undo(); _clear.Clicked += (_, _) => _pad.Clear();
        RefreshCopy();
    }
    private string T(string key) => _language["Handwriting." + key];
    public async Task ActivateAsync() { _active = true; Prepare(); RefreshCopy(); await RecognizeAsync(); }
    public void Prepare()
    {
        _ = WarmModelAsync();
        if (!_preparing) _ = PrepareLookupsAsync();
    }
    private async Task PrepareLookupsAsync()
    {
        _preparing = true;
        var pronunciation = Handler?.MauiContext?.Services.GetService<BundledPronunciationService>();
        try
        {
            await Task.Run(async () =>
            {
                await _store.PrepareAsync();
                if (pronunciation is not null) await pronunciation.GetCourseAsync();
            });
        }
        catch { /* An actual lookup reports failures, including retryable initialization. */ }
        finally { _preparing = false; }
    }
    private static async Task WarmModelAsync() { try { await Model.Value; } catch { /* Recognition reports load failures. */ } }
    public void Suspend() { _active = false; CancelRecognition(); _pad.CancelStroke(); }
    private void CancelRecognition() { _generation++; _operation?.Cancel(); _operation = null; _pendingRows.Clear(); }
    public void RefreshCopy()
    {
        SetActionDescription(_undo, T("Undo")); SetActionDescription(_clear, T("Clear"));
        SemanticProperties.SetDescription(_pad, T("Pad"));
        _status.IsVisible = false;
        Render();
    }
    private static void SetActionDescription(ImageButton button, string description)
    { SemanticProperties.SetDescription(button, description); ToolTipProperties.SetText(button, description); }
    private async Task RecognizeAsync()
    {
        CancelRecognition();
        if (!_active) return;
        var generation = _generation;
        var strokes = _pad.Snapshot();
        _rows = []; _matches.Clear(); _undo.IsEnabled = _clear.IsEnabled = strokes.Length > 0;
        _status.IsVisible = false;
        if (strokes.Length == 0) return;
        using var operation = new CancellationTokenSource(); _operation = operation;
        var token = operation.Token; _status.Text = T("Recognizing"); _status.IsVisible = true;
        try
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var model = await Model.Value.WaitAsync(token);
            var candidates = await Task.Run(() => model.Recognize(strokes, 6, token), token);
            token.ThrowIfCancellationRequested();
            if (!_active || generation != _generation) return;
            // Publish recognition immediately. Dictionary searches and course parsing are
            // enrichment, not prerequisites for seeing or selecting the candidates.
            _rows = candidates.Select(c => new CandidateRow(c.Character, null, [], -1)).ToArray();
            _status.IsVisible = false; Render();
            SpeechDiagnostics.Write($"handwriting candidates strokes={strokes.Length} ms={timer.ElapsedMilliseconds}");
            var services = Handler?.MauiContext?.Services;
            var teaching = new Lazy<Task<PinyinCourse?>>(async () =>
            {
                if (services is null) return null;
                var bundled = await services.GetRequiredService<BundledPronunciationService>().GetCourseAsync().WaitAsync(token);
                return await services.GetRequiredService<TeachingCatalogStore>().LoadAsync(bundled, token);
            });
            foreach (var candidate in candidates)
                _pendingRows[candidate.Character] = new(() => Task.Run(() => LoadRowAsync(candidate.Character, teaching, token), token));
            var pending = candidates.Select(c => _pendingRows[c.Character]).ToArray();
            // Keep pen-up candidates immediate; avoid starting six dictionary lookups
            // during the brief gap between strokes. Taps bypass this enrichment delay.
            await Task.Delay(100, token);
            for (var i = 0; i < pending.Length; i++)
            {
                var row = await pending[i].Value;
                token.ThrowIfCancellationRequested();
                if (!_active || generation != _generation) return;
                if (await _store.GetEpochAsync(token) != row.Epoch) { await RecognizeAsync(); return; }
                if (!_active || generation != _generation) return;
                _rows[i] = row;
                _matches.Children[i * 2] = RenderRow(row);
                if (i == 0) SpeechDiagnostics.Write($"handwriting first-row ms={timer.ElapsedMilliseconds}");
            }
            _status.Text = string.Format(T("Count"), strokes.Length);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (_active && generation == _generation) { _status.Text = T("Failed"); _status.IsVisible = true; } }
        finally { if (generation == _generation) _operation = null; }
    }
    private async Task<CandidateRow> LoadRowAsync(string character, Lazy<Task<PinyinCourse?>> teachingTask, CancellationToken token)
    {
        var epoch = await _store.GetEpochAsync(token);
        if (_rowCache.TryGetValue(character, out var cached) && cached.Epoch == epoch) return cached;
        var page = await _store.SearchAsync(character, cancellationToken: token, pageSize: 6);
        var exactIds = page.Items.Where(i => i.MatchTier == SearchMatchTier.HanziExact).Select(i => i.ContentId).ToHashSet();
        var entries = page.Items.Select(i => new Entry(JsonSerializer.Deserialize<ContentDocument>(i.BodyJson, ContentJson.Options)!, !i.IsReadOnly)).ToList();
        var teaching = await teachingTask.Value.WaitAsync(token);
        if (teaching is not null)
        {
            var visibleIds = teaching.Data.Items.SelectMany(i => i.Examples).Select(e => e.ContentId).ToHashSet();
            entries.AddRange(teaching.Data.Contents.Where(d => visibleIds.Contains(d.Id) && d.Kind == ContentKind.Word
                    && d.TextUnits.Any(u => u.Role == TextUnitRole.Headword && u.Text.Contains(character, StringComparison.Ordinal)))
                .Where(d => !entries.Any(e => (e.Installed || e.Document.Source.SourceId == HanMate.Infrastructure.Dictionary.DefaultDictionaryStore.SourceId)
                    && Headword(e.Document).Text == Headword(d).Text && Pinyin(e.Document) == Pinyin(d)))
                .Select(d => new Entry(d, false)));
        }
        var exact = entries.FirstOrDefault(e => exactIds.Contains(e.Document.Id) || Headword(e.Document).Text == character);
        var examples = entries.Where(e => !exactIds.Contains(e.Document.Id) && Headword(e.Document).Text != character)
            .DistinctBy(e => e.Document.Id).Take(5).ToArray();
        var row = new CandidateRow(character, exact, examples, page.Epoch);
        token.ThrowIfCancellationRequested();
        if (_rowCache.Count >= 64) _rowCache.Clear();
        _rowCache[character] = row;
        return row;
    }
    private static TextUnit Headword(ContentDocument doc) => doc.TextUnits.First(u => u.Role == TextUnitRole.Headword);
    private static string Pinyin(ContentDocument doc) => string.Join(" ", Headword(doc).Tokens.Select(t => t.Pinyin?.Display).Where(p => p is not null));
    private void Render()
    {
        _matches.Clear();
        foreach (var row in _rows)
        {
            _matches.Add(RenderRow(row));
            _matches.Add(new BoxView { HeightRequest = 1, Color = Color.FromArgb("#E9E2F0") });
        }
    }
    private Grid RenderRow(CandidateRow row)
    {
        var group = new Grid { ColumnDefinitions = { new(new GridLength(82)), new(GridLength.Star) }, ColumnSpacing = 10, Padding = new Thickness(0, 4) };
        var spelling = row.Exact is null ? "" : Pinyin(row.Exact.Document);
        var text = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center, InputTransparent = true };
        text.SetValue(AutomationProperties.ExcludedWithChildrenProperty, true);
        text.Add(new Label { Text = row.Character, FontSize = 28, HorizontalTextAlignment = TextAlignment.Center, TextColor = Color.FromArgb("#321568") });
        if (spelling.Length > 0) text.Add(new Label { Text = spelling, FontSize = 14, HorizontalTextAlignment = TextAlignment.Center, TextColor = Color.FromArgb("#695A7F") });
        var left = new Grid { MinimumHeightRequest = 72, VerticalOptions = LayoutOptions.Start, BackgroundColor = Color.FromArgb("#F5F0FC") };
        left.Add(text);
        var character = new Button { AutomationId = "Handwriting.Character." + row.Character,
            BackgroundColor = Colors.Transparent, BorderWidth = 0, Padding = 0, HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Fill };
        SemanticProperties.SetDescription(character, row.Character + " " + spelling);
        character.Clicked += async (_, _) => await OpenAsync(row.Exact, row.Character, row.Epoch);
        left.Add(character); group.Add(left);
        var examples = new WordFlowView { VerticalOptions = LayoutOptions.Start };
        foreach (var example in row.Examples)
        {
            var title = Headword(example.Document).Text;
            // The label owns text measurement. Android's multiline Button can report
            // a single-line height after flex shrinking, clipping the next line.
            var word = new Button { Padding = 0, BorderWidth = 0, CornerRadius = 8,
                AutomationId = "Handwriting.Word." + row.Character + "." + example.Document.Id,
                BackgroundColor = Colors.Transparent };
            SemanticProperties.SetDescription(word, title);
            word.Clicked += async (_, _) => await OpenAsync(example, Headword(example.Document).Text, row.Epoch);
            var label = new Label { Text = title, FontSize = 18, LineBreakMode = LineBreakMode.WordWrap,
                TextColor = Color.FromArgb("#321568"), VerticalOptions = LayoutOptions.Center };
            AutomationProperties.SetIsInAccessibleTree(label, false);
            var textArea = new Grid { Padding = new Thickness(9, 3), InputTransparent = true, CascadeInputTransparent = true };
            textArea.Add(label);
            var area = new Grid { MinimumHeightRequest = 48 }; area.Add(word); area.Add(textArea);
            examples.Children.Add(area);
        }
        group.Add(examples, 1);
        return group;
    }
    private async Task OpenAsync(Entry? entry, string word, long epoch)
    {
        if (!_active || _opening) return;
        _opening = true; var generation = _generation;
        try
        {
            if (epoch < 0)
            {
                if (!_pendingRows.TryGetValue(word, out var pending)) return;
                // A tap starts this candidate's lookup immediately instead of waiting
                // behind every preceding candidate. The same task supplies its row.
                var resolved = await pending.Value;
                if (!_active || generation != _generation) return;
                entry = resolved.Exact; epoch = resolved.Epoch;
            }
            if (await _store.GetEpochAsync() != epoch) { await RecognizeAsync(); return; }
            if (!_active || generation != _generation) return;
            if (entry is null)
            {
                // Recognition coverage is wider than the installed dictionary. Keep the
                // selected character navigable without inventing or saving a definition.
                await Navigation.PushAsync(new ContentPage { Title = word, Content = new ScrollView
                { Content = new VerticalStackLayout { Padding = 18, Spacing = 14, Children =
                    { new Label { Text = word, FontSize = 32 },
                      new Label { Text = T("NoDefinition"), AutomationId = "Handwriting.MissingDefinition", FontSize = 18 } } } } });
                return;
            }
            if (Handler?.MauiContext?.Services is not { } services) return;
            await Navigation.PushAsync(HanMate.App.Pages.DictionaryEntryPage.Create(entry.Document, _language, entry.Installed, services));
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (_active && generation == _generation) { _status.Text = T("Failed"); _status.IsVisible = true; } }
        finally { _opening = false; }
    }
}

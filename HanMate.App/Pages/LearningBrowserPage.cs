using System.Text.Json;
using HanMate.App.Localization;
using HanMate.App.Controls;
using HanMate.Core.Content;
using HanMate.Core.Reading;
using HanMate.Infrastructure.Database;

namespace HanMate.App.Pages;

public sealed partial class LearningBrowserPage(LearningCatalogStore store, LocalizationService language, ContentKind kind, string? wordCategory = null, string? wordCategoryName = null) : DataPage(language)
{
    private Difficulty? _difficulty;
    private SchoolStage? _stage;
    private int? _grade;
    private bool _personal;
    private readonly HashSet<string> _scenes = [];
    private int _offset;
    private bool _expanded;
    private LearningResult? _renderedResult;
    private string? _renderedCulture;
    protected override async Task RefreshAsync()
    {
        if (_renderedResult is not null && _renderedCulture == Language.CurrentCultureName)
        {
            var latest = await Task.Run(() => store.QueryAsync(new(kind, _difficulty, _stage, _grade, _scenes.ToArray(), _personal, wordCategory), _offset));
            if (latest.Total == _renderedResult.Total && latest.UnfilteredTotal == _renderedResult.UnfilteredTotal &&
                latest.Items.SequenceEqual(_renderedResult.Items)) return;
        }
        await ReloadAsync();
    }
    protected override async Task ReloadAsync()
    {
        await ResetWordPlaybackAsync();
        Status = LibraryLayout.Status();
        this.SetDynamicResource(StyleProperty, "LibraryPage");
        var result = await Task.Run(() => store.QueryAsync(new(kind, _difficulty, _stage, _grade, _scenes.ToArray(), _personal, wordCategory), _offset));
        if (result.Items.Count == 0 && _offset > 0) { _offset = 0; await ReloadAsync(); return; }
        Title = wordCategory is not null ? wordCategoryName ?? Language["WordCategories." + wordCategory]
            : kind == ContentKind.Grammar ? Language.KindGrammar : Language["Kind." + kind];
        var header = new VerticalStackLayout { Spacing = 10 };
        if (kind == ContentKind.Word)
        {
            header.Add(LibraryLayout.Muted(Language["LearningWords.Gesture"]));
            var create = new Button { Text = Language["WordEditor.NewWord"], AutomationId = "Learning.NewWord" };
            create.Clicked += async (_, _) => await RunAsync(async () =>
            {
                if (Handler?.MauiContext?.Services is { } services)
                    await Navigation.PushAsync(new PersonalWordEditorPage(wordCategory ?? WordCategories.Other, null,
                        services.GetRequiredService<PersonalWordStore>(), services.GetRequiredService<CustomWordCategoryStore>(), Language));
            });
            header.Add(create);
        }
        else if (kind is ContentKind.Text or ContentKind.Poem or ContentKind.Grammar)
        {
            var create = new Button { Text = Language["LearningEdit.New"], AutomationId = "Learning.NewContent" };
            create.Clicked += async (_, _) => await RunAsync(async () =>
            {
                if (Handler?.MauiContext?.Services is { } services)
                    await Navigation.PushAsync(new PersonalLearningEditorPage(kind, null,
                        services.GetRequiredService<PersonalLearningStore>(), Language));
            });
            header.Add(create);
        }
        var filters = LibraryLayout.Quiet(Button("Filters", async () => { _expanded = !_expanded; await ReloadAsync(); }));
        filters.AutomationId = "Library.Filters";
        var toolbar = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        filters.HorizontalOptions = LayoutOptions.Start; toolbar.Add(filters);
        header.Add(toolbar);
        var summary = new[] { _difficulty is { } d ? T(d.ToString()) : null, _stage is { } s ? T(s.ToString()) : null,
            _grade is { } g ? string.Format(T("GradeValue"), g) : null, _personal ? T("Personal") : null }.Where(x => x is not null).Concat(_scenes);
        var summaryText = string.Join(" · ", summary);
        if (summaryText.Length != 0) header.Add(LibraryLayout.Muted(summaryText));
        if (_expanded || summaryText.Length != 0)
        {
            var clear = LibraryLayout.Quiet(Button("Clear", async () => { _difficulty = null; _stage = null; _grade = null; _personal = false; _scenes.Clear(); _offset = 0; _expanded = false; await ReloadAsync(); }));
            clear.AutomationId = "Library.Clear"; toolbar.Add(clear, 1);
        }
        if (_expanded)
        {
            var fields = new VerticalStackLayout { Spacing = 8 };
            fields.Add(Select("Difficulty", Enum.GetValues<Difficulty>().Select(v => T(v.ToString())).ToArray(), _difficulty is null ? 0 : (int)_difficulty + 1,
                i => _difficulty = i == 0 ? null : (Difficulty)(i - 1)));
            fields.Add(Select("Stage", Enum.GetValues<SchoolStage>().Select(v => T(v.ToString())).ToArray(), _stage is null ? 0 : (int)_stage + 1,
                i => { _stage = i == 0 ? null : (SchoolStage)(i - 1); _grade = null; _ = RunAsync(ReloadAsync); }));
            fields.Add(Select("Grade", Enumerable.Range(1, _stage == SchoolStage.Primary ? 6 : 3).Select(i => string.Format(T("GradeValue"), i)).ToArray(), _grade ?? 0, i => _grade = i == 0 ? null : i));
            fields.Add(Select("Source", [T("Personal")], _personal ? 1 : 0, i => _personal = i == 1));
            var sceneRow = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
            var scenes = await Task.Run(() => store.GetScenesAsync(kind));
            foreach (var scene in scenes.Concat(_scenes).Distinct().Where(s => !WordCategories.All.Any(c => WordCategories.Scene(c) == s)))
            {
                var check = new CheckBox { IsChecked = _scenes.Contains(scene) };
                SemanticProperties.SetDescription(check, scene);
                check.CheckedChanged += (_, e) => { if (e.Value) _scenes.Add(scene); else _scenes.Remove(scene); };
                sceneRow.Add(new HorizontalStackLayout { Children = { check, new Label { Text = scene, VerticalTextAlignment = TextAlignment.Center } } });
            }
            fields.Add(sceneRow);
            var apply = Button("Apply", async () => { _offset = 0; _expanded = false; await ReloadAsync(); });
            apply.AutomationId = "Library.Apply"; apply.LineBreakMode = LineBreakMode.WordWrap; fields.Add(apply);
            header.Add(LibraryLayout.Surface(fields, new Thickness(16)));
        }
        Status.Text = result.Total == 0 ? "" : string.Format(T("Count"), _offset + 1, _offset + result.Items.Count, result.Total);
        Status.AutomationId = "Library.Count";
        var rows = new CollectionView { AutomationId = "Library.Items", SelectionMode = SelectionMode.None, ItemsSource = result.Items,
            ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Vertical) { ItemSpacing = 8 },
            EmptyView = new Label { Text = T(result.UnfilteredTotal == 0 || kind == ContentKind.Word && wordCategory is not null &&
                CustomWordCategoryStore.IsCustom(wordCategory) && result.Total == 0 && _difficulty is null && _stage is null &&
                _grade is null && !_personal && _scenes.Count == 0 ? "NoContent" : "NoMatches"),
                Margin = new Thickness(24, 40), HorizontalTextAlignment = TextAlignment.Center },
            ItemTemplate = new DataTemplate(() => LibraryLayout.CollectionRow(LibraryLayout.ContentRow(nameof(LearningRow.Title), open =>
            {
                SemanticProperties.SetHint(open, T("Read"));
                open.Clicked += async (_, _) =>
                {
                    if (open.BindingContext is not LearningRow row || Handler?.MauiContext?.Services is not { } services) return;
                    await RunAsync(async () => await Navigation.PushAsync(await LearningDetailPageFactory.CreateAsync(
                        await Task.Run(() => JsonSerializer.Deserialize<ContentDocument>(row.BodyJson, ContentJson.Options)!), Language, services)));
                };
            }))) };
        if (kind == ContentKind.Word)
        {
            var words = await Task.Run(() => result.Items.Select(row =>
            {
                var document = JsonSerializer.Deserialize<ContentDocument>(row.BodyJson, ContentJson.Options)!;
                return new WordListItem(document, document.TextUnits.Single(u => u.Role == TextUnitRole.Headword));
            }).ToArray());
            rows.ItemTemplate = new WordCardTemplateSelector(this);
            rows.ItemsSource = words;
        }
        else if (kind == ContentKind.Grammar)
        {
            rows.ItemTemplate = new DataTemplate(CreateGrammarCard);
            rows.ItemsSource = await Task.Run(() => result.Items.Select(row =>
                JsonSerializer.Deserialize<ContentDocument>(row.BodyJson, ContentJson.Options)!).ToArray());
        }
        else if (kind is ContentKind.Text or ContentKind.Poem)
        {
            rows.ItemTemplate = new DataTemplate(CreateLessonCard);
            rows.ItemsSource = await Task.Run(() => result.Items.Select(row =>
                JsonSerializer.Deserialize<ContentDocument>(row.BodyJson, ContentJson.Options)!).ToArray());
        }
        var previous = Button("Previous", async () => { _offset = Math.Max(0, _offset - 50); await ReloadAsync(); }); previous.IsEnabled = _offset > 0;
        var next = Button("Next", async () => { _offset += 50; await ReloadAsync(); }); next.IsEnabled = _offset + 50 < result.Total;
        previous.AutomationId = "Library.Previous"; next.AutomationId = "Library.Next";
        previous.LineBreakMode = next.LineBreakMode = LineBreakMode.WordWrap;
        var grid = new Grid { Padding = 16, RowSpacing = 10, MaximumWidthRequest = 920, RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) } };
        var filterScroll = new ScrollView { AutomationId = "Library.FilterScroll", Content = header, MaximumHeightRequest = _expanded ? 380 : 220 };
        grid.SizeChanged += (_, _) => { if (grid.Height > 0) filterScroll.MaximumHeightRequest = Math.Min(_expanded ? 380 : 220, grid.Height * .4); };
        // The result count must stay outside the deliberately bounded filter viewport.
        grid.Add(filterScroll); grid.Add(Status, 0, 1); grid.Add(rows, 0, 2);
        grid.Add(LibraryLayout.Paging(previous, next, result.Total > 50), 0, 3); Content = grid;
        if (kind == ContentKind.Word)
        {
            // Reserve the localized, wrapping message height even while idle: playback cannot move the list.
            var reserved = LibraryLayout.Muted(Language["DictionaryDetail.SpeechPending"]);
            reserved.Opacity = 0; reserved.InputTransparent = true;
            AutomationProperties.SetIsInAccessibleTree(reserved, false);
            var footer = new Grid { AutomationId = "Learning.PlaybackFooter", InputTransparent = true };
            footer.Add(reserved); footer.Add(_wordStatus);
            grid.RowDefinitions.Add(new(GridLength.Auto)); grid.Add(footer, 0, 4);
        }
        _renderedResult = result; _renderedCulture = Language.CurrentCultureName;
    }
    private Picker Select(string key, string[] options, int selected, Action<int> change)
    {
        var picker = new Picker { AutomationId = "Library.Filter." + key, Title = T(key), ItemsSource = new[] { T(key) + " · " + T("All") }.Concat(options).ToArray(), SelectedIndex = selected };
        SemanticProperties.SetDescription(picker, T(key));
        picker.SelectedIndexChanged += (_, _) => { if (picker.SelectedIndex >= 0) change(picker.SelectedIndex); }; return picker;
    }
}

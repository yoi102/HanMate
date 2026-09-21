using HanMate.App.Audio;
using HanMate.App.Localization;
using HanMate.Infrastructure.Database;

namespace HanMate.App.Pages;

/// <summary>Management and attribution belong in Settings, outside the practice surfaces.</summary>
public sealed class PinyinResourcesPage : ContentPage
{
    private readonly Label _status = new();
    private bool _busy;
    public PinyinResourcesPage(LocalizationService language, BundledPronunciationService source, TeachingCatalogStore teaching)
    {
        Title = language["Pinyin.Resources"];
        var import = new Button { Text = language["Pinyin.ImportCatalog"], AutomationId = "Pinyin.ImportCatalog" };
        var notices = new Button { Text = language["Pinyin.Sources"], AutomationId = "Pinyin.Sources" };
        import.Clicked += async (_, _) =>
        {
            if (_busy) return; _busy = true;
            try
            {
                var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = import.Text }); if (file is null) return;
                await using var stream = await file.OpenReadAsync(); var plan = await Task.Run(() => teaching.PlanAsync(stream));
                if (!await DisplayAlertAsync(import.Text, string.Format(language["Pinyin.ImportCatalogInfo"], plan.Count), import.Text, language["Library.Cancel"])) return;
                await Task.Run(() => teaching.CommitAsync(plan)); _status.Text = language["State.Saved"];
            }
            catch { _status.Text = language["Pinyin.LoadFailed"]; }
            finally { _busy = false; }
        };
        notices.Clicked += async (_, _) =>
        {
            if (_busy) return; _busy = true;
            try
            {
                var course = await source.GetCourseAsync();
                var body = new VerticalStackLayout { Padding = 20, Spacing = 14 };
                body.Add(new Label { Text = language["Pinyin.DraftNotice"] });
                body.Add(new Label { Text = language["Pinyin.AudioMethod"] });
                foreach (var asset in course.Data.Assets)
                    body.Add(new Label { Text = $"{asset.Key} · {asset.Author}\n{asset.License}\n{asset.SourceUrl}\n{asset.LicenseUrl}\n{asset.Changes}", FontSize = 14 });
                await Navigation.PushAsync(new ContentPage { Title = notices.Text, Content = new ScrollView { Content = body } });
            }
            catch { _status.Text = language["Pinyin.LoadFailed"]; }
            finally { _busy = false; }
        };
        var preferAi = new Switch { IsToggled = Preferences.Default.Get(PinyinSpeechService.PreferenceKey, false), AutomationId = "Pinyin.PreferAi" };
        SemanticProperties.SetDescription(preferAi, language["Pinyin.PreferAi"]);
        preferAi.Toggled += (_, e) => Preferences.Default.Set(PinyinSpeechService.PreferenceKey, e.Value);
        var preference = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)], ColumnSpacing = 12 };
        preference.Add(new Label { Text = language["Pinyin.PreferAi"], VerticalOptions = LayoutOptions.Center }, 0);
        preference.Add(preferAi, 1);
        Content = new ScrollView { Content = new VerticalStackLayout { Padding = 20, Spacing = 14, Children = {
            preference, new Label { Text = language["Pinyin.PreferAiHint"] }, import, notices, _status } } };
    }
}

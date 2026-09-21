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
                await using var sharedNotice = await FileSystem.OpenAppPackageFileAsync("WordAudio/NOTICE.txt");
                using var reader = new StreamReader(sharedNotice);
                body.Add(new Label { Text = await reader.ReadToEndAsync(), FontSize = 14 });
                foreach (var asset in course.Data.Assets)
                    body.Add(new Label { Text = $"{asset.Key} · {asset.Author}\n{asset.License}\n{asset.SourceUrl}\n{asset.LicenseUrl}\n{asset.Changes}", FontSize = 14 });
                await Navigation.PushAsync(new ContentPage { Title = notices.Text, Content = new ScrollView { Content = body } });
            }
            catch { _status.Text = language["Pinyin.LoadFailed"]; }
            finally { _busy = false; }
        };
        Content = new ScrollView { Content = new VerticalStackLayout { Padding = 20, Spacing = 14, Children = {
            new Label { Text = language["Pinyin.RecordingFirstHint"] }, import, notices, _status } } };
    }
}

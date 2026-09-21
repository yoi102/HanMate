using HanMate.App.Localization;
using HanMate.Infrastructure.Dictionary;

namespace HanMate.App.Pages;

public sealed class DefaultDictionaryPage : ContentPage
{
    public DefaultDictionaryPage(LocalizationService language)
    {
        Title = language["Dictionary.About"];
        var body = new VerticalStackLayout { Padding = 20, Spacing = 14 };
        body.Add(new Label { Text = DefaultDictionaryStore.Name, FontSize = 24, FontAttributes = FontAttributes.Bold });
        body.Add(new Label { Text = string.Format(language["Dictionary.Summary"], DefaultDictionaryStore.EntryCount.ToString("N0"), DefaultDictionaryStore.Version) });
        body.Add(new Label { Text = language["Dictionary.Description"] });
        var toggle = new Switch { AutomationId = "Dictionary.Enabled" };
        var row = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        row.Add(new Label { Text = language["Dictionary.Enabled"], VerticalOptions = LayoutOptions.Center }); row.Add(toggle, 1); body.Add(row);
        var initializing = true;
        Loaded += (_, _) =>
        {
            var store = Handler!.MauiContext!.Services.GetRequiredService<DefaultDictionaryStore>();
            toggle.IsToggled = store.IsEnabled; initializing = false;
        };
        toggle.Toggled += (_, e) =>
        {
            if (initializing) return;
            Preferences.Default.Set(DefaultDictionaryStore.PreferenceKey, e.Value);
            Handler!.MauiContext!.Services.GetRequiredService<DefaultDictionaryStore>().IsEnabled = e.Value;
        };
        body.Add(new Label { Text = "pwxcoo / chinese-xinhua · " + DefaultDictionaryStore.Version + "\nhttps://github.com/pwxcoo/chinese-xinhua", FontSize = 14 });
        body.Add(new Label { Text = DefaultDictionaryStore.ReadUsage(), FontSize = 14 });
        body.Add(new Label { Text = "Search index: OpenCC / opencc-python-reimplemented 0.1.7\nhttps://github.com/yichen0831/opencc-python\n" + DefaultDictionaryStore.ReadIndexLicense(), FontSize = 12 });
        body.Add(new Label { Text = "Pinyin: pypinyin 0.55.0\nhttps://github.com/mozillazg/python-pinyin\n" + DefaultDictionaryStore.ReadPinyinLicense(), FontSize = 12 });
        body.Add(new Label { Text = "Word frequency: jieba 0.42.1\nhttps://github.com/fxsjy/jieba\n" + DefaultDictionaryStore.ReadFrequencyLicense(), FontSize = 12 });
        Content = new ScrollView { Content = body };
    }
}

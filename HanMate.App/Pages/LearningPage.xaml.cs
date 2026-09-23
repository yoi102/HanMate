using System.ComponentModel;
using HanMate.App.Localization;
using HanMate.App.Controls;
using HanMate.Core.Content;
using HanMate.Infrastructure.Catalog;
using HanMate.Infrastructure.Packages;

namespace HanMate.App.Pages;

public partial class LearningPage : ContentPage
{
    private readonly LocalizationService _localization;
    private readonly BundledResourceCatalog _catalog;
    private readonly TextResourceInstaller _installer;
    private readonly HanMate.Infrastructure.Database.LearningCatalogStore _learning;
    private readonly HanMate.Infrastructure.Database.CustomWordCategoryStore _customCategories;
    private bool _opening;
    private bool _loadFailed;
    private int _columns = 2;
    private WordCategoriesPage? _wordCategories;
    private LearningBrowserPage? _grammar;
    private LearningBrowserPage? _texts;
    private LearningBrowserPage? _poems;

    public LearningPage(LocalizationService localization, BundledResourceCatalog catalog, TextResourceInstaller installer,
        HanMate.Infrastructure.Database.LearningCatalogStore learning, HanMate.Infrastructure.Database.CustomWordCategoryStore customCategories)
    {
        InitializeComponent();
        _localization = localization;
        _catalog = catalog; _installer = installer;
        _learning = learning; _customCategories = customCategories;
        BindingContext = localization;
        UpdateStatus();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _localization.PropertyChanged -= OnLocalizationChanged;
        _localization.PropertyChanged += OnLocalizationChanged;
        UpdateStatus();
        try { await Task.Run(() => _catalog.EnsureInstalledAsync()); _loadFailed = false; }
        catch { _loadFailed = true; }
        UpdateStatus();
    }

    protected override void OnDisappearing()
    {
        _localization.PropertyChanged -= OnLocalizationChanged;
        base.OnDisappearing();
    }

    private async void OnKindClicked(object? sender, EventArgs e)
    {
        if (_opening || sender is not NavigationTile button || !Enum.TryParse<ContentKind>(button.CommandParameter?.ToString(), out var kind)) return;
        _opening = true;
        try
        {
            await Task.Run(() => _catalog.EnsureInstalledAsync());
            await Navigation.PushAsync(kind == ContentKind.Word
                ? _wordCategories ??= new WordCategoriesPage(_learning, _customCategories, _localization)
                : kind == ContentKind.Grammar ? _grammar ??= new LearningBrowserPage(_learning, _localization, kind)
                : kind == ContentKind.Text ? _texts ??= new LearningBrowserPage(_learning, _localization, kind)
                : kind == ContentKind.Poem ? _poems ??= new LearningBrowserPage(_learning, _localization, kind)
                : new LearningBrowserPage(_learning, _localization, kind));
        }
        catch { _loadFailed = true; UpdateStatus(); }
        finally { _opening = false; }
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Shell.Current?.CurrentPage != this) return;
        if (e.PropertyName is nameof(LocalizationService.CurrentLanguage) or nameof(LocalizationService.ShowAuxiliaryTranslations))
            UpdateStatus();
    }

    private void UpdateStatus()
    {
        CatalogStatus.Text = _loadFailed ? _localization["Pinyin.LoadFailed"] : "";
        CatalogStatus.IsVisible = _loadFailed;
    }

    private void OnCategoriesSizeChanged(object? sender, EventArgs e)
    {
        var columns = CategoryGrid.Width < 480 ? 1 : 2;
        if (CategoryGrid.Width <= 0 || columns == _columns) return;
        _columns = columns;
        CategoryGrid.ColumnDefinitions.Clear(); CategoryGrid.RowDefinitions.Clear();
        for (var i = 0; i < columns; i++) CategoryGrid.ColumnDefinitions.Add(new(GridLength.Star));
        for (var i = 0; i < 4 / columns; i++) CategoryGrid.RowDefinitions.Add(new(GridLength.Auto));
        for (var i = 0; i < CategoryGrid.Children.Count; i++)
        { CategoryGrid.SetRow(CategoryGrid.Children[i], i / columns); CategoryGrid.SetColumn(CategoryGrid.Children[i], i % columns); }
    }

}

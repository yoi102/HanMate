using System.ComponentModel;
using HanMate.App.Localization;

namespace HanMate.App.Pages;

/// <summary>Shared lifetime for retained Shell pages and serialized local data operations.</summary>
public abstract class DataPage(LocalizationService language) : ContentPage
{
    protected readonly LocalizationService Language = language;
    protected Label Status = new();
    protected bool Busy;
    private Shell? _shell;
    protected string T(string key) => Language["Library." + key];
    protected Button Button(string key, Func<Task> action)
    {
        var button = new Button { Text = T(key) };
        button.Clicked += async (_, _) => await RunAsync(action); return button;
    }
    protected async Task RunAsync(Func<Task> action)
    {
        if (Busy) return; Busy = true;
        try { await action(); }
        catch (HanMate.Core.Contracts.RevisionConflictException) { Status.Text = T("Stale"); }
        catch (Exception exception) { System.Diagnostics.Debug.WriteLine(exception.GetType().Name); Status.Text = T("Failed"); if (Status.Parent is null) Content = new VerticalStackLayout { Padding = 16, Children = { Status } }; }
        finally { Busy = false; }
    }
    protected abstract Task ReloadAsync();
    protected virtual Task RefreshAsync() => ReloadAsync();
    protected override async void OnAppearing() { base.OnAppearing(); Content ??= new VerticalStackLayout { Padding = 16, Children = { Status } }; await RunAsync(RefreshAsync); }
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (_shell is not null) { _shell.Navigated -= Navigated; Language.PropertyChanged -= Changed; _shell = null; }
        if (Handler is not null) { _shell = Shell.Current; if (_shell is not null) _shell.Navigated += Navigated; Language.PropertyChanged += Changed; }
    }
    private async void Navigated(object? sender, ShellNavigatedEventArgs e) { if (_shell?.CurrentPage == this) await RunAsync(RefreshAsync); }
    private async void Changed(object? sender, PropertyChangedEventArgs e)
    {
        // Reload on appearing/navigation for hidden pages, including old window trees.
        if (string.IsNullOrEmpty(e.PropertyName) && _shell == Shell.Current && _shell?.CurrentPage == this)
            await RunAsync(RefreshAsync);
    }
}

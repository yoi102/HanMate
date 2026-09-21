using System.Text.Json;
using HanMate.App.Localization;
using HanMate.Core.Content;
using HanMate.Core.Reading;
using HanMate.Core.Resources;
using HanMate.Infrastructure.Database;

namespace HanMate.App.Pages;

public sealed class ContentTrashPage(ContentTrashStore store, LocalizationService language) : DataPage(language)
{
    private int _offset;
    protected override async Task ReloadAsync()
    {
        Title = T("Trash"); Status = new Label();
        var rows = await Task.Run(() => store.ListAsync(_offset));
        if (rows.Count == 0 && _offset > 0) { _offset = 0; await ReloadAsync(); return; }
        var body = new VerticalStackLayout { Padding = 16, Spacing = 10 };
        body.Add(new Label { Text = T("TrashHint") }); body.Add(Status);
        if (rows.Count == 0) body.Add(new Label { Text = T("TrashEmpty") });
        foreach (var row in rows)
        {
            body.Add(new Label { Text = row.Title, FontSize = 20 });
            body.Add(Button("Read", () => Navigation.PushAsync(new ReadingPage(new ReadingDocument(JsonSerializer.Deserialize<ContentDocument>(row.BodyJson, ContentJson.Options)!), Language, allowEditing: false))));
            body.Add(Button("RestoreContent", async () => { await Task.Run(() => store.RestoreAsync(row)); await ReloadAsync(); }));
        }
        var previous = Button("Previous", async () => { _offset = Math.Max(0, _offset - 50); await ReloadAsync(); }); previous.IsEnabled = _offset > 0;
        var next = Button("Next", async () => { _offset += 50; await ReloadAsync(); }); next.IsEnabled = rows.Count == 50;
        body.Add(previous); body.Add(next); Content = new ScrollView { Content = body };
    }
}

public sealed class RetainedContentPage(ResourceManagementStore store, LocalizationService language) : DataPage(language)
{
    private int _offset;
    protected override async Task ReloadAsync()
    {
        Title = T("RetainedContents"); Status = new Label();
        var rows = await Task.Run(() => store.GetRetainedAsync(_offset));
        if (rows.Count == 0 && _offset > 0) { _offset = 0; await ReloadAsync(); return; }
        var body = new VerticalStackLayout { Padding = 16, Spacing = 10 };
        body.Add(new Label { Text = T("RetainedHint") }); body.Add(Status);
        foreach (var row in rows)
        {
            body.Add(new Label { Text = row.Title, FontSize = 20 });
            body.Add(Button("Read", () => Navigation.PushAsync(new ReadingPage(new ReadingDocument(JsonSerializer.Deserialize<ContentDocument>(row.BodyJson, ContentJson.Options)!), Language))));
        }
        var previous = Button("Previous", async () => { _offset = Math.Max(0, _offset - 50); await ReloadAsync(); }); previous.IsEnabled = _offset > 0;
        var next = Button("Next", async () => { _offset += 50; await ReloadAsync(); }); next.IsEnabled = rows.Count == 50;
        body.Add(previous); body.Add(next); Content = new ScrollView { Content = body };
    }
}

public sealed class ResourceEntriesPage(ResourceManagementStore management, ResourceStateStore states, Guid resourceId, LocalizationService language) : DataPage(language)
{
    private int _offset;
    protected override async Task ReloadAsync()
    {
        Title = Language["Manage.Entries"]; Status = new Label();
        var resource = (await Task.Run(() => management.GetAsync())).Single(r => r.State.ResourceId == resourceId);
        var rows = await Task.Run(() => management.GetEntriesAsync(resourceId, _offset));
        if (rows.Count == 0 && _offset > 0) { _offset = 0; await ReloadAsync(); return; }
        var body = new VerticalStackLayout { Padding = 16, Spacing = 10 }; body.Add(Status);
        body.Add(new Label { Text = Language["Manage.EntriesHint"] });
        foreach (var row in rows)
        {
            body.Add(new Label { Text = row.Title, FontSize = 20 });
            body.Add(new Label { Text = Language[row.Removed ? "Library.Hidden" : "Library.Ready"] });
            body.Add(Button("Read", () => Navigation.PushAsync(new ReadingPage(new ReadingDocument(JsonSerializer.Deserialize<ContentDocument>(row.BodyJson, ContentJson.Options)!), Language))));
            var change = new Button { Text = Language[row.Removed ? "Manage.RestoreEntry" : "Manage.WithdrawEntry"] };
            change.Clicked += async (_, _) => await RunAsync(async () =>
            {
                if (!row.Removed && !await DisplayAlertAsync(change.Text, Language["Manage.WithdrawHint"], change.Text, T("Cancel"))) return;
                await Task.Run(() => states.SetEntryRemovedAsync(resourceId, row.EntryId, !row.Removed, resource.State.RowRevision, DateTimeOffset.UtcNow,
                    new(Guid.NewGuid(), resourceId, row.Removed ? ResourceOperationType.RestoreEntry : ResourceOperationType.Remove, resource.Epoch, DateTimeOffset.UtcNow, "{}")));
                await ReloadAsync();
            });
            body.Add(change);
        }
        var previous = Button("Previous", async () => { _offset = Math.Max(0, _offset - 50); await ReloadAsync(); }); previous.IsEnabled = _offset > 0;
        var next = Button("Next", async () => { _offset += 50; await ReloadAsync(); }); next.IsEnabled = rows.Count == 50;
        body.Add(previous); body.Add(next); body.Add(Button("Refresh", ReloadAsync)); Content = new ScrollView { Content = body };
    }
}

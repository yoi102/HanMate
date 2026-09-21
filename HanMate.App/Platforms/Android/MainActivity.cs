using Android.App;
using Android.Content.PM;
using Android.OS;

namespace HanMate.App;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private const int SaveRequest = 7412;
    private TaskCompletionSource<Android.Net.Uri?>? _save;

    public Task<Android.Net.Uri?> CreateDocumentAsync(string name, string mime)
    {
        if (_save is not null) throw new InvalidOperationException("Document picker is busy.");
        var completion = new TaskCompletionSource<Android.Net.Uri?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _save = completion;
        try
        {
            using var intent = new Android.Content.Intent(Android.Content.Intent.ActionCreateDocument);
            intent.AddCategory(Android.Content.Intent.CategoryOpenable);
            intent.SetType(mime); intent.PutExtra(Android.Content.Intent.ExtraTitle, name);
            StartActivityForResult(intent, SaveRequest);
        }
        catch { _save = null; throw; }
        return completion.Task;
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Android.Content.Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != SaveRequest) return;
        var completion = _save; _save = null;
        completion?.TrySetResult(resultCode == Result.Ok ? data?.Data : null);
    }

    protected override void OnDestroy()
    {
        _save?.TrySetResult(null); _save = null;
        base.OnDestroy();
    }
}

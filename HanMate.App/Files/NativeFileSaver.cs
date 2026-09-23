namespace HanMate.App.Files;

public sealed class FileSaveException(Exception inner) : IOException("The document provider could not complete the save.", inner);

/// <summary>Exports only an already validated private copy, through a user-selected document destination.</summary>
public static class NativeFileSaver
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<bool> SaveAsync(string source, bool zipFallback = false)
    {
        if (!await Gate.WaitAsync(0)) throw new InvalidOperationException("A save dialog is already open.");
        try
        {
            var name = Path.GetFileName(source) + (zipFallback ? ".zip" : "");
            if (!File.Exists(source)) throw new FileNotFoundException();
            return await MainThread.InvokeOnMainThreadAsync(() => SaveNativeAsync(source, name));
        }
        catch (Exception e) { throw new FileSaveException(e); }
        finally { Gate.Release(); }
    }

    private static async Task<bool> SaveNativeAsync(string source, string name)
    {
#if WINDOWS
        var picker = new global::Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = name };
        picker.FileTypeChoices.Add(Path.GetExtension(name), new List<string> { Path.GetExtension(name) });
        var window = Application.Current!.Windows[0].Handler!.PlatformView;
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
        var file = await picker.PickSaveFileAsync();
        if (file is null) return false;
        // A failed write must not replace the previous destination contents.
        using var transaction = await file.OpenTransactedWriteAsync();
        transaction.Stream.Size = 0;
        await using var input = File.OpenRead(source);
        using var output = transaction.Stream.AsStreamForWrite();
        await input.CopyToAsync(output); await output.FlushAsync();
        await transaction.CommitAsync();
        return true;
#elif ANDROID
        var activity = Platform.CurrentActivity as MainActivity ?? throw new InvalidOperationException();
        var mime = name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? "audio/wav"
            : name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? "application/zip"
            : "application/octet-stream";
        var uri = await activity.CreateDocumentAsync(name, mime);
        if (uri is null) return false;
        await using var input = File.OpenRead(source);
        await using var output = activity.ContentResolver!.OpenOutputStream(uri, "wt") ?? throw new IOException("Document provider did not open a stream.");
        await input.CopyToAsync(output); await output.FlushAsync();
        return true;
#elif IOS || MACCATALYST
        // Export-as-copy lets the document provider own its write transaction. A private temporary
        // directory also permits a .zip filename without renaming the verified source.
        var directory = Path.Combine(FileSystem.CacheDirectory, "save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var copy = Path.Combine(directory, name);
        try
        {
            File.Copy(source, copy);
            using var url = Foundation.NSUrl.FromFilename(copy);
            using var picker = new UIKit.UIDocumentPickerViewController(new[] { url }, true);
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            picker.DidPickDocumentAtUrls += (_, e) => done.TrySetResult(e.Urls.Length > 0);
            picker.WasCancelled += (_, _) => done.TrySetResult(false);
            picker.ModalInPresentation = true;
            var controller = Platform.GetCurrentUIViewController() ?? throw new InvalidOperationException();
            await controller.PresentViewControllerAsync(picker, true);
            return await done.Task;
        }
        finally { if (File.Exists(copy)) File.Delete(copy); Directory.Delete(directory); }
#else
        await Task.CompletedTask;
        throw new PlatformNotSupportedException();
#endif
    }
}

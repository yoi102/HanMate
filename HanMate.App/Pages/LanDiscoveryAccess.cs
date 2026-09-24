namespace HanMate.App.Pages;

/// <summary>Keeps Wi-Fi discovery packets available while a room or search is active.</summary>
internal static class LanDiscoveryAccess
{
    public static IDisposable Hold()
    {
#if ANDROID
        try
        {
            var wifi = Android.App.Application.Context.GetSystemService(Android.Content.Context.WifiService)
                as Android.Net.Wifi.WifiManager;
            var multicast = wifi?.CreateMulticastLock("HanMate-room-discovery");
            if (multicast is null) return Empty.Instance;
            multicast.SetReferenceCounted(false);
            multicast.Acquire();
            return new Lease(() => { multicast.Release(); multicast.Dispose(); });
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine(error);
        }
#endif
        return Empty.Instance;
    }

    private sealed class Empty : IDisposable
    {
        public static Empty Instance { get; } = new();
        public void Dispose() { }
    }

    private sealed class Lease(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}

using System.Text.Json;
using HanMate.App.Audio;
using HanMate.Core.Audio;

internal static class RecordingProbe
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "HanMate-record-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var stage = "devices";
        try
        {
            var devices = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(Windows.Media.Devices.MediaDevice.GetAudioCaptureSelector());
            Console.WriteLine(JsonSerializer.Serialize(new { captureDevices = devices.Select(d => new { d.Name, d.IsEnabled }), privateOutput = root }));
            stage = "native-capture";
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var path = Path.Combine(root, "capture.wav");
            await NativeWaveRecorder.CaptureAsync(path, _ => { }, stop.Token);
            stage = "inspect";
            using var wave = File.OpenRead(path);
            var info = PcmWave.Inspect(wave);
            Console.WriteLine(JsonSerializer.Serialize(new { status = "PASS", info, privateOutput = root, scope = "Native capture/container only; human listening is separate." }));
        }
        catch (Exception error)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { status = "FAIL", stage, type = error.GetType().FullName, hresult = $"0x{error.HResult:X8}", message = error.Message, stack = error.StackTrace }));
            Environment.ExitCode = 1;
        }
    }
}

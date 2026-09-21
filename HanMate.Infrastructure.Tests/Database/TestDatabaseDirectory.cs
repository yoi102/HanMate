namespace HanMate.Infrastructure.Tests.Database;

internal sealed class TestDatabaseDirectory : IDisposable
{
    public TestDatabaseDirectory()
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), "HanMate.Infrastructure.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        DatabasePath = Path.Combine(DirectoryPath, "hanmate.db");
    }

    public string DirectoryPath { get; }
    public string DatabasePath { get; }

    public void Dispose()
    {
        if (!Directory.Exists(DirectoryPath)) return;
        // This instance owns a generated temporary directory, including test audio subdirectories.
        Directory.Delete(DirectoryPath, recursive: true);
    }
}

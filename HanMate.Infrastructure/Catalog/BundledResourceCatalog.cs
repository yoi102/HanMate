using System.Security.Cryptography;
using HanMate.Core.Resources;
using HanMate.Infrastructure.Database;
using HanMate.Infrastructure.Packages;

namespace HanMate.Infrastructure.Catalog;

/// <summary>Only these embedded, hash-pinned payloads may create bundled resource registrations.</summary>
public sealed class BundledResourceCatalog(HanMateDatabase database, TextResourceInstaller installer)
{
    public const string Version = "1.0.1";
    public const string LearningVersion = "1.0.6";
    private readonly SemaphoreSlim _gate = new(1, 1);
    public static readonly Guid LearningId = Guid.Parse("48078b10-37d3-5e6c-898b-1c3eda38f629");
    public static readonly Guid DictionaryId = Guid.Parse("623598a5-6f26-52ad-bae5-a06868d8f079");
    public static bool IsReserved(Guid id) => id == LearningId || id == DictionaryId;

    public Task EnsureInstalledAsync(CancellationToken cancellationToken = default) => InstallAsync(null, cancellationToken);

    public Task RestoreAsync(Guid resourceId, CancellationToken cancellationToken = default)
    {
        if (!IsReserved(resourceId)) throw new ArgumentOutOfRangeException(nameof(resourceId));
        return InstallAsync(resourceId, cancellationToken);
    }

    private async Task InstallAsync(Guid? restoreId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Install("learning", LearningId, LearningVersion, "982aacac2174f3c1170fe99390dd9bbf00a9380b7bdcfca7bea0f567625ffe23");
            await Install("dictionary", DictionaryId, Version, "95e7ed1065b1180de7e8671c1d8872d1de7bd3c758e93f75236c06623a04dcaf");
        }
        finally { _gate.Release(); }

        async Task Install(string name, Guid id, string version, string expectedHash)
        {
            if (restoreId is not null && restoreId != id) return;
            var existing = await new ResourceStateStore(database).GetAsync(id, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                if (existing.Distribution != ResourceDistribution.Bundled) throw new PackageException("RESOURCE_ID_RESERVED");
                // Upgrade installed catalogs without changing enabled/priority/withdrawn state. Never reinstall
                // an explicitly removed catalog on startup, and never downgrade a newer restored catalog.
                if ((!existing.IsPresent && restoreId is null) ||
                    (existing.IsPresent && TextResourceUpdate.CompareVersions(existing.Version, version) >= 0)) return;
            }
            await using var input = typeof(BundledResourceCatalog).Assembly.GetManifestResourceStream("Starter." + name + ".zip") ?? throw new InvalidDataException("Bundled resource missing.");
            using var bytes = new MemoryStream(); await input.CopyToAsync(bytes, cancellationToken).ConfigureAwait(false);
            if (Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())) != expectedHash) throw new InvalidDataException("Bundled resource hash mismatch.");
            bytes.Position = 0;
            var package = await TextResourcePackageReader.ReadAsync(bytes, cancellationToken).ConfigureAwait(false);
            if (package.Registration.ResourceId != id || package.Registration.Version != version) throw new InvalidDataException("Bundled identity mismatch.");
            var trustedPackage = package with { Registration = package.Registration with { Distribution = ResourceDistribution.Bundled, Priority = name == "learning" ? 10 : 20 } };
            var plan = await installer.PlanPackageAsync(trustedPackage, cancellationToken).ConfigureAwait(false);
            // Unknown historical references must not break startup or be guessed/rebound.
            if (restoreId is null && plan.Update is { CanApply: false }) return;
            await installer.InstallAsync(plan, cancellationToken).ConfigureAwait(false);
        }
    }
}

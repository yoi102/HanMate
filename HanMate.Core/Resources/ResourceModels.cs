namespace HanMate.Core.Resources;

public enum ResourceKind { Learning, Dictionary }
public enum ResourceDistribution { Bundled, External }
public enum ResourceOperationType { Install, Update, Remove, RestoreEntry, Disable, Enable }
public enum RetainedReason { Favorite, UserAudio, Draft, ExplicitKeep, Multiple }

public sealed record ResourceRegistration(
    Guid ResourceId,
    ResourceKind Kind,
    string Version,
    string DescriptorJson,
    string DescriptorSha256,
    string PayloadFingerprint,
    ResourceDistribution Distribution,
    bool IsPresent,
    bool IsEnabled,
    int Priority);

public sealed record InstalledResourceSnapshot(
    Guid ResourceId,
    ResourceKind Kind,
    string Version,
    string DescriptorJson,
    string DescriptorSha256,
    string PayloadFingerprint,
    ResourceDistribution Distribution,
    bool IsPresent,
    bool IsEnabled,
    int Priority,
    long RowRevision);

public sealed record ResourceEntryLink(Guid ResourceId, string EntryId, Guid ContentId);

public sealed record ResourceEntryOverride(
    Guid ResourceId,
    string EntryId,
    bool IsRemoved,
    DateTimeOffset UpdatedAtUtc);

public sealed record RetainedContentRecord(
    Guid ContentId,
    Guid SourceResourceId,
    string SourceEntryId,
    string SourceVersion,
    RetainedReason Reason,
    DateTimeOffset RetainedAtUtc);

public sealed record ResourceOperationCommit(
    Guid OperationId,
    Guid ResourceId,
    ResourceOperationType OperationType,
    long ExpectedDataEpoch,
    DateTimeOffset CommittedAtUtc,
    string ResultJson);

public sealed record SearchAliasValue(
    int AliasOrdinal,
    string HanziKey,
    string PinyinJoined,
    string PinyinSeparated,
    string ToneJoined,
    string ToneSeparated,
    string SyllablesJson,
    int? FrequencyRank,
    int IndexVersion);

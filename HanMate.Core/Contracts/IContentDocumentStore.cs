using HanMate.Core.Content;

namespace HanMate.Core.Contracts;

public sealed record ContentDocumentSnapshot(ContentDocument Document, long RowRevision, long MembershipRevision);

public interface IContentDocumentStore
{
    Task<ContentDocumentSnapshot?> GetAsync(Guid contentId, CancellationToken cancellationToken = default);
    Task<ContentDocumentSnapshot> SaveAsync(
        ContentDocument document,
        long expectedRowRevision,
        CancellationToken cancellationToken = default);
}

public sealed class RevisionConflictException(string entityType, string entityId, long expectedRevision)
    : Exception($"{entityType} '{entityId}' no longer has expected revision {expectedRevision}.")
{
    public string EntityType { get; } = entityType;
    public string EntityId { get; } = entityId;
    public long ExpectedRevision { get; } = expectedRevision;
}

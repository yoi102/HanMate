using HanMate.Core.Content;
using HanMate.Infrastructure.Content;

namespace HanMate.Infrastructure.Tests.Content;

public sealed class JsonContentDocumentCodecTests
{
    [Fact]
    public void Read_ValidatesAndLoadsARealPlanningFixture()
    {
        var codec = new JsonContentDocumentCodec(new ContentDocumentValidator());
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "grammar-content.json"));
        var document = codec.Read(stream);
        Assert.Equal(ContentKind.Grammar, document.Kind);
        Assert.NotNull(document.Grammar);
    }

    [Fact]
    public void Write_RejectsInvalidContentBeforeSerialization()
    {
        var codec = new JsonContentDocumentCodec(new ContentDocumentValidator());
        var invalid = new ContentDocument
        {
            Id = Guid.NewGuid(), Kind = ContentKind.Text, Origin = ContentOrigin.Personal,
            Title = " ", ContentRevision = 1, AnnotationRevision = 1, MetadataRevision = 1,
            Source = new ContentSource
            {
                SourceId = "test", Type = SourceType.Original, AuthorProvider = "test", Reference = "test",
                LicenseIdentifier = "private", PermissionNotes = "test", CanDistribute = false, CanShare = false,
                BackupPolicy = BackupPolicy.Allowed, ReviewStatus = ReviewStatus.Draft
            },
            CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        using var stream = new MemoryStream();
        Assert.Throws<ContentDocumentValidationException>(() => codec.Write(stream, invalid));
        Assert.Equal(0, stream.Length);
    }
}

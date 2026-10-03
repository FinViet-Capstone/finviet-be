using System;

namespace FinViet.Infrastructure.Persistence.Entities;

/// <summary>
/// The original uploaded file behind a global knowledge <see cref="RagDocument"/>, stored in
/// Postgres rather than on local disk because the hosting container's filesystem is ephemeral.
/// Kept in its own table so listing documents never loads file bytes.
/// </summary>
public partial class RagDocumentFile
{
    public Guid DocumentId { get; set; }

    public byte[] Content { get; set; } = null!;

    public string ContentType { get; set; } = null!;

    public int SizeBytes { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual RagDocument Document { get; set; } = null!;
}

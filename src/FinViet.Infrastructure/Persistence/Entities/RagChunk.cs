using System;
using Pgvector;

namespace FinViet.Infrastructure.Persistence.Entities;

/// <summary>
/// One embedded text chunk of a <see cref="RagDocument"/>. <see cref="Embedding"/> is a
/// 768-dim Gemini text-embedding-004 vector queried via pgvector cosine distance.
/// <see cref="CustomerId"/> is denormalized from the parent document for fast per-user
/// filtering (null = global chunk).
/// </summary>
public partial class RagChunk
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    public Guid? CustomerId { get; set; }

    public string Content { get; set; } = null!;

    public Vector Embedding { get; set; } = null!;

    public string? Metadata { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual RagDocument? Document { get; set; }
}

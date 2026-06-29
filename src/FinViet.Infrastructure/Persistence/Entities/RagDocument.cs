using System;
using System.Collections.Generic;

namespace FinViet.Infrastructure.Persistence.Entities;

/// <summary>
/// A retrievable source for the RAG chatbot. Either a GLOBAL knowledge document
/// (<see cref="CustomerId"/> null, e.g. an ingested finance PDF) or a PER-CUSTOMER
/// narrative (a weekly report or monthly summary). Chunked into <see cref="RagChunk"/>s.
/// </summary>
public partial class RagDocument
{
    public Guid Id { get; set; }

    /// <summary>Null = global knowledge doc visible to all customers.</summary>
    public Guid? CustomerId { get; set; }

    /// <summary>'pdf' | 'weekly_report' | 'monthly_summary'.</summary>
    public string SourceType { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string? Uri { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<RagChunk> Chunks { get; set; } = new List<RagChunk>();
}

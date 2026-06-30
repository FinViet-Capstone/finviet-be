using FinViet.Application.DTOs.Ai;

namespace FinViet.Application.Interfaces;

/// <summary>Semantic retrieval over the RAG corpus for a customer's chatbot query.
/// Returns top-K chunks scoped to the customer's own narratives PLUS global knowledge
/// documents (customer_id IS NULL), ranked by cosine similarity.</summary>
public interface IRagRetriever
{
    Task<IReadOnlyList<RagHit>> RetrieveAsync(
        Guid customerId, string query, int k = 5, CancellationToken cancellationToken = default);
}

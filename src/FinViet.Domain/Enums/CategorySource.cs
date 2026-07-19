namespace FinViet.Domain.Enums;

/// <summary>
/// Maps to Postgres enum <c>category_source</c> (system, request). <c>Request</c> marks a
/// <c>CustomerCategory</c> row the customer set themselves (no admin approval involved —
/// the former category-request approval flow was removed).
/// </summary>
public enum CategorySource
{
    System,
    Request
}

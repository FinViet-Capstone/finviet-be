namespace FinViet.Domain.Enums;

/// <summary>
/// Maps to the canonical PostgreSQL <c>category_source</c> enum. <c>Persona</c> marks a
/// customer-selected bucket override; <c>System</c> marks an automatically assigned row.
/// </summary>
public enum CategorySource
{
    Persona,
    System
}

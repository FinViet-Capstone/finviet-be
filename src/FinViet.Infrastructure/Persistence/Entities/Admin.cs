using System;
using System.Collections.Generic;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class Admin
{
    /// <summary>Maps to column <c>id</c> in the v3 schema.</summary>
    public Guid AdminId { get; set; }

    public string Username { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public string Email { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<CategoryCorrectionLog> CategoryCorrectionLogs { get; set; } = new List<CategoryCorrectionLog>();

    public virtual ICollection<SystemAnalytic> SystemAnalytics { get; set; } = new List<SystemAnalytic>();
}

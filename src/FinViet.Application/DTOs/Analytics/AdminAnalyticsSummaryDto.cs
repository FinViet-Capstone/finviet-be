namespace FinViet.Application.DTOs.Analytics;

public class AdminAnalyticsSummaryDto
{
    public int TotalCustomers { get; set; }

    public int ActiveCustomers { get; set; }

    /// <summary>Customers created in the trailing 30 days.</summary>
    public int NewCustomers { get; set; }

    public int TotalTransactions { get; set; }

    public int TotalWallets { get; set; }

    public int TotalBudgets { get; set; }

    public int FreeSubscriptions { get; set; }

    public int PremiumSubscriptions { get; set; }
}

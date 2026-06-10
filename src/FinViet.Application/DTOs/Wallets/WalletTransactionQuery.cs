using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FinViet.Application.DTOs.Wallets
{
    public class WalletTransactionQuery
    {
        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = 10;

        public DateTimeOffset? FromDate { get; set; }

    public DateTimeOffset? ToDate { get; set; }

    public Guid? CategoryId { get; set; }

    // INCOME, EXPENSE, TRANSFER, DEBT_PAYMENT
    public string? TransactionType { get; set; }

        // asc / desc
        public string SortOrder { get; set; } = "desc";
    }
}

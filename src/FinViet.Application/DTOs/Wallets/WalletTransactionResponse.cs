using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FinViet.Application.DTOs.Wallets
{
    public class WalletTransactionResponse
    {
        public Guid TransactionId { get; set; }

        public Guid WalletId { get; set; }

        public Guid? CategoryId { get; set; }

        public Guid? SourceId { get; set; }

        public Guid? BatchId { get; set; }

        public Guid? ReportId { get; set; }

        public string TransactionType { get; set; } = string.Empty;

        public decimal Amount { get; set; }

        public DateTimeOffset TransactionDate { get; set; }

        public string? Note { get; set; }
    }
}

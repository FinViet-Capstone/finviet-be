using FinViet.Infrastructure.ExternalServices.Ocr;
using Xunit;

namespace FinViet.Application.UnitTests;

public class GeminiReceiptOcrServiceTests
{
    [Fact]
    public void ParseReceipt_NotAReceipt_ReturnsNull()
    {
        var result = GeminiReceiptOcrService.ParseReceipt("""{"isReceipt": false}""");

        Assert.Null(result);
    }

    [Fact]
    public void ParseReceipt_ZeroAmount_ReturnsNull()
    {
        var result = GeminiReceiptOcrService.ParseReceipt(
            """{"isReceipt": true, "amount": 0, "merchant": "Circle K"}""");

        Assert.Null(result);
    }

    [Fact]
    public void ParseReceipt_ValidReceipt_MapsFields()
    {
        var result = GeminiReceiptOcrService.ParseReceipt("""
            {
              "isReceipt": true,
              "amount": 125000,
              "merchant": "Circle K",
              "description": "Nuoc uong, banh mi",
              "transactionDate": "2026-08-10",
              "amountConfidence": 0.98,
              "merchantConfidence": 0.91,
              "transactionDateConfidence": 0.87
            }
            """);

        Assert.NotNull(result);
        Assert.Equal(125000m, result!.Amount);
        Assert.Equal("EXPENSE", result.Type);
        Assert.Equal("Circle K", result.Merchant);
        Assert.Equal("Nuoc uong, banh mi", result.Description);
        Assert.Equal(new DateTime(2026, 8, 10), result.TransactionDate);
        Assert.Equal(0.98m, result.AmountConfidence);
        Assert.Equal(0.91m, result.MerchantConfidence);
        Assert.Equal(0.87m, result.TransactionDateConfidence);
    }

    [Fact]
    public void ParseReceipt_MissingDate_FallsBackToUtcNow()
    {
        var before = DateTime.UtcNow;

        var result = GeminiReceiptOcrService.ParseReceipt(
            """{"isReceipt": true, "amount": 50000, "merchant": "Cafe ABC"}""");

        var after = DateTime.UtcNow;

        Assert.NotNull(result);
        Assert.InRange(result!.TransactionDate, before, after);
        Assert.Equal(0m, result.TransactionDateConfidence);
    }

    [Fact]
    public void ParseReceipt_MissingDescription_FallsBackToMerchant()
    {
        var result = GeminiReceiptOcrService.ParseReceipt(
            """{"isReceipt": true, "amount": 30000, "merchant": "Highlands Coffee"}""");

        Assert.NotNull(result);
        Assert.Equal("Highlands Coffee", result!.Description);
    }

    [Fact]
    public void ParseReceipt_WrappedInMarkdownCodeFence_StillParses()
    {
        var raw = "```json\n{\"isReceipt\": true, \"amount\": 99000}\n```";

        var result = GeminiReceiptOcrService.ParseReceipt(raw);

        Assert.NotNull(result);
        Assert.Equal(99000m, result!.Amount);
    }

    [Fact]
    public void ParseReceipt_ConfidenceOutsideRange_IsClamped()
    {
        var result = GeminiReceiptOcrService.ParseReceipt("""
            {
              "isReceipt": true,
              "amount": 87000,
              "amountConfidence": 1.4,
              "merchantConfidence": "0.72",
              "transactionDate": "2018-03-12",
              "transactionDateConfidence": -0.2
            }
            """);

        Assert.NotNull(result);
        Assert.Equal(1m, result!.AmountConfidence);
        Assert.Equal(0.72m, result.MerchantConfidence);
        Assert.Equal(0m, result.TransactionDateConfidence);
    }

    [Fact]
    public void ParseReceipt_InvalidDate_FallsBackWithZeroConfidence()
    {
        var before = DateTime.UtcNow;
        var result = GeminiReceiptOcrService.ParseReceipt("""
            {
              "isReceipt": true,
              "amount": 87000,
              "transactionDate": "khong-doc-duoc",
              "transactionDateConfidence": 0.99
            }
            """);
        var after = DateTime.UtcNow;

        Assert.NotNull(result);
        Assert.InRange(result!.TransactionDate, before, after);
        Assert.Equal(0m, result.TransactionDateConfidence);
    }
}

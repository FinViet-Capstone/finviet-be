using FinViet.Infrastructure.ExternalServices.VNPay;

namespace FinViet.Application.UnitTests;

// Correctness of VNPayHashHelper is the crux of the whole VNPay integration — an outbound
// payment URL or inbound IPN with a wrong hash fails silently against VNPay's real servers, so
// this is fully covered offline against the documented algorithm rather than relying on a live
// sandbox call (not available in this environment — see context/current-feature.md).
public class VNPayHashHelperTests
{
    private const string Secret = "test_hash_secret_12345";

    [Fact]
    public void Sign_IsDeterministic_ForSameParamsAndSecret()
    {
        var vnpParams = new Dictionary<string, string>
        {
            ["vnp_Amount"] = "4900000",
            ["vnp_TxnRef"] = "SUB123",
            ["vnp_TmnCode"] = "ABC123",
        };

        var hash1 = VNPayHashHelper.Sign(vnpParams, Secret);
        var hash2 = VNPayHashHelper.Sign(vnpParams, Secret);

        Assert.Equal(hash1, hash2);
        Assert.Equal(128, hash1.Length); // HMAC-SHA512 -> 64 bytes -> 128 hex chars
    }

    [Fact]
    public void Sign_IsOrderIndependent_ParamsAreSortedBeforeSigning()
    {
        var inOrder = new Dictionary<string, string>
        {
            ["vnp_Amount"] = "4900000",
            ["vnp_TxnRef"] = "SUB123",
            ["vnp_TmnCode"] = "ABC123",
        };
        var reversed = new Dictionary<string, string>
        {
            ["vnp_TmnCode"] = "ABC123",
            ["vnp_TxnRef"] = "SUB123",
            ["vnp_Amount"] = "4900000",
        };

        Assert.Equal(VNPayHashHelper.Sign(inOrder, Secret), VNPayHashHelper.Sign(reversed, Secret));
    }

    [Fact]
    public void Sign_ExcludesSecureHashAndSecureHashType_EvenIfPresentInInput()
    {
        var withoutHashFields = new Dictionary<string, string> { ["vnp_Amount"] = "100" };
        var withHashFields = new Dictionary<string, string>
        {
            ["vnp_Amount"] = "100",
            ["vnp_SecureHash"] = "should_be_ignored",
            ["vnp_SecureHashType"] = "SHA512",
        };

        Assert.Equal(
            VNPayHashHelper.Sign(withoutHashFields, Secret),
            VNPayHashHelper.Sign(withHashFields, Secret));
    }

    [Fact]
    public void Sign_SkipsEmptyValues_MatchingVNPaysSortedListConvention()
    {
        var withoutEmpty = new Dictionary<string, string> { ["vnp_Amount"] = "100" };
        var withEmpty = new Dictionary<string, string> { ["vnp_Amount"] = "100", ["vnp_OrderInfo"] = "" };

        Assert.Equal(VNPayHashHelper.Sign(withoutEmpty, Secret), VNPayHashHelper.Sign(withEmpty, Secret));
    }

    [Fact]
    public void Verify_AcceptsAHashProducedBySign()
    {
        var vnpParams = new Dictionary<string, string>
        {
            ["vnp_Amount"] = "4900000",
            ["vnp_TxnRef"] = "SUB123",
            ["vnp_ResponseCode"] = "00",
        };
        var hash = VNPayHashHelper.Sign(vnpParams, Secret);
        var inbound = new Dictionary<string, string>(vnpParams) { ["vnp_SecureHash"] = hash };

        Assert.True(VNPayHashHelper.Verify(inbound, Secret));
    }

    [Fact]
    public void Verify_RejectsATamperedValue()
    {
        var vnpParams = new Dictionary<string, string> { ["vnp_Amount"] = "4900000" };
        var hash = VNPayHashHelper.Sign(vnpParams, Secret);
        var tampered = new Dictionary<string, string>
        {
            ["vnp_Amount"] = "9999999", // changed after signing
            ["vnp_SecureHash"] = hash,
        };

        Assert.False(VNPayHashHelper.Verify(tampered, Secret));
    }

    [Fact]
    public void Verify_RejectsAWrongSecret()
    {
        var vnpParams = new Dictionary<string, string> { ["vnp_Amount"] = "100" };
        var hash = VNPayHashHelper.Sign(vnpParams, Secret);
        var inbound = new Dictionary<string, string>(vnpParams) { ["vnp_SecureHash"] = hash };

        Assert.False(VNPayHashHelper.Verify(inbound, "a_different_secret"));
    }

    [Fact]
    public void Verify_ReturnsFalse_WhenSecureHashMissing()
    {
        var vnpParams = new Dictionary<string, string> { ["vnp_Amount"] = "100" };
        Assert.False(VNPayHashHelper.Verify(vnpParams, Secret));
    }

    [Fact]
    public void BuildSignedQueryString_AppendsSecureHashParam()
    {
        var vnpParams = new Dictionary<string, string> { ["vnp_Amount"] = "100", ["vnp_TxnRef"] = "SUB1" };

        var query = VNPayHashHelper.BuildSignedQueryString(vnpParams, Secret);

        Assert.Contains("vnp_Amount=100", query);
        Assert.Contains("vnp_TxnRef=SUB1", query);
        Assert.Contains("&vnp_SecureHash=", query);
        // The query string itself (minus the appended hash) must be exactly what gets signed.
        var expectedHash = VNPayHashHelper.Sign(vnpParams, Secret);
        Assert.EndsWith($"vnp_SecureHash={expectedHash}", query);
    }
}

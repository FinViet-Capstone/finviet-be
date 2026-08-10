using FinViet.Application.Common.Exceptions;
using FinViet.Application.Features.Transactions.Handlers;

namespace FinViet.Application.UnitTests;

public class TransactionRulesTests
{
    // TC-TXN-U01
    [Fact]
    public void EnsureEditableFieldsAllowed_SepayLinkedWallet_AmountProvided_ThrowsSyncedFieldsLocked()
    {
        var error = Assert.Throws<BusinessRuleException>(() =>
            TransactionRules.EnsureEditableFieldsAllowed("sepay_linked", amountProvided: true, merchantProvided: false, dateProvided: false));

        Assert.Equal("synced_transaction_fields_locked", error.Code);
    }

    // TC-TXN-U02
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void EnsureEditableFieldsAllowed_SepayLinkedWallet_AnyRestrictedFieldProvided_Throws(
        bool amountProvided, bool merchantProvided, bool dateProvided)
    {
        var error = Assert.Throws<BusinessRuleException>(() =>
            TransactionRules.EnsureEditableFieldsAllowed("SEPAY_LINKED", amountProvided, merchantProvided, dateProvided));

        Assert.Equal("synced_transaction_fields_locked", error.Code);
    }

    // TC-TXN-U03
    [Fact]
    public void EnsureEditableFieldsAllowed_SepayLinkedWallet_OnlyCategoryChanging_DoesNotThrow()
    {
        TransactionRules.EnsureEditableFieldsAllowed("sepay_linked", amountProvided: false, merchantProvided: false, dateProvided: false);
    }

    // TC-TXN-U04
    [Fact]
    public void EnsureEditableFieldsAllowed_BasicWallet_AllFieldsProvided_DoesNotThrow()
    {
        TransactionRules.EnsureEditableFieldsAllowed("basic", amountProvided: true, merchantProvided: true, dateProvided: true);
    }

    // TC-TXN-U05
    [Fact]
    public void EnsureEditableFieldsAllowed_UnknownWalletType_FieldsProvided_DoesNotThrow()
    {
        TransactionRules.EnsureEditableFieldsAllowed(null, amountProvided: true, merchantProvided: false, dateProvided: false);
    }
}

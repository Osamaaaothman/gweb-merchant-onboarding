using Gweb.Shared.Errors;

namespace Gweb.Domain.Applications;

/// <summary>
/// Metadata only -- never raw online-banking credentials (docs/00-PRODUCT-BRIEF.md,
/// "Settlement bank account metadata only"). The account number is masked at capture
/// down to last4, same as GovernmentIdentification/RegistrationIdentifier.
/// </summary>
public sealed record SettlementBankAccount
{
    public string AccountHolder { get; }
    public string BankName { get; }
    public string Last4 { get; }
    public DateOnly StatementDate { get; }

    private SettlementBankAccount(string accountHolder, string bankName, string last4, DateOnly statementDate)
    {
        AccountHolder = accountHolder;
        BankName = bankName;
        Last4 = last4;
        StatementDate = statementDate;
    }

    public static SettlementBankAccount FromFullAccountNumber(
        string accountHolder, string bankName, string fullAccountNumber, DateOnly statementDate)
    {
        var trimmed = fullAccountNumber.Trim();
        if (trimmed.Length < 4)
        {
            throw new ValidationException("settlementBankAccount.accountNumber must be at least 4 characters.");
        }
        return new SettlementBankAccount(accountHolder, bankName, trimmed[^4..], statementDate);
    }

    public static SettlementBankAccount FromMaskedLast4(
        string accountHolder, string bankName, string last4, DateOnly statementDate) =>
        new(accountHolder, bankName, last4, statementDate);
}

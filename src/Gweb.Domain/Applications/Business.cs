using Gweb.Shared.Errors;

namespace Gweb.Domain.Applications;

/// <summary>
/// The business / legal entity (brief §3.2). Same incremental-PATCH shape as
/// Applicant. Ownership-percentage cross-checking against the applicant's own
/// percentage happens one layer up, in ApplicationService, since it needs both
/// entities -- see docs/03-ARCHITECTURE-RULES.md's "domain -> nothing" rule: Business
/// alone cannot know about Applicant.
/// </summary>
public sealed class Business
{
    private const int MaxNameLength = 200;
    private const int MaxDescriptionLength = 2000;

    public Guid ApplicationId { get; }
    public string? LegalBusinessName { get; private set; }
    public string? DbaName { get; private set; }
    public EntityType? EntityType { get; private set; }
    public string? FormationCountry { get; private set; }
    public string? FormationState { get; private set; }
    public RegistrationIdentifier? RegistrationIdentifier { get; private set; }
    public Address? RegisteredAddress { get; private set; }
    public Address? OperatingAddress { get; private set; }
    public string? WebsiteUrl { get; private set; }
    public string? BusinessDescription { get; private set; }
    public DateOnly? BusinessStartDate { get; private set; }
    public VolumeProfile? VolumeProfile { get; private set; }
    public IReadOnlyList<BeneficialOwner> BeneficialOwners { get; private set; } = [];
    public SettlementBankAccount? SettlementBankAccount { get; private set; }
    public string? ExistingProcessor { get; private set; }

    public long Version { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string CreatedBy { get; }
    public string CorrelationId { get; private set; }

    private Business(Guid applicationId, DateTimeOffset now, string createdBy, string correlationId)
    {
        ApplicationId = applicationId;
        CreatedAt = now;
        UpdatedAt = now;
        CreatedBy = createdBy;
        CorrelationId = correlationId;
        // 0 is a sentinel meaning "not yet persisted" -- see Applicant for the same pattern.
        Version = 0;
    }

    public static Business CreateEmpty(Guid applicationId, DateTimeOffset now, string createdBy, string correlationId) =>
        new(applicationId, now, createdBy, correlationId);

    /// <summary>An independent copy of the current state -- see Applicant.Snapshot()
    /// for why an in-memory repository needs this.</summary>
    public Business Snapshot() => Rehydrate(
        ApplicationId, LegalBusinessName, DbaName, EntityType, FormationCountry, FormationState,
        RegistrationIdentifier, RegisteredAddress, OperatingAddress, WebsiteUrl, BusinessDescription,
        BusinessStartDate, VolumeProfile, BeneficialOwners, SettlementBankAccount, ExistingProcessor,
        Version, CreatedAt, UpdatedAt, CreatedBy, CorrelationId);

    public static Business Rehydrate(
        Guid applicationId,
        string? legalBusinessName,
        string? dbaName,
        EntityType? entityType,
        string? formationCountry,
        string? formationState,
        RegistrationIdentifier? registrationIdentifier,
        Address? registeredAddress,
        Address? operatingAddress,
        string? websiteUrl,
        string? businessDescription,
        DateOnly? businessStartDate,
        VolumeProfile? volumeProfile,
        IReadOnlyList<BeneficialOwner> beneficialOwners,
        SettlementBankAccount? settlementBankAccount,
        string? existingProcessor,
        long version,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        string createdBy,
        string correlationId)
    {
        var business = new Business(applicationId, createdAt, createdBy, correlationId)
        {
            LegalBusinessName = legalBusinessName,
            DbaName = dbaName,
            EntityType = entityType,
            FormationCountry = formationCountry,
            FormationState = formationState,
            RegistrationIdentifier = registrationIdentifier,
            RegisteredAddress = registeredAddress,
            OperatingAddress = operatingAddress,
            WebsiteUrl = websiteUrl,
            BusinessDescription = businessDescription,
            BusinessStartDate = businessStartDate,
            VolumeProfile = volumeProfile,
            BeneficialOwners = beneficialOwners,
            SettlementBankAccount = settlementBankAccount,
            ExistingProcessor = existingProcessor,
            Version = version,
            UpdatedAt = updatedAt,
        };
        return business;
    }

    /// <summary>Sum of every beneficial owner's ownership percentage on this business
    /// record alone -- does not include the applicant's own percentage, which lives on
    /// a different entity. See ApplicationService for the combined check.</summary>
    public decimal BeneficialOwnersOwnershipTotal() => BeneficialOwners.Sum(o => o.OwnershipPercentage);

    public void ApplyUpdate(BusinessUpdate update, DateTimeOffset now)
    {
        var errors = new List<FieldValidationError>();

        ValidateRequiredText(update.LegalBusinessName, "legalBusinessName", MaxNameLength, errors);
        ValidateOptionalText(update.DbaName, "dbaName", MaxNameLength, errors);
        ValidateRequiredText(update.FormationCountry, "formationCountry", MaxNameLength, errors);
        ValidateOptionalText(update.FormationState, "formationState", MaxNameLength, errors);
        ValidateAddress(update.RegisteredAddress, "registeredAddress", errors);
        ValidateAddress(update.OperatingAddress, "operatingAddress", errors);
        ValidateWebsiteUrl(update.WebsiteUrl, errors);
        ValidateRequiredText(update.BusinessDescription, "businessDescription", MaxDescriptionLength, errors);
        ValidateBusinessStartDate(update.BusinessStartDate, errors);
        ValidateVolumeProfile(update.VolumeProfile, errors);
        ValidateBeneficialOwners(update.BeneficialOwners, errors);
        ValidateOptionalText(update.ExistingProcessor, "existingProcessor", MaxNameLength, errors);

        if (errors.Count > 0)
        {
            throw new ValidationException("Business validation failed.", errors);
        }

        if (update.LegalBusinessName is not null)
        {
            LegalBusinessName = update.LegalBusinessName.Trim();
        }
        if (update.DbaName is not null)
        {
            DbaName = update.DbaName.Trim();
        }
        if (update.EntityType is not null)
        {
            EntityType = update.EntityType;
        }
        if (update.FormationCountry is not null)
        {
            FormationCountry = update.FormationCountry.Trim();
        }
        if (update.FormationState is not null)
        {
            FormationState = update.FormationState.Trim();
        }
        if (update.RegistrationIdentifier is not null)
        {
            RegistrationIdentifier = Applications.RegistrationIdentifier.FromFullValue(
                update.RegistrationIdentifier.Type, update.RegistrationIdentifier.Value);
        }
        if (update.RegisteredAddress is not null)
        {
            RegisteredAddress = update.RegisteredAddress;
        }
        if (update.OperatingAddress is not null)
        {
            OperatingAddress = update.OperatingAddress;
        }
        if (update.WebsiteUrl is not null)
        {
            WebsiteUrl = update.WebsiteUrl.Trim();
        }
        if (update.BusinessDescription is not null)
        {
            BusinessDescription = update.BusinessDescription.Trim();
        }
        if (update.BusinessStartDate is not null)
        {
            BusinessStartDate = update.BusinessStartDate;
        }
        if (update.VolumeProfile is not null)
        {
            VolumeProfile = update.VolumeProfile;
        }
        if (update.BeneficialOwners is not null)
        {
            BeneficialOwners = update.BeneficialOwners;
        }
        if (update.SettlementBankAccount is not null)
        {
            var bankInput = update.SettlementBankAccount;
            SettlementBankAccount = Applications.SettlementBankAccount.FromFullAccountNumber(
                bankInput.AccountHolder, bankInput.BankName, bankInput.AccountNumber, bankInput.StatementDate);
        }
        if (update.ExistingProcessor is not null)
        {
            ExistingProcessor = update.ExistingProcessor.Trim();
        }

        UpdatedAt = now;
        Version += 1;
    }

    private static void ValidateRequiredText(string? value, string field, int maxLength, List<FieldValidationError> errors)
    {
        if (value is null)
        {
            return;
        }
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            errors.Add(new FieldValidationError(field, "must not be empty"));
        }
        else if (trimmed.Length > maxLength)
        {
            errors.Add(new FieldValidationError(field, $"must be at most {maxLength} characters"));
        }
    }

    private static void ValidateOptionalText(string? value, string field, int maxLength, List<FieldValidationError> errors)
    {
        if (value is not null && value.Trim().Length > maxLength)
        {
            errors.Add(new FieldValidationError(field, $"must be at most {maxLength} characters"));
        }
    }

    private static void ValidateAddress(Address? value, string field, List<FieldValidationError> errors)
    {
        if (value is null)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(value.Line1))
        {
            errors.Add(new FieldValidationError($"{field}.line1", "must not be empty"));
        }
        if (string.IsNullOrWhiteSpace(value.City))
        {
            errors.Add(new FieldValidationError($"{field}.city", "must not be empty"));
        }
        if (string.IsNullOrWhiteSpace(value.State))
        {
            errors.Add(new FieldValidationError($"{field}.state", "must not be empty"));
        }
        if (string.IsNullOrWhiteSpace(value.PostalCode))
        {
            errors.Add(new FieldValidationError($"{field}.postalCode", "must not be empty"));
        }
        if (string.IsNullOrWhiteSpace(value.Country))
        {
            errors.Add(new FieldValidationError($"{field}.country", "must not be empty"));
        }
    }

    private static void ValidateWebsiteUrl(string? value, List<FieldValidationError> errors)
    {
        if (value is null)
        {
            return;
        }
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            errors.Add(new FieldValidationError("websiteUrl", "must not be empty"));
            return;
        }
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add(new FieldValidationError("websiteUrl", "must be a valid absolute http(s) URL"));
        }
    }

    private static void ValidateBusinessStartDate(DateOnly? value, List<FieldValidationError> errors)
    {
        if (value is null)
        {
            return;
        }
        if (value > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            errors.Add(new FieldValidationError("businessStartDate", "must not be in the future"));
        }
    }

    private static void ValidateVolumeProfile(VolumeProfile? value, List<FieldValidationError> errors)
    {
        if (value is null)
        {
            return;
        }
        if (value.ExpectedAnnualCardVolume < 0)
        {
            errors.Add(new FieldValidationError("volumeProfile.expectedAnnualCardVolume", "must not be negative"));
        }
        if (value.AverageTicket < 0)
        {
            errors.Add(new FieldValidationError("volumeProfile.averageTicket", "must not be negative"));
        }
        if (value.HighestTicket < 0)
        {
            errors.Add(new FieldValidationError("volumeProfile.highestTicket", "must not be negative"));
        }
        if (value.HighestTicket < value.AverageTicket)
        {
            errors.Add(new FieldValidationError("volumeProfile.highestTicket", "must not be less than averageTicket"));
        }
        if (value.MonthlyTransactionCount < 0)
        {
            errors.Add(new FieldValidationError("volumeProfile.monthlyTransactionCount", "must not be negative"));
        }
        if (value.CardPresentPercentage is < 0 or > 100)
        {
            errors.Add(new FieldValidationError("volumeProfile.cardPresentPercentage", "must be between 0 and 100"));
        }
        if (value.EcommercePercentage is < 0 or > 100)
        {
            errors.Add(new FieldValidationError("volumeProfile.ecommercePercentage", "must be between 0 and 100"));
        }
    }

    private static void ValidateBeneficialOwners(IReadOnlyList<BeneficialOwner>? owners, List<FieldValidationError> errors)
    {
        if (owners is null)
        {
            return;
        }
        for (var i = 0; i < owners.Count; i++)
        {
            var owner = owners[i];
            if (string.IsNullOrWhiteSpace(owner.Name))
            {
                errors.Add(new FieldValidationError($"beneficialOwners[{i}].name", "must not be empty"));
            }
            if (string.IsNullOrWhiteSpace(owner.RoleTitle))
            {
                errors.Add(new FieldValidationError($"beneficialOwners[{i}].roleTitle", "must not be empty"));
            }
            if (owner.OwnershipPercentage is < 0 or > 100)
            {
                errors.Add(new FieldValidationError($"beneficialOwners[{i}].ownershipPercentage", "must be between 0 and 100"));
            }
        }

        var total = owners.Sum(o => o.OwnershipPercentage);
        if (total > 100)
        {
            errors.Add(new FieldValidationError("beneficialOwners", $"combined ownership percentage ({total}) must not exceed 100"));
        }
    }
}

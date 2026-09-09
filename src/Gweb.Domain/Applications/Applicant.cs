using System.Text.RegularExpressions;
using Gweb.Shared.Errors;

namespace Gweb.Domain.Applications;

/// <summary>
/// The primary applicant / control person (brief §3.1). Every field starts null and
/// is filled in incrementally via PATCH -- this is a multi-step, save/resume form, not
/// a single create-with-everything call. See CompletenessChecker for what "done" means.
/// </summary>
public sealed partial class Applicant
{
    private const int MaxNameLength = 100;

    public Guid ApplicationId { get; }
    public string? LegalFirstName { get; private set; }
    public string? LegalMiddleName { get; private set; }
    public string? LegalLastName { get; private set; }
    public DateOnly? DateOfBirth { get; private set; }
    public Address? ResidentialAddress { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? RoleTitle { get; private set; }
    public decimal? OwnershipPercentage { get; private set; }
    public GovernmentIdentification? GovernmentId { get; private set; }
    public DateTimeOffset? ConsentTimestamp { get; private set; }
    public string? ConsentVersion { get; private set; }

    public long Version { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string CreatedBy { get; }
    public string CorrelationId { get; private set; }

    private Applicant(Guid applicationId, DateTimeOffset now, string createdBy, string correlationId)
    {
        ApplicationId = applicationId;
        CreatedAt = now;
        UpdatedAt = now;
        CreatedBy = createdBy;
        CorrelationId = correlationId;
        // 0 is a sentinel meaning "not yet persisted" -- ApplyUpdate always increments,
        // so the first successful update lands at version 1, matching a freshly
        // created row. SaveAsync(expectedVersion: 0) means "create", not "update".
        Version = 0;
    }

    public static Applicant CreateEmpty(Guid applicationId, DateTimeOffset now, string createdBy, string correlationId) =>
        new(applicationId, now, createdBy, correlationId);

    /// <summary>
    /// An independent copy of the current state. Applicant is a mutable reference
    /// type; a repository that stores the object itself (rather than a serialized
    /// copy, e.g. an in-memory test double) must snapshot it here -- otherwise a
    /// caller mutating its own reference after saving would silently mutate the
    /// "persisted" copy too. The real DynamoDB repository doesn't need this (it
    /// serializes to AttributeValues, which is inherently a copy).
    /// </summary>
    public Applicant Snapshot() => Rehydrate(
        ApplicationId, LegalFirstName, LegalMiddleName, LegalLastName, DateOfBirth, ResidentialAddress,
        Email, Phone, RoleTitle, OwnershipPercentage, GovernmentId, ConsentTimestamp, ConsentVersion,
        Version, CreatedAt, UpdatedAt, CreatedBy, CorrelationId);

    public static Applicant Rehydrate(
        Guid applicationId,
        string? legalFirstName,
        string? legalMiddleName,
        string? legalLastName,
        DateOnly? dateOfBirth,
        Address? residentialAddress,
        string? email,
        string? phone,
        string? roleTitle,
        decimal? ownershipPercentage,
        GovernmentIdentification? governmentId,
        DateTimeOffset? consentTimestamp,
        string? consentVersion,
        long version,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        string createdBy,
        string correlationId)
    {
        var applicant = new Applicant(applicationId, createdAt, createdBy, correlationId)
        {
            LegalFirstName = legalFirstName,
            LegalMiddleName = legalMiddleName,
            LegalLastName = legalLastName,
            DateOfBirth = dateOfBirth,
            ResidentialAddress = residentialAddress,
            Email = email,
            Phone = phone,
            RoleTitle = roleTitle,
            OwnershipPercentage = ownershipPercentage,
            GovernmentId = governmentId,
            ConsentTimestamp = consentTimestamp,
            ConsentVersion = consentVersion,
            Version = version,
            UpdatedAt = updatedAt,
        };
        return applicant;
    }

    /// <summary>
    /// Validates every field present in <paramref name="update"/> and, only if all of
    /// them are individually valid, applies the whole batch and bumps the version.
    /// Never applies a partial batch -- either the whole PATCH succeeds or none of it
    /// does.
    /// </summary>
    public void ApplyUpdate(ApplicantUpdate update, DateTimeOffset now)
    {
        var errors = new List<FieldValidationError>();

        ValidateName(update.LegalFirstName, "legalFirstName", errors);
        ValidateName(update.LegalMiddleName, "legalMiddleName", errors, required: false);
        ValidateName(update.LegalLastName, "legalLastName", errors);
        ValidateDateOfBirth(update.DateOfBirth, errors);
        ValidateAddress(update.ResidentialAddress, errors);
        ValidateEmail(update.Email, errors);
        ValidatePhone(update.Phone, errors);
        ValidateRoleTitle(update.RoleTitle, errors);
        ValidateOwnershipPercentage(update.OwnershipPercentage, errors);
        ValidateConsentVersion(update.ConsentVersion, errors);

        if (errors.Count > 0)
        {
            throw new ValidationException("Applicant validation failed.", errors);
        }

        if (update.LegalFirstName is not null)
        {
            LegalFirstName = update.LegalFirstName.Trim();
        }
        if (update.LegalMiddleName is not null)
        {
            LegalMiddleName = update.LegalMiddleName.Trim();
        }
        if (update.LegalLastName is not null)
        {
            LegalLastName = update.LegalLastName.Trim();
        }
        if (update.DateOfBirth is not null)
        {
            DateOfBirth = update.DateOfBirth;
        }
        if (update.ResidentialAddress is not null)
        {
            ResidentialAddress = update.ResidentialAddress;
        }
        if (update.Email is not null)
        {
            Email = update.Email.Trim();
        }
        if (update.Phone is not null)
        {
            Phone = update.Phone.Trim();
        }
        if (update.RoleTitle is not null)
        {
            RoleTitle = update.RoleTitle.Trim();
        }
        if (update.OwnershipPercentage is not null)
        {
            OwnershipPercentage = update.OwnershipPercentage;
        }
        if (update.GovernmentId is not null)
        {
            GovernmentId = GovernmentIdentification.FromFullNumber(update.GovernmentId.Type, update.GovernmentId.Number);
        }
        if (update.ConsentVersion is not null)
        {
            ConsentVersion = update.ConsentVersion.Trim();
            ConsentTimestamp = now;
        }

        UpdatedAt = now;
        Version += 1;
    }

    private static void ValidateName(string? value, string field, List<FieldValidationError> errors, bool required = true)
    {
        if (value is null)
        {
            return;
        }
        var trimmed = value.Trim();
        if (required && trimmed.Length == 0)
        {
            errors.Add(new FieldValidationError(field, "must not be empty"));
        }
        else if (trimmed.Length > MaxNameLength)
        {
            errors.Add(new FieldValidationError(field, $"must be at most {MaxNameLength} characters"));
        }
    }

    private static void ValidateDateOfBirth(DateOnly? value, List<FieldValidationError> errors)
    {
        if (value is null)
        {
            return;
        }
        if (value >= DateOnly.FromDateTime(DateTime.UtcNow))
        {
            errors.Add(new FieldValidationError("dateOfBirth", "must be in the past"));
        }
    }

    private static void ValidateAddress(Address? value, List<FieldValidationError> errors)
    {
        if (value is null)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(value.Line1))
        {
            errors.Add(new FieldValidationError("residentialAddress.line1", "must not be empty"));
        }
        if (string.IsNullOrWhiteSpace(value.City))
        {
            errors.Add(new FieldValidationError("residentialAddress.city", "must not be empty"));
        }
        if (string.IsNullOrWhiteSpace(value.State))
        {
            errors.Add(new FieldValidationError("residentialAddress.state", "must not be empty"));
        }
        if (string.IsNullOrWhiteSpace(value.PostalCode))
        {
            errors.Add(new FieldValidationError("residentialAddress.postalCode", "must not be empty"));
        }
        if (string.IsNullOrWhiteSpace(value.Country))
        {
            errors.Add(new FieldValidationError("residentialAddress.country", "must not be empty"));
        }
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    private static void ValidateEmail(string? value, List<FieldValidationError> errors)
    {
        if (value is null)
        {
            return;
        }
        if (!EmailPattern().IsMatch(value.Trim()))
        {
            errors.Add(new FieldValidationError("email", "must be a valid email address"));
        }
    }

    [GeneratedRegex(@"^[0-9+()\-\s]{7,20}$")]
    private static partial Regex PhonePattern();

    private static void ValidatePhone(string? value, List<FieldValidationError> errors)
    {
        if (value is null)
        {
            return;
        }
        if (!PhonePattern().IsMatch(value.Trim()))
        {
            errors.Add(new FieldValidationError("phone", "must be 7-20 characters of digits, spaces, +, -, ( or )"));
        }
    }

    private static void ValidateRoleTitle(string? value, List<FieldValidationError> errors)
    {
        if (value is not null && value.Trim().Length == 0)
        {
            errors.Add(new FieldValidationError("roleTitle", "must not be empty"));
        }
    }

    private static void ValidateOwnershipPercentage(decimal? value, List<FieldValidationError> errors)
    {
        if (value is null)
        {
            return;
        }
        if (value < 0 || value > 100)
        {
            errors.Add(new FieldValidationError("ownershipPercentage", "must be between 0 and 100"));
        }
    }

    private static void ValidateConsentVersion(string? value, List<FieldValidationError> errors)
    {
        if (value is not null && value.Trim().Length == 0)
        {
            errors.Add(new FieldValidationError("consentVersion", "must not be empty"));
        }
    }
}

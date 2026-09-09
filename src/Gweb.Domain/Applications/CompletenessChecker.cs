namespace Gweb.Domain.Applications;

/// <summary>
/// What "ready to submit" means, per brief §3.1/§3.2's required fields. Pure function,
/// no I/O -- Phase 9's submission gate reuses this exact logic rather than
/// re-deriving it, so "what's missing" can never drift between the review screen and
/// the actual submit block.
/// </summary>
public static class CompletenessChecker
{
    public static CompletenessResult Check(Applicant? applicant, Business? business)
    {
        return new CompletenessResult(MissingApplicantFields(applicant), MissingBusinessFields(business));
    }

    private static List<string> MissingApplicantFields(Applicant? applicant)
    {
        var missing = new List<string>();
        if (applicant is null)
        {
            return ["legalFirstName", "legalLastName", "dateOfBirth", "residentialAddress", "email", "phone", "roleTitle", "governmentId", "consent"];
        }

        if (string.IsNullOrWhiteSpace(applicant.LegalFirstName))
        {
            missing.Add("legalFirstName");
        }
        if (string.IsNullOrWhiteSpace(applicant.LegalLastName))
        {
            missing.Add("legalLastName");
        }
        if (applicant.DateOfBirth is null)
        {
            missing.Add("dateOfBirth");
        }
        if (applicant.ResidentialAddress is null)
        {
            missing.Add("residentialAddress");
        }
        if (string.IsNullOrWhiteSpace(applicant.Email))
        {
            missing.Add("email");
        }
        if (string.IsNullOrWhiteSpace(applicant.Phone))
        {
            missing.Add("phone");
        }
        if (string.IsNullOrWhiteSpace(applicant.RoleTitle))
        {
            missing.Add("roleTitle");
        }
        if (applicant.GovernmentId is null)
        {
            missing.Add("governmentId");
        }
        if (applicant.ConsentVersion is null)
        {
            missing.Add("consent");
        }

        return missing;
    }

    private static List<string> MissingBusinessFields(Business? business)
    {
        var missing = new List<string>();
        if (business is null)
        {
            return ["legalBusinessName", "entityType", "formationCountry", "registrationIdentifier", "registeredAddress", "operatingAddress", "businessDescription", "businessStartDate", "volumeProfile", "settlementBankAccount"];
        }

        if (string.IsNullOrWhiteSpace(business.LegalBusinessName))
        {
            missing.Add("legalBusinessName");
        }
        if (business.EntityType is null)
        {
            missing.Add("entityType");
        }
        if (string.IsNullOrWhiteSpace(business.FormationCountry))
        {
            missing.Add("formationCountry");
        }
        if (business.RegistrationIdentifier is null)
        {
            missing.Add("registrationIdentifier");
        }
        if (business.RegisteredAddress is null)
        {
            missing.Add("registeredAddress");
        }
        if (business.OperatingAddress is null)
        {
            missing.Add("operatingAddress");
        }
        if (string.IsNullOrWhiteSpace(business.BusinessDescription))
        {
            missing.Add("businessDescription");
        }
        if (business.BusinessStartDate is null)
        {
            missing.Add("businessStartDate");
        }
        if (business.VolumeProfile is null)
        {
            missing.Add("volumeProfile");
        }
        if (business.SettlementBankAccount is null)
        {
            missing.Add("settlementBankAccount");
        }

        return missing;
    }
}

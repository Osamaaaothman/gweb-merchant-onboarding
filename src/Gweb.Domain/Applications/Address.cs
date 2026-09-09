namespace Gweb.Domain.Applications;

public sealed record Address(string Line1, string? Line2, string City, string State, string PostalCode, string Country);

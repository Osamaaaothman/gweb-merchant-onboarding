namespace Gweb.Shared.Errors;

/// <summary>
/// Base of the domain error taxonomy. One mapper (HttpErrorMapper) turns these into
/// HTTP responses — endpoints never build error responses by hand. Named *Exception
/// throughout (not *Error) to follow the .NET convention that Exception-derived types
/// end in "Exception" (CA1710).
/// </summary>
public abstract class DomainException(string message, object? details = null) : Exception(message)
{
    public abstract string Code { get; }
    public abstract int HttpStatus { get; }
    public abstract bool Retryable { get; }
    public object? Details { get; } = details;
}

public sealed class ValidationException(string message, object? details = null) : DomainException(message, details)
{
    public override string Code => "VALIDATION_FAILED";
    public override int HttpStatus => 400;
    public override bool Retryable => false;
}

public sealed class NotFoundException(string message, object? details = null) : DomainException(message, details)
{
    public override string Code => "NOT_FOUND";
    public override int HttpStatus => 404;
    public override bool Retryable => false;
}

/// <summary>
/// Optimistic-concurrency / idempotency conflicts — a version mismatch, a replayed
/// request with different content, an illegal state transition.
/// </summary>
public sealed class ConflictException(string message, object? details = null) : DomainException(message, details)
{
    public override string Code => "CONFLICT";
    public override int HttpStatus => 409;
    public override bool Retryable => false;
}

public sealed class DependencyTimeoutException(string message, object? details = null) : DomainException(message, details)
{
    public override string Code => "DEPENDENCY_TIMEOUT";
    public override int HttpStatus => 504;
    public override bool Retryable => true;
}

public sealed class DependencyUnavailableException(string message, object? details = null) : DomainException(message, details)
{
    public override string Code => "DEPENDENCY_UNAVAILABLE";
    public override int HttpStatus => 503;
    public override bool Retryable => true;
}

/// <summary>
/// A request that is well-formed but violates business/risk policy (e.g. a restricted
/// MCC). Distinct from ValidationException, which is about malformed input.
/// </summary>
public sealed class PolicyViolationException(string message, object? details = null) : DomainException(message, details)
{
    public override string Code => "POLICY_VIOLATION";
    public override int HttpStatus => 422;
    public override bool Retryable => false;
}

/// <summary>
/// Deployment/ops misconfiguration (missing required env var, invalid config value).
/// Deliberately NOT a DomainException: it is never a client-facing response, only a
/// cold-start failure that should surface loudly in CloudWatch.
/// </summary>
public sealed class ConfigurationException(string message) : Exception(message);

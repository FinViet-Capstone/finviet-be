namespace FinViet.Application.Common.Exceptions;

/// <summary>Thrown when a requested resource is not found (→ HTTP 404).</summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
    public NotFoundException(string name, object key) : base($"{name} with key '{key}' was not found.") { }
}

/// <summary>Thrown when a request conflicts with existing data (→ HTTP 409).</summary>
public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}

/// <summary>Thrown when authentication fails (→ HTTP 401).</summary>
public class UnauthorizedException : Exception
{
    public UnauthorizedException(string message = "Unauthorized.") : base(message) { }
}

/// <summary>Thrown when the caller does not have permission (→ HTTP 403).</summary>
public class ForbiddenException : Exception
{
    public ForbiddenException(string message = "Access denied.") : base(message) { }
}

/// <summary>General bad request error (→ HTTP 400).</summary>
public class BadRequestException : Exception
{
    public BadRequestException(string message) : base(message) { }
}

/// <summary>
/// Thrown when a business rule is violated (→ HTTP 422). Carries an optional
/// machine-readable <see cref="Code"/> (e.g. "last_wallet") that the FE maps to a VI message.
/// </summary>
public class BusinessRuleException : Exception
{
    public string? Code { get; }

    public BusinessRuleException(string message, string? code = null) : base(message)
    {
        Code = code;
    }
}

/// <summary>Failure while calling a required upstream service (HTTP 502).</summary>
public class ExternalServiceException : Exception
{
    public string? Code { get; }

    public ExternalServiceException(string message, string? code = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }
}

/// <summary>Required external integration is not configured (HTTP 503).</summary>
public class IntegrationUnavailableException : Exception
{
    public string? Code { get; }

    public IntegrationUnavailableException(string message, string? code = null)
        : base(message)
    {
        Code = code;
    }
}

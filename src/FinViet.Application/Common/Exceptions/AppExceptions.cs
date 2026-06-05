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

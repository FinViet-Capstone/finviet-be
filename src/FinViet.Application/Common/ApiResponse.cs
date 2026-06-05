namespace FinViet.Application.Common;

/// <summary>Standardized API response envelope.</summary>
public class ApiResponse<T>
{
    public bool Success { get; init; }
    public string? Message { get; init; }
    public T? Data { get; init; }

    public static ApiResponse<T> Ok(T data, string? message = null) =>
        new() { Success = true, Data = data, Message = message };

    public static ApiResponse<T> Fail(string message) =>
        new() { Success = false, Message = message };
}

public class ApiResponse : ApiResponse<object>
{
    public static ApiResponse Ok(string? message = null) =>
        new() { Success = true, Message = message };

    public new static ApiResponse Fail(string message) =>
        new() { Success = false, Message = message };
}

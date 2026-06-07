using FinViet.Application.Common;
using FinViet.Application.Common.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Hosting;
using System.Text.Json;

namespace FinViet.Api.Middlewares;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IWebHostEnvironment _env;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IWebHostEnvironment env)
    {
        _next   = next;
        _logger = logger;
        _env    = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            if (ex is BadRequestException
                or NotFoundException
                or ConflictException
                or UnauthorizedException
                or ForbiddenException
                or ValidationException
                or FinViet.Application.Exceptions.NotFoundException
                or FinViet.Application.Exceptions.ValidationException)
            {
                _logger.LogWarning(
                    ex,
                    "Handled exception while processing {Method} {Path}",
                    context.Request.Method,
                    context.Request.Path);
            }
            else
            {
                _logger.LogError(
                    ex,
                    "Unhandled exception while processing {Method} {Path}",
                    context.Request.Method,
                    context.Request.Path);
            }

            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        (int statusCode, string message, object? errors) = exception switch
        {
            ValidationException ve => (
                StatusCodes.Status400BadRequest,
                "Validation failed.",
                (object?)ve.Errors
                  .GroupBy(e => e.PropertyName)
                  .ToDictionary(
                      g => g.Key,
                      g => g.Select(e => e.ErrorMessage).ToArray())),

            FinViet.Application.Exceptions.ValidationException ve2 =>
                (StatusCodes.Status400BadRequest, ve2.Message, null),

            BadRequestException bre   => (StatusCodes.Status400BadRequest,   bre.Message, null),
            NotFoundException nfe     => (StatusCodes.Status404NotFound,      nfe.Message, null),
            FinViet.Application.Exceptions.NotFoundException nfe2 =>
                                         (StatusCodes.Status404NotFound,      nfe2.Message, null),
            ConflictException ce      => (StatusCodes.Status409Conflict,      ce.Message,  null),
            UnauthorizedException ue  => (StatusCodes.Status401Unauthorized,  ue.Message,  null),
            ForbiddenException fe     => (StatusCodes.Status403Forbidden,     fe.Message,  null),
            UnauthorizedAccessException uae =>
                                         (StatusCodes.Status401Unauthorized,  uae.Message, null),
            _                         => (StatusCodes.Status500InternalServerError,
                                          _env.IsDevelopment()
                                              ? exception.Message
                                              : "An unexpected error occurred.",
                                          _env.IsDevelopment()
                                              ? new { stackTrace = exception.StackTrace, type = exception.GetType().Name }
                                              : null)
        };

        context.Response.StatusCode = statusCode;

        var response = new
        {
            success = false,
            message,
            errors
        };

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }
}

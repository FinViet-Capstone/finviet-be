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
                or BusinessRuleException
                or ExternalServiceException
                or IntegrationUnavailableException
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

        (int statusCode, string message, object? errors, string? code) = exception switch
        {
            ValidationException ve => (
                StatusCodes.Status400BadRequest,
                "Validation failed.",
                (object?)ve.Errors
                  .GroupBy(e => e.PropertyName)
                  .ToDictionary(
                      g => g.Key,
                      g => g.Select(e => e.ErrorMessage).ToArray()),
                (string?)null),

            FinViet.Application.Exceptions.ValidationException ve2 =>
                (StatusCodes.Status400BadRequest, ve2.Message, null, null),

            // Business rule violation → 422 + machine-readable code (FE maps to VI message).
            BusinessRuleException bce => (StatusCodes.Status422UnprocessableEntity, bce.Message, null, bce.Code),
            ExternalServiceException ese =>
                                         (StatusCodes.Status502BadGateway, ese.Message, null, ese.Code),
            IntegrationUnavailableException iue =>
                                         (StatusCodes.Status503ServiceUnavailable, iue.Message, null, iue.Code),

            BadRequestException bre   => (StatusCodes.Status400BadRequest,   bre.Message, null, null),
            NotFoundException nfe     => (StatusCodes.Status404NotFound,      nfe.Message, null, null),
            FinViet.Application.Exceptions.NotFoundException nfe2 =>
                                         (StatusCodes.Status404NotFound,      nfe2.Message, null, null),
            ConflictException ce      => (StatusCodes.Status409Conflict,      ce.Message,  null, null),
            UnauthorizedException ue  => (StatusCodes.Status401Unauthorized,  ue.Message,  null, null),
            ForbiddenException fe     => (StatusCodes.Status403Forbidden,     fe.Message,  null, null),
            UnauthorizedAccessException uae =>
                                         (StatusCodes.Status401Unauthorized,  uae.Message, null, null),
            _                         => (StatusCodes.Status500InternalServerError,
                                          _env.IsDevelopment()
                                              ? exception.Message
                                              : "An unexpected error occurred.",
                                          _env.IsDevelopment()
                                              ? new { stackTrace = exception.StackTrace, type = exception.GetType().Name }
                                              : null,
                                          (string?)null)
        };

        context.Response.StatusCode = statusCode;

        var response = new
        {
            success = false,
            message,
            code,
            errors
        };

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }
}

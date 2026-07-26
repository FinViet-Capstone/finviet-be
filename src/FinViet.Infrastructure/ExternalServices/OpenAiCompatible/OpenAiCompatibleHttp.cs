using System.Net.Http.Headers;
using FinViet.Application.Exceptions;
using Microsoft.Extensions.Logging;

namespace FinViet.Infrastructure.ExternalServices.OpenAiCompatible;

internal static class OpenAiCompatibleHttp
{
    public static void AddAuthorization(HttpRequestMessage request, string apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public static async Task<string> SendAsync(
        HttpClient http,
        HttpRequestMessage request,
        ILogger logger,
        string operation,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "AI provider {Operation} timed out.", operation);
            throw new AiProviderUnavailableException($"The AI provider {operation} timed out.", ex);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "AI provider {Operation} failed.", operation);
            throw new AiProviderUnavailableException($"The AI provider {operation} failed.", ex);
        }

        using (response)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
                return payload;

            logger.LogWarning(
                "AI provider {Operation} returned {Status}: {Body}",
                operation,
                (int)response.StatusCode,
                payload);
            throw new AiProviderUnavailableException(
                $"The AI provider {operation} returned HTTP {(int)response.StatusCode}.");
        }
    }
}

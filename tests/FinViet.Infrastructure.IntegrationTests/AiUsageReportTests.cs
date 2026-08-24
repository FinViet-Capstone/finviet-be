using System.Globalization;
using System.Text;
using Npgsql;

namespace FinViet.Infrastructure.IntegrationTests;

/// <summary>Generates a markdown latency/outcome report from the `ai_usage_events` table that
/// <c>AiTelemetryRecorder</c> already populates on every real Gemini call (see
/// GeminiAiModelClient.GenerateAsync). Opt-in only (Category=Report) — this is meant to be run on
/// demand against a real database with accumulated usage, not as part of a routine test sweep.
/// Uses raw Npgsql rather than FinVietDbContext since it only ever reads one table and doesn't
/// need the full entity model (enum converters etc.) that writing through the DbContext requires.</summary>
public sealed class AiUsageReportTests
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=Finviet_update;Username=admin;Password=123456";

    [Trait("Category", "Report")]
    [SkippableFact]
    public async Task GenerateAiUsageReport()
    {
        var connectionString = Environment.GetEnvironmentVariable("FINVIET_REPORT_DB_CONNECTION")
            is { Length: > 0 } cs ? cs : DefaultConnectionString;

        await using var connection = new NpgsqlConnection(connectionString);
        Skip.If(!await TryOpenAsync(connection),
            "Database not reachable. Set FINVIET_REPORT_DB_CONNECTION to override the default " +
            $"local connection string ({DefaultConnectionString}).");

        var sinceRaw = Environment.GetEnvironmentVariable("FINVIET_REPORT_SINCE");
        DateTime? since = sinceRaw is { Length: > 0 }
            ? DateTime.Parse(sinceRaw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal)
            : null;

        var rows = await QueryUsageAsync(connection, since);
        Skip.If(rows.Count == 0, "No rows in ai_usage_events for the selected window.");

        var reportDir = Path.Combine(FindRepoRoot(), "docs", "benchmarks");
        Directory.CreateDirectory(reportDir);
        var path = Path.Combine(reportDir, $"ai-pipeline-usage-{DateTime.UtcNow:yyyyMMdd-HHmm}.md");
        await File.WriteAllTextAsync(path, BuildMarkdownReport(rows, since), Encoding.UTF8);

        Assert.True(File.Exists(path));
    }

    private static async Task<bool> TryOpenAsync(NpgsqlConnection connection)
    {
        try
        {
            await connection.OpenAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<List<UsageRow>> QueryUsageAsync(NpgsqlConnection connection, DateTime? since)
    {
        var sql = $"""
            select feature, outcome, count(*) as n,
                   avg(latency_ms) as avg_ms,
                   percentile_cont(0.5) within group (order by latency_ms) as p50_ms,
                   percentile_cont(0.95) within group (order by latency_ms) as p95_ms,
                   min(occurred_at) as first_seen, max(occurred_at) as last_seen
            from ai_usage_events
            {(since is not null ? "where occurred_at >= @since" : "")}
            group by feature, outcome
            order by feature, outcome;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        if (since is { } sinceValue)
            command.Parameters.AddWithValue("since", sinceValue);

        var rows = new List<UsageRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new UsageRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt64(2),
                reader.IsDBNull(3) ? null : reader.GetDouble(3),
                reader.IsDBNull(4) ? null : reader.GetDouble(4),
                reader.IsDBNull(5) ? null : reader.GetDouble(5),
                reader.GetDateTime(6),
                reader.GetDateTime(7)));
        }

        return rows;
    }

    private static string BuildMarkdownReport(List<UsageRow> rows, DateTime? since)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# AI Pipeline Usage Report");
        sb.AppendLine();
        sb.AppendLine($"Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC from `ai_usage_events`" +
            (since is { } s ? $", filtered to `occurred_at >= {s:yyyy-MM-dd HH:mm} UTC`." : ", all-time (no filter)."));
        sb.AppendLine();
        sb.AppendLine("| Feature | Outcome | Count | Avg ms | p50 ms | p95 ms | First seen (UTC) | Last seen (UTC) |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var row in rows)
        {
            sb.AppendLine($"| {row.Feature} | {row.Outcome} | {row.Count} | {FormatMs(row.AvgMs)} | " +
                $"{FormatMs(row.P50Ms)} | {FormatMs(row.P95Ms)} | {row.FirstSeen:yyyy-MM-dd HH:mm} | " +
                $"{row.LastSeen:yyyy-MM-dd HH:mm} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Findings");
        sb.AppendLine();

        var flagged = false;
        foreach (var group in rows.GroupBy(r => r.Feature).OrderBy(g => g.Key))
        {
            var successCount = group.Where(r => r.Outcome == "success").Sum(r => r.Count);
            var otherCount = group.Where(r => r.Outcome != "success").Sum(r => r.Count);
            if (otherCount <= successCount)
                continue;

            flagged = true;
            var total = successCount + otherCount;
            var successRate = total == 0 ? 0 : successCount * 100.0 / total;
            sb.AppendLine($"- **`{group.Key}`**: only {successCount}/{total} calls succeeded " +
                $"({successRate:F1}%) — non-success outcomes outnumber successes. Breakdown: " +
                string.Join(", ", group.Select(r => $"{r.Outcome}={r.Count}")) + ".");
        }

        if (!flagged)
            sb.AppendLine("No feature had more non-success outcomes than successes in this window.");

        return sb.ToString();
    }

    private static string FormatMs(double? ms) => ms is { } v ? v.ToString("F0", CultureInfo.InvariantCulture) : "—";

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.GetFiles("*.sln").Any())
            dir = dir.Parent;

        return dir?.FullName
            ?? throw new InvalidOperationException($"Could not locate the repo root (a *.sln file) above {AppContext.BaseDirectory}.");
    }

    private sealed record UsageRow(
        string Feature,
        string Outcome,
        long Count,
        double? AvgMs,
        double? P50Ms,
        double? P95Ms,
        DateTime FirstSeen,
        DateTime LastSeen);
}

using FinViet.Application.DTOs.Analytics;
using MediatR;

namespace FinViet.Application.Features.Analytics.Queries.GetAnalyticsTrend;

public record GetAnalyticsTrendQuery(string Metric, int Days) : IRequest<List<DailyMetricDto>>;

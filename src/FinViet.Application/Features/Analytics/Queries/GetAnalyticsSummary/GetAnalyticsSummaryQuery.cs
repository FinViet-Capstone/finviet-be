using FinViet.Application.DTOs.Analytics;
using MediatR;

namespace FinViet.Application.Features.Analytics.Queries.GetAnalyticsSummary;

public record GetAnalyticsSummaryQuery() : IRequest<AdminAnalyticsSummaryDto>;

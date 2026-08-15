using FinViet.Application.DTOs.Subscriptions;
using MediatR;

namespace FinViet.Application.Features.Subscriptions.Commands.ProcessVNPayIpn;

/// <summary>
/// Authoritative confirmation of a VNPay charge (initial or renewal) — see
/// ProcessVNPayIpnCommandHandler. This handler must never throw: it always returns a
/// VNPayIpnReplyDto with an appropriate RspCode, since VNPay's retry/ack behavior is driven by
/// that JSON body, not by HTTP status.
/// </summary>
public record ProcessVNPayIpnCommand(
    IReadOnlyDictionary<string, string> VnpParams
) : IRequest<VNPayIpnReplyDto>;

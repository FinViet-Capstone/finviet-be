using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Auth;
using FinViet.Application.Features.Auth.Commands.GoogleLogin;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Features.Auth.Commands.Login;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Features.Auth.Commands.GoogleLogin;

public class GoogleLoginCommandHandler : IRequestHandler<GoogleLoginCommand, AuthResponseDto>
{
    private readonly FinVietDbContext _db;
    private readonly IFirebaseAuthService _firebase;
    private readonly LoginCommandHandler _loginHelper;

    public GoogleLoginCommandHandler(
        FinVietDbContext db,
        IFirebaseAuthService firebase,
        LoginCommandHandler loginHelper)
    {
        _db = db;
        _firebase = firebase;
        _loginHelper = loginHelper;
    }

    public async Task<AuthResponseDto> Handle(GoogleLoginCommand request, CancellationToken cancellationToken)
    {
        var firebaseUser = await _firebase.VerifyIdTokenAsync(request.IdToken);

        if (firebaseUser is null)
            throw new UnauthorizedException("Invalid or expired Google ID token.");

        if (string.IsNullOrEmpty(firebaseUser.Email))
            throw new BadRequestException("Google account must have a verified email.");

        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.GoogleId == firebaseUser.Uid, cancellationToken)
            ?? await _db.Customers
            .FirstOrDefaultAsync(c => c.Email == firebaseUser.Email.ToLower(), cancellationToken);

        if (customer is null)
        {
            customer = new Customer
            {
                CustomerId      = Guid.NewGuid(),
                FullName        = firebaseUser.DisplayName ?? firebaseUser.Email,
                Email           = firebaseUser.Email.ToLower(),
                PasswordHash    = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString()),
                GoogleId        = firebaseUser.Uid,
                AvatarUrl       = firebaseUser.PhotoUrl,
                IsEmailVerified = firebaseUser.EmailVerified,
                EmailVerifiedAt = firebaseUser.EmailVerified ? DateTime.UtcNow : null,
                IsActive        = true,
                CreatedAt       = DateTime.UtcNow
            };
            _db.Customers.Add(customer);
        }
        else
        {
            if (!customer.IsActive)
                throw new ForbiddenException("This account has been deactivated.");

            if (customer.GoogleId is null)
                customer.GoogleId = firebaseUser.Uid;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return await _loginHelper.IssueTokensAsync(customer, cancellationToken);
    }
}

using FinViet.Application.Common;
using FinViet.Application.DTOs.Users;
using MediatR;

namespace FinViet.Application.Features.Users.Queries.GetUsers;

public record GetUsersQuery(UserQueryDto Query) : IRequest<PagedResult<UserResponseDto>>;

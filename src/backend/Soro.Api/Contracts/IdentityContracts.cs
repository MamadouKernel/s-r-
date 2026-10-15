using Soro.Domain.Identity;

namespace Soro.Api.Contracts;

public sealed record LogInRequest
{
    public required Guid TenantId { get; init; }

    public required string Identifier { get; init; }

    public required string Pin { get; init; }
}

public sealed record RegisterUserRequest
{
    public required Guid UserId { get; init; }

    public required string Identifier { get; init; }

    public required string DisplayName { get; init; }

    public required Role Role { get; init; }

    public required string Pin { get; init; }
}

public sealed record UserSummary(Guid Id, string DisplayName, Role Role);

public sealed record LogInResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt, UserSummary User);

public sealed record UserRegisteredResponse(Guid UserId, bool AlreadyProcessed);

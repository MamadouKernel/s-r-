using Soro.Domain.Identity;

namespace Soro.Api.Contracts;

public sealed record LogInRequest
{
    public required Guid TenantId { get; init; }

    public required string Identifier { get; init; }

    public required string Pin { get; init; }

    /// <summary>L'appareil, tel que l'enrôlement l'a donné. Facultatif seulement tant que <c>Devices:Required</c> est faux.</summary>
    public Guid? DeviceId { get; init; }

    public string? DeviceSecret { get; init; }
}

public sealed record RegisterUserRequest
{
    public required Guid UserId { get; init; }

    public required string Identifier { get; init; }

    public required string DisplayName { get; init; }

    public required Role Role { get; init; }

    public required string Pin { get; init; }
}

/// <summary>Nouveau code PIN d'un membre (PIN oublié) ou code PIN de son retour (réactivation). Jamais rendu, jamais journalisé.</summary>
public sealed record SetPinRequest
{
    public required string Pin { get; init; }
}

public sealed record ChangeRoleRequest
{
    public required Role Role { get; init; }
}

public sealed record UserSummary(Guid Id, string DisplayName, Role Role);

public sealed record LogInResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt, UserSummary User);

public sealed record UserRegisteredResponse(Guid UserId, bool AlreadyProcessed);

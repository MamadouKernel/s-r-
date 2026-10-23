using Soro.Domain.Tenancy;

namespace Soro.Api.Contracts;

public sealed record StartSignupRequest
{
    public required string Phone { get; init; }

    public required string CommerceName { get; init; }

    public required ActivityType Activity { get; init; }

    public required string OwnerName { get; init; }

    public required string TermsVersion { get; init; }
}

public sealed record SignupStartedResponse(Guid VerificationId, DateTimeOffset ExpiresAt, DateTimeOffset ResendAvailableAt);

public sealed record ResendSignupCodeRequest
{
    public required Guid VerificationId { get; init; }
}

public sealed record CompleteSignupRequest
{
    public required Guid VerificationId { get; init; }

    public required string Code { get; init; }

    public required string Password { get; init; }
}

public sealed record OwnerLogInRequest
{
    public required string Phone { get; init; }

    public required string Password { get; init; }
}

/// <summary>Session du propriétaire, après l'inscription comme après la connexion : le jeton et ce qu'il faut pour afficher le commerce.</summary>
public sealed record OwnerSessionResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset ExpiresAt,
    UserSummary User,
    Guid TenantId,
    string CommerceName,
    DateTimeOffset? TrialEndsAt);

public sealed record StartPasswordResetRequest
{
    public required string Phone { get; init; }
}

public sealed record ConfirmPasswordResetRequest
{
    public required string Phone { get; init; }

    public required string Code { get; init; }

    public required string NewPassword { get; init; }
}

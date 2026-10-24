namespace Soro.Api.Contracts;

public sealed record CreateDeviceInvitationRequest
{
    public string? SuggestedName { get; init; }
}

/// <summary>Le jeton n'est rendu qu'ici. Le client en fait un lien et un QR ; il ne le montre jamais en clair à l'utilisateur.</summary>
public sealed record DeviceInvitationResponse(Guid InvitationId, string Token, DateTimeOffset ExpiresAt);

public sealed record EnrollDeviceRequest
{
    public required string Token { get; init; }

    public string? DeviceName { get; init; }
}

/// <summary>Ce que l'appareil garde. Le secret n'est rendu qu'une fois.</summary>
public sealed record DeviceEnrolledResponse(Guid DeviceId, string DeviceSecret, Guid TenantId, string CommerceName, string DeviceName);

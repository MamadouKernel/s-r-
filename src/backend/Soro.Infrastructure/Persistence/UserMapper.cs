using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Infrastructure.Persistence;

internal static class UserMapper
{
    public static UserRow ToRow(User user)
    {
        var row = new UserRow
        {
            Id = user.Id.Value,
            TenantId = user.Tenant.Value,
            Identifier = user.Identifier,
            DisplayName = user.DisplayName,
            Email = user.Email?.Value,
            CreatedAt = user.CreatedAt.ToUniversalTime(),
        };
        Apply(row, user);
        return row;
    }

    /// <summary>Reporte l'état changeant (profil, PIN, statut, compteur d'échecs, verrou, dernière connexion, fin des sessions).</summary>
    public static void Apply(UserRow row, User user)
    {
        row.Role = user.Role;
        row.PinHash = user.PinHash;
        row.SessionsValidFrom = user.SessionsValidFrom?.ToUniversalTime();
        row.PasswordHash = user.PasswordHash;
        row.Status = user.Status;
        row.FailedAttempts = user.FailedAttempts;
        row.LockedUntil = user.LockedUntil?.ToUniversalTime();
        row.LastLoginAt = user.LastLoginAt?.ToUniversalTime();
    }

    public static User ToDomain(UserRow row) =>
        User.Restore(
            new UserId(row.Id),
            new TenantId(row.TenantId),
            row.Identifier,
            row.DisplayName,
            row.Role,
            row.PinHash,
            row.CreatedAt,
            row.Status,
            row.FailedAttempts,
            row.LockedUntil,
            row.LastLoginAt,
            row.Email is null ? null : EmailAddress.Parse(row.Email),
            row.PasswordHash,
            row.SessionsValidFrom);
}

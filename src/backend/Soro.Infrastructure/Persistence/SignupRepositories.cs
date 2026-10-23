using Microsoft.EntityFrameworkCore;
using Soro.Application.Signup;
using Soro.Domain.Common;
using Soro.Domain.Tenancy;

namespace Soro.Infrastructure.Persistence;

public sealed class PhoneVerificationRepository(SoroDbContext db) : IPhoneVerificationRepository
{
    public async Task<PhoneVerification?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await db.PhoneVerifications.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return row is null ? null : Map(row);
    }

    public async Task<PhoneVerification?> FindLatestPendingAsync(
        PhoneNumber phone,
        VerificationPurpose purpose,
        CancellationToken cancellationToken)
    {
        var row = await db.PhoneVerifications
            .Where(item => item.Phone == phone.Value && item.Purpose == purpose && item.ConsumedAt == null)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : Map(row);
    }

    public Task<int> CountStartedSinceAsync(
        PhoneNumber phone,
        VerificationPurpose purpose,
        DateTimeOffset since,
        CancellationToken cancellationToken) =>
        db.PhoneVerifications.CountAsync(
            item => item.Phone == phone.Value && item.Purpose == purpose && item.CreatedAt >= since.ToUniversalTime(),
            cancellationToken);

    public Task AddAsync(PhoneVerification verification, CancellationToken cancellationToken)
    {
        db.PhoneVerifications.Add(new PhoneVerificationRow
        {
            Id = verification.Id,
            Purpose = verification.Purpose,
            Phone = verification.Phone.Value,
            Payload = verification.Payload,
            CreatedAt = verification.CreatedAt.ToUniversalTime(),
        }.With(verification));
        return Task.CompletedTask;
    }

    public async Task UpdateAsync(PhoneVerification verification, CancellationToken cancellationToken)
    {
        var row = await db.PhoneVerifications.FindAsync([verification.Id], cancellationToken)
            ?? throw new InvalidOperationException($"La demande {verification.Id} n'existe pas.");
        row.With(verification);
    }

    private static PhoneVerification Map(PhoneVerificationRow row) =>
        PhoneVerification.Restore(
            row.Id,
            row.Purpose,
            PhoneNumber.Parse(row.Phone),
            row.CodeHash,
            row.Payload,
            row.CreatedAt,
            row.ExpiresAt,
            row.Attempts,
            row.ResendCount,
            row.LastSentAt,
            row.ConsumedAt);
}

internal static class PhoneVerificationRowExtensions
{
    /// <summary>Reporte l'état changeant d'une demande (code, expiration, essais, renvois, consommation).</summary>
    public static PhoneVerificationRow With(this PhoneVerificationRow row, PhoneVerification verification)
    {
        row.CodeHash = verification.CodeHash;
        row.ExpiresAt = verification.ExpiresAt.ToUniversalTime();
        row.Attempts = verification.Attempts;
        row.ResendCount = verification.ResendCount;
        row.LastSentAt = verification.LastSentAt.ToUniversalTime();
        row.ConsumedAt = verification.ConsumedAt?.ToUniversalTime();
        return row;
    }
}

public sealed class OwnerDirectory(SoroDbContext db) : IOwnerDirectory
{
    public async Task<OwnerAccount?> FindAsync(PhoneNumber phone, CancellationToken cancellationToken)
    {
        var row = await db.OwnerDirectory.AsNoTracking().SingleOrDefaultAsync(item => item.Phone == phone.Value, cancellationToken);
        return row is null ? null : new OwnerAccount(phone, new TenantId(row.TenantId), new UserId(row.UserId));
    }

    public Task AddAsync(OwnerAccount account, CancellationToken cancellationToken)
    {
        db.OwnerDirectory.Add(new OwnerDirectoryRow
        {
            Phone = account.Phone.Value,
            TenantId = account.Tenant.Value,
            UserId = account.User.Value,
        });
        return Task.CompletedTask;
    }
}

public sealed class CommerceRepository(SoroDbContext db) : ICommerceRepository
{
    public async Task<Commerce?> GetAsync(TenantId tenant, CancellationToken cancellationToken)
    {
        var row = await db.Commerces.AsNoTracking().SingleOrDefaultAsync(item => item.Id == tenant.Value, cancellationToken);
        return row is null
            ? null
            : Commerce.Restore(
                new TenantId(row.Id),
                row.Name,
                row.Activity,
                PhoneNumber.Parse(row.OwnerPhone),
                row.Formula,
                row.CreatedAt,
                row.TrialEndsAt,
                row.TermsVersion,
                row.TermsAcceptedAt);
    }

    public Task AddAsync(Commerce commerce, CancellationToken cancellationToken)
    {
        db.Commerces.Add(new CommerceRow
        {
            Id = commerce.Id.Value,
            Name = commerce.Name,
            Activity = commerce.Activity,
            OwnerPhone = commerce.OwnerPhone.Value,
            Formula = commerce.Formula,
            CreatedAt = commerce.CreatedAt.ToUniversalTime(),
            TrialEndsAt = commerce.TrialEndsAt.ToUniversalTime(),
            TermsVersion = commerce.TermsVersion,
            TermsAcceptedAt = commerce.TermsAcceptedAt.ToUniversalTime(),
        });
        return Task.CompletedTask;
    }
}

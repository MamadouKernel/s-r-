using Microsoft.EntityFrameworkCore;
using Soro.Application.Signup;
using Soro.Domain.Common;
using Soro.Domain.Tenancy;

namespace Soro.Infrastructure.Persistence;

public sealed class EmailVerificationRepository(SoroDbContext db) : IEmailVerificationRepository
{
    public async Task<EmailVerification?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await db.EmailVerifications.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return row is null ? null : Map(row);
    }

    public async Task<EmailVerification?> FindLatestPendingAsync(
        EmailAddress email,
        VerificationPurpose purpose,
        CancellationToken cancellationToken)
    {
        var row = await db.EmailVerifications
            .Where(item => item.Email == email.Value && item.Purpose == purpose && item.ConsumedAt == null)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : Map(row);
    }

    public Task<int> CountStartedSinceAsync(
        EmailAddress email,
        VerificationPurpose purpose,
        DateTimeOffset since,
        CancellationToken cancellationToken) =>
        db.EmailVerifications.CountAsync(
            item => item.Email == email.Value && item.Purpose == purpose && item.CreatedAt >= since.ToUniversalTime(),
            cancellationToken);

    public Task AddAsync(EmailVerification verification, CancellationToken cancellationToken)
    {
        db.EmailVerifications.Add(new EmailVerificationRow
        {
            Id = verification.Id,
            Purpose = verification.Purpose,
            Email = verification.Email.Value,
            Payload = verification.Payload,
            CreatedAt = verification.CreatedAt.ToUniversalTime(),
        }.With(verification));
        return Task.CompletedTask;
    }

    public async Task UpdateAsync(EmailVerification verification, CancellationToken cancellationToken)
    {
        var row = await db.EmailVerifications.FindAsync([verification.Id], cancellationToken)
            ?? throw new InvalidOperationException($"La demande {verification.Id} n'existe pas.");
        row.With(verification);
    }

    private static EmailVerification Map(EmailVerificationRow row) =>
        EmailVerification.Restore(
            row.Id,
            row.Purpose,
            EmailAddress.Parse(row.Email),
            row.CodeHash,
            row.Payload,
            row.CreatedAt,
            row.ExpiresAt,
            row.Attempts,
            row.ResendCount,
            row.LastSentAt,
            row.ConsumedAt);
}

internal static class EmailVerificationRowExtensions
{
    /// <summary>Reporte l'état changeant d'une demande (code, expiration, essais, renvois, consommation).</summary>
    public static EmailVerificationRow With(this EmailVerificationRow row, EmailVerification verification)
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
    public async Task<OwnerAccount?> FindAsync(EmailAddress email, CancellationToken cancellationToken)
    {
        var row = await db.OwnerDirectory.AsNoTracking().SingleOrDefaultAsync(item => item.Email == email.Value, cancellationToken);
        return row is null ? null : new OwnerAccount(email, new TenantId(row.TenantId), new UserId(row.UserId));
    }

    public Task AddAsync(OwnerAccount account, CancellationToken cancellationToken)
    {
        db.OwnerDirectory.Add(new OwnerDirectoryRow
        {
            Email = account.Email.Value,
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
        return row is null ? null : CommerceMapper.ToDomain(row);
    }

    public Task AddAsync(Commerce commerce, CancellationToken cancellationToken)
    {
        db.Commerces.Add(new CommerceRow
        {
            Id = commerce.Id.Value,
            Name = commerce.Name,
            Activity = commerce.Activity,
            OwnerEmail = commerce.OwnerEmail.Value,
            Formula = commerce.Formula,
            CreatedAt = commerce.CreatedAt.ToUniversalTime(),
            TrialEndsAt = commerce.TrialEndsAt.ToUniversalTime(),
            TermsVersion = commerce.TermsVersion,
            TermsAcceptedAt = commerce.TermsAcceptedAt.ToUniversalTime(),
        });
        return Task.CompletedTask;
    }
}

internal static class CommerceMapper
{
    public static Commerce ToDomain(CommerceRow row) =>
        Commerce.Restore(
            new TenantId(row.Id),
            row.Name,
            row.Activity,
            EmailAddress.Parse(row.OwnerEmail),
            row.Formula,
            row.CreatedAt,
            row.TrialEndsAt,
            row.TermsVersion,
            row.TermsAcceptedAt,
            row.PaidUntil);
}

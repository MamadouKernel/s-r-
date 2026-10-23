using Soro.Domain.Common;

namespace Soro.Domain.Tenancy;

public enum VerificationPurpose
{
    Signup,
    PasswordReset,
}

/// <summary>
/// Demande de vérification d'un numéro par un code SMS (inscription ou mot de passe oublié). Elle porte le condensat du code,
/// jamais le code ; sa durée, ses essais et ses renvois sont bornés (ADR 0005).
/// </summary>
public sealed class PhoneVerification
{
    public const int MaxAttempts = 5;
    public const int MaxResends = 3;

    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan ResendDelay = TimeSpan.FromSeconds(60);

    private PhoneVerification(
        Guid id,
        VerificationPurpose purpose,
        PhoneNumber phone,
        string codeHash,
        string? payload,
        DateTimeOffset createdAt)
    {
        Id = id;
        Purpose = purpose;
        Phone = phone;
        CodeHash = codeHash;
        Payload = payload;
        CreatedAt = createdAt;
        ExpiresAt = createdAt + CodeLifetime;
        LastSentAt = createdAt;
    }

    public Guid Id { get; }

    public VerificationPurpose Purpose { get; }

    public PhoneNumber Phone { get; }

    public string CodeHash { get; private set; }

    /// <summary>Informations saisies à l'inscription (JSON), gardées jusqu'à la finalisation ; <c>null</c> pour un mot de passe oublié.</summary>
    public string? Payload { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public int Attempts { get; private set; }

    public int ResendCount { get; private set; }

    public DateTimeOffset LastSentAt { get; private set; }

    public DateTimeOffset? ConsumedAt { get; private set; }

    public bool IsConsumed => ConsumedAt is not null;

    public int AttemptsLeft => Math.Max(MaxAttempts - Attempts, 0);

    public DateTimeOffset ResendAvailableAt => LastSentAt + ResendDelay;

    public static PhoneVerification Start(
        Guid id,
        VerificationPurpose purpose,
        PhoneNumber phone,
        string codeHash,
        string? payload,
        DateTimeOffset now)
    {
        Ensure.That(!string.IsNullOrWhiteSpace(codeHash), "Le condensat du code est obligatoire.");
        return new PhoneVerification(id, purpose, phone, codeHash, payload, now);
    }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    /// <summary>Utilisable : ni consommée, ni expirée, ni bloquée par trop d'essais.</summary>
    public bool IsUsable(DateTimeOffset now) => !IsConsumed && !IsExpired(now) && AttemptsLeft > 0;

    public void RecordFailedAttempt() => Attempts++;

    public void Consume(DateTimeOffset now)
    {
        Ensure.That(IsUsable(now), "Cette demande de vérification n'est plus utilisable.");
        ConsumedAt = now;
    }

    public bool CanResend(DateTimeOffset now) =>
        !IsConsumed && !IsExpired(now) && ResendCount < MaxResends && now >= ResendAvailableAt;

    /// <summary>Remplace le code (donc sa durée et ses essais) : l'ancien code ne vaut plus rien.</summary>
    public void Reissue(string newCodeHash, DateTimeOffset now)
    {
        Ensure.That(CanResend(now), "Un nouveau code ne peut pas être envoyé maintenant.");
        Ensure.That(!string.IsNullOrWhiteSpace(newCodeHash), "Le condensat du code est obligatoire.");
        CodeHash = newCodeHash;
        ExpiresAt = now + CodeLifetime;
        LastSentAt = now;
        Attempts = 0;
        ResendCount++;
    }

    /// <summary>Reconstruction depuis le stockage : l'état a déjà été validé par les opérations métier.</summary>
    internal static PhoneVerification Restore(
        Guid id,
        VerificationPurpose purpose,
        PhoneNumber phone,
        string codeHash,
        string? payload,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        int attempts,
        int resendCount,
        DateTimeOffset lastSentAt,
        DateTimeOffset? consumedAt) =>
        new(id, purpose, phone, codeHash, payload, createdAt)
        {
            ExpiresAt = expiresAt,
            Attempts = attempts,
            ResendCount = resendCount,
            LastSentAt = lastSentAt,
            ConsumedAt = consumedAt,
        };
}

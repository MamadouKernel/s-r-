using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Application.Settings;
using Soro.Domain.Cash;
using Soro.Domain.Common;
using Soro.Domain.Sales;
using Soro.Infrastructure.Persistence;
using Soro.Domain.Identity;

namespace Soro.Integration.Tests;

internal sealed class StaticUser(TenantId tenant, UserId user, Role role) : ICurrentUser
{
    public TenantId Tenant { get; } = tenant;

    public UserId User { get; } = user;

    public Role Role { get; } = role;
}

internal sealed class FixedClock : IClock
{
    public DateTimeOffset UtcNow { get; } = Samples.Morning.AddHours(1);
}

internal sealed class DefaultSettings : IShopSettings
{
    public Task<ShopSettings> GetAsync(TenantId tenant, CancellationToken cancellationToken) =>
        Task.FromResult(ShopSettings.Default);
}

internal sealed class EmptyDirectory : IUserDirectory
{
    public Task<Role?> GetRoleAsync(UserId user, TenantId tenant, CancellationToken cancellationToken) =>
        Task.FromResult<Role?>(null);
}

internal static class Db
{
    /// <summary>Contexte du rôle applicatif, lié à un commerce : l'isolation (RLS) s'applique.</summary>
    public static SoroDbContext App(
        PostgresFixture fixture,
        TenantId tenant,
        string applicationName = "soro-tests",
        int maxPoolSize = 20) =>
        new(PersistenceOptions.Create(
            fixture.AppConnectionString(applicationName, maxPoolSize),
            new StaticUser(tenant, UserId.New(), Role.Cashier)));

    /// <summary>Contexte du propriétaire du schéma (migrations, contrôles d'administration).</summary>
    public static SoroDbContext Owner(PostgresFixture fixture) =>
        new(PersistenceOptions.Create(fixture.OwnerConnectionString));
}

internal static class Samples
{
    public static readonly DateTimeOffset Morning = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    public static CashRegisterSession Session(TenantId tenant, RegisterId? register = null, long openingFloat = 10_000) =>
        CashRegisterSession.Open(
            SessionId.New(),
            new RegisterAssignment(tenant, PointOfSaleId.New(), register ?? RegisterId.New(), UserId.New()),
            Money.Fcfa(openingFloat),
            Morning);

    public static Sale CashSale(CashRegisterSession session, long price = 1_000, long sequence = 1) =>
        session.RecordSale(Draft(sequence, price, Payment.Cash(PaymentId.New(), Money.Fcfa(price), Money.Fcfa(price))));

    public static Sale MobileSale(
        CashRegisterSession session,
        string reference,
        long sequence = 1,
        PaymentMethod method = PaymentMethod.OrangeMoney) =>
        session.RecordSale(Draft(
            sequence, 1_000, Payment.MobileMoney(PaymentId.New(), method, Money.Fcfa(1_000), reference)));

    public static SaleDraft Draft(long sequence, long price, params Payment[] payments) =>
        new(
            SaleId.New(),
            sequence,
            Morning.AddMinutes(5),
            [SaleLine.Create(ProductId.New(), "Produit", 1, Money.Fcfa(price))],
            Money.Zero,
            payments,
            null);
}

/// <summary>Garde chaque SMS envoyé par le serveur de test, sans rien envoyer.</summary>
internal sealed class CapturingSms : Soro.Application.Signup.ISmsSender
{
    private readonly List<(PhoneNumber To, string Message)> _sent = [];
    private readonly Lock _gate = new();

    public IReadOnlyList<(PhoneNumber To, string Message)> Sent
    {
        get
        {
            lock (_gate)
            {
                return _sent.ToArray();
            }
        }
    }

    public string LastCodeFor(string phone)
    {
        var number = PhoneNumber.Parse(phone);
        var message = Sent.Last(item => item.To == number).Message;
        return new string(message.SkipWhile(c => !char.IsAsciiDigit(c)).TakeWhile(char.IsAsciiDigit).ToArray());
    }

    public Task SendAsync(PhoneNumber recipient, string message, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _sent.Add((recipient, message));
        }

        return Task.CompletedTask;
    }
}

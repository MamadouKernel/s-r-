using Microsoft.EntityFrameworkCore;
using Soro.Application.Common;
using Soro.Domain.Cash;
using Soro.Domain.Common;
using Soro.Domain.Sales;
using Soro.Infrastructure.Persistence;

namespace Soro.Integration.Tests;

[Collection(Postgres.Collection)]
public class PersistenceTests(PostgresFixture fixture)
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private async Task SaveAsync(TenantId tenant, CashRegisterSession session, params Sale[] sales)
    {
        await using var db = Db.App(fixture, tenant);
        await new SessionRepository(db).AddAsync(session, Ct);
        var saleRepository = new SaleRepository(db);
        foreach (var sale in sales)
        {
            await saleRepository.AddAsync(sale, Ct);
        }

        await new EfUnitOfWork(db).CommitAsync(Ct);
    }

    [Fact]
    public async Task A_session_round_trips_through_the_database()
    {
        var tenant = TenantId.New();
        var session = Samples.Session(tenant, openingFloat: 10_000);
        await SaveAsync(tenant, session);

        await using var db = Db.App(fixture, tenant);
        var loaded = await new SessionRepository(db).GetAsync(session.Id, tenant, Ct);

        Assert.NotNull(loaded);
        Assert.Equal(session.Assignment, loaded.Assignment);
        Assert.Equal(Money.Fcfa(10_000), loaded.OpeningFloat);
        Assert.Equal(session.OpenedAt, loaded.OpenedAt);
        Assert.Equal(SessionStatus.Open, loaded.Status);
    }

    [Fact]
    public async Task Cash_sales_and_the_closing_are_persisted_on_the_session()
    {
        var tenant = TenantId.New();
        var session = Samples.Session(tenant, openingFloat: 10_000);
        await SaveAsync(tenant, session);

        await using (var db = Db.App(fixture, tenant))
        {
            var repository = new SessionRepository(db);
            var loaded = (await repository.GetAsync(session.Id, tenant, Ct))!;
            loaded.RecordSale(Samples.Draft(1, 2_000, Payment.Cash(PaymentId.New(), Money.Fcfa(2_000), Money.Fcfa(2_000))));
            loaded.Close(Money.Fcfa(11_500), Samples.Morning.AddHours(8));
            await repository.UpdateAsync(loaded, Ct);
            await new EfUnitOfWork(db).CommitAsync(Ct);
        }

        await using var verify = Db.App(fixture, tenant);
        var closed = (await new SessionRepository(verify).GetAsync(session.Id, tenant, Ct))!;
        Assert.Equal(SessionStatus.Closed, closed.Status);
        Assert.Equal(Money.Fcfa(12_000), closed.ExpectedCash);
        Assert.Equal(Money.Fcfa(11_500), closed.CountedCash);
        Assert.Equal(-500L, closed.CashVariance);
    }

    [Fact]
    public async Task A_sale_and_the_closing_at_the_same_moment_do_not_overwrite_each_other()
    {
        var tenant = TenantId.New();
        var session = Samples.Session(tenant, openingFloat: 10_000);
        await SaveAsync(tenant, session);

        await using var first = Db.App(fixture, tenant);
        await using var second = Db.App(fixture, tenant);
        var closing = (await new SessionRepository(first).GetAsync(session.Id, tenant, Ct))!;
        var selling = (await new SessionRepository(second).GetAsync(session.Id, tenant, Ct))!;
        closing.Close(Money.Fcfa(10_000), Samples.Morning.AddHours(8));
        selling.RecordSale(Samples.Draft(1, 10_000, Payment.Cash(PaymentId.New(), Money.Fcfa(10_000), Money.Fcfa(10_000))));
        await new SessionRepository(first).UpdateAsync(closing, Ct);
        await new SessionRepository(second).UpdateAsync(selling, Ct);

        await new EfUnitOfWork(first).CommitAsync(Ct);
        await Assert.ThrowsAsync<ConflictException>(() => new EfUnitOfWork(second).CommitAsync(Ct));

        await using var read = Db.App(fixture, tenant);
        var stored = (await new SessionRepository(read).GetAsync(session.Id, tenant, Ct))!;
        Assert.Equal(SessionStatus.Closed, stored.Status);
        Assert.Equal(Money.Zero, stored.CashReceived); // la vente perdue n'a pas faussé le total : elle est rejouée, pas écrasée
    }

    [Fact]
    public async Task A_sale_round_trips_with_its_lines_and_mixed_payments()
    {
        var tenant = TenantId.New();
        var session = Samples.Session(tenant);
        var sale = session.RecordSale(new SaleDraft(
            SaleId.New(),
            7,
            Samples.Morning.AddMinutes(5),
            [
                SaleLine.Create(ProductId.New(), "Eau 1,5 L", 2, Money.Fcfa(500)),
                SaleLine.Create(ProductId.New(), "Pain", 1, Money.Fcfa(300)),
            ],
            Money.Zero,
            [
                Payment.Cash(PaymentId.New(), Money.Fcfa(800), Money.Fcfa(1_000)),
                Payment.MobileMoney(PaymentId.New(), PaymentMethod.Wave, Money.Fcfa(500), "WV-001"),
            ],
            null));
        await SaveAsync(tenant, session, sale);

        await using var db = Db.App(fixture, tenant);
        var loaded = await new SaleRepository(db).GetAsync(sale.Id, tenant, Ct);

        Assert.NotNull(loaded);
        Assert.Equal(sale.Lines, loaded.Lines);
        Assert.Equal(sale.Payments, loaded.Payments);
        Assert.Equal(sale.Assignment, loaded.Assignment);
        Assert.Equal(sale.NetTotal, loaded.NetTotal);
        Assert.Equal(7, loaded.LocalSequence);
        Assert.Equal(SaleStatus.Validated, loaded.Status);
    }

    [Fact]
    public async Task A_cancellation_is_persisted_without_touching_the_sale()
    {
        var tenant = TenantId.New();
        var session = Samples.Session(tenant);
        var sale = Samples.CashSale(session);
        await SaveAsync(tenant, session, sale);
        var manager = UserId.New();

        await using (var db = Db.App(fixture, tenant))
        {
            var repository = new SaleRepository(db);
            var loaded = (await repository.GetAsync(sale.Id, tenant, Ct))!;
            await repository.UpdateAsync(loaded.Cancel(manager, "Erreur de saisie", Samples.Morning.AddMinutes(30)), Ct);
            await new EfUnitOfWork(db).CommitAsync(Ct);
        }

        await using var verify = Db.App(fixture, tenant);
        var cancelled = (await new SaleRepository(verify).GetAsync(sale.Id, tenant, Ct))!;
        Assert.Equal(SaleStatus.Cancelled, cancelled.Status);
        Assert.Equal(manager, cancelled.Cancellation!.CancelledBy);
        Assert.Equal("Erreur de saisie", cancelled.Cancellation.Reason);
        Assert.Equal(sale.Lines, cancelled.Lines);
    }

    [Fact]
    public async Task An_audit_entry_is_persisted()
    {
        var tenant = TenantId.New();
        var resource = Guid.NewGuid();
        await using (var db = Db.App(fixture, tenant))
        {
            await new AuditTrail(db).RecordAsync(
                new AuditEntry(tenant, UserId.New(), AuditActions.CancelRefused, resource, "Motif", Samples.Morning), Ct);
            await new EfUnitOfWork(db).CommitAsync(Ct);
        }

        await using var verify = Db.App(fixture, tenant);
        var actions = await verify.Database
            .SqlQuery<string>($"SELECT action AS \"Value\" FROM audit_entries WHERE resource_id = {resource}")
            .ToListAsync(Ct);

        Assert.Equal([AuditActions.CancelRefused], actions);
    }
}

using Soro.Application.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Soro.Domain.Cash;
using Soro.Domain.Common;
using Soro.Domain.Sales;
using Soro.Infrastructure.Persistence;

namespace Soro.Integration.Tests;

[Collection(Postgres.Collection)]
public class ConstraintTests(PostgresFixture fixture)
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private async Task SaveAsync(TenantId tenant, CashRegisterSession session, params Sale[] sales)
    {
        await using var db = Db.App(fixture, tenant);
        await new SessionRepository(db).AddAsync(session, Ct);
        foreach (var sale in sales)
        {
            await new SaleRepository(db).AddAsync(sale, Ct);
        }

        await new EfUnitOfWork(db).CommitAsync(Ct);
    }

    [Fact]
    public async Task The_database_rejects_a_reused_mobile_money_reference_whatever_its_case()
    {
        var tenant = TenantId.New();
        var session = Samples.Session(tenant);
        await SaveAsync(tenant, session, Samples.MobileSale(session, "MP-100", sequence: 1));

        var duplicate = Samples.MobileSale(session, "mp-100", sequence: 2);
        await using var db = Db.App(fixture, tenant);
        await new SaleRepository(db).AddAsync(duplicate, Ct);

        var failure = await Assert.ThrowsAsync<ConflictException>(() => new EfUnitOfWork(db).CommitAsync(Ct));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(failure.InnerException!.InnerException).SqlState);
    }

    [Fact]
    public async Task The_reference_lookup_finds_a_used_reference_for_the_same_operator_only()
    {
        var tenant = TenantId.New();
        var session = Samples.Session(tenant);
        await SaveAsync(tenant, session, Samples.MobileSale(session, "MP-200", method: PaymentMethod.OrangeMoney));

        await using var db = Db.App(fixture, tenant);
        var repository = new SaleRepository(db);

        Assert.True(await repository.IsMobileMoneyReferenceUsedAsync(tenant, PaymentMethod.OrangeMoney, "mp-200", Ct));
        Assert.False(await repository.IsMobileMoneyReferenceUsedAsync(tenant, PaymentMethod.MtnMomo, "MP-200", Ct));
    }

    [Fact]
    public async Task Another_tenant_may_reuse_the_same_reference()
    {
        var first = TenantId.New();
        var second = TenantId.New();
        var firstSession = Samples.Session(first);
        var secondSession = Samples.Session(second);
        await SaveAsync(first, firstSession, Samples.MobileSale(firstSession, "MP-300"));

        await SaveAsync(second, secondSession, Samples.MobileSale(secondSession, "MP-300"));
    }

    [Fact]
    public async Task A_local_sequence_is_unique_per_register()
    {
        var tenant = TenantId.New();
        var register = RegisterId.New();
        var session = Samples.Session(tenant, register);
        await SaveAsync(tenant, session, Samples.CashSale(session, sequence: 1));

        await using var db = Db.App(fixture, tenant);
        await new SaleRepository(db).AddAsync(Samples.CashSale(session, sequence: 1), Ct);

        await Assert.ThrowsAsync<ConflictException>(() => new EfUnitOfWork(db).CommitAsync(Ct));
    }

    [Fact]
    public async Task A_register_cannot_have_two_open_sessions()
    {
        var tenant = TenantId.New();
        var register = RegisterId.New();
        await SaveAsync(tenant, Samples.Session(tenant, register));

        await using var db = Db.App(fixture, tenant);
        await new SessionRepository(db).AddAsync(Samples.Session(tenant, register), Ct);

        await Assert.ThrowsAsync<ConflictException>(() => new EfUnitOfWork(db).CommitAsync(Ct));
    }

    [Fact]
    public async Task A_validated_sale_cannot_be_deleted_even_by_the_schema_owner()
    {
        var tenant = TenantId.New();
        var session = Samples.Session(tenant);
        var sale = Samples.CashSale(session);
        await SaveAsync(tenant, session, sale);

        await using var owner = Db.Owner(fixture);

        await Assert.ThrowsAsync<PostgresException>(() =>
            owner.Database.ExecuteSqlRawAsync("DELETE FROM sales", Ct));
    }
}

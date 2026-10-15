using Microsoft.EntityFrameworkCore;
using Npgsql;
using Soro.Domain.Common;
using Soro.Domain.Sales;
using Soro.Infrastructure.Persistence;

namespace Soro.Integration.Tests;

/// <summary>Seconde barrière de RG-15 : PostgreSQL filtre par commerce même si l'application se trompe.</summary>
[Collection(Postgres.Collection)]
public class TenantIsolationTests(PostgresFixture fixture)
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private async Task<Sale> SeedSaleAsync(TenantId tenant)
    {
        var session = Samples.Session(tenant);
        var sale = Samples.CashSale(session);
        await using var db = Db.App(fixture, tenant);
        await new SessionRepository(db).AddAsync(session, Ct);
        await new SaleRepository(db).AddAsync(sale, Ct);
        await new EfUnitOfWork(db).CommitAsync(Ct);
        return sale;
    }

    private static Task<long> CountSalesAsync(SoroDbContext db) =>
        db.Database.SqlQueryRaw<long>("SELECT count(*)::bigint AS \"Value\" FROM sales").SingleAsync();

    [Fact]
    public async Task A_tenant_only_sees_its_own_rows_even_when_asking_for_another_tenants_id()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var saleOfA = await SeedSaleAsync(tenantA);
        await SeedSaleAsync(tenantB);

        await using var asB = Db.App(fixture, tenantB);

        Assert.Equal(1, await CountSalesAsync(asB));
        Assert.Null(await new SaleRepository(asB).GetAsync(saleOfA.Id, tenantA, Ct));
    }

    [Fact]
    public async Task A_tenant_cannot_write_rows_for_another_tenant()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var session = Samples.Session(tenantA);
        await using (var asA = Db.App(fixture, tenantA))
        {
            await new SessionRepository(asA).AddAsync(session, Ct);
            await new EfUnitOfWork(asA).CommitAsync(Ct);
        }

        await using var asB = Db.App(fixture, tenantB);
        await new SaleRepository(asB).AddAsync(Samples.CashSale(session), Ct);

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => new EfUnitOfWork(asB).CommitAsync(Ct));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
    }

    [Fact]
    public async Task A_connection_without_tenant_context_sees_nothing()
    {
        await SeedSaleAsync(TenantId.New());

        await using var connection = new NpgsqlConnection(fixture.AppConnectionString("no-context"));
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM sales", connection);

        Assert.Equal(0L, await command.ExecuteScalarAsync(Ct));
    }

    [Fact]
    public async Task The_tenant_context_does_not_leak_between_pooled_connections()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        await SeedSaleAsync(tenantA);
        await SeedSaleAsync(tenantB);
        await SeedSaleAsync(tenantB);

        long seenByA;
        long seenByB;
        await using (var asA = Db.App(fixture, tenantA, "single-connection", maxPoolSize: 1))
        {
            seenByA = await CountSalesAsync(asA);
        }

        await using (var asB = Db.App(fixture, tenantB, "single-connection", maxPoolSize: 1))
        {
            seenByB = await CountSalesAsync(asB);
        }

        Assert.Equal(1, seenByA);
        Assert.Equal(2, seenByB);
    }
}

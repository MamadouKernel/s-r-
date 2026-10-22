using Microsoft.EntityFrameworkCore;
using Npgsql;
using Soro.Domain.Common;
using Soro.Domain.Stock;
using Soro.Infrastructure.Persistence;

namespace Soro.Integration.Tests;

[Collection(Postgres.Collection)]
public class StockPersistenceTests(PostgresFixture fixture)
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly DateTimeOffset At = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);

    private static Product NewProduct(TenantId tenant, string? barcode = "6111000000011", string name = "Jus 1L") =>
        Product.Create(
            ProductId.New(), tenant, name, barcode, "Boissons",
            Money.Fcfa(400), Money.Fcfa(600), 5, true, At);

    private async Task SaveAsync(TenantId tenant, params Product[] products)
    {
        await using var db = Db.App(fixture, tenant);
        foreach (var product in products)
        {
            await new ProductRepository(db).AddAsync(product, Ct);
        }

        await new EfUnitOfWork(db).CommitAsync(Ct);
    }

    [Fact]
    public async Task A_product_and_its_stock_round_trip_through_the_database()
    {
        var tenant = TenantId.New();
        var product = NewProduct(tenant);
        var movement = product.Receive(24, "Grossiste", UserId.New(), At);
        await using (var db = Db.App(fixture, tenant))
        {
            await new ProductRepository(db).AddAsync(product, Ct);
            await new StockMovementRepository(db).AddAsync(movement, Ct);
            await new EfUnitOfWork(db).CommitAsync(Ct);
        }

        await using var read = Db.App(fixture, tenant);
        var loaded = await new ProductRepository(read).GetAsync(product.Id, tenant, Ct);
        var history = await new StockMovementRepository(read).ListForProductAsync(product.Id, tenant, 10, Ct);

        Assert.Equal(24, loaded!.QuantityOnHand);
        Assert.Equal("Jus 1L", loaded.Name);
        Assert.Equal(Money.Fcfa(600), loaded.SalePrice);
        Assert.Equal(movement.Id, Assert.Single(history).Id);
    }

    [Fact]
    public async Task A_barcode_is_unique_per_tenant_but_reusable_across_tenants()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        await SaveAsync(tenantA, NewProduct(tenantA, "999"));
        await SaveAsync(tenantB, NewProduct(tenantB, "999"));

        await using var db = Db.App(fixture, tenantA);
        await new ProductRepository(db).AddAsync(NewProduct(tenantA, "999", "Doublon"), Ct);

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => new EfUnitOfWork(db).CommitAsync(Ct));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
    }

    [Fact]
    public async Task Several_products_without_barcode_are_allowed()
    {
        var tenant = TenantId.New();

        await SaveAsync(tenant, NewProduct(tenant, null, "Coupe"), NewProduct(tenant, null, "Brushing"));
    }

    [Fact]
    public async Task A_tenant_never_sees_the_products_of_another()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var product = NewProduct(tenantA);
        await SaveAsync(tenantA, product);

        await using var asB = Db.App(fixture, tenantB);
        var repository = new ProductRepository(asB);

        Assert.Null(await repository.GetAsync(product.Id, tenantA, Ct));
        Assert.Empty(await repository.ListAsync(tenantB, null, true, Ct));
        Assert.Empty(await repository.ListAsync(tenantA, null, true, Ct));
    }

    [Fact]
    public async Task A_stock_movement_cannot_be_changed_or_deleted()
    {
        var tenant = TenantId.New();
        var product = NewProduct(tenant);
        var movement = product.Receive(5, null, UserId.New(), At);
        await using (var db = Db.App(fixture, tenant))
        {
            await new ProductRepository(db).AddAsync(product, Ct);
            await new StockMovementRepository(db).AddAsync(movement, Ct);
            await new EfUnitOfWork(db).CommitAsync(Ct);
        }

        await using var attempt = Db.App(fixture, tenant);
        var failure = await Assert.ThrowsAsync<PostgresException>(() =>
            attempt.Database.ExecuteSqlRawAsync("UPDATE stock_movements SET quantity_delta = 99", Ct));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, failure.SqlState);
    }

    [Fact]
    public async Task A_product_cannot_be_deleted()
    {
        var tenant = TenantId.New();
        await SaveAsync(tenant, NewProduct(tenant));

        await using var attempt = Db.App(fixture, tenant);
        var failure = await Assert.ThrowsAsync<PostgresException>(() =>
            attempt.Database.ExecuteSqlRawAsync("DELETE FROM products", Ct));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, failure.SqlState);
    }

    [Fact]
    public async Task Two_simultaneous_stock_changes_do_not_overwrite_each_other()
    {
        var tenant = TenantId.New();
        var product = NewProduct(tenant);
        await SaveAsync(tenant, product);

        await using var first = Db.App(fixture, tenant);
        await using var second = Db.App(fixture, tenant);
        var firstProduct = (await new ProductRepository(first).GetAsync(product.Id, tenant, Ct))!;
        var secondProduct = (await new ProductRepository(second).GetAsync(product.Id, tenant, Ct))!;
        await new ProductRepository(first).UpdateAsync(
            Apply(firstProduct, p => p.Receive(10, null, UserId.New(), At)), Ct);
        await new ProductRepository(second).UpdateAsync(
            Apply(secondProduct, p => p.Receive(7, null, UserId.New(), At)), Ct);

        await new EfUnitOfWork(first).CommitAsync(Ct);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => new EfUnitOfWork(second).CommitAsync(Ct));

        await using var read = Db.App(fixture, tenant);
        Assert.Equal(10, (await new ProductRepository(read).GetAsync(product.Id, tenant, Ct))!.QuantityOnHand);
    }

    [Fact]
    public async Task Low_stock_and_search_queries_filter_in_the_database()
    {
        var tenant = TenantId.New();
        var low = NewProduct(tenant, "1", "Savon liquide");
        var fine = NewProduct(tenant, "2", "Jus d'orange");
        fine.Receive(50, null, UserId.New(), At);
        await SaveAsync(tenant, low, fine);

        await using var db = Db.App(fixture, tenant);
        var repository = new ProductRepository(db);

        Assert.Equal(low.Id, Assert.Single(await repository.ListLowStockAsync(tenant, Ct)).Id);
        Assert.Equal(fine.Id, Assert.Single(await repository.ListAsync(tenant, "ORANGE", false, Ct)).Id);
        Assert.Equal(low.Id, Assert.Single(await repository.ListAsync(tenant, "1", false, Ct)).Id);
        Assert.Empty(await repository.ListAsync(tenant, "100%", false, Ct));
    }

    private static Product Apply(Product product, Action<Product> change)
    {
        change(product);
        return product;
    }
}

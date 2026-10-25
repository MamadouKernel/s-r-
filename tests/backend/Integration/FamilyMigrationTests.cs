using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Soro.Infrastructure.Persistence;

namespace Soro.Integration.Tests;

/// <summary>La reprise de l'existant (FMT-08) : des produits d'avant les familles deviennent chacun leur famille, sans rien perdre.</summary>
[Collection(Postgres.Collection)]
public class FamilyMigrationTests(PostgresFixture fixture)
{
    private const string PreviousMigration = "20261024123841_AddCustomersAndCredit";

    [Fact]
    public async Task Existing_products_become_their_own_family_with_nothing_changed()
    {
        var database = $"soro_migration_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(fixture.OwnerConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync();
        }

        var connection = new NpgsqlConnectionStringBuilder(fixture.OwnerConnectionString) { Database = database }.ConnectionString;
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        try
        {
            await using var db = new SoroDbContext(PersistenceOptions.Create(connection));
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);
            await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO products (id, tenant_id, name, barcode, category, purchase_price, sale_price, alert_threshold, tracks_stock, created_at, is_active, quantity_on_hand)
                VALUES ({first}, {tenantA}, 'Savon Omo', '111', 'Hygiène', 300, 450, 3, true, now(), true, 12),
                       ({second}, {tenantB}, 'Jus 1L', NULL, NULL, 400, 600, 0, true, now(), false, -2);
                """);

            await migrator.MigrateAsync();

            var rows = await db.Database.SqlQueryRaw<FamilyRow>(
                "SELECT p.id AS \"Id\", p.family_id AS \"FamilyId\", p.format_label AS \"FormatLabel\", p.name AS \"ProductName\", f.name AS \"FamilyName\", f.tenant_id AS \"TenantId\", p.quantity_on_hand AS \"Quantity\" " +
                "FROM products p JOIN product_families f ON f.id = p.family_id ORDER BY p.name").ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, row =>
            {
                Assert.Equal(row.Id, row.FamilyId);
                Assert.Null(row.FormatLabel);
                Assert.Equal(row.ProductName, row.FamilyName);
            });
            Assert.Equal(-2, rows.Single(row => row.Id == second).Quantity);
            Assert.Equal(tenantB, rows.Single(row => row.Id == second).TenantId);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(fixture.OwnerConnectionString);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {database} WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed record FamilyRow(Guid Id, Guid FamilyId, string? FormatLabel, string ProductName, string FamilyName, Guid TenantId, int Quantity);
}

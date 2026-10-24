using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Soro.Application.Reporting;
using Soro.Domain.Cash;
using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Domain.Sales;
using Soro.Domain.Stock;
using Soro.Infrastructure.Persistence;

namespace Soro.Integration.Tests;

/// <summary>RG-11 de bout en bout : prix d'achat moyen, coût retenu à la vente, marge exacte ou estimée (SC-M1 à SC-M9).</summary>
[Collection(Postgres.Collection)]
public sealed class AverageCostTests(PostgresFixture fixture) : IDisposable
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly DateTimeOffset DayStart = new(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);

    private readonly ApiFactory _factory = new(fixture);
    private readonly TenantId _tenant = TenantId.New();

    public void Dispose() => _factory.Dispose();

    private async Task<Product> SeedProductAsync(string name, long purchase, long price = 600)
    {
        var product = Product.Create(ProductId.New(), _tenant, name, null, null, Money.Fcfa(purchase), Money.Fcfa(price), 0, true, DayStart);
        await using var db = Db.App(fixture, _tenant);
        await new ProductRepository(db).AddAsync(product, Ct);
        await new EfUnitOfWork(db).CommitAsync(Ct);
        return product;
    }

    private async Task<Sale> SeedSaleAsync(ProductId product, string name, int quantity, long price, long? cost, int minute = 0)
    {
        var at = DayStart.AddHours(9).AddMinutes(minute);
        var session = CashRegisterSession.Open(
            SessionId.New(), new RegisterAssignment(_tenant, PointOfSaleId.New(), RegisterId.New(), UserId.New()), Money.Zero, at.AddHours(-1));
        var total = Money.Fcfa(price * quantity);
        var sale = session.RecordSale(new SaleDraft(
            SaleId.New(), 1, at,
            [SaleLine.Create(product, name, quantity, Money.Fcfa(price), cost is { } known ? Money.Fcfa(known) : null)],
            Money.Zero, [Payment.Cash(PaymentId.New(), total, total)], null));
        await using var db = Db.App(fixture, _tenant);
        await new SessionRepository(db).AddAsync(session, Ct);
        await new SaleRepository(db).AddAsync(sale, Ct);
        await new EfUnitOfWork(db).CommitAsync(Ct);
        return sale;
    }

    private async Task<ProductRanking> MarginOfAsync(string name, bool includeMargin = true)
    {
        await using var db = Db.App(fixture, _tenant);
        var ranking = await new ReportingQueries(db).GetTopProductsAsync(
            _tenant, DayStart, DayStart.AddDays(1), RankingCriterion.Margin, 20, includeMargin, Ct);
        return ranking.Single(item => item.Name == name);
    }

    [Fact]
    public async Task The_cost_of_a_sale_line_and_the_price_of_a_delivery_survive_the_database()
    {
        var product = await SeedProductAsync("Jus", 400);
        var delivery = product.Receive(10, "Grossiste", UserId.New(), DayStart, null, Money.Fcfa(500));
        await using (var db = Db.App(fixture, _tenant))
        {
            await new StockMovementRepository(db).AddAsync(delivery, Ct);
            await new StockMovementRepository(db).AddAsync(
                product.Receive(5, null, UserId.New(), DayStart.AddMinutes(1)), Ct); // sans prix : null en base
            await new ProductRepository(db).UpdateAsync(product, Ct);
            await new EfUnitOfWork(db).CommitAsync(Ct);
        }

        var sale = await SeedSaleAsync(product.Id, "Jus", 2, 600, 450);

        await using var read = Db.App(fixture, _tenant);
        var stockedSale = await new SaleRepository(read).GetAsync(sale.Id, _tenant, Ct);
        Assert.Equal(Money.Fcfa(450), Assert.Single(stockedSale!.Lines).UnitCost);
        var movements = await new StockMovementRepository(read).ListForProductAsync(product.Id, _tenant, 10, Ct);
        Assert.Equal([null, Money.Fcfa(500)], movements.Where(m => m.Kind == StockMovementKind.Reception).Select(m => m.UnitCost).OrderBy(c => c?.Amount));
        var stored = await new ProductRepository(read).GetAsync(product.Id, _tenant, Ct);
        Assert.Equal(Money.Fcfa(500), stored!.PurchasePrice); // étagère vide : le prix de la livraison
    }

    [Fact]
    public async Task The_margin_uses_the_costs_kept_at_the_sales_and_ignores_a_later_price_change_SC_M6()
    {
        var product = await SeedProductAsync("Jus", 400);
        await SeedSaleAsync(product.Id, "Jus", 10, 600, 400, minute: 1);
        await SeedSaleAsync(product.Id, "Jus", 10, 600, 500, minute: 2);
        await using (var db = Db.App(fixture, _tenant))
        {
            var current = (await new ProductRepository(db).GetAsync(product.Id, _tenant, Ct))!;
            current.Edit(current.Name, null, null, Money.Fcfa(900), current.SalePrice, 0); // le prix d'achat du jour n'a plus rien à voir
            await new ProductRepository(db).UpdateAsync(current, Ct);
            await new EfUnitOfWork(db).CommitAsync(Ct);
        }

        var jus = await MarginOfAsync("Jus");

        Assert.Equal(12_000 - 4_000 - 5_000, jus.MarginFcfa);
        Assert.False(jus.MarginIsEstimate);
    }

    [Fact]
    public async Task Sales_without_a_kept_cost_are_estimated_with_the_current_price_and_flagged_SC_M7()
    {
        var product = await SeedProductAsync("Savon", 100, 250);
        await SeedSaleAsync(product.Id, "Savon", 10, 250, null, minute: 1);

        var savon = await MarginOfAsync("Savon");

        Assert.Equal(2_500 - 1_000, savon.MarginFcfa);
        Assert.True(savon.MarginIsEstimate);
    }

    [Fact]
    public async Task One_line_without_a_cost_makes_the_whole_margin_an_estimate()
    {
        var product = await SeedProductAsync("Jus", 500);
        await SeedSaleAsync(product.Id, "Jus", 10, 600, 400, minute: 1); // exact : 2 000
        await SeedSaleAsync(product.Id, "Jus", 10, 600, null, minute: 2); // estimé avec 500 : 1 000

        var jus = await MarginOfAsync("Jus");

        Assert.Equal(3_000, jus.MarginFcfa);
        Assert.True(jus.MarginIsEstimate);
    }

    [Fact]
    public async Task A_product_removed_from_the_catalogue_keeps_an_exact_margin_when_its_lines_have_costs()
    {
        var ghost = ProductId.New();
        await SeedSaleAsync(ghost, "Fantôme", 2, 100, 30);

        var ranked = await MarginOfAsync("Fantôme");

        Assert.Equal(200 - 60, ranked.MarginFcfa);
        Assert.False(ranked.MarginIsEstimate);
    }

    [Fact]
    public async Task A_product_removed_from_the_catalogue_without_costs_has_an_unknown_margin()
    {
        await SeedSaleAsync(ProductId.New(), "Fantôme", 2, 100, null);

        var ranked = await MarginOfAsync("Fantôme");

        Assert.Null(ranked.MarginFcfa);
    }

    [Fact]
    public async Task Without_the_right_to_see_purchase_prices_neither_margin_nor_estimate_leaks()
    {
        var product = await SeedProductAsync("Savon", 100, 250);
        await SeedSaleAsync(product.Id, "Savon", 10, 250, null, minute: 1);

        var savon = await MarginOfAsync("Savon", includeMargin: false);

        Assert.Null(savon.MarginFcfa);
        Assert.False(savon.MarginIsEstimate);
    }

    [Fact]
    public async Task Through_the_api_receptions_average_the_price_sales_keep_it_and_the_history_shows_each_delivery()
    {
        using var manager = _factory.ClientFor(_tenant, UserId.New(), Role.Manager);
        var id = Guid.NewGuid();
        await manager.PostAsJsonAsync("/api/v1/products", new
        {
            productId = id, name = "Jus d'orange 1L", barcode = "611", category = "Boissons",
            purchasePriceFcfa = 400, salePriceFcfa = 600, alertThreshold = 5, tracksStock = true,
        });
        var first = await manager.PostAsJsonAsync($"/api/v1/products/{id}/receptions", new { movementId = Guid.NewGuid(), quantity = 10 });
        Assert.Equal(400, (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("purchasePriceFcfa").GetInt64());

        var second = await manager.PostAsJsonAsync(
            $"/api/v1/products/{id}/receptions", new { movementId = Guid.NewGuid(), quantity = 10, purchasePriceFcfa = 500 });

        var body = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(450, body.GetProperty("purchasePriceFcfa").GetInt64()); // SC-M1
        Assert.Equal(20, body.GetProperty("quantityOnHand").GetInt32());
        var list = await manager.GetFromJsonAsync<JsonElement>("/api/v1/products?search=jus");
        Assert.Equal(450, Assert.Single(list.EnumerateArray()).GetProperty("purchasePriceFcfa").GetInt64());
        var history = await manager.GetFromJsonAsync<JsonElement>($"/api/v1/products/{id}/movements");
        var costs = history.EnumerateArray().Select(m => m.GetProperty("unitCostFcfa").ValueKind == JsonValueKind.Null ? (long?)null : m.GetProperty("unitCostFcfa").GetInt64()).ToList();
        Assert.Contains(500L, costs);
        Assert.Contains(null, costs);
    }

    [Fact]
    public async Task A_replayed_reception_is_acknowledged_without_a_second_average_and_a_negative_price_is_refused()
    {
        using var manager = _factory.ClientFor(_tenant, UserId.New(), Role.Manager);
        var id = Guid.NewGuid();
        await manager.PostAsJsonAsync("/api/v1/products", new
        {
            productId = id, name = "Jus", barcode = (string?)null, category = (string?)null,
            purchasePriceFcfa = 400, salePriceFcfa = 600, alertThreshold = 0, tracksStock = true,
        });
        var movement = Guid.NewGuid();
        await manager.PostAsJsonAsync($"/api/v1/products/{id}/receptions", new { movementId = movement, quantity = 10 });
        await manager.PostAsJsonAsync($"/api/v1/products/{id}/receptions", new { movementId = movement, quantity = 10, purchasePriceFcfa = 500 });

        var replay = await manager.PostAsJsonAsync($"/api/v1/products/{id}/receptions", new { movementId = movement, quantity = 10, purchasePriceFcfa = 500 });
        var negative = await manager.PostAsJsonAsync($"/api/v1/products/{id}/receptions", new { movementId = Guid.NewGuid(), quantity = 5, purchasePriceFcfa = -1 });

        // Le premier envoi a déjà posé le mouvement : le « prix 500 » du second n'a donc jamais été appliqué, ni rejoué.
        var body = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("alreadyProcessed").GetBoolean());
        Assert.Equal(400, body.GetProperty("purchasePriceFcfa").GetInt64());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, negative.StatusCode);
    }

    [Fact]
    public async Task A_sale_recorded_through_the_api_keeps_the_average_of_that_moment_and_the_margin_is_exact()
    {
        using var manager = _factory.ClientFor(_tenant, UserId.New(), Role.Manager);
        var id = Guid.NewGuid();
        await manager.PostAsJsonAsync("/api/v1/products", new
        {
            productId = id, name = "Jus", barcode = (string?)null, category = (string?)null,
            purchasePriceFcfa = 400, salePriceFcfa = 600, alertThreshold = 0, tracksStock = true,
        });
        await manager.PostAsJsonAsync($"/api/v1/products/{id}/receptions", new { movementId = Guid.NewGuid(), quantity = 20, purchasePriceFcfa = 400 });
        var session = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await manager.PostAsJsonAsync("/api/v1/register-sessions", new
        {
            sessionId = session, pointOfSaleId = Guid.NewGuid(), registerId = Guid.NewGuid(), openingFloatFcfa = 0, openedAt = now.AddHours(-2),
        });

        async Task SellAsync(long sequence) => Assert.Equal(HttpStatusCode.OK, (await manager.PostAsJsonAsync("/api/v1/sales", new
        {
            saleId = Guid.NewGuid(), sessionId = session, localSequence = sequence, at = now.AddMinutes(-sequence),
            lines = new[] { new { productId = id, name = "Jus", quantity = 2, unitPriceFcfa = 600 } },
            discountFcfa = 0,
            payments = new[] { new { paymentId = Guid.NewGuid(), method = "Cash", amountFcfa = 1_200, tenderedFcfa = (long?)1_200, reference = (string?)null } },
        })).StatusCode);

        await SellAsync(1); // coût 400
        await manager.PostAsJsonAsync($"/api/v1/products/{id}/receptions", new { movementId = Guid.NewGuid(), quantity = 20, purchasePriceFcfa = 600 }); // moyenne : (18×400 + 20×600)/38 = 505,26 → 505
        await SellAsync(2); // coût 505

        var from = Uri.EscapeDataString(now.AddHours(-1).ToString("O"));
        var to = Uri.EscapeDataString(now.AddHours(1).ToString("O"));
        var ranking = await manager.GetFromJsonAsync<JsonElement>($"/api/v1/reports/top-products?by=margin&from={from}&to={to}");
        var jus = ranking.EnumerateArray().Single();
        Assert.Equal(2_400 - (2 * 400 + 2 * 505), jus.GetProperty("marginFcfa").GetInt64());
        Assert.False(jus.GetProperty("marginIsEstimate").GetBoolean());
    }
}

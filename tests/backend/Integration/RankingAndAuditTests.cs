using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Soro.Application.Common;
using Soro.Application.Reporting;
using Soro.Domain.Cash;
using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Domain.Sales;
using Soro.Domain.Stock;
using Soro.Infrastructure.Persistence;

namespace Soro.Integration.Tests;

[Collection(Postgres.Collection)]
public sealed class RankingAndAuditTests(PostgresFixture fixture) : IDisposable
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly DateTimeOffset DayStart = new(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);

    private readonly ApiFactory _factory = new(fixture);
    private readonly TenantId _tenant = TenantId.New();

    public void Dispose() => _factory.Dispose();

    private static string Iso(DateTimeOffset value) => Uri.EscapeDataString(value.ToString("O"));

    private string Period => $"from={Iso(DayStart)}&to={Iso(DayStart.AddDays(1))}";

    private async Task<(ProductId Id, string Name)> SeedProductAsync(TenantId tenant, string name, long price, long purchase)
    {
        var product = Product.Create(ProductId.New(), tenant, name, null, null, Money.Fcfa(purchase), Money.Fcfa(price), 0, true, DayStart);
        await using var db = Db.App(fixture, tenant);
        await new ProductRepository(db).AddAsync(product, Ct);
        await new EfUnitOfWork(db).CommitAsync(Ct);
        return (product.Id, name);
    }

    /// <summary>Une vente validée d'un produit, dans une session propre, pour la date donnée.</summary>
    private async Task<Sale> SeedSaleAsync(TenantId tenant, ProductId product, string name, int quantity, long price, DateTimeOffset at, bool cancel = false)
    {
        var session = CashRegisterSession.Open(
            SessionId.New(), new RegisterAssignment(tenant, PointOfSaleId.New(), RegisterId.New(), UserId.New()), Money.Fcfa(0), at.AddHours(-1));
        var total = Money.Fcfa(price * quantity);
        var sale = session.RecordSale(new SaleDraft(
            SaleId.New(), 1, at, [SaleLine.Create(product, name, quantity, Money.Fcfa(price))], Money.Zero,
            [Payment.Cash(PaymentId.New(), total, total)], null));

        await using var db = Db.App(fixture, tenant);
        await new SessionRepository(db).AddAsync(session, Ct);
        await new SaleRepository(db).AddAsync(sale, Ct);
        await new EfUnitOfWork(db).CommitAsync(Ct);

        if (cancel)
        {
            await using var cancelDb = Db.App(fixture, tenant);
            var repository = new SaleRepository(cancelDb);
            sale = (await repository.GetAsync(sale.Id, tenant, Ct))!.Cancel(UserId.New(), "Erreur", at.AddMinutes(5));
            await repository.UpdateAsync(sale, Ct);
            await new EfUnitOfWork(cancelDb).CommitAsync(Ct);
        }

        return sale;
    }

    /// <summary>
    /// Jus (10 × 600, achat 400) : 6 000 de chiffre d'affaires, 2 000 de marge. Savon (30 × 250, achat 100) : 7 500, 4 500.
    /// Coupe (4 × 1 500, achat 0) : 6 000, 6 000. Une vente annulée de 100 Jus ne compte nulle part. Un « Fantôme » sans fiche : 2 × 100.
    /// Quantité : Savon, Jus, Fantôme, Coupe. Chiffre d'affaires : Savon, puis Coupe avant Jus (égalité, nom). Marge : Coupe, Savon, Jus.
    /// </summary>
    private async Task SeedCatalogueAndSalesAsync(TenantId? tenant = null)
    {
        var owner = tenant ?? _tenant;
        var jus = await SeedProductAsync(owner, "Jus", 600, 400);
        var savon = await SeedProductAsync(owner, "Savon", 250, 100);
        var coupe = await SeedProductAsync(owner, "Coupe", 1_500, 0);
        var at = DayStart.AddHours(9);
        await SeedSaleAsync(owner, jus.Id, jus.Name, 6, 600, at);
        await SeedSaleAsync(owner, jus.Id, jus.Name, 4, 600, at.AddMinutes(10));
        await SeedSaleAsync(owner, savon.Id, savon.Name, 30, 250, at.AddMinutes(20));
        await SeedSaleAsync(owner, coupe.Id, coupe.Name, 4, 1_500, at.AddMinutes(30));
        await SeedSaleAsync(owner, jus.Id, jus.Name, 100, 600, at.AddMinutes(40), cancel: true);
        await SeedSaleAsync(owner, ProductId.New(), "Fantôme", 2, 100, at.AddMinutes(50));
    }

    private async Task<IReadOnlyList<ProductRanking>> RankAsync(RankingCriterion by, int limit = 10, bool includeMargin = true, TenantId? tenant = null)
    {
        var owner = tenant ?? _tenant;
        await using var db = Db.App(fixture, owner);
        return await new ReportingQueries(db).GetTopProductsAsync(owner, DayStart, DayStart.AddDays(1), by, limit, includeMargin, Ct);
    }

    [Fact]
    public async Task The_three_rankings_give_three_different_correct_orders_and_ignore_cancelled_sales()
    {
        await SeedCatalogueAndSalesAsync();

        var byQuantity = await RankAsync(RankingCriterion.Quantity);
        var byRevenue = await RankAsync(RankingCriterion.Revenue);
        var byMargin = await RankAsync(RankingCriterion.Margin);

        Assert.Equal(["Savon", "Jus", "Coupe", "Fantôme"], byQuantity.Select(item => item.Name).Take(2).Concat(byQuantity.Skip(2).Select(item => item.Name).Order()));
        Assert.Equal([30, 10], byQuantity.Take(2).Select(item => item.Quantity));
        Assert.Equal(["Savon", "Coupe", "Jus"], byRevenue.Take(3).Select(item => item.Name));
        Assert.Equal(["Coupe", "Savon", "Jus"], byMargin.Take(3).Select(item => item.Name));
        Assert.Equal([6_000L, 4_500L, 2_000L], byMargin.Take(3).Select(item => item.MarginFcfa!.Value));
        Assert.Equal(6_000, byRevenue.Single(item => item.Name == "Jus").RevenueFcfa); // les 100 annulés ne comptent pas
        Assert.Equal(10, byRevenue.Single(item => item.Name == "Jus").Quantity);
    }

    [Fact]
    public async Task A_product_without_a_sheet_has_no_margin_instead_of_a_wrong_one_and_sinks_in_the_margin_ranking()
    {
        await SeedCatalogueAndSalesAsync();

        var byMargin = await RankAsync(RankingCriterion.Margin);

        var ghost = byMargin.Single(item => item.Name == "Fantôme");
        Assert.Null(ghost.MarginFcfa);
        Assert.Equal("Fantôme", byMargin[^1].Name);
        Assert.Equal(200, ghost.RevenueFcfa);
    }

    [Fact]
    public async Task Without_the_right_to_see_purchase_prices_the_margin_is_never_read()
    {
        await SeedCatalogueAndSalesAsync();

        var ranking = await RankAsync(RankingCriterion.Revenue, includeMargin: false);

        Assert.All(ranking, item => Assert.Null(item.MarginFcfa));
    }

    [Fact]
    public async Task The_limit_the_period_and_the_tenant_are_respected()
    {
        await SeedCatalogueAndSalesAsync();
        var other = TenantId.New();
        await SeedCatalogueAndSalesAsync(other);
        var outsideProduct = await SeedProductAsync(_tenant, "Hors période", 100, 10);
        await SeedSaleAsync(_tenant, outsideProduct.Id, "Hors période", 50, 100, DayStart.AddDays(3));

        var top2 = await RankAsync(RankingCriterion.Quantity, limit: 2);
        var strangerView = await RankAsync(RankingCriterion.Quantity, tenant: TenantId.New());

        Assert.Equal(2, top2.Count);
        Assert.DoesNotContain(top2, item => item.Name == "Hors période");
        Assert.Empty(strangerView); // la sécurité par ligne de la base masque les autres commerces
    }

    [Fact]
    public async Task The_api_ranks_for_the_owner_the_accountant_and_refuses_margin_and_cashier()
    {
        await SeedCatalogueAndSalesAsync();
        using var owner = _factory.ClientFor(_tenant, UserId.New(), Role.Owner);
        using var accountant = _factory.ClientFor(_tenant, UserId.New(), Role.Accountant);
        using var cashier = _factory.ClientFor(_tenant, UserId.New(), Role.Cashier);

        var margin = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/top-products?{Period}&by=margin&limit=3");
        var revenue = await accountant.GetFromJsonAsync<JsonElement>($"/api/v1/reports/top-products?{Period}&by=revenue");
        var accountantMargin = await accountant.GetAsync($"/api/v1/reports/top-products?{Period}&by=margin");
        var cashierRefused = await cashier.GetAsync($"/api/v1/reports/top-products?{Period}");
        var noPeriod = await owner.GetAsync("/api/v1/reports/top-products");
        var unknownCriterion = await owner.GetAsync($"/api/v1/reports/top-products?{Period}&by=poids");
        var upperCase = await owner.GetAsync($"/api/v1/reports/top-products?{Period}&by=REVENUE");

        Assert.Equal(["Coupe", "Savon", "Jus"], margin.EnumerateArray().Select(item => item.GetProperty("name").GetString()));
        Assert.Equal(6_000, margin[0].GetProperty("marginFcfa").GetInt64());
        Assert.All(revenue.EnumerateArray(), item => Assert.Equal(JsonValueKind.Null, item.GetProperty("marginFcfa").ValueKind));
        Assert.Equal(HttpStatusCode.Forbidden, accountantMargin.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, cashierRefused.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noPeriod.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unknownCriterion.StatusCode);
        Assert.Equal(HttpStatusCode.OK, upperCase.StatusCode); // la casse n'a pas d'importance
    }

    // ---- Journal des actions sensibles -------------------------------------------------------------

    private async Task SeedAuditAsync(TenantId tenant, string action, int minutes, string detail = "détail", UserId? user = null)
    {
        await using var db = Db.App(fixture, tenant);
        await new AuditTrail(db).RecordAsync(
            new AuditEntry(tenant, user ?? UserId.New(), action, Guid.NewGuid(), detail, DayStart.AddHours(8).AddMinutes(minutes)), Ct);
        await new EfUnitOfWork(db).CommitAsync(Ct);
    }

    [Fact]
    public async Task The_journal_is_paginated_without_gaps_or_duplicates_newest_first_with_a_correct_has_more()
    {
        for (var index = 0; index < 120; index++)
        {
            await SeedAuditAsync(_tenant, AuditActions.SaleCancelled, index, $"annulation {index}");
        }

        await using var db = Db.App(fixture, _tenant);
        var queries = new AuditLogQueries(db);
        var first = await queries.ListAsync(_tenant, new AuditLogFilter(null, null, null, 50, 0), Ct);
        var second = await queries.ListAsync(_tenant, new AuditLogFilter(null, null, null, 50, 50), Ct);
        var third = await queries.ListAsync(_tenant, new AuditLogFilter(null, null, null, 50, 100), Ct);

        Assert.Equal([50, 50, 20], [first.Entries.Count, second.Entries.Count, third.Entries.Count]);
        Assert.Equal([true, true, false], [first.HasMore, second.HasMore, third.HasMore]);
        var all = first.Entries.Concat(second.Entries).Concat(third.Entries).ToList();
        Assert.Equal(120, all.Select(entry => entry.Id).Distinct().Count());
        Assert.Equal("annulation 119", all[0].Detail);
        Assert.Equal("annulation 0", all[^1].Detail);
        Assert.Equal(all.OrderByDescending(entry => entry.At).Select(entry => entry.Id), all.Select(entry => entry.Id));
    }

    [Fact]
    public async Task A_page_that_ends_exactly_on_the_last_entry_says_there_is_no_more()
    {
        for (var index = 0; index < 50; index++)
        {
            await SeedAuditAsync(_tenant, AuditActions.StockLoss, index);
        }

        await using var db = Db.App(fixture, _tenant);
        var page = await new AuditLogQueries(db).ListAsync(_tenant, new AuditLogFilter(null, null, null, 50, 0), Ct);

        Assert.Equal(50, page.Entries.Count);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task The_category_and_the_period_filter_the_journal()
    {
        await SeedAuditAsync(_tenant, AuditActions.DiscountApproved, 1);
        await SeedAuditAsync(_tenant, AuditActions.SaleCancelled, 2);
        await SeedAuditAsync(_tenant, AuditActions.CancelRefused, 3);
        await SeedAuditAsync(_tenant, AuditActions.ProductUpdated, 4);
        await SeedAuditAsync(_tenant, AuditActions.StockLoss, 5);
        await SeedAuditAsync(_tenant, AuditActions.UserDisabled, 6);

        await using var db = Db.App(fixture, _tenant);
        var queries = new AuditLogQueries(db);
        var sales = await queries.ListAsync(_tenant, new AuditLogFilter(null, null, ["sale."], 50, 0), Ct);
        var team = await queries.ListAsync(_tenant, new AuditLogFilter(null, null, ["user."], 50, 0), Ct);
        var window = await queries.ListAsync(
            _tenant, new AuditLogFilter(DayStart.AddHours(8).AddMinutes(2), DayStart.AddHours(8).AddMinutes(5), null, 50, 0), Ct);

        Assert.Equal(3, sales.Entries.Count);
        Assert.All(sales.Entries, entry => Assert.StartsWith("sale.", entry.Action));
        Assert.Equal(AuditActions.UserDisabled, Assert.Single(team.Entries).Action);
        Assert.Equal([AuditActions.ProductUpdated, AuditActions.CancelRefused, AuditActions.SaleCancelled], window.Entries.Select(entry => entry.Action)); // [début, fin[
    }

    [Fact]
    public async Task Another_tenant_never_sees_these_entries()
    {
        await SeedAuditAsync(_tenant, AuditActions.SaleCancelled, 1);
        var stranger = TenantId.New();

        await using var db = Db.App(fixture, stranger);
        var page = await new AuditLogQueries(db).ListAsync(stranger, new AuditLogFilter(null, null, null, 50, 0), Ct);
        var asked = await new AuditLogQueries(db).ListAsync(_tenant, new AuditLogFilter(null, null, null, 50, 0), Ct);

        Assert.Empty(page.Entries);
        Assert.Empty(asked.Entries);
    }

    [Fact]
    public async Task The_api_serves_the_journal_to_the_owner_and_the_accountant_only()
    {
        var author = UserId.New();
        await SeedAuditAsync(_tenant, AuditActions.SaleCancelled, 1, "Erreur de saisie", author);
        await SeedAuditAsync(_tenant, AuditActions.ProductUpdated, 2);
        using var owner = _factory.ClientFor(_tenant, UserId.New(), Role.Owner);
        using var accountant = _factory.ClientFor(_tenant, UserId.New(), Role.Accountant);

        var all = await owner.GetFromJsonAsync<JsonElement>("/api/v1/audit-log");
        var sales = await accountant.GetFromJsonAsync<JsonElement>("/api/v1/audit-log?category=sales");

        Assert.Equal(2, all.GetProperty("entries").GetArrayLength());
        Assert.False(all.GetProperty("hasMore").GetBoolean());
        var entry = Assert.Single(sales.GetProperty("entries").EnumerateArray());
        Assert.Equal("sale.cancelled", entry.GetProperty("action").GetString());
        Assert.Equal(author.Value.ToString(), entry.GetProperty("userId").GetString());
        Assert.Equal("Erreur de saisie", entry.GetProperty("detail").GetString());

        foreach (var role in new[] { Role.Manager, Role.Cashier, Role.StockKeeper })
        {
            using var refused = _factory.ClientFor(_tenant, UserId.New(), role);
            Assert.Equal(HttpStatusCode.Forbidden, (await refused.GetAsync("/api/v1/audit-log")).StatusCode);
        }
    }

    [Fact]
    public async Task The_api_refuses_an_unknown_category_and_an_inverted_period_with_a_422()
    {
        using var owner = _factory.ClientFor(_tenant, UserId.New(), Role.Owner);

        var unknown = await owner.GetAsync("/api/v1/audit-log?category=secrets");
        var inverted = await owner.GetAsync($"/api/v1/audit-log?from={Iso(DayStart.AddDays(2))}&to={Iso(DayStart)}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, inverted.StatusCode);
    }

    [Fact]
    public async Task The_accountant_can_read_the_directory_to_name_the_authors_but_the_cashier_cannot()
    {
        using var accountant = _factory.ClientFor(_tenant, UserId.New(), Role.Accountant);
        using var cashier = _factory.ClientFor(_tenant, UserId.New(), Role.Cashier);

        Assert.Equal(HttpStatusCode.OK, (await accountant.GetAsync("/api/v1/users/directory")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("/api/v1/users/directory")).StatusCode);
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Soro.Application.Reporting;
using Soro.Domain.Cash;
using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Domain.Sales;
using Soro.Infrastructure.Persistence;

namespace Soro.Integration.Tests;

[Collection(Postgres.Collection)]
public sealed class ReportingTests(PostgresFixture fixture) : IDisposable
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly DateTimeOffset DayStart =
        new(Samples.Morning.Year, Samples.Morning.Month, Samples.Morning.Day, 0, 0, 0, TimeSpan.Zero);

    private readonly ApiFactory _factory = new(fixture);
    private readonly TenantId _tenant = TenantId.New();

    public void Dispose() => _factory.Dispose();

    private static Sale RecordAt(CashRegisterSession session, int sequence, long price, Payment payment) =>
        session.RecordSale(new SaleDraft(
            SaleId.New(),
            sequence,
            Samples.Morning.AddMinutes(sequence),
            [SaleLine.Create(ProductId.New(), "Produit", 1, Money.Fcfa(price))],
            Money.Zero,
            [payment],
            null));

    /// <summary>
    /// Une session clôturée avec un manque de 500 FCFA : une vente espèces de 1 000, une vente espèces de 2 000
    /// annulée ensuite, une vente Orange Money de 1 500. Espèces attendues : 10 000 + 1 000 + 2 000 = 13 000
    /// (l'effet d'une annulation sur la caisse reste à définir, T017), comptées : 12 500.
    /// </summary>
    private async Task<(CashRegisterSession Session, Sale Cash, Sale Cancelled, Sale Mobile)> SeedDayAsync(TenantId tenant)
    {
        var session = Samples.Session(tenant);
        var cash = RecordAt(session, 1, 1_000, Payment.Cash(PaymentId.New(), Money.Fcfa(1_000), Money.Fcfa(1_000)));
        var cancelled = RecordAt(session, 2, 2_000, Payment.Cash(PaymentId.New(), Money.Fcfa(2_000), Money.Fcfa(2_000)));
        var mobile = RecordAt(
            session, 3, 1_500,
            Payment.MobileMoney(PaymentId.New(), PaymentMethod.OrangeMoney, Money.Fcfa(1_500), "MP-2"));
        session.Close(Money.Fcfa(12_500), Samples.Morning.AddHours(9));

        await using var db = Db.App(fixture, tenant);
        await new SessionRepository(db).AddAsync(session, Ct);
        foreach (var sale in new[] { cash, cancelled, mobile })
        {
            await new SaleRepository(db).AddAsync(sale, Ct);
        }

        await new EfUnitOfWork(db).CommitAsync(Ct);

        await using var cancelDb = Db.App(fixture, tenant);
        var repository = new SaleRepository(cancelDb);
        cancelled = (await repository.GetAsync(cancelled.Id, tenant, Ct))!
            .Cancel(UserId.New(), "Erreur de saisie", Samples.Morning.AddHours(2));
        await repository.UpdateAsync(cancelled, Ct);
        await new EfUnitOfWork(cancelDb).CommitAsync(Ct);
        return (session, cash, cancelled, mobile);
    }

    [Fact]
    public async Task The_activity_report_counts_validated_cancelled_payments_cashiers_and_variance()
    {
        var (session, _, _, _) = await SeedDayAsync(_tenant);
        await using var db = Db.App(fixture, _tenant);

        var report = await new ReportingQueries(db).GetActivityAsync(_tenant, DayStart, DayStart.AddDays(1), Ct);

        Assert.Equal(2, report.SalesCount);
        Assert.Equal(2_500, report.NetTotalFcfa);
        Assert.Equal(1, report.CancelledCount);
        Assert.Equal(2_000, report.CancelledTotalFcfa);
        Assert.Equal(
            [(PaymentMethod.OrangeMoney, 1_500L, 1), (PaymentMethod.Cash, 1_000L, 1)],
            report.ByPaymentMethod.Select(item => (item.Method, item.AmountFcfa, item.Count)));
        var cashier = Assert.Single(report.ByCashier);
        Assert.Equal(session.Assignment.Cashier.Value, cashier.CashierId);
        Assert.Equal(2, cashier.SalesCount);
        Assert.Equal(2_500, cashier.NetTotalFcfa);
        Assert.Equal(1, report.SessionsClosed);
        Assert.Equal(500, report.ShortfallFcfa);
        Assert.Equal(0, report.SurplusFcfa);
    }

    [Fact]
    public async Task An_empty_period_gives_a_zero_report()
    {
        await SeedDayAsync(_tenant);
        await using var db = Db.App(fixture, _tenant);

        var report = await new ReportingQueries(db).GetActivityAsync(
            _tenant, DayStart.AddDays(10), DayStart.AddDays(11), Ct);

        Assert.Equal(0, report.SalesCount);
        Assert.Equal(0, report.NetTotalFcfa);
        Assert.Empty(report.ByPaymentMethod);
        Assert.Empty(report.ByCashier);
    }

    [Fact]
    public async Task The_sale_list_is_newest_first_filterable_and_paginated()
    {
        var (session, cash, cancelled, mobile) = await SeedDayAsync(_tenant);
        await using var db = Db.App(fixture, _tenant);
        var queries = new ReportingQueries(db);

        var all = await queries.ListSalesAsync(_tenant, new SalesFilter(null, null, null, null, true, 50, 0), Ct);
        var validated = await queries.ListSalesAsync(_tenant, new SalesFilter(null, null, null, null, false, 50, 0), Ct);
        var secondPage = await queries.ListSalesAsync(_tenant, new SalesFilter(null, null, session.Id, null, true, 1, 1), Ct);
        var otherCashier = await queries.ListSalesAsync(
            _tenant, new SalesFilter(null, null, null, UserId.New(), true, 50, 0), Ct);

        Assert.Equal([mobile.Id.Value, cancelled.Id.Value, cash.Id.Value], all.Select(sale => sale.Id));
        Assert.Equal([mobile.Id.Value, cash.Id.Value], validated.Select(sale => sale.Id));
        Assert.Equal(cancelled.Id.Value, Assert.Single(secondPage).Id);
        Assert.Equal([PaymentMethod.OrangeMoney], all[0].PaymentMethods);
        Assert.Empty(otherCashier);
    }

    [Fact]
    public async Task The_sale_detail_carries_lines_payments_and_the_cancellation()
    {
        var (_, _, cancelled, mobile) = await SeedDayAsync(_tenant);
        await using var db = Db.App(fixture, _tenant);
        var queries = new ReportingQueries(db);

        var mobileDetail = (await queries.GetSaleAsync(_tenant, mobile.Id, Ct))!;
        var cancelledDetail = (await queries.GetSaleAsync(_tenant, cancelled.Id, Ct))!;

        Assert.Equal(1_500, mobileDetail.NetTotalFcfa);
        Assert.Equal("MP-2", Assert.Single(mobileDetail.Payments).Reference);
        Assert.Equal("Produit", Assert.Single(mobileDetail.Lines).ProductName);
        Assert.Null(mobileDetail.Cancellation);
        Assert.Equal(SaleStatus.Cancelled, cancelledDetail.Status);
        Assert.Equal("Erreur de saisie", cancelledDetail.Cancellation!.Reason);
    }

    [Fact]
    public async Task Sessions_expose_the_expected_counted_and_variance_and_filter_on_variance()
    {
        var (session, _, _, _) = await SeedDayAsync(_tenant);
        var balanced = Samples.Session(_tenant);
        balanced.Close(Money.Fcfa(10_000), Samples.Morning.AddHours(1));
        await using (var seed = Db.App(fixture, _tenant))
        {
            await new SessionRepository(seed).AddAsync(balanced, Ct);
            await new EfUnitOfWork(seed).CommitAsync(Ct);
        }

        await using var db = Db.App(fixture, _tenant);
        var queries = new ReportingQueries(db);

        var withVariance = await queries.ListSessionsAsync(
            _tenant, new SessionsFilter(null, null, null, true, 50, 0), Ct);
        var closed = await queries.ListSessionsAsync(
            _tenant, new SessionsFilter(null, null, SessionStatus.Closed, false, 50, 0), Ct);

        var found = Assert.Single(withVariance);
        Assert.Equal(session.Id.Value, found.Id);
        Assert.Equal(13_000, found.ExpectedCashFcfa);
        Assert.Equal(12_500, found.CountedCashFcfa);
        Assert.Equal(-500, found.VarianceFcfa);
        Assert.Equal(2, closed.Count);
    }

    [Fact]
    public async Task Another_tenant_sees_nothing()
    {
        var (session, cash, _, _) = await SeedDayAsync(_tenant);
        var stranger = TenantId.New();
        await using var db = Db.App(fixture, stranger);
        var queries = new ReportingQueries(db);

        Assert.Empty(await queries.ListSalesAsync(stranger, new SalesFilter(null, null, null, null, true, 50, 0), Ct));
        Assert.Empty(await queries.ListSalesAsync(stranger, new SalesFilter(null, null, session.Id, null, true, 50, 0), Ct));
        Assert.Null(await queries.GetSaleAsync(stranger, cash.Id, Ct));
        Assert.Empty(await queries.ListSessionsAsync(stranger, new SessionsFilter(null, null, null, false, 50, 0), Ct));
        Assert.Equal(0, (await queries.GetActivityAsync(stranger, DayStart, DayStart.AddDays(1), Ct)).SalesCount);
    }

    [Fact]
    public async Task The_api_serves_the_read_endpoints_to_the_owner_and_refuses_the_cashier()
    {
        var (_, cash, _, _) = await SeedDayAsync(_tenant);
        var from = Uri.EscapeDataString(DayStart.ToString("O"));
        var to = Uri.EscapeDataString(DayStart.AddDays(1).ToString("O"));
        using var owner = _factory.ClientFor(_tenant, UserId.New(), Role.Owner);
        using var cashier = _factory.ClientFor(_tenant, UserId.New(), Role.Cashier);

        var report = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/activity?from={from}&to={to}");
        var sales = await owner.GetFromJsonAsync<JsonElement>("/api/v1/sales?includeCancelled=false");
        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/sales/{cash.Id.Value}");
        var sessions = await owner.GetFromJsonAsync<JsonElement>("/api/v1/register-sessions?status=Closed&onlyWithVariance=true");

        Assert.Equal(2, report.GetProperty("salesCount").GetInt32());
        Assert.Equal(2, sales.GetArrayLength());
        Assert.Equal("Cash", detail.GetProperty("payments")[0].GetProperty("method").GetString());
        Assert.Equal(1, sessions.GetArrayLength());
        // Le caissier retrouve ses propres tickets, pas ceux des autres, et aucun rapport.
        Assert.Equal(0, (await cashier.GetFromJsonAsync<JsonElement>("/api/v1/sales")).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync($"/api/v1/sales/{cash.Id.Value}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync($"/api/v1/reports/activity?from={from}&to={to}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("/api/v1/register-sessions")).StatusCode);
    }

    [Fact]
    public async Task The_api_rejects_an_invalid_period_and_an_unknown_sale()
    {
        using var owner = _factory.ClientFor(_tenant, UserId.New(), Role.Owner);
        var from = Uri.EscapeDataString(DayStart.AddDays(2).ToString("O"));
        var to = Uri.EscapeDataString(DayStart.ToString("O"));

        var inverted = await owner.GetAsync($"/api/v1/reports/activity?from={from}&to={to}");
        var missing = await owner.GetAsync($"/api/v1/sales/{Guid.NewGuid()}");
        var noPeriod = await owner.GetAsync("/api/v1/reports/activity");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, inverted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noPeriod.StatusCode);
    }
}

using Soro.Application.Common;
using Soro.Application.Export;
using Soro.Application.Reporting;
using Soro.Domain.Identity;
using Soro.Domain.Sales;
using Soro.Domain.Stock;

namespace Soro.Application.Tests;

public class ExportTests
{
    private static async Task<TestWorld> OwnerWorldAsync()
    {
        var world = new TestWorld();
        await world.GivenCommerceAsync();
        world.ActAs(world.Owner, Role.Owner);
        return world;
    }

    private static DateOnly Today(TestWorld world) => DateOnly.FromDateTime(world.Clock.UtcNow.UtcDateTime);

    [Fact]
    public async Task The_owner_gets_a_named_xlsx_with_the_twelve_last_months_by_default()
    {
        var world = await OwnerWorldAsync();
        world.ExportQueries.Counts = new ExportCounts(3, 2, 1, 10, 25, 7);

        var result = await world.ExportData.HandleAsync(new ExportDataQuery(null, null));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal($"soro-export-{Today(world):yyyy-MM-dd}.xlsx", result.Value!.FileName);
        Assert.Equal(world.ExportWriter.Bytes, result.Value.Bytes);
        var period = world.ExportWriter.Last!.Period;
        Assert.Equal(Today(world), period.To);
        Assert.Equal(Today(world).AddMonths(-12), period.From);
        Assert.Equal("Supérette du Carrefour", world.ExportWriter.Last.CommerceName);
        Assert.Equal(world.Clock.UtcNow, world.ExportWriter.Last.At);
    }

    [Theory]
    [InlineData(Role.Manager)]
    [InlineData(Role.Cashier)]
    [InlineData(Role.StockKeeper)]
    [InlineData(Role.Accountant)]
    public async Task Only_the_owner_exports(Role role)
    {
        var world = await OwnerWorldAsync();
        world.ActAs(world.Cashier, role);

        var result = await world.ExportData.HandleAsync(new ExportDataQuery(null, null));

        Assert.Equal(ErrorCodes.Forbidden, result.Error!.Code);
        Assert.Equal(0, world.ExportQueries.Reads);
        Assert.Empty(world.Audit.Entries);
    }

    [Fact]
    public async Task It_works_in_every_subscription_state_even_in_read_only()
    {
        var world = await OwnerWorldAsync();
        world.Clock.UtcNow += TimeSpan.FromDays(200);

        var result = await world.ExportData.HandleAsync(new ExportDataQuery(null, null));

        Assert.True(result.IsSuccess);
        Assert.True((await world.SubscriptionState.HandleAsync(world.Tenant))!.IsReadOnly);
    }

    [Fact]
    public async Task A_chosen_period_is_passed_as_is_and_its_last_day_is_included()
    {
        var world = await OwnerWorldAsync();
        var from = Today(world).AddDays(-30);
        var to = Today(world).AddDays(-1);

        await world.ExportData.HandleAsync(new ExportDataQuery(from, to));

        Assert.Equal(new ExportPeriod(from, to), world.ExportWriter.Last!.Period);
        Assert.Equal(to.AddDays(1).ToDateTime(TimeOnly.MinValue), world.ExportWriter.Last.Period.EndExclusive.UtcDateTime);
    }

    [Fact]
    public async Task A_start_after_the_end_and_a_period_over_five_years_are_refused_in_plain_words()
    {
        var world = await OwnerWorldAsync();

        var reversed = await world.ExportData.HandleAsync(new ExportDataQuery(Today(world), Today(world).AddDays(-1)));
        var tooLong = await world.ExportData.HandleAsync(new ExportDataQuery(Today(world).AddYears(-6), Today(world)));

        Assert.Equal(ErrorCodes.RuleViolation, reversed.Error!.Code);
        Assert.Contains("début", reversed.Error.Message, StringComparison.Ordinal);
        Assert.Equal(ErrorCodes.RuleViolation, tooLong.Error!.Code);
        Assert.Contains("5 ans", tooLong.Error.Message, StringComparison.Ordinal);
        Assert.Equal(0, world.ExportQueries.Reads);
    }

    [Fact]
    public async Task A_missing_end_means_today_and_a_missing_start_means_twelve_months_before_the_end()
    {
        var world = await OwnerWorldAsync();
        var from = Today(world).AddDays(-10);

        await world.ExportData.HandleAsync(new ExportDataQuery(from, null));
        var onlyStart = world.ExportWriter.Last!.Period;
        await world.ExportData.HandleAsync(new ExportDataQuery(null, Today(world).AddMonths(-1)));
        var onlyEnd = world.ExportWriter.Last.Period;

        Assert.Equal(new ExportPeriod(from, Today(world)), onlyStart);
        Assert.Equal(new ExportPeriod(Today(world).AddMonths(-13), Today(world).AddMonths(-1)), onlyEnd);
    }

    [Fact]
    public async Task More_than_one_hundred_thousand_lines_are_refused_before_anything_is_built_and_say_how_many()
    {
        var world = await OwnerWorldAsync();
        world.ExportQueries.Counts = new ExportCounts(100, 50, 0, 40_000, 60_000, 1);

        var result = await world.ExportData.HandleAsync(new ExportDataQuery(null, null));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
        Assert.Contains("trop de données", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("100 151", result.Error.Message, StringComparison.Ordinal);
        Assert.Equal(0, world.ExportQueries.Reads);
        Assert.Equal(0, world.ExportWriter.Writes);
        Assert.Empty(world.Audit.Entries);
    }

    [Fact]
    public async Task Exactly_one_hundred_thousand_lines_still_pass()
    {
        var world = await OwnerWorldAsync();
        world.ExportQueries.Counts = new ExportCounts(0, 0, 0, 100_000, 0, 0);

        Assert.True((await world.ExportData.HandleAsync(new ExportDataQuery(null, null))).IsSuccess);
    }

    [Fact]
    public async Task A_successful_export_is_written_in_the_journal_with_the_period_and_the_counts()
    {
        var world = await OwnerWorldAsync();
        world.ExportQueries.Counts = new ExportCounts(3, 2, 1, 10, 25, 7);

        await world.ExportData.HandleAsync(new ExportDataQuery(null, null));

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.DataExported, entry.Action);
        Assert.Equal(world.Owner, entry.User);
        Assert.Contains("10 ventes", entry.Detail, StringComparison.Ordinal);
        Assert.Contains("3 produits", entry.Detail, StringComparison.Ordinal);
        Assert.Equal(1, world.UnitOfWork.Commits);
    }

    [Fact]
    public async Task The_sixth_export_of_the_hour_is_refused_with_the_waiting_time_and_leaves_no_trace()
    {
        var world = await OwnerWorldAsync();
        var now = world.Clock.UtcNow;
        world.AuditLog.Next = Enumerable.Range(0, 5)
            .Select(index => new AuditEntryView(Guid.NewGuid(), now.AddMinutes(-50 + (index * 5)), world.Owner.Value, AuditActions.DataExported, Guid.NewGuid(), "x"))
            .Reverse()
            .ToArray();

        var result = await world.ExportData.HandleAsync(new ExportDataQuery(null, null));

        Assert.Equal(ErrorCodes.TooManyRequests, result.Error!.Code);
        Assert.Contains("réessayez dans 10 minutes", result.Error.Message, StringComparison.Ordinal);
        Assert.Equal(0, world.ExportQueries.Reads);
        Assert.Empty(world.Audit.Entries);
        Assert.Equal([AuditActions.DataExported], world.AuditLog.LastFilter!.ActionPrefixes);
        Assert.Equal(now.AddHours(-1), world.AuditLog.LastFilter.From);
    }

    [Fact]
    public async Task Four_exports_in_the_last_hour_still_allow_a_fifth()
    {
        var world = await OwnerWorldAsync();
        world.AuditLog.Next = Enumerable.Range(0, 4)
            .Select(index => new AuditEntryView(Guid.NewGuid(), world.Clock.UtcNow.AddMinutes(-index), world.Owner.Value, AuditActions.DataExported, Guid.NewGuid(), "x"))
            .ToArray();

        Assert.True((await world.ExportData.HandleAsync(new ExportDataQuery(null, null))).IsSuccess);
    }

    [Fact]
    public async Task The_data_of_another_shop_is_never_read()
    {
        var world = await OwnerWorldAsync();

        await world.ExportData.HandleAsync(new ExportDataQuery(null, null));

        Assert.Equal(world.Tenant, world.ExportQueries.LastTenant);
    }
}

public class ExportLabelsTests
{
    [Fact]
    public void A_sale_key_is_readable_and_has_no_technical_identifier()
    {
        var key = ExportKeys.SaleKey(new DateTimeOffset(2026, 10, 12, 8, 14, 0, TimeSpan.Zero), 12, "Awa Koné");

        Assert.Equal("12/10/2026 08:14 · ticket 12 · Awa Koné", key);
    }

    [Theory]
    [InlineData(PaymentMethod.Cash, "Espèces")]
    [InlineData(PaymentMethod.OrangeMoney, "Orange Money")]
    [InlineData(PaymentMethod.MtnMomo, "MTN MoMo")]
    [InlineData(PaymentMethod.MoovMoney, "Moov Money")]
    [InlineData(PaymentMethod.Wave, "Wave")]
    [InlineData(PaymentMethod.Credit, "Crédit")]
    public void A_payment_method_is_named_in_french(PaymentMethod method, string expected) => Assert.Equal(expected, ExportLabels.Method(method));

    [Theory]
    [InlineData(StockMovementKind.Reception, "Réception")]
    [InlineData(StockMovementKind.Sale, "Vente")]
    [InlineData(StockMovementKind.Loss, "Perte")]
    [InlineData(StockMovementKind.SaleCancellation, "Annulation de vente")]
    public void A_stock_movement_is_named_in_french(StockMovementKind kind, string expected) => Assert.Equal(expected, ExportLabels.Movement(kind));

    [Fact]
    public void A_sale_status_is_named_in_french()
    {
        Assert.Equal("Validée", ExportLabels.Status(SaleStatus.Validated));
        Assert.Equal("Annulée", ExportLabels.Status(SaleStatus.Cancelled));
    }
}

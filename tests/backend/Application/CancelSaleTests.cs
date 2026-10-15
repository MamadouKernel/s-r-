using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Application.Sales;
using Soro.Domain.Common;
using Soro.Domain.Sales;

namespace Soro.Application.Tests;

public class CancelSaleTests
{
    [Fact]
    public async Task A_manager_cancels_a_sale_with_a_reason_and_it_is_audited()
    {
        var world = new TestWorld();
        var sale = world.GivenRecordedSale(world.GivenOpenSession());
        world.ActAs(world.Manager, Role.Manager);

        var result = await world.CancelSale.HandleAsync(new CancelSaleCommand(sale.Id, "Erreur de saisie"));

        Assert.True(result.IsSuccess);
        var stored = await world.Sales.GetAsync(sale.Id, world.Tenant, CancellationToken.None);
        Assert.Equal(SaleStatus.Cancelled, stored!.Status);
        Assert.Contains(world.Audit.Entries, entry => entry.Action == AuditActions.SaleCancelled);
        Assert.Equal(1, world.UnitOfWork.Commits);
    }

    [Fact]
    public async Task A_cashier_cannot_cancel_and_the_attempt_is_audited()
    {
        var world = new TestWorld();
        var sale = world.GivenRecordedSale(world.GivenOpenSession());

        var result = await world.CancelSale.HandleAsync(new CancelSaleCommand(sale.Id, "Erreur"));

        Assert.Equal(ErrorCodes.Forbidden, result.Error!.Code);
        var stored = await world.Sales.GetAsync(sale.Id, world.Tenant, CancellationToken.None);
        Assert.Equal(SaleStatus.Validated, stored!.Status);
        Assert.Contains(world.Audit.Entries, entry => entry.Action == AuditActions.CancelRefused);
    }

    [Fact]
    public async Task Requires_a_reason()
    {
        var world = new TestWorld();
        var sale = world.GivenRecordedSale(world.GivenOpenSession());
        world.ActAs(world.Manager, Role.Manager);

        var result = await world.CancelSale.HandleAsync(new CancelSaleCommand(sale.Id, " "));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
        var stored = await world.Sales.GetAsync(sale.Id, world.Tenant, CancellationToken.None);
        Assert.Equal(SaleStatus.Validated, stored!.Status);
    }

    [Fact]
    public async Task Fails_when_the_sale_does_not_exist()
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Manager);

        var result = await world.CancelSale.HandleAsync(new CancelSaleCommand(SaleId.New(), "Erreur"));

        Assert.Equal(ErrorCodes.NotFound, result.Error!.Code);
    }

    [Fact]
    public async Task Does_not_reveal_a_sale_of_another_tenant()
    {
        var world = new TestWorld();
        var sale = world.GivenRecordedSale(world.GivenOpenSession(tenant: TenantId.New()));
        world.ActAs(world.Manager, Role.Manager);

        var result = await world.CancelSale.HandleAsync(new CancelSaleCommand(sale.Id, "Erreur"));

        Assert.Equal(ErrorCodes.NotFound, result.Error!.Code);
    }
}

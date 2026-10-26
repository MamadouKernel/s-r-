using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Application.Sales;
using Soro.Application.Stock;
using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Domain.Sales;
using Soro.Domain.Stock;

namespace Soro.Application.Tests;

/// <summary>Vente au poids et à la mesure côté serveur (docs/specs/vente-au-poids VPO-01 à VPO-05, VPO-09).</summary>
public class WeightSaleTests
{
    private static Product Rice(TestWorld world, int stockGrams = 25_000, MeasureUnit unit = MeasureUnit.Kilogram)
    {
        var product = Product.Create(
            ProductId.New(), world.Tenant, "Riz parfumé", null, null, Money.Fcfa(600), Money.Fcfa(800), 0, true, TestWorld.Morning, null, null, unit);
        if (stockGrams > 0)
        {
            product.Receive(stockGrams, null, world.Manager, TestWorld.Morning);
        }

        world.Products.Put(product);
        return product;
    }

    private static RecordSaleCommand SaleOf(TestWorld world, Domain.Cash.CashRegisterSession session, Product product, int quantity, long total, MeasureUnit? unit)
    {
        var line = new SaleLineInput(product.Id, product.Name, quantity, product.SalePrice.Amount) { Unit = unit };
        return world.CashSale(session, total) with
        {
            Lines = [line],
            Payments = [new PaymentInput(PaymentId.New(), PaymentMethod.Cash, total, total, null)],
        };
    }

    [Fact]
    public async Task One_kilo_two_fifty_of_rice_at_800_francs_is_1000_francs_and_takes_exactly_that_from_the_stock()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession();
        var rice = Rice(world);

        var result = await world.RecordSale.HandleAsync(SaleOf(world, session, rice, 1_250, 1_000, MeasureUnit.Kilogram));

        Assert.True(result.IsSuccess);
        Assert.Equal(1_000, result.Value!.NetTotalFcfa);
        Assert.Equal(23_750, rice.QuantityOnHand);
        var line = Assert.Single(Assert.Single(world.Sales.All).Lines);
        Assert.Equal(MeasureUnit.Kilogram, line.Unit);
        Assert.Equal(1_250, line.Quantity);
    }

    [Fact]
    public async Task A_payment_that_ignores_the_unit_does_not_cover_the_sale()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession();
        var rice = Rice(world);

        // 1 250 g × 800 pris comme 1 250 pièces : 1 000 000 FCFA. Un paiement de 1 000 ne le couvre pas, et réciproquement.
        var result = await world.RecordSale.HandleAsync(SaleOf(world, session, rice, 1_250, 1_250, MeasureUnit.Kilogram));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
        Assert.Empty(world.Sales.All);
        Assert.Equal(25_000, rice.QuantityOnHand);
    }

    [Fact]
    public async Task A_device_that_does_not_know_the_weight_cannot_sell_a_weighed_product_and_is_told_to_update()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession();
        var rice = Rice(world);

        var result = await world.RecordSale.HandleAsync(SaleOf(world, session, rice, 2, 1_600, unit: null)); // ancienne caisse : « 2 » = 2 pièces

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
        Assert.Contains("mettre à jour", result.Error.Message, StringComparison.Ordinal);
        Assert.Empty(world.Sales.All);
        Assert.Equal(25_000, rice.QuantityOnHand);
    }

    [Fact]
    public async Task The_unit_sent_must_be_the_one_of_the_product()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession();
        var rice = Rice(world);

        var result = await world.RecordSale.HandleAsync(SaleOf(world, session, rice, 1_000, 800, MeasureUnit.Litre));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
        Assert.Empty(world.Sales.All);
    }

    [Fact]
    public async Task A_piece_product_sold_without_a_unit_is_unchanged()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession();
        var juice = world.GivenProduct(name: "Jus", barcode: null, stock: 10);

        var result = await world.RecordSale.HandleAsync(world.CashSale(session, 1_200) with
        {
            Lines = [new SaleLineInput(juice.Id, juice.Name, 2, 600)],
            Payments = [new PaymentInput(PaymentId.New(), PaymentMethod.Cash, 1_200, 1_200, null)],
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(8, juice.QuantityOnHand);
        Assert.Equal(MeasureUnit.Piece, Assert.Single(Assert.Single(world.Sales.All).Lines).Unit);
    }

    [Fact]
    public async Task The_cost_of_a_weighed_line_is_frozen_per_kilogram()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession();
        var rice = Rice(world);

        await world.RecordSale.HandleAsync(SaleOf(world, session, rice, 1_250, 1_000, MeasureUnit.Kilogram));

        var line = Assert.Single(Assert.Single(world.Sales.All).Lines);
        Assert.Equal(Money.Fcfa(600), line.UnitCost);
    }

    [Fact]
    public async Task A_sale_from_the_offline_queue_of_a_weighed_product_takes_the_stock_in_grams_and_may_leave_it_negative()
    {
        var world = new TestWorld();
        var session = world.GivenOpenSession();
        var rice = Rice(world, stockGrams: 500);

        var result = await world.SyncSales.HandleAsync(new SyncSalesCommand([SaleOf(world, session, rice, 1_250, 1_000, MeasureUnit.Kilogram)]));

        Assert.Equal(SyncOutcome.Accepted, Assert.Single(result.Statuses).Outcome);
        Assert.Equal(-750, rice.QuantityOnHand); // une vente qui a eu lieu ne se refuse pas sur le stock
    }
}

public class ProductUnitManagementTests
{
    private static TestWorld AsManager()
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Manager);
        return world;
    }

    [Fact]
    public async Task A_product_is_created_by_the_kilogram_and_the_list_says_so()
    {
        var world = AsManager();
        var command = new CreateProductCommand(ProductId.New(), "Riz parfumé", null, "Épicerie", 600, 800, 0, true) { Unit = MeasureUnit.Kilogram };

        var created = await world.CreateProduct.HandleAsync(command);
        var listed = await world.ListProducts.HandleAsync(new ListProductsQuery(null, true));

        Assert.True(created.IsSuccess);
        Assert.Equal(MeasureUnit.Kilogram, Assert.Single(listed).Unit);
    }

    [Fact]
    public async Task A_product_with_no_history_may_change_its_unit()
    {
        var world = AsManager();
        var product = world.GivenProduct(name: "Sucre", barcode: null, stock: 0, tracksStock: false);

        var result = await world.UpdateProduct.HandleAsync(new UpdateProductCommand(product.Id, "Sucre", null, null, 300, 700, 0) { Unit = MeasureUnit.Kilogram });

        Assert.True(result.IsSuccess);
        Assert.Equal(MeasureUnit.Kilogram, product.Unit);
    }

    [Fact]
    public async Task A_product_with_a_history_keeps_its_unit_and_the_message_says_why()
    {
        var world = AsManager();
        var product = world.GivenProduct(name: "Sucre", barcode: null, stock: 0);
        world.Products.MarkHasHistory(product.Id);

        var result = await world.UpdateProduct.HandleAsync(new UpdateProductCommand(product.Id, "Sucre", null, null, 300, 700, 0) { Unit = MeasureUnit.Kilogram });

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
        Assert.Contains("historique", result.Error.Message, StringComparison.Ordinal);
        Assert.Equal(MeasureUnit.Piece, product.Unit);
    }

    [Fact]
    public async Task Leaving_the_unit_out_of_an_update_changes_nothing()
    {
        var world = AsManager();
        var product = world.GivenProduct(name: "Sucre", barcode: null, stock: 0);
        world.Products.MarkHasHistory(product.Id);

        var result = await world.UpdateProduct.HandleAsync(new UpdateProductCommand(product.Id, "Sucre", null, null, 300, 700, 0));

        Assert.True(result.IsSuccess);
        Assert.Equal(MeasureUnit.Piece, product.Unit);
    }
}

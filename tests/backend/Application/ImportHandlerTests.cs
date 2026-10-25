using Soro.Application.Common;
using Soro.Application.Stock;
using Soro.Domain.Common;
using Soro.Domain.Identity;
using Soro.Domain.Stock;

namespace Soro.Application.Tests;

public class ImportHandlerTests
{
    private static readonly byte[] File = [0x50, 0x4B, 0x03, 0x04, 1, 2, 3];

    private static TestWorld ManagerWorld(params ImportRow[] rows)
    {
        var world = new TestWorld();
        world.ActAs(world.Manager, Role.Manager);
        world.SheetReader.Next = new SheetRead(null, rows);
        return world;
    }

    private static ImportRow[] RiceAndSalt() =>
    [
        Row.At(2, "Riz parfumé", "5 kg", "Épicerie", buy: "2400", sell: "3200", alert: "5", stock: "20"),
        Row.At(3, "Riz parfumé", "25 kg", "Épicerie", buy: "11500", sell: "14500", alert: "2", stock: "6"),
        Row.At(4, "Sel fin", sell: "200", buy: "120"),
    ];

    private static async Task<IReadOnlyList<Product>> AllAsync(TestWorld world) =>
        await world.Products.ListAsync(world.Tenant, null, true, CancellationToken.None);

    [Fact]
    public async Task A_valid_file_creates_the_families_and_formats_in_one_commit()
    {
        var world = ManagerWorld(RiceAndSalt());

        var result = await world.ImportProducts.HandleAsync(new ImportProductsCommand(Guid.NewGuid(), File));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Imported);
        Assert.Equal(2, result.Value.NewProducts);
        Assert.Equal(3, result.Value.NewFormats);
        var products = await AllAsync(world);
        Assert.Equal(["Riz parfumé · 25 kg", "Riz parfumé · 5 kg", "Sel fin"], products.Select(product => product.Name).Order().ToArray());
        Assert.Equal(1, world.UnitOfWork.Commits);
        Assert.Contains(world.Audit.Entries, entry => entry.Action == AuditActions.ProductsImported);
        var rice = products.Single(product => product.Name == "Riz parfumé · 25 kg");
        Assert.Equal("Épicerie", rice.Category);
        Assert.Equal(14500, rice.SalePrice.Amount);
        Assert.Equal(2, rice.AlertThreshold);
    }

    [Fact]
    public async Task A_starting_stock_becomes_a_reception_that_sets_the_average_cost()
    {
        var world = ManagerWorld(RiceAndSalt());

        await world.ImportProducts.HandleAsync(new ImportProductsCommand(Guid.NewGuid(), File));

        var big = (await AllAsync(world)).Single(product => product.Name == "Riz parfumé · 25 kg");
        Assert.Equal(6, big.QuantityOnHand);
        Assert.Equal(11500, big.PurchasePrice.Amount);
        var history = await world.Movements.ListForProductAsync(big.Id, world.Tenant, 10, CancellationToken.None);
        var movement = Assert.Single(history);
        Assert.Equal(StockMovementKind.Reception, movement.Kind);
        Assert.Equal("Import Excel", movement.Reason);
        Assert.Equal(11500, movement.UnitCost!.Value.Amount);
        var salt = (await AllAsync(world)).Single(product => product.Name == "Sel fin");
        Assert.Empty(await world.Movements.ListForProductAsync(salt.Id, world.Tenant, 10, CancellationToken.None));
    }

    [Fact]
    public async Task One_wrong_line_creates_nothing_and_reports_the_problems()
    {
        var world = ManagerWorld(Row.At(2, "Bon", sell: "100"), Row.At(3, "Mauvais", sell: "abc"));

        var result = await world.ImportProducts.HandleAsync(new ImportProductsCommand(Guid.NewGuid(), File));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Imported);
        Assert.Equal(1, result.Value.ProblemCount);
        Assert.Equal(3, result.Value.Problems[0].Line);
        Assert.Empty(await AllAsync(world));
        Assert.Equal(0, world.UnitOfWork.Commits);
        Assert.Empty(world.Audit.Entries);
    }

    [Fact]
    public async Task The_preview_reports_the_same_numbers_and_writes_nothing()
    {
        var world = ManagerWorld(RiceAndSalt());

        var result = await world.PreviewImport.HandleAsync(new ProductImportQuery(File));

        Assert.Equal(2, result.Value!.NewProducts);
        Assert.Equal(3, result.Value.NewFormats);
        Assert.Equal(3, result.Value.RowsRead);
        Assert.True(result.Value.CanImport);
        Assert.False(result.Value.Imported);
        Assert.Empty(await AllAsync(world));
        Assert.Equal(0, world.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Importing_the_same_import_twice_creates_nothing_more_and_says_it_was_already_done()
    {
        var world = ManagerWorld(RiceAndSalt());
        var id = Guid.NewGuid();
        await world.ImportProducts.HandleAsync(new ImportProductsCommand(id, File));

        var replay = await world.ImportProducts.HandleAsync(new ImportProductsCommand(id, File));

        Assert.True(replay.Value!.AlreadyProcessed);
        Assert.Equal(3, (await AllAsync(world)).Count);
        Assert.Equal(1, world.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Importing_the_same_file_again_under_a_new_import_skips_everything()
    {
        var world = ManagerWorld(RiceAndSalt());
        await world.ImportProducts.HandleAsync(new ImportProductsCommand(Guid.NewGuid(), File));

        var again = await world.ImportProducts.HandleAsync(new ImportProductsCommand(Guid.NewGuid(), File));

        Assert.False(again.Value!.Imported);
        Assert.Equal(3, again.Value.Skipped.Count);
        Assert.Equal(0, again.Value.ProblemCount);
        Assert.Equal(3, (await AllAsync(world)).Count);
        Assert.Equal(1, world.UnitOfWork.Commits);
    }

    [Fact]
    public async Task A_new_size_is_added_to_the_existing_family_and_existing_products_are_untouched()
    {
        var world = ManagerWorld(RiceAndSalt());
        await world.ImportProducts.HandleAsync(new ImportProductsCommand(Guid.NewGuid(), File));
        var before = (await AllAsync(world)).Single(product => product.Name == "Riz parfumé · 5 kg");
        world.SheetReader.Next = new SheetRead(null, [Row.At(2, "Riz parfumé", "1 kg", sell: "800", buy: "500", stock: "10"), Row.At(3, "Riz parfumé", "5 kg", sell: "9999")]);

        var result = await world.ImportProducts.HandleAsync(new ImportProductsCommand(Guid.NewGuid(), File));

        Assert.True(result.Value!.Imported);
        Assert.Equal(0, result.Value.NewProducts);
        Assert.Equal(1, result.Value.NewFormats);
        var products = await AllAsync(world);
        var added = products.Single(product => product.Name == "Riz parfumé · 1 kg");
        Assert.Equal(before.FamilyId, added.FamilyId);
        Assert.Equal("Épicerie", added.Category);
        Assert.Equal(10, added.QuantityOnHand);
        var untouched = products.Single(product => product.Id == before.Id);
        Assert.Equal(3200, untouched.SalePrice.Amount);
        Assert.Equal(20, untouched.QuantityOnHand);
    }

    [Theory]
    [InlineData(Role.Cashier)]
    [InlineData(Role.StockKeeper)]
    [InlineData(Role.Accountant)]
    public async Task Only_a_manager_imports(Role role)
    {
        var world = ManagerWorld(RiceAndSalt());
        world.ActAs(world.Cashier, role);

        var template = await world.ImportTemplate.HandleAsync();
        var preview = await world.PreviewImport.HandleAsync(new ProductImportQuery(File));
        var import = await world.ImportProducts.HandleAsync(new ImportProductsCommand(Guid.NewGuid(), File));

        Assert.All(new[] { template.Error, preview.Error, import.Error }, error => Assert.Equal(ErrorCodes.Forbidden, error!.Code));
        Assert.Equal(0, world.SheetReader.Calls);
        Assert.Empty(await AllAsync(world));
    }

    [Fact]
    public async Task The_manager_gets_the_template_bytes()
    {
        var world = ManagerWorld();

        var template = await world.ImportTemplate.HandleAsync();

        Assert.Equal(world.TemplateWriter.Bytes, template.Value);
    }

    [Fact]
    public async Task A_file_over_two_megabytes_is_refused_before_it_is_read()
    {
        var world = ManagerWorld(RiceAndSalt());

        var result = await world.PreviewImport.HandleAsync(new ProductImportQuery(new byte[2 * 1024 * 1024 + 1]));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
        Assert.Contains("trop lourd", result.Error.Message, StringComparison.Ordinal);
        Assert.Equal(0, world.SheetReader.Calls);
    }

    [Fact]
    public async Task A_file_that_is_not_an_xlsx_workbook_is_refused_by_its_content()
    {
        var world = ManagerWorld(RiceAndSalt());
        var csv = "Nom du produit;Prix de vente\nJus;300"u8.ToArray();
        var oldExcel = new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0 };

        var asCsv = await world.PreviewImport.HandleAsync(new ProductImportQuery(csv));
        var asXls = await world.ImportProducts.HandleAsync(new ImportProductsCommand(Guid.NewGuid(), oldExcel));

        Assert.Equal(ErrorCodes.RuleViolation, asCsv.Error!.Code);
        Assert.Contains(".xlsx", asCsv.Error.Message, StringComparison.Ordinal);
        Assert.Equal(ErrorCodes.RuleViolation, asXls.Error!.Code);
        Assert.Equal(0, world.SheetReader.Calls);
    }

    [Fact]
    public async Task A_problem_found_while_reading_the_workbook_is_returned_as_is()
    {
        var world = ManagerWorld();
        world.SheetReader.Next = new SheetRead("Il manque la colonne « Prix de vente ».", []);

        var result = await world.PreviewImport.HandleAsync(new ProductImportQuery(File));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
        Assert.Equal("Il manque la colonne « Prix de vente ».", result.Error.Message);
    }

    [Fact]
    public async Task More_than_two_thousand_lines_are_refused()
    {
        var world = ManagerWorld(Enumerable.Range(2, 2001).Select(line => Row.At(line, "P" + line)).ToArray());

        var result = await world.PreviewImport.HandleAsync(new ProductImportQuery(File));

        Assert.Equal(ErrorCodes.RuleViolation, result.Error!.Code);
        Assert.Contains("2 000", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_file_without_any_line_has_nothing_to_import()
    {
        var world = ManagerWorld();

        var result = await world.PreviewImport.HandleAsync(new ProductImportQuery(File));

        Assert.False(result.Value!.CanImport);
        Assert.Equal(0, result.Value.RowsRead);
    }

    [Fact]
    public async Task A_barcode_of_another_shop_is_not_a_conflict()
    {
        var world = ManagerWorld(Row.At(2, "Jus", barcode: "123"));
        world.GivenProduct("Ailleurs", "123", tenant: TenantId.New());

        var result = await world.ImportProducts.HandleAsync(new ImportProductsCommand(Guid.NewGuid(), File));

        Assert.True(result.Value!.Imported);
    }

    [Fact]
    public async Task A_barcode_already_used_in_this_shop_skips_the_line()
    {
        var world = ManagerWorld(Row.At(2, "Jus", barcode: "123"));
        world.GivenProduct("Déjà là", "123");

        var result = await world.ImportProducts.HandleAsync(new ImportProductsCommand(Guid.NewGuid(), File));

        Assert.False(result.Value!.Imported);
        Assert.Single(result.Value.Skipped);
        Assert.Single(await AllAsync(world));
    }
}

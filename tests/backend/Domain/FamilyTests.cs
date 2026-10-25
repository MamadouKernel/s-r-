using Soro.Domain.Common;
using Soro.Domain.Stock;

namespace Soro.Domain.Tests;

public class FamilyTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("1,5 L", "1,5 L")]
    [InlineData("  1,5   L  ", "1,5 L")]
    [InlineData("Pack de  6", "Pack de 6")]
    public void A_label_is_trimmed_and_its_spaces_are_collapsed(string raw, string expected) =>
        Assert.Equal(expected, FormatLabel.Normalize(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_label_means_no_label(string? raw) => Assert.Null(FormatLabel.Normalize(raw));

    [Fact]
    public void A_label_cannot_exceed_forty_characters() =>
        Assert.Throws<DomainException>(() => FormatLabel.Normalize(new string('x', 41)));

    [Fact]
    public void Two_labels_that_differ_only_by_case_or_spaces_are_the_same_label() =>
        Assert.Equal(FormatLabel.Key("1 L"), FormatLabel.Key("  1   l "));

    [Fact]
    public void The_display_name_joins_the_family_and_the_label()
    {
        Assert.Equal("Eau minérale · 1,5 L", FormatLabel.DisplayName("Eau minérale", "1,5 L"));
        Assert.Equal("Sel fin", FormatLabel.DisplayName("Sel fin", null));
    }

    [Fact]
    public void A_display_name_cannot_exceed_the_product_name_limit() =>
        Assert.Throws<DomainException>(() => FormatLabel.DisplayName(new string('a', 190), "1234567890"));

    [Fact]
    public void A_family_needs_a_name_and_trims_it()
    {
        Assert.Throws<DomainException>(() => ProductFamily.Create(FamilyId.New(), TenantId.New(), "  ", null, At));
        Assert.Equal("Riz parfumé", ProductFamily.Create(FamilyId.New(), TenantId.New(), " Riz parfumé ", "  ", At).Name);
    }

    [Fact]
    public void A_blank_category_means_no_category() =>
        Assert.Null(ProductFamily.Create(FamilyId.New(), TenantId.New(), "Riz", " ", At).Category);

    [Fact]
    public void Describing_a_product_sets_its_family_label_name_and_category()
    {
        var family = ProductFamily.Create(FamilyId.New(), TenantId.New(), "Eau minérale", "Boissons", At);
        var product = Product.Create(
            ProductId.New(), family.Tenant, "Eau", null, null, Money.Fcfa(100), Money.Fcfa(150), 0, true, At);

        product.Describe(family, "1,5 L");

        Assert.Equal(family.Id, product.FamilyId);
        Assert.Equal("1,5 L", product.FormatLabel);
        Assert.Equal("Eau minérale · 1,5 L", product.Name);
        Assert.Equal("Boissons", product.Category);
    }

    [Fact]
    public void A_product_created_the_old_way_is_its_own_family()
    {
        var id = ProductId.New();
        var product = Product.Create(id, TenantId.New(), "Savon", null, null, Money.Fcfa(100), Money.Fcfa(150), 0, true, At);

        Assert.Equal(new FamilyId(id.Value), product.FamilyId);
        Assert.Null(product.FormatLabel);
        Assert.Null(product.PhotoId);
    }
}

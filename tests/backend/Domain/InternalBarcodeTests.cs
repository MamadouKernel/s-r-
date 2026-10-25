using Soro.Domain.Common;
using Soro.Domain.Stock;

namespace Soro.Domain.Tests;

/// <summary>Codes-barres internes (docs/specs/etiquettes-tickets ETQ-01) : EAN-13 de la plage « usage en magasin », préfixe 29.</summary>
public class InternalBarcodeTests
{
    [Theory]
    [InlineData("611100000001", 4)]
    [InlineData("290000000001", 8)]
    [InlineData("290000000002", 5)]
    public void Computes_the_check_digit(string twelveDigits, int expected) =>
        Assert.Equal(expected, InternalBarcode.CheckDigit(twelveDigits));

    [Theory]
    [InlineData("6111000000014", true)]
    [InlineData("2900000000018", true)]
    [InlineData("6111000000011", false)]
    [InlineData("611100000001", false)]
    [InlineData("61110000000111", false)]
    [InlineData("611100000001a", false)]
    [InlineData("", false)]
    public void Recognises_a_valid_ean13(string code, bool expected) => Assert.Equal(expected, InternalBarcode.IsValidEan13(code));

    [Fact]
    public void Composes_the_code_of_a_sequence_with_the_prefix_29_and_a_valid_check_digit()
    {
        Assert.Equal("2900000000018", InternalBarcode.Compose(1));
        Assert.Equal("2900000000025", InternalBarcode.Compose(2));
        Assert.True(InternalBarcode.IsValidEan13(InternalBarcode.Compose(9_999_999_999)));
        Assert.Equal(13, InternalBarcode.Compose(123_456).Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10_000_000_000)]
    public void Refuses_a_sequence_out_of_range(long sequence) =>
        Assert.Throws<DomainException>(() => InternalBarcode.Compose(sequence));

    [Fact]
    public void Reads_the_sequence_back_only_from_a_valid_internal_code()
    {
        Assert.True(InternalBarcode.TryGetSequence("2900000000018", out var sequence));
        Assert.Equal(1, sequence);
        Assert.True(InternalBarcode.TryGetSequence(InternalBarcode.Compose(4242), out var other));
        Assert.Equal(4242, other);

        Assert.False(InternalBarcode.TryGetSequence("6111000000014", out _)); // un code d'usine
        Assert.False(InternalBarcode.TryGetSequence("2900000000019", out _)); // clé fausse
        Assert.False(InternalBarcode.TryGetSequence("290000000001", out _)); // trop court
    }

    [Fact]
    public void Tells_whether_a_code_is_internal()
    {
        Assert.True(InternalBarcode.IsInternal("2900000000018"));
        Assert.False(InternalBarcode.IsInternal("6111000000011"));
        Assert.False(InternalBarcode.IsInternal(null));
    }
}

public class ProductAssignBarcodeTests
{
    private static Product NewProduct(string? barcode) =>
        Product.Create(
            ProductId.New(), TenantId.New(), "Pain", barcode, null, Money.Fcfa(100), Money.Fcfa(150), 0, false,
            new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public void A_product_without_a_barcode_receives_one()
    {
        var product = NewProduct(null);

        product.AssignBarcode("2900000000018");

        Assert.Equal("2900000000018", product.Barcode);
    }

    [Fact]
    public void A_product_never_receives_a_second_barcode()
    {
        var product = NewProduct("6111000000011");

        Assert.Throws<DomainException>(() => product.AssignBarcode("2900000000018"));
        Assert.Equal("6111000000011", product.Barcode);
    }
}

namespace Soro.Api.Contracts;

public sealed record CreateProductRequest
{
    public required Guid ProductId { get; init; }

    public required string Name { get; init; }

    public string? Barcode { get; init; }

    public string? Category { get; init; }

    public required long PurchasePriceFcfa { get; init; }

    public required long SalePriceFcfa { get; init; }

    public int AlertThreshold { get; init; }

    public bool TracksStock { get; init; } = true;
}

public sealed record UpdateProductRequest
{
    public required string Name { get; init; }

    public string? Barcode { get; init; }

    public string? Category { get; init; }

    public required long PurchasePriceFcfa { get; init; }

    public required long SalePriceFcfa { get; init; }

    public int AlertThreshold { get; init; }
}

public sealed record ReceiveStockRequest
{
    public required Guid MovementId { get; init; }

    public required int Quantity { get; init; }

    public string? Supplier { get; init; }

    public long? PurchasePriceFcfa { get; init; }
}

public sealed record RecordStockLossRequest
{
    public required Guid MovementId { get; init; }

    public required int Quantity { get; init; }

    public required string Reason { get; init; }
}

public sealed record ProductCreatedResponse(Guid ProductId, bool AlreadyProcessed);

/// <param name="PurchasePriceFcfa">Le prix d'achat moyen après une réception (RG-11) ; absent après une perte.</param>
public sealed record StockChangedResponse(Guid MovementId, int QuantityOnHand, bool AlreadyProcessed, long? PurchasePriceFcfa = null);

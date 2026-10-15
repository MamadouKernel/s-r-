using Soro.Application.Common;
using Soro.Domain.Common;

namespace Soro.Application.Sales;

/// <summary>Lot de ventes d'une caisse envoyé au retour du réseau.</summary>
public sealed record SyncSalesCommand(IReadOnlyList<RecordSaleCommand> Sales);

public enum SyncOutcome
{
    Accepted,
    AlreadyProcessed,
    Rejected,
}

/// <summary>Accusé par vente : le client le conserve pour mettre sa file locale à jour (REQ-06).</summary>
public sealed record SaleSyncStatus(SaleId SaleId, SyncOutcome Outcome, string? ErrorCode, string? Message);

public sealed record SyncSalesResult(IReadOnlyList<SaleSyncStatus> Statuses);

/// <summary>
/// Synchronisation idempotente (REQ-06, RG-12). Les ventes sont traitées dans l'ordre de la caisse ;
/// un rejet est rapporté vente par vente et ne bloque pas les suivantes.
/// </summary>
public sealed class SyncSalesHandler(RecordSaleHandler recordSale)
{
    public async Task<SyncSalesResult> HandleAsync(
        SyncSalesCommand command,
        CancellationToken cancellationToken = default)
    {
        var statuses = new List<SaleSyncStatus>();
        foreach (var sale in command.Sales.OrderBy(sale => sale.LocalSequence))
        {
            var result = await recordSale.HandleAsync(sale, cancellationToken);
            statuses.Add(ToStatus(sale.Id, result));
        }

        return new SyncSalesResult(statuses);
    }

    private static SaleSyncStatus ToStatus(SaleId id, Result<RecordSaleResult> result) =>
        result.IsSuccess
            ? new SaleSyncStatus(
                id,
                result.Value!.AlreadyProcessed ? SyncOutcome.AlreadyProcessed : SyncOutcome.Accepted,
                null,
                null)
            : new SaleSyncStatus(id, SyncOutcome.Rejected, result.Error!.Code, result.Error.Message);
}

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
            // La vente a déjà eu lieu : un plafond de crédit dépassé entre-temps ne la fait pas rejeter, il est signalé au gérant (CRD-04).
            var offline = sale with { EnforceCreditCeiling = false, FromOfflineQueue = true };
            try
            {
                statuses.Add(ToStatus(sale.Id, await recordSale.HandleAsync(offline, cancellationToken)));
            }
            catch (ConflictException)
            {
                statuses.Add(await RetryAfterConflictAsync(offline, cancellationToken));
            }
        }

        return new SyncSalesResult(statuses);
    }

    /// <summary>
    /// Deux envois de la même vente en même temps : le second trouve la vente déjà écrite et la reconnaît. Sinon c'est un numéro local déjà pris
    /// par une autre vente (caisse réinitialisée) : cette vente est refusée seule, le reste du lot passe.
    /// </summary>
    private async Task<SaleSyncStatus> RetryAfterConflictAsync(RecordSaleCommand sale, CancellationToken cancellationToken)
    {
        try
        {
            return ToStatus(sale.Id, await recordSale.HandleAsync(sale, cancellationToken));
        }
        catch (ConflictException)
        {
            return new SaleSyncStatus(
                sale.Id,
                SyncOutcome.Rejected,
                ErrorCodes.RuleViolation,
                "Le numéro de cette vente est déjà pris par une autre vente de la même caisse. Signalez-le à votre gérant.");
        }
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

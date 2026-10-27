using Soro.Application.Common;
using Soro.Domain.Common;
using Soro.Domain.Sales;

namespace Soro.Application.Sales;

/// <param name="OperationId">Fixé par l'écran à son ouverture : un envoi répété ne rembourse jamais deux fois. Sans lui, il est tiré à l'appel.</param>
/// <param name="Register">La caisse dont le tiroir rend les espèces ; sans elle, la session de la vente si elle est encore ouverte.</param>
/// <param name="Resellable"><c>false</c> : la marchandise est abîmée, elle ne revient pas en rayon.</param>
public sealed record CancelSaleCommand(
    SaleId SaleId,
    string Reason,
    SaleReturnId? OperationId = null,
    RegisterId? Register = null,
    bool Resellable = true,
    Approval? Approval = null);

/// <summary>
/// Annulation d'une vente validée (RG-02, docs/decisions/0020) : motivée, validée par un gérant (PIN), tracée ; elle remet le stock, rend l'argent et
/// efface la dette correspondante. Refusée si la vente a déjà un retour (faire un retour pour le reste).
/// </summary>
public sealed class CancelSaleHandler(SaleReversal reversal)
{
    public Task<Result<ReversalResult>> HandleAsync(CancelSaleCommand command, CancellationToken cancellationToken = default) =>
        reversal.ExecuteAsync(
            new SaleReversal.Request(
                command.SaleId, command.OperationId ?? SaleReturnId.New(), ReturnKind.Cancellation, null, command.Resellable, command.Reason, command.Register, command.Approval),
            cancellationToken);
}

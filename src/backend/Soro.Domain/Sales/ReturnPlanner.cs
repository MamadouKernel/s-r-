using Soro.Domain.Common;

namespace Soro.Domain.Sales;

/// <summary>
/// Calcule ce qu'un retour rembourse (docs/decisions/0020, ARR-03 et ARR-04). Les montants se calculent sur le **cumul** de ce qui a été rendu
/// puis se retranchent de ce qui a déjà été remboursé : les arrondis s'annulent, et le dernier retour solde exactement ce qui a été payé.
/// </summary>
public static class ReturnPlanner
{
    public static ReturnPlan Plan(Sale sale, IReadOnlyList<SaleReturn> previous, IReadOnlyList<ReturnRequest> requested)
    {
        Ensure.That(sale.Status == SaleStatus.Validated, "Cette vente est annulée : rien ne peut plus être rendu.");
        Ensure.That(requested.Count > 0, "Choisissez au moins un article à rendre.");

        var already = ReturnedQuantities(sale, previous);
        var now = (int[])already.Clone();
        var lines = new List<ReturnedLine>();
        foreach (var request in requested)
        {
            Ensure.That(request.LineIndex >= 0 && request.LineIndex < sale.Lines.Count, "Cette ligne n'existe pas dans la vente.");
            Ensure.That(request.Quantity > 0, "La quantité à rendre doit être strictement positive.");
            now[request.LineIndex] = checked(now[request.LineIndex] + request.Quantity);
            Ensure.That(now[request.LineIndex] <= sale.Lines[request.LineIndex].Quantity, "Vous rendez plus que ce qui a été vendu.");
            lines.Add(new ReturnedLine(request.LineIndex, request.Quantity, request.Resellable));
        }

        return new ReturnPlan(lines, Split(sale, previous, NetRefunded(sale, now) - NetRefunded(sale, already)));
    }

    /// <summary>L'annulation de la vente entière : tout ce qui restait est rendu. Refusée si un retour a déjà eu lieu.</summary>
    public static ReturnPlan PlanAll(Sale sale, IReadOnlyList<SaleReturn> previous, bool resellable)
    {
        Ensure.That(previous.Count == 0, "Cette vente a déjà un retour : faites un retour pour le reste.");
        return Plan(sale, previous, sale.Lines.Select((line, index) => new ReturnRequest(index, line.Quantity, resellable)).ToArray());
    }

    /// <summary>Ce qui a déjà été rendu, par ligne.</summary>
    public static int[] ReturnedQuantities(Sale sale, IReadOnlyList<SaleReturn> previous)
    {
        var quantities = new int[sale.Lines.Count];
        foreach (var line in previous.SelectMany(item => item.Lines))
        {
            quantities[line.LineIndex] += line.Quantity;
        }

        return quantities;
    }

    /// <summary>Remboursement cumulé pour ces quantités rendues : lignes arrondies au franc, moins la part de remise au prorata.</summary>
    private static long NetRefunded(Sale sale, int[] returned)
    {
        long gross = 0;
        for (var index = 0; index < sale.Lines.Count; index++)
        {
            gross += Share(sale.Lines[index].Total.Amount, returned[index], sale.Lines[index].Quantity);
        }

        var discount = sale.GrossTotal.Amount == 0 ? 0 : Share(sale.Discount.Amount, gross, sale.GrossTotal.Amount);
        return gross - discount;
    }

    /// <summary>total × part ÷ ensemble, arrondi au franc le plus proche (demi vers le haut) : les mêmes entiers que le total d'une ligne.</summary>
    private static long Share(long total, long part, long whole) => whole == 0 ? 0 : ((total * part * 2) + whole) / (whole * 2);

    private static List<RefundPart> Split(Sale sale, IReadOnlyList<SaleReturn> previous, long amount)
    {
        Ensure.That(amount >= 0, "Le montant à rembourser ne peut pas être négatif.");
        var paid = new Dictionary<PaymentMethod, long>();
        var order = new List<PaymentMethod>();
        foreach (var payment in sale.Payments)
        {
            if (!paid.ContainsKey(payment.Method))
            {
                order.Add(payment.Method);
            }

            paid[payment.Method] = paid.GetValueOrDefault(payment.Method) + payment.Amount.Amount;
        }

        foreach (var part in previous.SelectMany(item => item.Refund))
        {
            paid[part.Method] -= part.Amount.Amount;
        }

        // Dans l'ordre : la dette du client d'abord, puis les espèces, puis le Mobile Money dans l'ordre de la vente.
        var sequence = order
            .OrderBy(method => method == PaymentMethod.Credit ? 0 : method == PaymentMethod.Cash ? 1 : 2)
            .ToArray();
        var parts = new List<RefundPart>();
        var remaining = amount;
        foreach (var method in sequence)
        {
            var take = Math.Min(remaining, Math.Max(paid[method], 0));
            if (take > 0)
            {
                parts.Add(new RefundPart(method, Money.Fcfa(take)));
                remaining -= take;
            }
        }

        Ensure.That(remaining == 0, "Le remboursement dépasse ce qui a été payé.");
        return parts;
    }
}

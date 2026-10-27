using System.Globalization;
using Soro.Domain.Common;
using Soro.Domain.Sales;
using Soro.Domain.Stock;

namespace Soro.Application.Export;

/// <summary>Une période en jours entiers, bornes comprises. Le pays n'a pas d'heure d'été et suit l'heure UTC : un jour est un jour UTC.</summary>
public sealed record ExportPeriod(DateOnly From, DateOnly To)
{
    public DateTimeOffset Start => new(From.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    /// <summary>Le début du jour qui suit la fin : la fin comprend le jour entier.</summary>
    public DateTimeOffset EndExclusive => new(To.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}

public sealed record ExportCounts(int Products, int Customers, int Repayments, int Sales, int SaleLines, int Movements, int Returns = 0)
{
    public long Total => (long)Products + Customers + Repayments + Sales + SaleLines + Movements + Returns;
}

public sealed record ProductExportRow(
    string Name, string? Format, string? Category, string? Barcode, long PurchasePriceFcfa, long SalePriceFcfa, int Stock, int Threshold, bool TracksStock, bool IsActive,
    MeasureUnit Unit = MeasureUnit.Piece);

public sealed record CustomerExportRow(string Name, string? Phone, long CreditCeilingFcfa, long DebtFcfa, bool IsActive);

public sealed record RepaymentExportRow(DateTimeOffset At, string Customer, long AmountFcfa, PaymentMethod Method, string? Reference, string By);

public sealed record SaleExportRow(
    string Key,
    DateTimeOffset At,
    long Ticket,
    string Cashier,
    string? Customer,
    long GrossFcfa,
    long DiscountFcfa,
    long NetFcfa,
    IReadOnlyList<PaymentMethod> Methods,
    SaleStatus Status,
    string? CancelReason);

public sealed record SaleLineExportRow(string Key, DateTimeOffset At, long Ticket, string Product, int Quantity, long UnitPriceFcfa, long TotalFcfa, long? UnitCostFcfa,
    MeasureUnit Unit = MeasureUnit.Piece);

/// <summary>Une annulation ou un retour : ce qui a été rendu, à qui, par quel moyen et qui l'a validé (docs/decisions/0020).</summary>
public sealed record ReturnExportRow(
    DateTimeOffset At, string SaleKey, long Ticket, ReturnKind Kind, string Reason, long TotalFcfa, long CashFcfa, long CreditFcfa, long MobileMoneyFcfa, string By, string? ApprovedBy);

public sealed record MovementExportRow(DateTimeOffset At, string Product, StockMovementKind Kind, int QuantityDelta, string? Reason, long? UnitCostFcfa, string By,
    MeasureUnit Unit = MeasureUnit.Piece);

/// <summary>Les données lues, sans aucun identifiant technique : tout est déjà en mots (docs/specs/export-donnees DON-02).</summary>
public sealed record DataExportData(
    IReadOnlyList<ProductExportRow> Products,
    IReadOnlyList<CustomerExportRow> Customers,
    IReadOnlyList<RepaymentExportRow> Repayments,
    IReadOnlyList<SaleExportRow> Sales,
    IReadOnlyList<SaleLineExportRow> SaleLines,
    IReadOnlyList<MovementExportRow> Movements,
    IReadOnlyList<ReturnExportRow>? Returns = null);

public sealed record DataExport(
    string CommerceName,
    ExportPeriod Period,
    DateTimeOffset At,
    IReadOnlyList<ProductExportRow> Products,
    IReadOnlyList<CustomerExportRow> Customers,
    IReadOnlyList<RepaymentExportRow> Repayments,
    IReadOnlyList<SaleExportRow> Sales,
    IReadOnlyList<SaleLineExportRow> SaleLines,
    IReadOnlyList<MovementExportRow> Movements,
    IReadOnlyList<ReturnExportRow>? Returns = null);

/// <summary>Lecture des données du commerce pour l'export, en une seule lecture cohérente.</summary>
public interface IDataExportQueries
{
    Task<ExportCounts> CountAsync(TenantId tenant, ExportPeriod period, CancellationToken cancellationToken);

    Task<DataExportData> ReadAsync(TenantId tenant, ExportPeriod period, CancellationToken cancellationToken);
}

/// <summary>Fabrique le classeur .xlsx : valeurs seulement, jamais de formule.</summary>
public interface IDataExportWriter
{
    byte[] Write(DataExport data);
}

public static class ExportKeys
{
    /// <summary>« 12/10/2026 08:14 · ticket 12 · Awa Koné » : relie une ligne de vente à sa vente avec un filtre Excel, sans code interne.</summary>
    public static string SaleKey(DateTimeOffset at, long ticket, string cashier) =>
        $"{at.UtcDateTime.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)} · ticket {ticket.ToString(CultureInfo.InvariantCulture)} · {cashier}";
}

public static class ExportLabels
{
    public static string Method(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "Espèces",
        PaymentMethod.OrangeMoney => "Orange Money",
        PaymentMethod.MtnMomo => "MTN MoMo",
        PaymentMethod.MoovMoney => "Moov Money",
        PaymentMethod.Wave => "Wave",
        _ => "Crédit",
    };

    public static string Movement(StockMovementKind kind) => kind switch
    {
        StockMovementKind.Reception => "Réception",
        StockMovementKind.Sale => "Vente",
        StockMovementKind.Loss => "Perte",
        StockMovementKind.Return => "Retour d'un client",
        _ => "Annulation de vente",
    };

    public static string Status(SaleStatus status) => status == SaleStatus.Cancelled ? "Annulée" : "Validée";
}

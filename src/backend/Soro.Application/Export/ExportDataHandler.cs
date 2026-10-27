using System.Globalization;
using Soro.Application.Common;
using Soro.Application.Identity;
using Soro.Application.Reporting;
using Soro.Application.Signup;

namespace Soro.Application.Export;

public sealed record ExportDataQuery(DateOnly? From, DateOnly? To);

public sealed record ExportedFile(string FileName, byte[] Bytes);

/// <summary>
/// L'export Excel des données du propriétaire (docs/specs/export-donnees). Propriétaire seul ; jamais bloqué par l'abonnement (c'est une lecture) ;
/// borné à 100 000 lignes, vérifié avant de fabriquer quoi que ce soit ; limité à 5 par heure et par boutique ; tracé au journal.
/// </summary>
public sealed class ExportDataHandler(
    ICurrentUser user,
    ICommerceRepository commerces,
    IDataExportQueries queries,
    IDataExportWriter writer,
    IAuditLogQueries auditLog,
    IAuditTrail audit,
    IClock clock,
    IUnitOfWork unitOfWork)
{
    public const int MaxLines = 100_000;
    public const int MaxPerHour = 5;
    private const int DefaultMonths = 12;
    private const int MaxYears = 5;

    public async Task<Result<ExportedFile>> HandleAsync(ExportDataQuery query, CancellationToken cancellationToken = default)
    {
        if (user.Role != Domain.Identity.Role.Owner)
        {
            return Result<ExportedFile>.Failure(ApplicationError.Forbidden("Seul le propriétaire peut exporter les données."));
        }

        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var to = query.To ?? today;
        var from = query.From ?? to.AddMonths(-DefaultMonths);
        if (from > to)
        {
            return Result<ExportedFile>.Failure(ApplicationError.RuleViolation("Le début de la période doit être avant sa fin."));
        }

        if (from.AddYears(MaxYears) < to)
        {
            return Result<ExportedFile>.Failure(ApplicationError.RuleViolation("La période ne peut pas dépasser 5 ans."));
        }

        var tooMany = await CheckRateAsync(user.Tenant, now, cancellationToken);
        if (tooMany is not null)
        {
            return Result<ExportedFile>.Failure(tooMany);
        }

        var period = new ExportPeriod(from, to);
        var counts = await queries.CountAsync(user.Tenant, period, cancellationToken);
        if (counts.Total > MaxLines)
        {
            return Result<ExportedFile>.Failure(
                ApplicationError.RuleViolation($"Il y a trop de données pour cette période ({Spaced(counts.Total)} lignes). Choisissez une période plus courte."));
        }

        var commerce = await commerces.GetAsync(user.Tenant, cancellationToken);
        var data = await queries.ReadAsync(user.Tenant, period, cancellationToken);
        var bytes = writer.Write(new DataExport(
            commerce?.Name ?? "Boutique", period, now, data.Products, data.Customers, data.Repayments, data.Sales, data.SaleLines, data.Movements, data.Returns));

        await audit.RecordAsync(
            new AuditEntry(
                user.Tenant,
                user.User,
                AuditActions.DataExported,
                user.User.Value,
                $"Période du {from:dd/MM/yyyy} au {to:dd/MM/yyyy} : {counts.Products} produits, {counts.Customers} clients, {counts.Sales} ventes, "
                + $"{counts.SaleLines} lignes de vente, {counts.Repayments} versements, {counts.Movements} mouvements de stock.",
                now),
            cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Result<ExportedFile>.Success(new($"soro-export-{today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.xlsx", bytes));
    }

    /// <summary>Le journal sert de compteur : il est déjà là, durable et commun à tous les serveurs.</summary>
    private async Task<ApplicationError?> CheckRateAsync(Domain.Common.TenantId tenant, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var recent = (await auditLog.ListAsync(tenant, new AuditLogFilter(now.AddHours(-1), null, [AuditActions.DataExported], MaxPerHour, 0), cancellationToken)).Entries;
        if (recent.Count < MaxPerHour)
        {
            return null;
        }

        var wait = Math.Max(1, (int)Math.Ceiling((recent.Min(entry => entry.At).AddHours(1) - now).TotalMinutes));
        return new ApplicationError(
            ErrorCodes.TooManyRequests,
            $"Vous avez demandé plusieurs exports : réessayez dans {wait} minute{(wait > 1 ? "s" : string.Empty)}.");
    }

    private static string Spaced(long value) => value.ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ');
}

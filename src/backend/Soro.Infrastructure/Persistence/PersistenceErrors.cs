using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Soro.Infrastructure.Persistence;

public static class PersistenceErrors
{
    /// <summary>
    /// Vrai quand deux requêtes concurrentes ont écrit la même clé : la seconde peut simplement être rejouée,
    /// les cas d'usage étant idempotents.
    /// </summary>
    public static bool IsUniqueViolation(Exception exception) =>
        exception is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } };
}

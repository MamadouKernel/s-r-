using Microsoft.EntityFrameworkCore.Design;

namespace Soro.Infrastructure.Persistence;

/// <summary>
/// Utilisé uniquement par <c>dotnet ef</c> pour générer les migrations. La génération ne se connecte pas à la base ;
/// SORO_DESIGN_CONNECTION permet d'appliquer une migration sur une base de développement.
/// </summary>
public sealed class SoroDbContextFactory : IDesignTimeDbContextFactory<SoroDbContext>
{
    public SoroDbContext CreateDbContext(string[] args) =>
        new(PersistenceOptions.Create(
            Environment.GetEnvironmentVariable("SORO_DESIGN_CONNECTION")
            ?? "Host=localhost;Database=soro_design;Username=postgres"));
}

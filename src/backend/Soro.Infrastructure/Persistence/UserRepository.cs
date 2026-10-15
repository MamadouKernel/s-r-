using Microsoft.EntityFrameworkCore;
using Soro.Application.Identity;
using Soro.Domain.Common;
using Soro.Domain.Identity;

namespace Soro.Infrastructure.Persistence;

public sealed class UserRepository(SoroDbContext db) : IUserRepository
{
    public async Task<User?> GetAsync(UserId id, TenantId tenant, CancellationToken cancellationToken)
    {
        var row = await db.Users.SingleOrDefaultAsync(
            user => user.Id == id.Value && user.TenantId == tenant.Value,
            cancellationToken);
        return row is null ? null : UserMapper.ToDomain(row);
    }

    public async Task<User?> FindByIdentifierAsync(
        TenantId tenant,
        string identifier,
        CancellationToken cancellationToken)
    {
        var row = await db.Users.SingleOrDefaultAsync(
            user => user.TenantId == tenant.Value && user.Identifier == identifier,
            cancellationToken);
        return row is null ? null : UserMapper.ToDomain(row);
    }

    public Task AddAsync(User user, CancellationToken cancellationToken)
    {
        db.Users.Add(UserMapper.ToRow(user));
        return Task.CompletedTask;
    }

    public async Task UpdateAsync(User user, CancellationToken cancellationToken)
    {
        var row = await db.Users.FindAsync([user.Id.Value], cancellationToken)
            ?? throw new InvalidOperationException($"L'utilisateur {user.Id.Value} n'existe pas.");
        UserMapper.Apply(row, user);
    }
}

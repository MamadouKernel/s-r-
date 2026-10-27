using Microsoft.EntityFrameworkCore;
using Soro.Application.Cash;
using Soro.Application.Identity;
using Soro.Application.Sales;
using Soro.Domain.Cash;
using Soro.Domain.Common;
using Soro.Domain.Sales;
using Soro.Infrastructure.Persistence;
using Soro.Infrastructure.Security;
using Soro.Domain.Identity;

namespace Soro.Integration.Tests;

/// <summary>Scénarios SC-02/SC-03 de bout en bout : cas d'usage réels, dépôts réels, PostgreSQL réel.</summary>
[Collection(Postgres.Collection)]
public class SynchronisationTests(PostgresFixture fixture)
{
    private readonly TenantId _tenant = TenantId.New();
    private readonly UserId _cashier = UserId.New();

    private StaticUser User => new(_tenant, _cashier, Role.Cashier);

    private SoroDbContext NewRequest() =>
        new(PersistenceOptions.Create(fixture.AppConnectionString(), User));

    private async Task<SyncSalesResult> SyncAsync(SyncSalesCommand batch)
    {
        await using var db = NewRequest();
        var recordSale = new RecordSaleHandler(
            User,
            new SessionRepository(db),
            new SaleRepository(db),
            new ProductRepository(db),
            new StockMovementRepository(db),
            new CustomerRepository(db),
            new DefaultSettings(),
            new UserRepository(db),
            new Pbkdf2PinHasher(new byte[32]),
            new HmacApprovalSeal(new byte[32]),
            new DebtRepaymentRepository(db),
            new AuditTrail(db),
            new FixedClock(),
            new EfUnitOfWork(db));
        return await new SyncSalesHandler(recordSale).HandleAsync(batch);
    }

    private static RecordSaleCommand CashSale(SessionId session, long sequence) =>
        new(
            SaleId.New(),
            session,
            sequence,
            Samples.Morning.AddMinutes(10),
            [new SaleLineInput(ProductId.New(), "Produit", 1, 1_000)],
            0,
            [new PaymentInput(PaymentId.New(), PaymentMethod.Cash, 1_000, 1_000, null)],
            null,
            null);

    [Fact]
    public async Task Replaying_a_batch_after_a_network_cut_creates_no_duplicate_in_the_database()
    {
        var sessionId = SessionId.New();
        await using (var db = NewRequest())
        {
            var open = new OpenRegisterSessionHandler(User, new SessionRepository(db), new EfUnitOfWork(db));
            var opened = await open.HandleAsync(
                new OpenRegisterSessionCommand(sessionId, PointOfSaleId.New(), RegisterId.New(), 10_000, Samples.Morning));
            Assert.True(opened.IsSuccess);
        }

        var batch = new SyncSalesCommand([CashSale(sessionId, 1), CashSale(sessionId, 2)]);
        var first = await SyncAsync(batch);
        var replay = await SyncAsync(batch);

        Assert.All(first.Statuses, status => Assert.Equal(SyncOutcome.Accepted, status.Outcome));
        Assert.All(replay.Statuses, status => Assert.Equal(SyncOutcome.AlreadyProcessed, status.Outcome));

        await using var verify = NewRequest();
        var salesInDatabase = await verify.Database
            .SqlQuery<long>($"SELECT count(*)::bigint AS \"Value\" FROM sales WHERE session_id = {sessionId.Value}")
            .SingleAsync();
        Assert.Equal(2, salesInDatabase);
        var session = (await new SessionRepository(verify).GetAsync(sessionId, _tenant, CancellationToken.None))!;
        Assert.Equal(Money.Fcfa(12_000), session.ExpectedCash);
    }
}

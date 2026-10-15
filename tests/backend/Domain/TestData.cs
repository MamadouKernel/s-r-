using Soro.Domain.Cash;
using Soro.Domain.Common;
using Soro.Domain.Sales;

namespace Soro.Domain.Tests;

internal static class TestData
{
    public static readonly DateTimeOffset Morning = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    public static RegisterAssignment Assignment() =>
        new(TenantId.New(), PointOfSaleId.New(), RegisterId.New(), UserId.New());

    public static CashRegisterSession OpenSession(long openingFloat = 10_000) =>
        CashRegisterSession.Open(SessionId.New(), Assignment(), Money.Fcfa(openingFloat), Morning);

    public static SaleLine Line(long unitPrice, int quantity = 1) =>
        SaleLine.Create(ProductId.New(), "Produit", quantity, Money.Fcfa(unitPrice));

    public static Payment Cash(long amount, long? tendered = null) =>
        Payment.Cash(PaymentId.New(), Money.Fcfa(amount), Money.Fcfa(tendered ?? amount));

    public static Payment Mobile(
        long amount,
        string reference = "MP261015.1234.A12345",
        PaymentMethod method = PaymentMethod.OrangeMoney) =>
        Payment.MobileMoney(PaymentId.New(), method, Money.Fcfa(amount), reference);

    public static SaleDraft Draft(
        IReadOnlyList<SaleLine> lines,
        IReadOnlyList<Payment> payments,
        Money? discount = null,
        CustomerId? customer = null) =>
        new(SaleId.New(), 1, Morning.AddMinutes(5), lines, discount ?? Money.Zero, payments, customer);
}

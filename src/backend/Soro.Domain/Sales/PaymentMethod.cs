namespace Soro.Domain.Sales;

public enum PaymentMethod
{
    Cash,
    OrangeMoney,
    MtnMomo,
    MoovMoney,
    Wave,
    Credit,
}

public static class PaymentMethodExtensions
{
    public static bool IsMobileMoney(this PaymentMethod method) =>
        method is PaymentMethod.OrangeMoney
            or PaymentMethod.MtnMomo
            or PaymentMethod.MoovMoney
            or PaymentMethod.Wave;
}

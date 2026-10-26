using Soro.Application.Cash;
using Soro.Application.Sales;
using Soro.Domain.Common;

namespace Soro.Api.Contracts;

internal static class RequestMapping
{
    public static OpenRegisterSessionCommand ToCommand(this OpenRegisterSessionRequest request) =>
        new(
            new SessionId(request.SessionId),
            new PointOfSaleId(request.PointOfSaleId),
            new RegisterId(request.RegisterId),
            request.OpeningFloatFcfa,
            request.OpenedAt);

    public static RecordSaleCommand ToCommand(this RecordSaleRequest request) =>
        new(
            new SaleId(request.SaleId),
            new SessionId(request.SessionId),
            request.LocalSequence,
            request.At,
            request.Lines
                .Select(line => new SaleLineInput(new ProductId(line.ProductId), line.Name, line.Quantity, line.UnitPriceFcfa))
                .ToArray(),
            request.DiscountFcfa,
            request.Payments
                .Select(payment => new PaymentInput(
                    new PaymentId(payment.PaymentId),
                    payment.Method,
                    payment.AmountFcfa,
                    payment.TenderedFcfa,
                    payment.Reference))
                .ToArray(),
            request.CustomerId is { } customer ? new CustomerId(customer) : null,
            request.DiscountApproverId is { } approver ? new Approval(new UserId(approver), request.DiscountApproverPin ?? string.Empty) : null,
            request.CreditApproverId is { } creditApprover ? new Approval(new UserId(creditApprover), request.CreditApproverPin ?? string.Empty) : null);
}

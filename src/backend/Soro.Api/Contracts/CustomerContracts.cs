using Soro.Domain.Sales;

namespace Soro.Api.Contracts;

public sealed record CreateCustomerRequest
{
    public required Guid CustomerId { get; init; }

    public required string Name { get; init; }

    public string? Phone { get; init; }

    /// <summary>Réservé au propriétaire et au gérant ; absent, le client reçoit le plafond par défaut du commerce.</summary>
    public long? CreditCeilingFcfa { get; init; }
}

public sealed record UpdateCustomerRequest
{
    public required string Name { get; init; }

    public string? Phone { get; init; }

    public required long CreditCeilingFcfa { get; init; }
}

public sealed record RecordRepaymentRequest
{
    public required Guid RepaymentId { get; init; }

    public required long AmountFcfa { get; init; }

    public required PaymentMethod Method { get; init; }

    public string? Reference { get; init; }
}

public sealed record CustomerCreatedResponse(Guid CustomerId, bool AlreadyProcessed, long CreditCeilingFcfa);

public sealed record RepaymentRecordedResponse(Guid RepaymentId, long DebtFcfa, bool AlreadyProcessed);

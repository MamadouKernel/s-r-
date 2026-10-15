namespace Soro.Application.Common;

/// <summary>Échec attendu d'un cas d'usage : un code stable pour les clients, un message en français.</summary>
public sealed record ApplicationError(string Code, string Message)
{
    public static ApplicationError NotFound(string message) => new(ErrorCodes.NotFound, message);

    public static ApplicationError Forbidden(string message) => new(ErrorCodes.Forbidden, message);

    public static ApplicationError RuleViolation(string message) => new(ErrorCodes.RuleViolation, message);
}

public static class ErrorCodes
{
    public const string NotFound = "not_found";
    public const string Forbidden = "forbidden";
    public const string RuleViolation = "rule_violation";
    public const string MobileMoneyReferenceUsed = "mobile_money_reference_used";
    public const string RegisterAlreadyOpen = "register_already_open";
    public const string InvalidCredentials = "invalid_credentials";
    public const string AccountLocked = "account_locked";
    public const string IdentifierTaken = "identifier_taken";
}

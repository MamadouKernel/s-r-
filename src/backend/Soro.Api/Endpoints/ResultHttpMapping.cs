using Soro.Application.Common;

namespace Soro.Api.Endpoints;

internal static class ResultHttpMapping
{
    public static IResult ToHttp<T>(this Result<T> result, Func<T, IResult> onSuccess) =>
        result.IsSuccess ? onSuccess(result.Value!) : ToProblem(result.Error!);

    /// <summary>Erreur applicative → <c>application/problem+json</c> avec le code stable en extension.</summary>
    public static IResult ToProblem(ApplicationError error)
    {
        var status = StatusFor(error.Code);
        return Results.Problem(
            title: TitleFor(status),
            detail: error.Message,
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }

    private static int StatusFor(string code) => code switch
    {
        ErrorCodes.NotFound => StatusCodes.Status404NotFound,
        ErrorCodes.Forbidden => StatusCodes.Status403Forbidden,
        ErrorCodes.RuleViolation => StatusCodes.Status422UnprocessableEntity,
        ErrorCodes.MobileMoneyReferenceUsed or ErrorCodes.RegisterAlreadyOpen or ErrorCodes.IdentifierTaken
            or ErrorCodes.BarcodeTaken or ErrorCodes.EmailAlreadyRegistered
            => StatusCodes.Status409Conflict,
        ErrorCodes.InvalidCode or ErrorCodes.VerificationExpired => StatusCodes.Status422UnprocessableEntity,
        ErrorCodes.TooManyRequests => StatusCodes.Status429TooManyRequests,
        ErrorCodes.EmailNotSent => StatusCodes.Status503ServiceUnavailable,
        ErrorCodes.InvalidCredentials => StatusCodes.Status401Unauthorized,
        ErrorCodes.AccountLocked => StatusCodes.Status423Locked,
        _ => StatusCodes.Status400BadRequest,
    };

    private static string TitleFor(int status) => status switch
    {
        StatusCodes.Status404NotFound => "Ressource introuvable",
        StatusCodes.Status403Forbidden => "Action interdite",
        StatusCodes.Status422UnprocessableEntity => "Règle métier non respectée",
        StatusCodes.Status409Conflict => "Conflit",
        StatusCodes.Status401Unauthorized => "Identifiants invalides",
        StatusCodes.Status423Locked => "Compte verrouillé",
        StatusCodes.Status429TooManyRequests => "Trop de demandes",
        StatusCodes.Status503ServiceUnavailable => "Envoi impossible",
        _ => "Requête invalide",
    };
}

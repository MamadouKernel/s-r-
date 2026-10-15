namespace Soro.Application.Common;

/// <summary>Résultat d'un cas d'usage : une valeur, ou une <see cref="ApplicationError"/> attendue (jamais une exception métier).</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1000", Justification = "Fabriques statiques : forme idiomatique du motif Result.")]
public sealed class Result<T>
{
    private Result(T? value, ApplicationError? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }

    public ApplicationError? Error { get; }

    public bool IsSuccess => Error is null;

    public static Result<T> Success(T value) => new(value, null);

    public static Result<T> Failure(ApplicationError error) => new(default, error);
}

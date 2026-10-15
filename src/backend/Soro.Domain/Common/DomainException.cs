namespace Soro.Domain.Common;

/// <summary>Violation d'une règle métier du domaine.</summary>
public sealed class DomainException(string message) : Exception(message);

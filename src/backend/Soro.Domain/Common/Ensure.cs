namespace Soro.Domain.Common;

internal static class Ensure
{
    public static void That(bool condition, string message)
    {
        if (!condition)
        {
            throw new DomainException(message);
        }
    }
}

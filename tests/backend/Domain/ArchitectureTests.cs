using Soro.Domain.Common;

namespace Soro.Domain.Tests;

public class ArchitectureTests
{
    [Fact]
    public void Domain_depends_only_on_the_base_class_library()
    {
        var unexpected = typeof(Money).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => !name.StartsWith("System", StringComparison.Ordinal))
            .Where(name => name is not ("netstandard" or "mscorlib"))
            .ToList();

        Assert.Empty(unexpected);
    }
}

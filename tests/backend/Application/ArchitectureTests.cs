using Soro.Application.Common;

namespace Soro.Application.Tests;

public class ArchitectureTests
{
    [Fact]
    public void Application_depends_only_on_the_domain_and_the_base_class_library()
    {
        var unexpected = typeof(Result<>).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => !name.StartsWith("System", StringComparison.Ordinal))
            .Where(name => name is not ("netstandard" or "mscorlib" or "Soro.Domain"))
            .ToList();

        Assert.Empty(unexpected);
    }
}

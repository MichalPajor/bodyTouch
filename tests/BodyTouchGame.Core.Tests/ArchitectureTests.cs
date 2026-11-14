using System.Reflection;

namespace BodyTouchGame.Core.Tests;

public class ArchitectureTests
{
    [Fact]
    public void CoreAssemblyDoesNotReferenceMaui()
    {
        var assembly = Assembly.Load("BodyTouchGame.Core");

        var mauiReferences = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name.StartsWith("Microsoft.Maui", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(mauiReferences);
    }
}

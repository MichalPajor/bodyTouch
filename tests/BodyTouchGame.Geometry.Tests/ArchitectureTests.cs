using System.Reflection;

namespace BodyTouchGame.Geometry.Tests;

public class ArchitectureTests
{
    [Fact]
    public void GeometryAssemblyDoesNotReferenceMaui()
    {
        var assembly = Assembly.Load("BodyTouchGame.Geometry");

        var mauiReferences = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name.StartsWith("Microsoft.Maui", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(mauiReferences);
    }
}

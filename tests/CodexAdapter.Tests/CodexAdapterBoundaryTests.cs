namespace CodexThemeStudio.CodexAdapter.Tests;

using CodexThemeStudio.CodexAdapter;

public class CodexAdapterBoundaryTests
{
    [Fact]
    public void CodexAdapterAssembly_DoesNotReferenceDesktop()
    {
        var references = typeof(CodexAdapterAssemblyMarker)
            .Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();

        Assert.DoesNotContain("PresentationFramework", references);
        Assert.DoesNotContain("CodexThemeStudio.Desktop", references);
    }
}

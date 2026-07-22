namespace CodexThemeStudio.Storage.Tests;

using CodexThemeStudio.Storage;

public class StorageBoundaryTests
{
    [Fact]
    public void StorageAssembly_DoesNotReferenceUiOrCodexAdapter()
    {
        var references = typeof(StorageAssemblyMarker)
            .Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();

        Assert.DoesNotContain("PresentationFramework", references);
        Assert.DoesNotContain("CodexThemeStudio.Desktop", references);
        Assert.DoesNotContain("CodexThemeStudio.CodexAdapter", references);
    }
}

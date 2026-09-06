using System.Reflection;
using CodexThemeStudio.Desktop.MacOS.ViewModels;

namespace CodexThemeStudio.Desktop.MacOS.Tests.Architecture;

public sealed class DependencyBoundaryTests
{
    [Fact]
    public void DesktopHasNoDirectForbiddenProductReferences()
    {
        var referenced = typeof(MainWindowViewModel)
            .Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name)
            .Where(name => name is not null)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(
            "CodexThemeStudio.CodexRuntime",
            referenced);
        Assert.DoesNotContain(
            "CodexThemeStudio.CodexAdapter.MacOS",
            referenced);
    }

    [Fact]
    public void ViewModelsDoNotReferenceAvaloniaTypes()
    {
        var viewModelTypes = typeof(MainWindowViewModel)
            .Assembly
            .DefinedTypes
            .Where(type =>
                type.Namespace?.Contains(
                    ".ViewModels",
                    StringComparison.Ordinal) == true)
            .ToArray();

        var referencedTypes = viewModelTypes
            .SelectMany(type => type.DeclaredProperties)
            .Select(property => property.PropertyType)
            .Concat(
                viewModelTypes
                    .SelectMany(type => type.DeclaredMethods)
                    .Select(method => method.ReturnType))
            .Where(type => type.Namespace is not null)
            .ToArray();

        Assert.DoesNotContain(
            referencedTypes,
            type => type.Namespace!.StartsWith(
                "Avalonia",
                StringComparison.Ordinal));
    }
}

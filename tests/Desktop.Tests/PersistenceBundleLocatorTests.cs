using System.IO;

namespace CodexThemeStudio.Desktop.Tests;

public sealed class PersistenceBundleLocatorTests
{
    [Fact]
    public void Find_UsesApplicationDirectory_WhenItContainsCompleteBundle()
    {
        using var fixture = new BundleFixture();
        fixture.CreateBundle(fixture.ApplicationDirectory);

        var result = PersistenceBundleLocator.Find(
            fixture.ApplicationDirectory,
            "1.1.7");

        Assert.Equal(
            Path.GetFullPath(fixture.ApplicationDirectory),
            result);
    }

    [Fact]
    public void Find_UsesExactVersionDevelopmentRelease_WhenOutputIsIncomplete()
    {
        using var fixture = new BundleFixture();
        fixture.CreateSolutionMarker();
        var expected = fixture.CreateReleaseBundle("1.1.7");
        fixture.CreateReleaseBundle("1.1.8");

        var result = PersistenceBundleLocator.Find(
            fixture.ApplicationDirectory,
            "1.1.7");

        Assert.Equal(Path.GetFullPath(expected), result);
    }

    [Fact]
    public void Find_DoesNotUseDifferentVersionOrMalformedVersion()
    {
        using var fixture = new BundleFixture();
        fixture.CreateSolutionMarker();
        fixture.CreateReleaseBundle("1.1.8");

        var differentVersion = PersistenceBundleLocator.Find(
            fixture.ApplicationDirectory,
            "1.1.7");
        var malformedVersion = PersistenceBundleLocator.Find(
            fixture.ApplicationDirectory,
            @"..\1.1.8");

        Assert.Equal(
            Path.GetFullPath(fixture.ApplicationDirectory),
            differentVersion);
        Assert.Equal(
            Path.GetFullPath(fixture.ApplicationDirectory),
            malformedVersion);
    }

    private sealed class BundleFixture : IDisposable
    {
        public BundleFixture()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "CodexThemeStudio.PersistenceBundleLocatorTests",
                Guid.NewGuid().ToString("N"));
            ApplicationDirectory = Path.Combine(
                Root,
                "src",
                "CodexThemeStudio.Desktop",
                "bin",
                "Release",
                "net8.0-windows");
            Directory.CreateDirectory(ApplicationDirectory);
        }

        public string Root { get; }

        public string ApplicationDirectory { get; }

        public void CreateSolutionMarker() =>
            File.WriteAllText(
                Path.Combine(Root, "CodexThemeStudio.sln"),
                string.Empty);

        public string CreateReleaseBundle(string version)
        {
            var bundle = Path.Combine(
                Root,
                "artifacts",
                "release",
                version,
                $"CodexThemeManager-{version}-win-x64-portable");
            CreateBundle(bundle);
            return bundle;
        }

        public void CreateBundle(string root)
        {
            var files = new[]
            {
                Path.Combine(root, "agent", "CodexThemeStudio.Agent.exe"),
                Path.Combine(root, "runtime", "node", "node.exe"),
                Path.Combine(root, "runtime", "injector", "index.mjs"),
            };
            foreach (var file in files)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, Path.GetFileName(file));
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}

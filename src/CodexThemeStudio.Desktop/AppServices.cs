using System.IO;
using CodexThemeStudio.CodexAdapter;
using CodexThemeStudio.Contracts.Interfaces;
using CodexThemeStudio.Desktop.Services;
using CodexThemeStudio.Desktop.ViewModels;
using CodexThemeStudio.Storage;

namespace CodexThemeStudio.Desktop;

public sealed class AppServices
{
    private AppServices(MainWindowViewModel mainWindowViewModel)
    {
        MainWindowViewModel = mainWindowViewModel;
    }

    public MainWindowViewModel MainWindowViewModel { get; }

    public static async Task<AppServices> CreateAsync(CancellationToken cancellationToken)
    {
        var storageLocation = StorageLocationService.CreateDefault();
        var defaultDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexThemeStudio",
            "Data");
        var location = await storageLocation.InitializeAsync(
            defaultDataRoot,
            cancellationToken);
        if (!location.IsSuccess ||
            !location.Value!.IsAvailable ||
            !location.Value.IsWritable)
        {
            throw new InvalidOperationException(
                location.Error?.UserMessage ?? location.Value?.UserMessage);
        }

        var dataRoot = location.Value.DataRoot;
        var database = new SqliteDatabaseInitializer(dataRoot);
        var initialized = await database.InitializeAsync(cancellationToken);
        if (!initialized.IsSuccess)
        {
            throw new InvalidOperationException(initialized.Error!.UserMessage);
        }

        IThemeRepository repository = new SqliteThemeRepository(dataRoot);
        IThemeAssetStore assetStore = new FileThemeAssetStore(dataRoot);
        IImagePipeline imagePipeline = new ImagePipeline(dataRoot);
        IThemeImageImportService imageImport = new ThemeImageImportService(
            dataRoot,
            imagePipeline,
            repository);
        IThemePackageService packageService = new ThemePackageService(
            dataRoot,
            repository,
            imageImport);
        var injector = CreateInjectorClient();
        ICodexThemeRuntime runtime = new CodexThemeRuntimeService(
            injector,
            injector,
            injector,
            assetStore,
            repository,
            new AtomicCurrentSessionStore(dataRoot));
        IPersistenceService persistence = new PersistenceService(
            runtime,
            assetStore,
            repository,
            new PersistenceSnapshotStore(),
            new ManagedAgentInstaller(),
            new WindowsRunStartupManager(),
            new AgentProcessController(),
            AppContext.BaseDirectory);

        var resolver = new TrustedPathResolver(dataRoot);
        string? ResolveThumbnail(string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return null;
            }

            var resolved = resolver.Resolve(relativePath);
            return resolved.IsSuccess && File.Exists(resolved.Value)
                ? resolved.Value
                : null;
        }

        string? ResolveDataPath(string relativePath)
        {
            var resolved = resolver.Resolve(relativePath);
            return resolved.IsSuccess && File.Exists(resolved.Value)
                ? resolved.Value
                : null;
        }

        var viewModel = new MainWindowViewModel(
            repository,
            runtime,
            persistence,
            storageLocation,
            new WpfUserDialogService(),
            ResolveThumbnail,
            packageService,
            new ThemeEditorViewModel(
                repository,
                imagePipeline,
                assetStore,
                ResolveDataPath));
        return new AppServices(viewModel);
    }

    private static InjectorCommandClient CreateInjectorClient()
    {
        var runtimeRoot = FindRuntimeRoot();
        var nodePath = Path.Combine(runtimeRoot, "node", "node.exe");
        if (!File.Exists(nodePath))
        {
            nodePath = FindOnPath("node.exe") ?? nodePath;
        }

        return new InjectorCommandClient(
            nodePath,
            Path.Combine(runtimeRoot, "injector", "index.mjs"));
    }

    private static string FindRuntimeRoot()
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "runtime"),
            Path.Combine(Environment.CurrentDirectory, "runtime"),
        };
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var index = 0; directory is not null && index < 6; index++)
        {
            candidates.Add(Path.Combine(directory.FullName, "runtime"));
            directory = directory.Parent;
        }

        return candidates.FirstOrDefault(path =>
                   File.Exists(Path.Combine(path, "injector", "index.mjs")))
            ?? candidates[0];
    }

    private static string? FindOnPath(string fileName)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
            }
        }

        return null;
    }
}

using System.IO;
using System.Windows;
using CodexThemeStudio.CodexAdapter;
using CodexThemeStudio.Contracts.Interfaces;
using CodexThemeStudio.Desktop.Services;
using CodexThemeStudio.Desktop.ViewModels;
using CodexThemeStudio.Storage;
using CodexThemeStudio.Update;

namespace CodexThemeStudio.Desktop;

public sealed class AppServices
{
    private AppServices(
        MainWindowViewModel mainWindowViewModel,
        IColorHistoryService colorHistory)
    {
        MainWindowViewModel = mainWindowViewModel;
        ColorHistory = colorHistory;
    }

    public MainWindowViewModel MainWindowViewModel { get; }

    public IColorHistoryService ColorHistory { get; }

    public static async Task<AppServices> CreateAsync(
        CancellationToken cancellationToken,
        LocalDiagnosticService? diagnostics = null,
        Guid? diagnosticSessionId = null,
        string? appVersion = null)
    {
        diagnostics ??= new LocalDiagnosticService(
            LocalDiagnosticService.GetDefaultLogDirectory());
        appVersion ??= DiagnosticEventFactory.GetApplicationVersion(
            typeof(AppServices).Assembly);
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
        IColorHistoryService colorHistory = new ColorHistoryService(dataRoot);
        await colorHistory.InitializeAsync(cancellationToken);
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
        var targetSelection = new CodexTargetSelectionService();
        var injector = CreateInjectorClient(
            targetSelection,
            diagnostics,
            diagnosticSessionId,
            appVersion);
        var currentSessionStore = new AtomicCurrentSessionStore(dataRoot);
        IExternalPersistenceService externalPersistence =
            new OkkSkinExternalPersistenceService();
        var externalThemeCatalog = new ExternalThemeCatalogService(
            new OkkSkinExternalThemeSource(injector, currentSessionStore),
            repository,
            imageImport);
        ICodexThemeRuntime runtime = new CodexThemeRuntimeService(
            injector,
            injector,
            injector,
            assetStore,
            repository,
            currentSessionStore);
        IPersistenceService persistence = new PersistenceService(
            runtime,
            assetStore,
            repository,
            new PersistenceSnapshotStore(),
            new ManagedAgentInstaller(),
            new WindowsRunStartupManager(),
            new AgentProcessController(),
            PersistenceBundleLocator.Find(
                AppContext.BaseDirectory,
                appVersion));

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
                ResolveDataPath,
                colorHistory),
            externalTheme: null,
            externalPersistence,
            targetSelection,
            diagnostics,
            diagnostics,
            diagnostics,
            static text => Clipboard.SetText(text),
            appVersion,
            diagnosticSessionId,
            externalThemeCatalog,
            codexDiscovery: injector,
            updateService: new GitHubUpdateService(
                new GitHubUpdateServiceOptions { CurrentVersion = appVersion }),
            updateDialogs: new WpfUpdateDialogService(),
            updateInstaller: CreateUpdateInstaller(appVersion),
            updatePreflight: zipBytes => UpdatePreflightValidator.Check(
                AppContext.BaseDirectory,
                GetUpdatesRoot(),
                zipBytes),
            requestApplicationShutdown: RequestApplicationShutdown);
        return new AppServices(viewModel, colorHistory);
    }

    private static InjectorCommandClient CreateInjectorClient(
        CodexTargetSelectionService targetSelection,
        IDiagnosticEventSink diagnostics,
        Guid? diagnosticSessionId,
        string appVersion)
    {
        var runtimeRoot = FindRuntimeRoot();
        var nodePath = Path.Combine(runtimeRoot, "node", "node.exe");
        if (!File.Exists(nodePath))
        {
            nodePath = FindOnPath("node.exe") ?? nodePath;
        }

        return new InjectorCommandClient(
            nodePath,
            Path.Combine(runtimeRoot, "injector", "index.mjs"),
            targetSelection: targetSelection,
            diagnosticSink: diagnostics,
            diagnosticSessionId: diagnosticSessionId,
            diagnosticAppVersion: appVersion);
    }

    private static IUpdateInstaller CreateUpdateInstaller(string appVersion)
    {
        var runtimeRoot = FindRuntimeRoot();
        var applicationRoot = Path.GetFullPath(AppContext.BaseDirectory);
        var executable = File.Exists(Path.Combine(applicationRoot, "CodexThemeManager.exe"))
            ? "CodexThemeManager.exe"
            : "CodexThemeStudio.Desktop.exe";
        return new TransactionalUpdateInstaller(
            new TransactionalUpdateInstallerOptions
            {
                CurrentVersion = appVersion,
                ApplicationRoot = applicationRoot,
                NodeExecutablePath = Path.Combine(runtimeRoot, "node", "node.exe"),
                UpdaterScriptPath = Path.Combine(runtimeRoot, "updater", "apply-update.mjs"),
                ExecutableRelativePath = executable,
                UpdatesRoot = GetUpdatesRoot(),
            });
    }

    private static string GetUpdatesRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexThemeStudio",
        "Updates");

    private static void RequestApplicationShutdown()
    {
        _ = Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(350);
            Application.Current.Shutdown();
        });
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

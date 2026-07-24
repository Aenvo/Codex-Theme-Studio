using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.Storage;

public interface IThemeDirectoryRecycleService
{
    OperationResult MoveToSystemRecycleBin(string fullPath);
}

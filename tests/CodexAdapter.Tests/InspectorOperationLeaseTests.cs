namespace CodexThemeStudio.CodexAdapter.Tests;

public sealed class InspectorOperationLeaseTests
{
    [Fact]
    public async Task Lease_CanBeReleasedFromAThreadDifferentFromTheAcquirer()
    {
        var result = await InspectorOperationLease.AcquireAsync(
            "probe",
            CancellationToken.None,
            $@"Local\CodexThemeStudio.InspectorOperationSemaphore.Tests.{Guid.NewGuid():N}");

        Assert.True(result.IsSuccess);

        var exception = await Record.ExceptionAsync(
            () => Task.Run(() => result.Value!.Dispose()));

        Assert.Null(exception);
    }
}

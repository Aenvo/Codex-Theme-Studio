namespace CodexThemeStudio.Integration.Tests;

using Xunit;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute(string action)
    {
        var requestedAction = Environment.GetEnvironmentVariable("CTS_LIVE_ACTION");
        if (!string.Equals(requestedAction, action, StringComparison.OrdinalIgnoreCase))
        {
            Skip =
                $"真实持久化场景未执行：设置 CTS_LIVE_ACTION={action}、" +
                "CTS_LIVE_BUNDLE 和 CTS_LIVE_DATA_ROOT 后单独运行。";
        }
    }
}

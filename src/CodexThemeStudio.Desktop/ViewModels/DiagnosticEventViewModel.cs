using CodexThemeStudio.Contracts.Models;

namespace CodexThemeStudio.Desktop.ViewModels;

public sealed class DiagnosticEventViewModel
{
    public DiagnosticEventViewModel(DiagnosticEventGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        Source = group.Event.Source == DiagnosticSource.Desktop
            ? "桌面应用"
            : "持久化 Agent";
        Level = group.Event.Level;
        Title = DescribeEvent(group.Event);
        Code = group.Event.DiagnosticCode ??
            group.Event.ErrorCode?.ToString() ??
            group.Event.EventName;
        TimeText = group.RepeatCount > 1
            ? $"{group.FirstTimestampUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss} — " +
              $"{group.LastTimestampUtc.ToLocalTime():HH:mm:ss}"
            : group.LastTimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        RepeatText = group.RepeatCount > 1
            ? $"重复 {group.RepeatCount} 次"
            : string.Empty;
    }

    public string Source { get; }

    public DiagnosticLevel Level { get; }

    public string Title { get; }

    public string Code { get; }

    public string TimeText { get; }

    public string RepeatText { get; }

    private static string DescribeEvent(DiagnosticEvent item) =>
        item.EventName switch
        {
            "desktop.started" => "桌面应用开始启动",
            "desktop.ready" => "桌面应用启动完成",
            "desktop.stopped" => "桌面应用正常退出",
            "desktop.operation.started" => "操作开始",
            "desktop.operation.succeeded" => "操作完成",
            "desktop.operation.failed" => "操作失败",
            "desktop.unhandled_exception" => "桌面应用发生未处理异常",
            "agent.started" => "持久化 Agent 已启动",
            "agent.stopped" => "持久化 Agent 已停止",
            "agent.recovered" => "持久化 Agent 已从错误中恢复",
            "agent.cycle.failed" => "持久化 Agent 本轮执行失败",
            "agent.configuration.failed" => "持久化 Agent 配置无效",
            "agent.state.persistent" => "持久主题运行正常",
            "agent.state.notrunning" => "Codex 未运行，Agent 正在等待",
            "agent.state.notinstalled" => "未检测到 Codex",
            "agent.state.unsupported" => "当前 Codex 构建尚未取得持久化资格",
            "legacy.cycle" => "旧版 Agent 状态记录",
            "legacy.error" => "旧版 Agent 错误记录",
            _ => item.Level == DiagnosticLevel.Error
                ? "诊断事件报告错误"
                : "诊断状态已更新",
        };
}

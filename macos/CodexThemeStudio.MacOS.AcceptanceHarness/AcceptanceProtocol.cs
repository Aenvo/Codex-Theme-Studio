using System.Text.Json;
using System.Text.Json.Serialization;
using CodexThemeStudio.Application.MacOS;

namespace CodexThemeStudio.MacOS.AcceptanceHarness;

public sealed record LiveAuthorization(
    string Purpose,
    bool AcknowledgeStatefulInspector,
    bool AcknowledgeTemporaryTheme,
    DateTimeOffset ExpiresAtUtc,
    string StagingAssemblyId);

public sealed record AcceptanceRequest(
    int SchemaVersion,
    Guid RequestId,
    string Operation,
    int DeadlineMilliseconds,
    LiveAuthorization Authorization,
    MacThemeInput? Theme);

public static class AcceptanceProtocol
{
    public const int MaximumRequestBytes = 64 * 1024;
    public const int MaximumResponseBytes = 256 * 1024;
    public const int MaximumDeadlineMilliseconds = 120_000;
    public const int ProcessHardLimitMilliseconds = 150_000;
    public static readonly TimeSpan MaximumAuthorizationLifetime =
        TimeSpan.FromMinutes(5);

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static AcceptanceRequest Decode(
        ReadOnlySpan<byte> input,
        DateTimeOffset now)
    {
        if (input.IsEmpty || input.Length > MaximumRequestBytes)
        {
            throw new AcceptanceProtocolException(
                "protocol.request_too_large",
                "request");
        }

        AcceptanceRequest? request;
        bool containsTheme;
        try
        {
            using var document = JsonDocument.Parse(input.ToArray());
            containsTheme = document.RootElement.TryGetProperty(
                "theme",
                out _);
            request = JsonSerializer.Deserialize<AcceptanceRequest>(
                input,
                JsonOptions);
        }
        catch (JsonException)
        {
            throw new AcceptanceProtocolException(
                "protocol.request_invalid",
                "request");
        }

        if (request is null ||
            request.SchemaVersion != 1 ||
            request.RequestId == Guid.Empty ||
            request.Operation is not
                ("qualification-cycle" or "inspector-diagnostic") ||
            request.DeadlineMilliseconds is <= 0 or
                > MaximumDeadlineMilliseconds)
        {
            throw new AcceptanceProtocolException(
                "protocol.request_invalid",
                "request");
        }

        if (request.Authorization is null ||
            !request.Authorization.AcknowledgeStatefulInspector ||
            !IsSha256(request.Authorization.StagingAssemblyId) ||
            request.Authorization.ExpiresAtUtc.Offset != TimeSpan.Zero ||
            request.Authorization.ExpiresAtUtc <= now ||
            request.Authorization.ExpiresAtUtc - now >
                MaximumAuthorizationLifetime)
        {
            throw new AcceptanceProtocolException(
                "authorization.live_required",
                "authorization");
        }

        if (request.Operation == "qualification-cycle" &&
            (request.Authorization.Purpose != "stage-7b.2b" ||
             !request.Authorization.AcknowledgeTemporaryTheme ||
             !containsTheme ||
             request.Theme is null ||
             !request.Theme.IsValid))
        {
            throw new AcceptanceProtocolException(
                "request.theme_invalid",
                "request");
        }
        if (request.Operation == "inspector-diagnostic" &&
            (request.Authorization.Purpose != "stage-7b.2b-diagnostic" ||
             request.Authorization.AcknowledgeTemporaryTheme))
        {
            throw new AcceptanceProtocolException(
                "authorization.live_required",
                "authorization");
        }
        if (request.Operation == "inspector-diagnostic" &&
            containsTheme)
        {
            throw new AcceptanceProtocolException(
                "request.theme_invalid",
                "request");
        }

        return request;
    }

    public static byte[] Encode(object value)
    {
        var output = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        if (output.Length > MaximumResponseBytes)
        {
            throw new AcceptanceProtocolException(
                "protocol.response_too_large",
                "response");
        }
        return output;
    }

    private static bool IsSha256(string value) =>
        value is not null &&
        value.Length == 64 &&
        value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

public sealed class AcceptanceProtocolException(
    string code,
    string stage) : Exception
{
    public string Code { get; } = code;
    public string Stage { get; } = stage;
}

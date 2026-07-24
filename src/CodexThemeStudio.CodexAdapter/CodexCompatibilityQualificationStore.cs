using System.Text.Json;
using CodexThemeStudio.Contracts.Models;
using CodexThemeStudio.Contracts.Results;

namespace CodexThemeStudio.CodexAdapter;

public sealed class CodexCompatibilityQualificationStore
{
    public const int CurrentProbeContractVersion = 2;
    public const int CurrentRequiredCapabilitiesVersion = 1;

    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, QualificationRecord>? inMemory;
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public CodexCompatibilityQualificationStore(string? path = null)
    {
        this.path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexThemeStudio",
            "Runtime",
            "compatibility-qualifications.json");
    }

    private CodexCompatibilityQualificationStore(
        Dictionary<string, QualificationRecord> inMemory)
    {
        path = string.Empty;
        this.inMemory = inMemory;
    }

    public static CodexCompatibilityQualificationStore CreateInMemory(
        params string[] qualifiedExecutableSha256)
    {
        var records = qualifiedExecutableSha256.ToDictionary(
            hash => hash,
            hash => QualificationRecord.Legacy(hash),
            StringComparer.OrdinalIgnoreCase);
        return new CodexCompatibilityQualificationStore(records);
    }

    internal static CodexCompatibilityQualificationStore CreateWithRecordsInMemory(
        params QualificationRecord[] records) =>
        new(records.ToDictionary(
            record => record.ExecutableSha256,
            StringComparer.OrdinalIgnoreCase));

    public async Task<OperationResult<bool>> IsQualifiedAsync(
        string executableSha256,
        CancellationToken cancellationToken)
    {
        var state = await ReadStateAsync(cancellationToken).ConfigureAwait(false);
        return state.IsSuccess
            ? OperationResult<bool>.Success(
                (state.Value!.Records.TryGetValue(executableSha256, out var record) &&
                 record.PersistenceQualified) ||
                state.Value.LegacyQualifiedExecutableSha256.Contains(executableSha256))
            : OperationResult<bool>.Failure(state.Error!);
    }

    internal async Task<OperationResult<QualificationRecord?>> FindCompatibleAsync(
        CodexInstallationInfo installation,
        CancellationToken cancellationToken)
    {
        var state = await ReadStateAsync(cancellationToken).ConfigureAwait(false);
        if (!state.IsSuccess)
        {
            return OperationResult<QualificationRecord?>.Failure(state.Error!);
        }

        if (!state.Value!.Records.TryGetValue(
                installation.ExecutableSha256,
                out var record) ||
            !record.IsReusableFor(installation))
        {
            return OperationResult<QualificationRecord?>.SuccessOptional(null);
        }

        return OperationResult<QualificationRecord?>.Success(record);
    }

    internal async Task<OperationResult<QualificationRecord?>> ReadLatestAsync(
        CancellationToken cancellationToken)
    {
        var state = await ReadStateAsync(cancellationToken).ConfigureAwait(false);
        if (!state.IsSuccess)
        {
            return OperationResult<QualificationRecord?>.Failure(state.Error!);
        }

        return OperationResult<QualificationRecord?>.SuccessOptional(
            state.Value!.Records.Values
                .Where(record => record.HasReusableCapabilityEvidence)
                .OrderByDescending(record => record.ProbedAtUtc)
                .FirstOrDefault());
    }

    public async Task<OperationResult> QualifyAsync(
        string executableSha256,
        DateTimeOffset probedAtUtc,
        CancellationToken cancellationToken)
    {
        var read = await ReadStateAsync(cancellationToken).ConfigureAwait(false);
        if (!read.IsSuccess)
        {
            return OperationResult.Failure(read.Error!);
        }

        var record = read.Value!.Records.TryGetValue(executableSha256, out var existing)
            ? existing with
            {
                PersistenceQualified = true,
                ProbedAtUtc = existing.ProbedAtUtc == DateTimeOffset.MinValue
                    ? probedAtUtc
                    : existing.ProbedAtUtc,
            }
            : QualificationRecord.Legacy(executableSha256) with
            {
                ProbedAtUtc = probedAtUtc,
                PersistenceQualified = true,
            };
        return await QualifyAsync(record, cancellationToken).ConfigureAwait(false);
    }

    internal Task<OperationResult> CacheCapabilityAsync(
        QualificationRecord record,
        CancellationToken cancellationToken) =>
        QualifyAsync(record, cancellationToken);

    internal async Task<OperationResult> QualifyAsync(
        QualificationRecord record,
        CancellationToken cancellationToken)
    {
        if (!record.IsValidForWrite)
        {
            return OperationResult.Failure(
                OperationErrorCode.ValidationFailed,
                "Codex 兼容资格证据不完整，未写入缓存。",
                "compatibility.qualification.evidence_incomplete");
        }

        if (inMemory is not null)
        {
            inMemory[record.ExecutableSha256] = record;
            return OperationResult.Success();
        }

        var read = await ReadStateAsync(cancellationToken).ConfigureAwait(false);
        if (!read.IsSuccess)
        {
            return OperationResult.Failure(read.Error!);
        }

        var records = new Dictionary<string, QualificationRecord>(
            read.Value!.Records,
            StringComparer.OrdinalIgnoreCase);
        if ((records.TryGetValue(record.ExecutableSha256, out var existing) &&
             existing.PersistenceQualified) ||
            read.Value.LegacyQualifiedExecutableSha256.Contains(record.ExecutableSha256))
        {
            record = record with { PersistenceQualified = true };
        }

        records[record.ExecutableSha256] = record;
        var legacy = new HashSet<string>(
            read.Value.LegacyQualifiedExecutableSha256,
            StringComparer.OrdinalIgnoreCase);
        if (record.PersistenceQualified)
        {
            legacy.Add(record.ExecutableSha256);
        }

        return await WriteAsync(
            new QualificationStateV2(
                QualificationStateV2.CurrentSchemaVersion,
                records,
                legacy,
                record.ProbedAtUtc),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<OperationResult<QualificationStateV2>> ReadStateAsync(
        CancellationToken cancellationToken)
    {
        if (inMemory is not null)
        {
            return OperationResult<QualificationStateV2>.Success(
                new QualificationStateV2(
                    QualificationStateV2.CurrentSchemaVersion,
                    new Dictionary<string, QualificationRecord>(
                        inMemory,
                        StringComparer.OrdinalIgnoreCase),
                    new HashSet<string>(
                        inMemory.Values
                            .Where(record => record.PersistenceQualified)
                            .Select(record => record.ExecutableSha256),
                        StringComparer.OrdinalIgnoreCase),
                    inMemory.Values
                        .Select(record => record.ProbedAtUtc)
                        .DefaultIfEmpty(DateTimeOffset.MinValue)
                        .Max()));
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path))
            {
                return OperationResult<QualificationStateV2>.Success(
                    QualificationStateV2.Empty);
            }

            await using var stream = File.OpenRead(path);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!TryGetProperty(
                    document.RootElement,
                    "schemaVersion",
                    "SchemaVersion",
                    out var schema))
            {
                return Invalid("compatibility.qualification.invalid");
            }

            return schema.GetInt32() switch
            {
                1 => ReadV1(document.RootElement),
                QualificationStateV2.CurrentSchemaVersion => ReadV2(document.RootElement),
                _ => Invalid("compatibility.qualification.schema_unsupported"),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return Invalid("compatibility.qualification.read_failed");
        }
        finally
        {
            gate.Release();
        }
    }

    private static OperationResult<QualificationStateV2> ReadV1(JsonElement root)
    {
        var hashes = TryGetProperty(
                root,
                "qualifiedExecutableSha256",
                "QualifiedExecutableSha256",
                out var qualified)
            ? qualified.Deserialize<HashSet<string>>() ??
              new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            :
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return OperationResult<QualificationStateV2>.Success(
            new QualificationStateV2(
                QualificationStateV2.CurrentSchemaVersion,
                new Dictionary<string, QualificationRecord>(
                    StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(hashes, StringComparer.OrdinalIgnoreCase),
                TryGetProperty(root, "updatedAtUtc", "UpdatedAtUtc", out var updated)
                    ? updated.GetDateTimeOffset()
                    : DateTimeOffset.MinValue));
    }

    private static bool TryGetProperty(
        JsonElement root,
        string camelCaseName,
        string pascalCaseName,
        out JsonElement value) =>
        root.TryGetProperty(camelCaseName, out value) ||
        root.TryGetProperty(pascalCaseName, out value);

    private static OperationResult<QualificationStateV2> ReadV2(JsonElement root)
    {
        var state = root.Deserialize<QualificationStateV2>(JsonOptions);
        if (state is null ||
            state.SchemaVersion != QualificationStateV2.CurrentSchemaVersion ||
            state.Records is null ||
            state.LegacyQualifiedExecutableSha256 is null)
        {
            return Invalid("compatibility.qualification.invalid");
        }

        return OperationResult<QualificationStateV2>.Success(
            state with
            {
                Records = new Dictionary<string, QualificationRecord>(
                    state.Records,
                    StringComparer.OrdinalIgnoreCase),
                LegacyQualifiedExecutableSha256 = new HashSet<string>(
                    state.LegacyQualifiedExecutableSha256,
                    StringComparer.OrdinalIgnoreCase),
            });
    }

    private async Task<OperationResult> WriteAsync(
        QualificationStateV2 state,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await using (var stream = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(
                        stream,
                        state,
                        JsonOptions,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }

            return OperationResult.Success();
        }
        catch (IOException)
        {
            return OperationResult.Failure(
                OperationErrorCode.StorageUnavailable,
                "无法保存 Codex 兼容资格记录。",
                "compatibility.qualification.write_failed");
        }
        finally
        {
            gate.Release();
        }
    }

    private static OperationResult<QualificationStateV2> Invalid(string diagnosticCode) =>
        OperationResult<QualificationStateV2>.Failure(
            OperationErrorCode.StorageUnavailable,
            "无法读取 Codex 兼容资格记录。",
            diagnosticCode);

    internal sealed record QualificationRecord(
        string ExecutableSha256,
        string CodexVersion,
        string PackageFullName,
        string ExecutablePath,
        CodexInstallationSource InstallationSource,
        CodexIdentityAssessment IdentityAssessment,
        CodexCompatibilityLevel CompatibilityLevel,
        int ProbeContractVersion,
        int RequiredCapabilitiesVersion,
        DateTimeOffset ProbedAtUtc,
        bool CanaryApplied,
        bool CanaryCleaned,
        bool PersistenceQualified)
    {
        public bool HasReusableCapabilityEvidence =>
            ProbeContractVersion == CurrentProbeContractVersion &&
            RequiredCapabilitiesVersion == CurrentRequiredCapabilitiesVersion &&
            CompatibilityLevel != CodexCompatibilityLevel.Incompatible &&
            CanaryApplied &&
            CanaryCleaned;

        public bool IsValidForWrite =>
            !string.IsNullOrWhiteSpace(ExecutableSha256) &&
            ProbedAtUtc != DateTimeOffset.MinValue &&
            (HasReusableCapabilityEvidence ||
             string.IsNullOrEmpty(CodexVersion));

        public bool IsReusableFor(CodexInstallationInfo installation) =>
            HasReusableCapabilityEvidence &&
            installation.SourceAcknowledged &&
            string.Equals(
                ExecutableSha256,
                installation.ExecutableSha256,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                CodexVersion,
                installation.Version,
                StringComparison.Ordinal) &&
            string.Equals(
                PackageFullName,
                installation.PackageFullName,
                StringComparison.Ordinal) &&
            string.Equals(
                ExecutablePath,
                installation.ExecutablePath,
                StringComparison.OrdinalIgnoreCase) &&
            InstallationSource == installation.Source &&
            IdentityAssessment == installation.IdentityAssessment;

        public static QualificationRecord Legacy(string hash) =>
            new(
                hash,
                string.Empty,
                string.Empty,
                string.Empty,
                CodexInstallationSource.StoreAutomatic,
                CodexIdentityAssessment.TrustedStore,
                CodexCompatibilityLevel.CompatibleByProbe,
                0,
                0,
                DateTimeOffset.MinValue,
                false,
                false,
                true);
    }

    private sealed record QualificationStateV2(
        int SchemaVersion,
        Dictionary<string, QualificationRecord> Records,
        HashSet<string> LegacyQualifiedExecutableSha256,
        DateTimeOffset UpdatedAtUtc)
    {
        public const int CurrentSchemaVersion = 2;
        public static QualificationStateV2 Empty { get; } =
            new(
                CurrentSchemaVersion,
                new Dictionary<string, QualificationRecord>(
                    StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                DateTimeOffset.MinValue);
    }
}

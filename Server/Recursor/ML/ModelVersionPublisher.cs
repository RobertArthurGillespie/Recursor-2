using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.ML;

namespace NCATAIBlazorFrontendTest.Server.Recursor.ML;

/// <summary>
/// Shared immutable model-version publishing helper for the temporal trainers (Phase 10E
/// behavior-state, temporal-risk, temporal-elevated-risk). Enforces: existing versions are
/// never overwritten, artifacts are staged and load-verified before publish, publish is a
/// single atomic directory rename, and a version is never left partially activated if one
/// horizon fails after another already succeeded.
///
/// Layout: {contentRoot}/Recursor/TrainingModels/versions/{family}/{version}/h{n}.zip + manifest.json
///
/// This is independent of and never modifies the legacy flat model paths
/// (Recursor:Models:*ModelPath in appsettings.json) that the prediction services load at
/// startup — publishing a version here never changes what the running app has loaded.
/// Activating a newly published version requires an operator to point the configured model
/// path/version at the new version directory and restart/redeploy (see
/// docs/recursor-model-versioning.md).
/// </summary>
public static class ModelVersionPublisher
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    public static string GetVersionsRoot(string contentRoot, string family) =>
        Path.Combine(contentRoot, "Recursor", "TrainingModels", "versions", family);

    public static string GetVersionDirectory(string contentRoot, string family, string version) =>
        Path.Combine(GetVersionsRoot(contentRoot, family), version);

    /// <summary>A version "exists" once it has a completed manifest.json in its final directory.</summary>
    public static bool VersionExists(string contentRoot, string family, string version) =>
        File.Exists(Path.Combine(GetVersionDirectory(contentRoot, family, version), "manifest.json"));

    /// <summary>
    /// Attempts to begin a publish for (family, version). Returns null if another publish for
    /// the exact same (family, version) is already in progress in this process — callers should
    /// treat null as "another request is already publishing this version", not retry silently.
    /// The returned session MUST be disposed (via `using`); disposal without calling Complete()
    /// cleans up the staging directory and releases the lock.
    /// </summary>
    public static ModelVersionPublishSession? TryBeginPublish(string contentRoot, string family, string version)
    {
        var gate = Locks.GetOrAdd($"{family}::{version}", _ => new SemaphoreSlim(1, 1));
        return gate.Wait(0) ? new ModelVersionPublishSession(contentRoot, family, version, gate) : null;
    }
}

public sealed class ModelVersionPublishSession : IDisposable
{
    private readonly SemaphoreSlim _gate;
    private bool _completed;
    private bool _disposed;

    public string ContentRoot { get; }
    public string Family { get; }
    public string Version { get; }
    public string StagingDirectory { get; }
    public string FinalDirectory { get; }

    internal ModelVersionPublishSession(string contentRoot, string family, string version, SemaphoreSlim gate)
    {
        ContentRoot = contentRoot;
        Family = family;
        Version = version;
        _gate = gate;
        FinalDirectory = ModelVersionPublisher.GetVersionDirectory(contentRoot, family, version);
        StagingDirectory = FinalDirectory + $".staging-{Guid.NewGuid():N}";
        Directory.CreateDirectory(StagingDirectory);
    }

    public string GetHorizonStagingPath(int horizon) => Path.Combine(StagingDirectory, $"h{horizon}.zip");

    /// <summary>Verifies a staged model artifact can actually be loaded before it's eligible to publish.</summary>
    public static bool TryVerifyLoadable(MLContext mlContext, string path, out string? error)
    {
        try
        {
            mlContext.Model.Load(path, out _);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Writes manifest.json into the staging directory and atomically publishes by renaming the
    /// staging directory to its final version directory (Directory.Move is a single rename when
    /// source and destination share a volume, which staging-under-the-versions-root guarantees).
    /// Throws if the final directory was published concurrently between the caller's existence
    /// check and this call.
    /// </summary>
    public string Complete(object manifest)
    {
        if (_completed)
            throw new InvalidOperationException("This publish session has already been completed.");
        if (Directory.Exists(FinalDirectory))
            throw new InvalidOperationException(
                $"Model version '{Version}' for family '{Family}' was published by another request " +
                "while this training run was in progress.");

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(StagingDirectory, "manifest.json"), json);

        var parent = Path.GetDirectoryName(FinalDirectory);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);

        Directory.Move(StagingDirectory, FinalDirectory);
        _completed = true;
        return FinalDirectory;
    }

    /// <summary>True once Complete() has successfully published this session's artifacts.</summary>
    public bool IsCompleted => _completed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (!_completed)
        {
            try
            {
                if (Directory.Exists(StagingDirectory))
                    Directory.Delete(StagingDirectory, recursive: true);
            }
            catch
            {
                // Best-effort cleanup. A leftover .staging-* directory is inert: VersionExists()
                // only looks for manifest.json inside the *final* (non-staging) directory name.
            }
        }

        _gate.Release();
    }
}

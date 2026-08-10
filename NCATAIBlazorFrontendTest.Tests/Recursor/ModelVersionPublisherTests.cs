using Microsoft.ML;
using NCATAIBlazorFrontendTest.Server.Recursor.ML;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Stage 4 corrective-pass tests: the shared immutable model-version publisher used by all
/// three temporal trainers (Phase 10E behavior-state, temporal-risk, temporal-elevated-risk).
/// Each test uses its own throwaway content-root directory under the OS temp folder so nothing
/// here touches the real Server/Recursor/TrainingModels tree.
/// </summary>
public class ModelVersionPublisherTests : IDisposable
{
    private readonly string _contentRoot;

    public ModelVersionPublisherTests()
    {
        _contentRoot = Path.Combine(Path.GetTempPath(), "recursor-publisher-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_contentRoot);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_contentRoot)) Directory.Delete(_contentRoot, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private static string UniqueFamily() => "test-family-" + Guid.NewGuid().ToString("N")[..8];

    // ── VersionExists / existing-version rejection ──────────────────────────

    [Fact]
    public void VersionExists_ReturnsFalse_WhenNeverPublished()
    {
        var family = UniqueFamily();

        Assert.False(ModelVersionPublisher.VersionExists(_contentRoot, family, "v1"));
    }

    [Fact]
    public void VersionExists_ReturnsTrue_AfterSuccessfulComplete()
    {
        var family = UniqueFamily();
        using var session = ModelVersionPublisher.TryBeginPublish(_contentRoot, family, "v1");
        Assert.NotNull(session);

        File.WriteAllText(session!.GetHorizonStagingPath(1), "fake-model-bytes");
        session.Complete(new { note = "test manifest" });

        Assert.True(ModelVersionPublisher.VersionExists(_contentRoot, family, "v1"));
    }

    [Fact]
    public void VersionExists_ReturnsFalse_WhenSessionDisposedWithoutComplete()
    {
        var family = UniqueFamily();
        using (var session = ModelVersionPublisher.TryBeginPublish(_contentRoot, family, "v1"))
        {
            Assert.NotNull(session);
            File.WriteAllText(session!.GetHorizonStagingPath(1), "fake-model-bytes");
            // Simulates a partial training failure: never call Complete().
        }

        Assert.False(ModelVersionPublisher.VersionExists(_contentRoot, family, "v1"));
    }

    // ── Temporary-file / staging-directory cleanup ──────────────────────────

    [Fact]
    public void Dispose_WithoutComplete_DeletesStagingDirectory()
    {
        var family = UniqueFamily();
        string stagingDir;
        using (var session = ModelVersionPublisher.TryBeginPublish(_contentRoot, family, "v1"))
        {
            Assert.NotNull(session);
            stagingDir = session!.StagingDirectory;
            File.WriteAllText(session.GetHorizonStagingPath(1), "partial");
            Assert.True(Directory.Exists(stagingDir));
        }

        Assert.False(Directory.Exists(stagingDir));
    }

    [Fact]
    public void Complete_DoesNotLeaveStagingDirectoryBehind()
    {
        var family = UniqueFamily();
        string stagingDir;
        using (var session = ModelVersionPublisher.TryBeginPublish(_contentRoot, family, "v1"))
        {
            Assert.NotNull(session);
            stagingDir = session!.StagingDirectory;
            File.WriteAllText(session.GetHorizonStagingPath(1), "complete-me");
            session.Complete(new { });
        }

        Assert.False(Directory.Exists(stagingDir));
    }

    // ── Partial failure => nothing published ────────────────────────────────

    [Fact]
    public void PartialFailure_NoHorizonFilesAppearInFinalDirectory()
    {
        var family = UniqueFamily();
        string finalDir;
        using (var session = ModelVersionPublisher.TryBeginPublish(_contentRoot, family, "v1"))
        {
            Assert.NotNull(session);
            finalDir = session!.FinalDirectory;
            // Horizon 1 "succeeds" (file staged)...
            File.WriteAllText(session.GetHorizonStagingPath(1), "h1-ok");
            // ...horizon 2 "fails" — caller never stages it and never calls Complete().
        }

        Assert.False(Directory.Exists(finalDir));
        Assert.False(ModelVersionPublisher.VersionExists(_contentRoot, family, "v1"));
    }

    // ── Concurrency guard ────────────────────────────────────────────────────

    [Fact]
    public void TryBeginPublish_SecondConcurrentRequestForSameVersion_ReturnsNull()
    {
        var family = UniqueFamily();
        using var first = ModelVersionPublisher.TryBeginPublish(_contentRoot, family, "v1");
        Assert.NotNull(first);

        var second = ModelVersionPublisher.TryBeginPublish(_contentRoot, family, "v1");

        Assert.Null(second);
    }

    [Fact]
    public void TryBeginPublish_DifferentVersions_BothSucceedConcurrently()
    {
        var family = UniqueFamily();
        using var v1 = ModelVersionPublisher.TryBeginPublish(_contentRoot, family, "v1");
        using var v2 = ModelVersionPublisher.TryBeginPublish(_contentRoot, family, "v2");

        Assert.NotNull(v1);
        Assert.NotNull(v2);
    }

    [Fact]
    public void TryBeginPublish_AfterPriorSessionDisposed_LockIsReleasedForRetry()
    {
        var family = UniqueFamily();
        using (var first = ModelVersionPublisher.TryBeginPublish(_contentRoot, family, "v1"))
            Assert.NotNull(first);

        using var retry = ModelVersionPublisher.TryBeginPublish(_contentRoot, family, "v1");

        Assert.NotNull(retry);
    }

    // ── Existing-version rejection (the caller-facing contract trainers rely on) ───

    [Fact]
    public void AlreadyPublishedVersion_CallerMustCheckVersionExistsBeforeRetraining()
    {
        var family = UniqueFamily();
        using (var session = ModelVersionPublisher.TryBeginPublish(_contentRoot, family, "v1"))
        {
            File.WriteAllText(session!.GetHorizonStagingPath(1), "h1");
            session.Complete(new { });
        }

        Assert.True(ModelVersionPublisher.VersionExists(_contentRoot, family, "v1"));

        // A second publish attempt for the same version can still acquire the lock (the lock
        // alone does not enforce immutability) — trainers must check VersionExists() first,
        // exactly as TemporalBehaviorStateModelTrainingService/TemporalRiskModelTrainingService/
        // TemporalElevatedRiskModelTrainingService.TrainAsync all do before calling TryBeginPublish.
        using var secondAttempt = ModelVersionPublisher.TryBeginPublish(_contentRoot, family, "v1");
        Assert.NotNull(secondAttempt);

        // Completing it would throw, because the final directory already exists.
        File.WriteAllText(secondAttempt!.GetHorizonStagingPath(1), "h1-again");
        Assert.Throws<InvalidOperationException>(() => secondAttempt.Complete(new { }));
    }

    // ── No automatic activation ──────────────────────────────────────────────

    [Fact]
    public void Complete_NeverWritesOutsideTheVersionsDirectory()
    {
        var family = UniqueFamily();
        using var session = ModelVersionPublisher.TryBeginPublish(_contentRoot, family, "v1");
        File.WriteAllText(session!.GetHorizonStagingPath(1), "h1");
        var publishedDir = session.Complete(new { });

        // Publishing never touches anything outside versions/{family}/{version} — in
        // particular, it never writes to or near the legacy flat model paths that a running
        // TemporalXPredictionService has already loaded, so publishing can never activate
        // a newly trained version as a side effect.
        Assert.StartsWith(ModelVersionPublisher.GetVersionsRoot(_contentRoot, family), publishedDir);
        var entriesOutsideVersions = Directory.GetFileSystemEntries(_contentRoot)
            .Where(e => !string.Equals(Path.GetFileName(e), "Recursor", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(entriesOutsideVersions);
    }

    // ── Load verification ────────────────────────────────────────────────────

    [Fact]
    public void TryVerifyLoadable_ReturnsFalse_ForCorruptArtifact()
    {
        var badPath = Path.Combine(_contentRoot, "not-a-real-model.zip");
        File.WriteAllBytes(badPath, new byte[] { 1, 2, 3, 4, 5 });
        var mlContext = new MLContext(seed: 1);

        var ok = ModelVersionPublishSession.TryVerifyLoadable(mlContext, badPath, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }
}

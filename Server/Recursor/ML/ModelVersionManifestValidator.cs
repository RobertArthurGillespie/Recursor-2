using System.Text.Json;

namespace NCATAIBlazorFrontendTest.Server.Recursor.ML;

/// <summary>
/// Stage 3 (corrective pass): validates a resolved model version's manifest.json before the
/// prediction service loads it, so a corrupted or misconfigured published version fails loudly
/// at startup instead of silently loading partial/inconsistent artifacts. This is distinct from
/// the "no model trained yet" case, which remains a tolerated, non-error state everywhere else
/// in this architecture (see the prediction services' own shadow/no-op fallback) — a version
/// directory that does not exist at all is not validated here and never throws.
/// </summary>
public static class ModelVersionManifestValidator
{
    /// <summary>
    /// Validates the manifest for a canonically-resolved version directory
    /// (ModelVersionPublisher.GetVersionDirectory(contentRoot, family, version)). Returns
    /// (no-op) if the directory does not exist at all — nothing has been published yet for this
    /// family/version, which is normal. Throws InvalidOperationException if the directory exists
    /// but its manifest is missing, unparseable, or inconsistent with the requested
    /// family/version/horizons — including a horizon whose manifest FileName does not resolve
    /// inside this same version directory (i.e. it must never point at another version).
    /// </summary>
    public static void ValidateVersionDirectory(string contentRoot, string family, string version)
    {
        var versionDirectory = ModelVersionPublisher.GetVersionDirectory(contentRoot, family, version);
        if (!Directory.Exists(versionDirectory))
            return;

        var manifestPath = Path.Combine(versionDirectory, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException(
                $"Model version directory '{versionDirectory}' exists but has no manifest.json. A " +
                "version directory without a manifest is not a completed published version — it may " +
                "be a leftover partial/staging directory. Remove it or finish publishing before " +
                "configuring the app to use this version.");
        }

        var manifest = ParseManifest(manifestPath);

        if (!string.Equals(manifest.ModelFamily, family, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Model version manifest '{manifestPath}' has ModelFamily '{manifest.ModelFamily}', " +
                $"expected '{family}'.");
        }

        if (!string.Equals(manifest.ModelVersion, version, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Model version manifest '{manifestPath}' has ModelVersion '{manifest.ModelVersion}', " +
                $"expected '{version}'. A version directory must never contain a manifest for a " +
                "different version.");
        }

        if (!string.Equals(manifest.CompletionStatus, "Complete", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Model version manifest '{manifestPath}' has CompletionStatus " +
                $"'{manifest.CompletionStatus}', expected 'Complete'.");
        }

        for (int horizon = 1; horizon <= 3; horizon++)
        {
            var horizonManifest = manifest.Horizons.FirstOrDefault(h => h.Horizon == horizon);
            if (horizonManifest is null)
            {
                throw new InvalidOperationException(
                    $"Model version manifest '{manifestPath}' is missing horizon {horizon}.");
            }

            var expectedFileName = $"h{horizon}.zip";
            if (!string.Equals(horizonManifest.FileName, expectedFileName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Model version manifest '{manifestPath}' horizon {horizon} FileName is " +
                    $"'{horizonManifest.FileName}', expected '{expectedFileName}' — a horizon must " +
                    "never point outside its own version directory.");
            }

            var artifactPath = Path.Combine(versionDirectory, expectedFileName);
            if (!File.Exists(artifactPath))
            {
                throw new InvalidOperationException(
                    $"Model version manifest '{manifestPath}' references '{expectedFileName}' for " +
                    $"horizon {horizon}, but '{artifactPath}' does not exist.");
            }
        }
    }

    /// <summary>
    /// Cross-checks an explicit (legacy-mode) override path against a manifest.json alongside
    /// it, if one exists. A true legacy flat file with no manifest.json in its directory is
    /// tolerated silently (there is nothing to cross-check). If a manifest.json IS present next
    /// to the override, it must agree with the configured family/version — this catches an
    /// override that accidentally points into a different version's published directory. A
    /// resolved path that does not exist on disk yet is tolerated (model not deployed yet).
    /// </summary>
    public static void ValidateExplicitOverridePath(string resolvedPath, string family, string version, int horizon)
    {
        if (!File.Exists(resolvedPath))
            return;

        var directory = Path.GetDirectoryName(resolvedPath);
        if (string.IsNullOrEmpty(directory))
            return;

        var manifestPath = Path.Combine(directory, "manifest.json");
        if (!File.Exists(manifestPath))
            return;

        var manifest = ParseManifest(manifestPath);

        if (!string.Equals(manifest.ModelFamily, family, StringComparison.Ordinal) ||
            !string.Equals(manifest.ModelVersion, version, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Explicit model path '{resolvedPath}' for horizon {horizon} sits alongside a " +
                $"manifest.json for family '{manifest.ModelFamily}' version '{manifest.ModelVersion}', " +
                $"but the app is configured for family '{family}' version '{version}'. This override " +
                "appears to point into a different version's published directory — fix the configured " +
                "path or version.");
        }
    }

    private static ModelVersionManifest ParseManifest(string manifestPath)
    {
        ModelVersionManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ModelVersionManifest>(File.ReadAllText(manifestPath));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Model version manifest '{manifestPath}' could not be parsed: {ex.Message}", ex);
        }

        if (manifest is null)
        {
            throw new InvalidOperationException($"Model version manifest '{manifestPath}' parsed to null.");
        }

        return manifest;
    }
}

using NCATAIBlazorFrontendTest.Server.Recursor.Models;
using NCATAIBlazorFrontendTest.Server.Recursor.Repositories;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Seeding;

// Phase 8D — test user threshold seeding.
//
// Seeds two contrasting users into IUserThresholdRepository at startup so that
// manual validation runs can exercise the per-user threshold layer without needing
// a real user record in ADX.
//
// User A ("recursor-test-sensitive"):  conservative — confusion block fires early,
//   hint fade requires strong below-baseline evidence.
// User B ("recursor-test-tolerant"):   permissive  — confusion block fires only at
//   large spikes, hint fade triggers with light evidence.
//
// Global defaults (RecursorPoliciesOptions) sit between the two profiles intentionally,
// so you can compare all three behaviours side-by-side in a single run.
//
// Remove or replace with an admin endpoint once real threshold management is in place.
public static class RecursorTestThresholdSeeder
{
    // ── User IDs ──────────────────────────────────────────────────────────────
    // Use these exactly when submitting sessions you want to validate.
    public const string SensitiveUserId = "recursor-test-sensitive";
    public const string TolerantUserId  = "recursor-test-tolerant";

    // ── Global defaults for reference (matches RecursorPoliciesOptions defaults) ─
    //   ConfusionBlockDifficultyIncreaseThreshold  = 0.05
    //   HintFadeConfusionDeltaThreshold            = -0.05
    //   HintFadeHintDependenceDeltaThreshold       = -0.05
    //   HintFadeMaxCurrentConfusionScore           = 0.30
    //   HintFadeMinCurrentGoalUnderstanding        = 0.60

    public static void Seed(IUserThresholdRepository repo)
    {
        // ── User A: sensitive / conservative ─────────────────────────────────
        // Confusion block: fires when confusion is ≥ 2% above baseline (global: 5%).
        //   Any meaningful confusion spike stops a difficulty increase.
        // Hint fade: requires confusion AND hint-dependence each 12% below baseline
        //   (global: 5%), and stricter absolute gates.
        //   Only fires for users performing strongly both in relative and absolute terms.
        repo.Upsert(new UserPolicyThresholds
        {
            UserId = SensitiveUserId,

            ConfusionBlockDifficultyIncreaseThreshold = 0.02,   // ← tighter than global 0.05

            HintFadeConfusionDeltaThreshold        = -0.12,     // ← stricter than global -0.05
            HintFadeHintDependenceDeltaThreshold   = -0.12,     // ← stricter than global -0.05
            HintFadeMaxCurrentConfusionScore       =  0.22,     // ← tighter than global 0.30
            HintFadeMinCurrentGoalUnderstanding    =  0.70,     // ← higher floor than global 0.60
        });

        // ── User B: tolerant / aggressive ────────────────────────────────────
        // Confusion block: fires only when confusion is ≥ 15% above baseline (global: 5%).
        //   Moderate confusion spikes do not interrupt difficulty progression.
        // Hint fade: fires when confusion AND hint-dependence are only 2% below baseline
        //   (global: 5%), and relaxed absolute gates.
        //   Enables earlier hint removal for users who absorb challenge well.
        repo.Upsert(new UserPolicyThresholds
        {
            UserId = TolerantUserId,

            ConfusionBlockDifficultyIncreaseThreshold = 0.15,   // ← looser than global 0.05

            HintFadeConfusionDeltaThreshold        = -0.02,     // ← looser than global -0.05
            HintFadeHintDependenceDeltaThreshold   = -0.02,     // ← looser than global -0.05
            HintFadeMaxCurrentConfusionScore       =  0.40,     // ← looser than global 0.30
            HintFadeMinCurrentGoalUnderstanding    =  0.50,     // ← lower floor than global 0.60
        });
    }
}

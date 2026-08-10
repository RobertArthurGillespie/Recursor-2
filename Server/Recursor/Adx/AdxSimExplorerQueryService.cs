using Kusto.Data.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NCATAIBlazorFrontendTest.Shared;

namespace NCATAIBlazorFrontendTest.Server.Recursor.Adx;

// ── Phase 16A: Sim Explorer — read-only ADX query service ─────────────────────

public interface IAdxSimExplorerQueryService
{
    Task<List<SimExplorerSimSummaryDto>> GetSimsAsync();
    Task<List<SimExplorerUserSummaryDto>> GetUsersForSimAsync(string simId);
    Task<List<SimExplorerUserSummaryDto>> GetAllUsersAsync();
    Task<List<SimExplorerSessionSummaryDto>> GetSessionsForSimUserAsync(string simId, string userId);
    // Stage 10: optional model-version overrides forwarded to IAdxDashboardQueryService.GetSessionTimelineAsync.
    Task<SimExplorerSessionDetailDto?> GetSessionDetailAsync(
        string simId, string userId, string sessionId,
        string? riskModelVersion = null, string? behaviorStateModelVersion = null);
    Task<List<string>> GetSimIdsForUserAsync(string userId);
}

public class AdxSimExplorerQueryService : IAdxSimExplorerQueryService
{
    private readonly ICslQueryProvider? _queryProvider;
    private readonly string _database;
    private readonly ILogger<AdxSimExplorerQueryService> _logger;
    private readonly IAdxDashboardQueryService _dashboardQuery;

    public AdxSimExplorerQueryService(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger<AdxSimExplorerQueryService> logger,
        IAdxDashboardQueryService dashboardQuery)
    {
        _queryProvider  = services.GetService<ICslQueryProvider>();
        _database       = configuration["Adx:Database"] ?? "RecursorDbMain";
        _logger         = logger;
        _dashboardQuery = dashboardQuery;
    }

    // ── Public KQL builders — exposed for unit tests ──────────────────────────

    public static string BuildSimsKql() => @"let windows = BehaviorStateTrainingRows
| summarize
    ScenarioCount = dcount(ScenarioId),
    UserCount     = dcount(UserId),
    SessionCount  = dcount(SessionId),
    WindowCount   = count(),
    FirstSeenUtc  = min(CreatedAtUtc),
    LastSeenUtc   = max(CreatedAtUtc)
  by SimId;
let raw = RawEvents
| summarize RawEventCount = count() by SimId;
let sessionSim = BehaviorStateTrainingRows
| summarize SimId = take_any(SimId) by SessionId;
let adapt = AdaptationDecisions_v2
| join kind=inner sessionSim on SessionId
| summarize AdaptationCount = count() by SimId;
windows
| join kind=leftouter raw on SimId
| join kind=leftouter adapt on SimId
| project SimId, ScenarioCount, UserCount, SessionCount, WindowCount,
          RawEventCount   = coalesce(RawEventCount, tolong(0)),
          AdaptationCount = coalesce(AdaptationCount, tolong(0)),
          FirstSeenUtc, LastSeenUtc
| order by LastSeenUtc desc";

    public static string BuildUsersForSimKql(string sanitizedSimId) => $@"let windows = BehaviorStateTrainingRows
| where SimId == '{sanitizedSimId}'
| summarize
    ScenarioCount          = dcount(ScenarioId),
    SessionCount           = dcount(SessionId),
    WindowCount            = count(),
    AvgConfusionScore      = avg(ConfusionScore),
    AvgGoalUnderstanding   = avg(GoalUnderstanding),
    AvgHintDependenceScore = avg(HintDependenceScore),
    FirstSeenUtc           = min(CreatedAtUtc),
    LastSeenUtc            = max(CreatedAtUtc)
  by UserId;
let raw = RawEvents
| where SimId == '{sanitizedSimId}'
| summarize RawEventCount = count() by UserId;
let sessionSim = BehaviorStateTrainingRows
| where SimId == '{sanitizedSimId}'
| summarize by SessionId, UserId;
let adapt = AdaptationDecisions_v2
| join kind=inner sessionSim on SessionId
| summarize AdaptationCount = count() by UserId;
windows
| join kind=leftouter raw on UserId
| join kind=leftouter adapt on UserId
| project UserId, ScenarioCount, SessionCount, WindowCount,
          RawEventCount          = coalesce(RawEventCount, tolong(0)),
          AdaptationCount        = coalesce(AdaptationCount, tolong(0)),
          AvgConfusionScore, AvgGoalUnderstanding, AvgHintDependenceScore,
          FirstSeenUtc, LastSeenUtc
| order by LastSeenUtc desc";

    public static string BuildAllUsersKql() => @"let windows = BehaviorStateTrainingRows
| summarize
    ScenarioCount          = dcount(ScenarioId),
    SessionCount           = dcount(SessionId),
    WindowCount            = count(),
    AvgConfusionScore      = avg(ConfusionScore),
    AvgGoalUnderstanding   = avg(GoalUnderstanding),
    AvgHintDependenceScore = avg(HintDependenceScore),
    FirstSeenUtc           = min(CreatedAtUtc),
    LastSeenUtc            = max(CreatedAtUtc)
  by UserId;
let raw = RawEvents
| summarize RawEventCount = count() by UserId;
let sessionSim = BehaviorStateTrainingRows
| summarize by SessionId, UserId;
let adapt = AdaptationDecisions_v2
| join kind=inner sessionSim on SessionId
| summarize AdaptationCount = count() by UserId;
windows
| join kind=leftouter raw on UserId
| join kind=leftouter adapt on UserId
| project UserId, ScenarioCount, SessionCount, WindowCount,
          RawEventCount          = coalesce(RawEventCount, tolong(0)),
          AdaptationCount        = coalesce(AdaptationCount, tolong(0)),
          AvgConfusionScore, AvgGoalUnderstanding, AvgHintDependenceScore,
          FirstSeenUtc, LastSeenUtc
| order by LastSeenUtc desc";

    public static string BuildSessionsForSimUserKql(string sanitizedSimId, string sanitizedUserId) => $@"let windows = BehaviorStateTrainingRows
| where SimId == '{sanitizedSimId}' and UserId == '{sanitizedUserId}'
| summarize
    ScenarioId             = take_any(ScenarioId),
    SimId                  = take_any(SimId),
    UserId                 = take_any(UserId),
    WindowCount            = count(),
    AvgConfusionScore      = avg(ConfusionScore),
    AvgGoalUnderstanding   = avg(GoalUnderstanding),
    AvgHintDependenceScore = avg(HintDependenceScore),
    FirstSeenUtc           = min(CreatedAtUtc),
    LastSeenUtc            = max(CreatedAtUtc)
  by SessionId;
let raw = RawEvents
| where SimId == '{sanitizedSimId}' and UserId == '{sanitizedUserId}'
| summarize
    RawEventCount    = count(),
    SafetyErrorCount = countif(Category == ""safety_error""),
    ErrorCount       = countif(Category == ""error""),
    SuccessCount     = countif(Category == ""success"")
  by SessionId;
let adapt = AdaptationDecisions_v2
| join kind=inner (
    BehaviorStateTrainingRows
    | where SimId == '{sanitizedSimId}' and UserId == '{sanitizedUserId}'
    | summarize by SessionId
  ) on SessionId
| summarize AdaptationCount = count() by SessionId;
windows
| join kind=leftouter raw on SessionId
| join kind=leftouter adapt on SessionId
| project SessionId, ScenarioId, SimId, UserId, WindowCount,
          RawEventCount    = coalesce(RawEventCount, tolong(0)),
          AdaptationCount  = coalesce(AdaptationCount, tolong(0)),
          AvgConfusionScore, AvgGoalUnderstanding, AvgHintDependenceScore,
          SafetyErrorCount = coalesce(SafetyErrorCount, tolong(0)),
          ErrorCount       = coalesce(ErrorCount, tolong(0)),
          SuccessCount     = coalesce(SuccessCount, tolong(0)),
          FirstSeenUtc, LastSeenUtc
| order by LastSeenUtc desc";

    public static string BuildSimIdsForUserKql(string sanitizedUserId) => $@"BehaviorStateTrainingRows
| where UserId == '{sanitizedUserId}'
| summarize LastSeenUtc = max(CreatedAtUtc) by SimId
| order by LastSeenUtc desc
| project SimId";

    public static string BuildRawEventsKql(string sanitizedSessionId) => $@"RawEvents
| where SessionId == '{sanitizedSessionId}'
| order by SequenceNumber asc, TimestampUtc asc
| take 2000
| project EventId, SequenceNumber, TimestampUtc, EventType, Category, Actor, Target, Metrics, Context, Payload";

    // ── Interface implementations ─────────────────────────────────────────────

    public async Task<List<SimExplorerSimSummaryDto>> GetSimsAsync()
    {
        if (_queryProvider is null)
        {
            _logger.LogWarning("ADX not configured — sim-explorer sims returning empty.");
            return [];
        }

        var kql = BuildSimsKql();
        try
        {
            using var reader = await _queryProvider.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());
            var results = new List<SimExplorerSimSummaryDto>();
            while (reader.Read())
            {
                results.Add(new SimExplorerSimSummaryDto
                {
                    SimId           = reader.IsDBNull(0) ? "" : reader.GetString(0),
                    ScenarioCount   = (int)reader.GetInt64(1),
                    UserCount       = (int)reader.GetInt64(2),
                    SessionCount    = (int)reader.GetInt64(3),
                    WindowCount     = (int)reader.GetInt64(4),
                    RawEventCount   = reader.GetInt64(5),
                    AdaptationCount = (int)reader.GetInt64(6),
                    FirstSeenUtc    = reader.GetDateTime(7),
                    LastSeenUtc     = reader.GetDateTime(8),
                });
            }
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sim explorer sims query failed.");
            return [];
        }
    }

    public async Task<List<SimExplorerUserSummaryDto>> GetUsersForSimAsync(string simId)
    {
        if (_queryProvider is null)
        {
            _logger.LogWarning("ADX not configured — sim-explorer users returning empty.");
            return [];
        }

        var sid = Sanitize(simId);
        var kql = BuildUsersForSimKql(sid);
        try
        {
            using var reader = await _queryProvider.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());
            var results = new List<SimExplorerUserSummaryDto>();
            while (reader.Read())
            {
                results.Add(new SimExplorerUserSummaryDto
                {
                    SimId                  = simId,
                    UserId                 = reader.IsDBNull(0) ? "" : reader.GetString(0),
                    ScenarioCount          = (int)reader.GetInt64(1),
                    SessionCount           = (int)reader.GetInt64(2),
                    WindowCount            = (int)reader.GetInt64(3),
                    RawEventCount          = reader.GetInt64(4),
                    AdaptationCount        = (int)reader.GetInt64(5),
                    AvgConfusionScore      = reader.IsDBNull(6)  ? null : reader.GetDouble(6),
                    AvgGoalUnderstanding   = reader.IsDBNull(7)  ? null : reader.GetDouble(7),
                    AvgHintDependenceScore = reader.IsDBNull(8)  ? null : reader.GetDouble(8),
                    FirstSeenUtc           = reader.GetDateTime(9),
                    LastSeenUtc            = reader.GetDateTime(10),
                });
            }
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sim explorer users query failed for sim '{SimId}'.", sid);
            return [];
        }
    }

    public async Task<List<SimExplorerUserSummaryDto>> GetAllUsersAsync()
    {
        if (_queryProvider is null)
        {
            _logger.LogWarning("ADX not configured — sim-explorer all-users returning empty.");
            return [];
        }

        var kql = BuildAllUsersKql();
        try
        {
            using var reader = await _queryProvider.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());
            var results = new List<SimExplorerUserSummaryDto>();
            while (reader.Read())
            {
                results.Add(new SimExplorerUserSummaryDto
                {
                    UserId                 = reader.IsDBNull(0) ? "" : reader.GetString(0),
                    ScenarioCount          = (int)reader.GetInt64(1),
                    SessionCount           = (int)reader.GetInt64(2),
                    WindowCount            = (int)reader.GetInt64(3),
                    RawEventCount          = reader.GetInt64(4),
                    AdaptationCount        = (int)reader.GetInt64(5),
                    AvgConfusionScore      = reader.IsDBNull(6)  ? null : reader.GetDouble(6),
                    AvgGoalUnderstanding   = reader.IsDBNull(7)  ? null : reader.GetDouble(7),
                    AvgHintDependenceScore = reader.IsDBNull(8)  ? null : reader.GetDouble(8),
                    FirstSeenUtc           = reader.GetDateTime(9),
                    LastSeenUtc            = reader.GetDateTime(10),
                });
            }
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sim explorer all-users query failed.");
            return [];
        }
    }

    public async Task<List<SimExplorerSessionSummaryDto>> GetSessionsForSimUserAsync(string simId, string userId)
    {
        if (_queryProvider is null)
        {
            _logger.LogWarning("ADX not configured — sim-explorer sessions returning empty.");
            return [];
        }

        var sid = Sanitize(simId);
        var uid = Sanitize(userId);
        var kql = BuildSessionsForSimUserKql(sid, uid);
        try
        {
            using var reader = await _queryProvider.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());
            var results = new List<SimExplorerSessionSummaryDto>();
            while (reader.Read())
            {
                results.Add(new SimExplorerSessionSummaryDto
                {
                    SessionId              = reader.IsDBNull(0)  ? "" : reader.GetString(0),
                    ScenarioId             = reader.IsDBNull(1)  ? "" : reader.GetString(1),
                    SimId                  = reader.IsDBNull(2)  ? simId : reader.GetString(2),
                    UserId                 = reader.IsDBNull(3)  ? userId : reader.GetString(3),
                    WindowCount            = (int)reader.GetInt64(4),
                    RawEventCount          = reader.GetInt64(5),
                    AdaptationCount        = (int)reader.GetInt64(6),
                    AvgConfusionScore      = reader.IsDBNull(7)  ? null : reader.GetDouble(7),
                    AvgGoalUnderstanding   = reader.IsDBNull(8)  ? null : reader.GetDouble(8),
                    AvgHintDependenceScore = reader.IsDBNull(9)  ? null : reader.GetDouble(9),
                    SafetyErrorCount       = reader.GetInt64(10),
                    ErrorCount             = reader.GetInt64(11),
                    SuccessCount           = reader.GetInt64(12),
                    FirstSeenUtc           = reader.GetDateTime(13),
                    LastSeenUtc            = reader.GetDateTime(14),
                });
            }
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sim explorer sessions query failed for sim '{SimId}' user '{UserId}'.", sid, uid);
            return [];
        }
    }

    public async Task<List<string>> GetSimIdsForUserAsync(string userId)
    {
        if (_queryProvider is null)
        {
            _logger.LogWarning("ADX not configured — sim-explorer sim-ids-for-user returning empty.");
            return [];
        }

        var uid = Sanitize(userId);
        var kql = BuildSimIdsForUserKql(uid);
        try
        {
            using var reader = await _queryProvider.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());
            var results = new List<string>();
            while (reader.Read())
            {
                if (!reader.IsDBNull(0))
                    results.Add(reader.GetString(0));
            }
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sim explorer sim-ids-for-user query failed for user '{UserId}'.", uid);
            return [];
        }
    }

    public async Task<SimExplorerSessionDetailDto?> GetSessionDetailAsync(
        string simId, string userId, string sessionId,
        string? riskModelVersion = null, string? behaviorStateModelVersion = null)
    {
        if (_queryProvider is null)
        {
            _logger.LogWarning("ADX not configured — sim-explorer session detail returning null.");
            return null;
        }

        var sid     = Sanitize(simId);
        var uid     = Sanitize(userId);
        var sessId  = Sanitize(sessionId);

        // Verify the session exists in the given sim/user context and get metadata.
        var meta = await QuerySessionMetaAsync(sid, uid, sessId);
        if (meta is null) return null;

        // Delegate timeline assembly to the existing dashboard service. Stage 7: the dashboard
        // service now returns a typed Status alongside the value; sim-explorer's own session
        // lookup (QuerySessionMetaAsync above) is the authoritative existence check for this
        // page, so only the timeline Value itself is threaded through here — a null Value
        // (session-not-found or the underlying query failing) still degrades to a null timeline
        // exactly as before, matching this method's existing behavior for its own callers.
        var timeline = (await _dashboardQuery.GetSessionTimelineAsync(sessionId, riskModelVersion, behaviorStateModelVersion)).Value;

        var rawEvents     = await QueryRawEventsAsync(sessId);
        var countByCategory  = await QueryEventCountsAsync(sessId, "Category");
        var countByEventType = await QueryEventCountsAsync(sessId, "EventType");

        // 2001st row would indicate truncation; we only take 2000.
        bool truncated = rawEvents.Count == 2000;

        return new SimExplorerSessionDetailDto
        {
            SessionId               = sessionId,
            SimId                   = meta.SimId,
            UserId                  = meta.UserId,
            ScenarioId              = meta.ScenarioId,
            Timeline                = timeline,
            RawEvents               = rawEvents,
            RawEventsTruncated      = truncated,
            EventCountsByCategory   = countByCategory,
            EventCountsByEventType  = countByEventType,
        };
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private record SessionMeta(string SimId, string UserId, string ScenarioId);

    private async Task<SessionMeta?> QuerySessionMetaAsync(
        string sanitizedSimId, string sanitizedUserId, string sanitizedSessionId)
    {
        var kql = $@"BehaviorStateTrainingRows
| where SimId == '{sanitizedSimId}' and UserId == '{sanitizedUserId}' and SessionId == '{sanitizedSessionId}'
| take 1
| project SimId, UserId, ScenarioId";

        try
        {
            using var reader = await _queryProvider!.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());
            if (!reader.Read()) return null;
            return new SessionMeta(
                SimId:      reader.IsDBNull(0) ? "" : reader.GetString(0),
                UserId:     reader.IsDBNull(1) ? "" : reader.GetString(1),
                ScenarioId: reader.IsDBNull(2) ? "" : reader.GetString(2));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sim explorer session meta query failed for session '{SessionId}'.", sanitizedSessionId);
            return null;
        }
    }

    private async Task<List<SimExplorerRawEventDto>> QueryRawEventsAsync(string sanitizedSessionId)
    {
        var kql = BuildRawEventsKql(sanitizedSessionId);
        try
        {
            using var reader = await _queryProvider!.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());
            var results = new List<SimExplorerRawEventDto>();
            while (reader.Read())
            {
                results.Add(new SimExplorerRawEventDto
                {
                    EventId         = reader.IsDBNull(0) ? "" : reader.GetString(0),
                    SequenceNumber  = reader.GetInt64(1),
                    TimestampUtc    = reader.GetDateTime(2),
                    EventType       = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Category        = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    Actor           = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    Target          = reader.IsDBNull(6) ? "" : reader.GetString(6),
                    MetricsJson     = ReadDynamicAsString(reader, 7),
                    ContextJson     = ReadDynamicAsString(reader, 8),
                    PayloadJson     = ReadDynamicAsString(reader, 9),
                });
            }
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sim explorer raw events query failed for session '{SessionId}'.", sanitizedSessionId);
            return [];
        }
    }

    private async Task<List<SimExplorerEventCountDto>> QueryEventCountsAsync(
        string sanitizedSessionId, string groupByColumn)
    {
        var kql = $@"RawEvents
| where SessionId == '{sanitizedSessionId}'
| summarize Count = count() by {groupByColumn}
| order by Count desc";

        try
        {
            using var reader = await _queryProvider!.ExecuteQueryAsync(_database, kql, new ClientRequestProperties());
            var results = new List<SimExplorerEventCountDto>();
            while (reader.Read())
            {
                results.Add(new SimExplorerEventCountDto
                {
                    Label = reader.IsDBNull(0) ? "" : reader.GetString(0),
                    Count = reader.GetInt64(1),
                });
            }
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sim explorer event counts by '{Column}' failed for session '{SessionId}'.",
                groupByColumn, sanitizedSessionId);
            return [];
        }
    }

    private string ReadDynamicAsString(System.Data.IDataReader reader, int index)
    {
        try
        {
            if (reader.IsDBNull(index)) return "null";
            var raw = reader.GetValue(index);
            return raw switch
            {
                string s => s,
                null     => "null",
                _        => raw.ToString() ?? "null"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sim explorer: failed to read dynamic column at index {Index}.", index);
            return "null";
        }
    }

    private static string Sanitize(string value) => value.Replace("'", "");
}

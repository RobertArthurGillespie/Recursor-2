namespace NCATAIBlazorFrontendTest.Server.Configuration;

/// <summary>
/// Typed options for the "Adx" section in appsettings.json.
/// Bound via builder.Services.Configure&lt;AdxOptions&gt;(config.GetSection("Adx")).
/// </summary>
public class AdxOptions
{
    /// <summary>ADX query cluster URI. Example: https://mycluster.eastus.kusto.windows.net</summary>
    public string ClusterUri { get; set; } = "";

    /// <summary>ADX ingest cluster URI. Example: https://ingest-mycluster.eastus.kusto.windows.net</summary>
    public string IngestUri { get; set; } = "";

    /// <summary>ADX database name.</summary>
    public string Database { get; set; } = "RecursorDb";

    /// <summary>Azure tenant ID. Required for ServicePrincipal and UserPrompt auth.</summary>
    public string TenantId { get; set; } = "";

    /// <summary>Service principal application (client) ID. Required for ServicePrincipal auth only.</summary>
    public string ClientId { get; set; } = "";

    /// <summary>Service principal client secret. Required for ServicePrincipal auth only.</summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>
    /// Kusto authentication mode.
    /// <list type="bullet">
    ///   <item><term>UserPrompt</term><description>Interactive browser login. Default for local development.</description></item>
    ///   <item><term>ManagedIdentity</term><description>System-assigned managed identity. Use in production Azure hosting.</description></item>
    ///   <item><term>ServicePrincipal</term><description>Client ID + secret. Use in CI/CD pipelines or when MSI is unavailable.</description></item>
    /// </list>
    /// </summary>
    public string AuthMode { get; set; } = "UserPrompt";
}

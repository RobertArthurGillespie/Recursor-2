namespace NCATAIBlazorFrontendTest.Server.Recursor.Models;

public class SimCatalogDocument
{
    public string Id { get; set; } = "";
    public string DocumentType { get; set; } = "SimCatalog";
    public string SimId { get; set; } = "";
    public List<string> SupportedDimensions { get; set; } = new();
    public List<string> EventTypes { get; set; } = new();
    public List<AdaptiveParameterDefinition> AdaptiveParameters { get; set; } = new();
}

public class AdaptiveParameterDefinition
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public double? Min { get; set; }
    public double? Max { get; set; }
    public List<string>? AllowedValues { get; set; }
}

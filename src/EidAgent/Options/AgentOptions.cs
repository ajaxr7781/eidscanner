namespace EidAgent.Options;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    public int Port { get; set; } = 9443;

    public string SharedSecret { get; set; } = string.Empty;

    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
}

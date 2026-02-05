namespace EidAgent.Options;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    public int Port { get; set; } = 9443;

    public string SharedSecret { get; set; } = string.Empty;

    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();

    public bool IcaProcessMode { get; set; } = true;

    public string IcaConfigPath { get; set; } = "config_ap";

    public string IcaPreferredReaderName { get; set; } = string.Empty;
<<<<<<< codex/create-.net-8-emirates-id-agent-service-cyqy04

    public bool ValidateSdkResponseIntegrity { get; set; } = false;
=======
>>>>>>> main
}

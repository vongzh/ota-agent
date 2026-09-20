namespace StayOta.Agent.Abstractions.Options;

/// <summary>
/// Fail-fast checks for demo vs non-demo hosting configuration.
/// </summary>
public static class HostingGuards
{
    public static void Validate(HostingOptions hosting)
    {
        if (hosting.ResetDatabaseOnStartup && !hosting.DemoEnabled)
            throw new InvalidOperationException("Hosting:ResetDatabaseOnStartup requires DemoEnabled=true");

        if (!hosting.DemoEnabled && string.IsNullOrWhiteSpace(hosting.ApiKey))
            throw new InvalidOperationException(
                "Hosting:ApiKey is required when DemoEnabled=false (set Hosting__ApiKey or Hosting:ApiKey)");
    }
}

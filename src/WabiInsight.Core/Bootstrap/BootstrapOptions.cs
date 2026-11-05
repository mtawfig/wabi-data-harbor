namespace WabiInsight.Core.Bootstrap;

/// <summary>
/// Pre-migration deployment configuration for the first Platform Administrator (specification §7.1).
/// Supplied through environment variables, user secrets or a secret store; never committed outside
/// the Development sample file.
/// </summary>
public sealed class BootstrapOptions
{
    public const string SectionName = "Bootstrap";

    public bool Enabled { get; set; }

    public string AdministratorEmail { get; set; } = "support@wabitex.com";

    public string AdministratorFullName { get; set; } = "Platform Administrator";

    public string? AdministratorPassword { get; set; }

    public string? InitializationSecret { get; set; }
}

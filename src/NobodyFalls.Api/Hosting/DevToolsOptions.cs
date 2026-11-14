namespace NobodyFalls.Api.Hosting;

/// <summary>
/// Development tools: simulated clock, fake players, bots, dev login. Refused in the
/// Production environment whatever the configuration says.
/// </summary>
internal sealed class DevToolsOptions
{
    public bool Enabled { get; set; }

    public static DevToolsOptions From(IConfiguration configuration, IHostEnvironment environment)
    {
        var enabled = configuration.GetValue<bool>("DevTools:Enabled");
        if (enabled && environment.IsProduction())
        {
            throw new InvalidOperationException("DevTools cannot be enabled in Production.");
        }

        return new DevToolsOptions { Enabled = enabled };
    }
}

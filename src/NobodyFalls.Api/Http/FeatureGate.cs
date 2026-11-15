using NobodyFalls.Application.Config;
using NobodyFalls.Domain.Config;

namespace NobodyFalls.Api.Http;

/// <summary>A feature switched off (ADR 0022) is hidden by the client and refused here.</summary>
internal static class FeatureGate
{
    public static RouteHandlerBuilder RequiresFeature(this RouteHandlerBuilder builder, FeatureFlag flag) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var flags = context.HttpContext.RequestServices.GetRequiredService<IFeatureFlags>();
            return await flags.IsEnabledAsync(flag, context.HttpContext.RequestAborted)
                ? await next(context)
                : Problems.From(FeatureErrors.Disabled);
        });

    /// <summary>The flags the game needs to show or hide what they switch.</summary>
    public static async Task<Dictionary<string, bool>> ForClientAsync(IFeatureFlags flags, CancellationToken cancellationToken)
    {
        var states = new Dictionary<string, bool>();
        foreach (var flag in FeatureFlags.All)
        {
            states[flag.Key] = await flags.IsEnabledAsync(flag, cancellationToken);
        }

        return states;
    }
}

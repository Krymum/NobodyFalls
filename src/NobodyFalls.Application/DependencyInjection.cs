using Microsoft.Extensions.DependencyInjection;
using NobodyFalls.Application.Hands;
using NobodyFalls.Application.Night;
using NobodyFalls.Application.Players;

namespace NobodyFalls.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<NightCycle>();
        services.AddScoped<GiveHand>();
        services.AddScoped<ComeBack>();
        return services;
    }
}

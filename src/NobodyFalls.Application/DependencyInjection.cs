using Microsoft.Extensions.DependencyInjection;
using NobodyFalls.Application.Accounts;
using NobodyFalls.Application.Dev;
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
        services.AddScoped<RegisterPlayer>();
        services.AddScoped<UpdateSettings>();
        services.AddScoped<ChangeAppearance>();
        services.AddScoped<SimulateBots>();
        return services;
    }
}

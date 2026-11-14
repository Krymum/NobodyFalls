using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NobodyFalls.Application.Accounts;
using NobodyFalls.Application.Dev;
using NobodyFalls.Application.Feedback;
using NobodyFalls.Application.Friends;
using NobodyFalls.Application.Hands;
using NobodyFalls.Application.Night;
using NobodyFalls.Application.Players;
using NobodyFalls.Application.Push;
using NobodyFalls.Application.Realtime;
using NobodyFalls.Application.Staff;

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
        services.AddScoped<FriendActions>();
        services.AddScoped<DispatchNotifications>();
        services.AddScoped<AdminAuthentication>();
        services.AddScoped<FeedbackActions>();
        services.TryAddSingleton<IRealtimePublisher, NullRealtimePublisher>();
        return services;
    }
}

using System.Globalization;
using System.Resources;
using NobodyFalls.Application.Push;

namespace NobodyFalls.Infrastructure.Push;

/// <summary>
/// Notification texts from <c>Resources/Notifications.resx</c> (French, the neutral
/// language) and <c>Notifications.en.resx</c>. One name, or "first name and N others".
/// </summary>
internal sealed class ResxNotificationTexts : INotificationTexts
{
    private static readonly ResourceManager Resources =
        new("NobodyFalls.Infrastructure.Push.Resources.Notifications", typeof(ResxNotificationTexts).Assembly);

    public PushMessage Render(string kind, string locale, IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var culture = CultureInfo.GetCultureInfo(locale == "en" ? "en" : "fr");
        var lang = culture.TwoLetterISOLanguageName;

        string Text(string key) => Resources.GetString(key, culture)
            ?? throw new InvalidOperationException($"Missing notification text '{key}'.");

        string AboutNames(string prefix) => names.Count switch
        {
            0 => Text(prefix + ".None"),
            1 => string.Format(culture, Text(prefix + ".One"), names[0]),
            _ => string.Format(culture, Text(prefix + ".Many"), names[0], names.Count - 1),
        };

        return kind switch
        {
            NotificationKinds.Fell => new(Text("Fell.Title"), Text("Fell.Body"), $"/{lang}/", kind),
            NotificationKinds.FriendsFell => new(Text("FriendsFell.Title"), AboutNames("FriendsFell"), $"/{lang}/friends", kind),
            NotificationKinds.SavedBy => new(Text("SavedBy.Title"), AboutNames("SavedBy"), $"/{lang}/profile", kind),
            NotificationKinds.SkinsUnlocked => new(Text("SkinsUnlocked.Title"), Text("SkinsUnlocked.Body"), $"/{lang}/profile", kind),
            NotificationKinds.Storm => new(Text("Storm.Title"), Text("Storm.Body"), $"/{lang}/", kind),
            NotificationKinds.FriendsBelow => new(Text("FriendsBelow.Title"), AboutNames("FriendsBelow"), $"/{lang}/friends", kind),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown notification kind."),
        };
    }
}

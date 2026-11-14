using Microsoft.Extensions.FileProviders;
using Microsoft.Net.Http.Headers;

namespace NobodyFalls.Api.Hosting;

/// <summary>
/// Serves the built front end (wwwroot) under <c>/fr</c> and <c>/en</c> (GAME_DESIGN §11),
/// from the same origin as the API so that cookies stay simple.
/// </summary>
internal static class SpaHosting
{
    public const string LanguageCookie = "nf-lang";

    public static void MapSpa(this WebApplication app)
    {
        // "/" picks the language: remembered choice, then the browser's, then French.
        app.MapGet("/", (HttpContext context) =>
        {
            var remembered = context.Request.Cookies[LanguageCookie];
            var language = remembered is "fr" or "en" ? remembered : FromHeader(context.Request.Headers.AcceptLanguage.ToString());
            return Results.Redirect($"/{language}/");
        }).ExcludeFromDescription();

        var index = app.Environment.WebRootFileProvider.GetFileInfo("index.html");
        foreach (var language in new[] { "fr", "en" })
        {
            app.MapGet($"/{language}/{{**path}}", (HttpContext context) =>
            {
                if (!index.Exists)
                {
                    return Results.NotFound();
                }

                context.Response.Headers[HeaderNames.CacheControl] = "no-cache";
                return Results.File(index.CreateReadStream(), "text/html; charset=utf-8");
            }).ExcludeFromDescription();
        }
    }

    public static void UseSpaStaticFiles(this WebApplication app) =>
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = app.Environment.WebRootFileProvider ?? new NullFileProvider(),
            OnPrepareResponse = context =>
            {
                // Hashed build assets never change; everything else is revalidated.
                if (context.Context.Request.Path.StartsWithSegments("/assets", StringComparison.Ordinal))
                {
                    context.Context.Response.Headers[HeaderNames.CacheControl] = "public, max-age=31536000, immutable";
                }
            },
        });

    private static string FromHeader(string acceptLanguage)
    {
        foreach (var part in acceptLanguage.Split(','))
        {
            var tag = part.Split(';')[0].Trim();
            if (tag.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            {
                return "en";
            }

            if (tag.StartsWith("fr", StringComparison.OrdinalIgnoreCase))
            {
                return "fr";
            }
        }

        return "fr";
    }
}

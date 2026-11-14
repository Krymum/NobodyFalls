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

        MapAdminApp(app);

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

    /// <summary>
    /// The admin is a separate front end (ADR 0017) under <c>/admin/</c>: never indexed,
    /// never framed, scripts from this origin only.
    /// </summary>
    private static void MapAdminApp(WebApplication app)
    {
        var index = app.Environment.WebRootFileProvider.GetFileInfo("admin/index.html");
        app.MapGet("/admin/{**path}", (HttpContext context, string? path) =>
        {
            // A missing build file is a 404, not the page (static files are served before routing).
            if (!index.Exists || (path is not null && path.StartsWith("assets/", StringComparison.Ordinal)))
            {
                return Results.NotFound();
            }

            var headers = context.Response.Headers;
            headers[HeaderNames.CacheControl] = "no-cache";
            headers["X-Robots-Tag"] = "noindex, nofollow";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "same-origin";
            headers["Content-Security-Policy"] =
                "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
                "font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
            return Results.File(index.CreateReadStream(), "text/html; charset=utf-8");
        }).ExcludeFromDescription();
    }

    public static void UseSpaStaticFiles(this WebApplication app)
    {
        var files = app.Environment.WebRootFileProvider ?? new NullFileProvider();
        app.UsePrecompressedFiles(files);
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = files,
            ContentTypeProvider = new PrecompressedFiles.ContentTypes(),
            OnPrepareResponse = context =>
            {
                var headers = context.Context.Response.Headers;
                var served = context.Context.Request.Path.Value ?? "";
                if (PrecompressedFiles.EncodingOf(served) is { } encoding)
                {
                    headers[HeaderNames.ContentEncoding] = encoding;
                }

                // Hashed build assets never change; the service worker must always be fresh.
                var path = PrecompressedFiles.OriginalPath(served);
                if (path.StartsWith("/assets/", StringComparison.Ordinal) || path.StartsWith("/admin/assets/", StringComparison.Ordinal))
                {
                    headers[HeaderNames.CacheControl] = "public, max-age=31536000, immutable";
                }
                else if (path is "/sw.js" or "/manifest.webmanifest")
                {
                    headers[HeaderNames.CacheControl] = "no-cache";
                }
            },
        });
    }

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

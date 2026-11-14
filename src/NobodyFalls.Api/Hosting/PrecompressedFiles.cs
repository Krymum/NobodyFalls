using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Net.Http.Headers;

namespace NobodyFalls.Api.Hosting;

/// <summary>
/// Serves the brotli or gzip copy that the front-end build writes next to each text
/// file (<c>app.js.br</c>, <c>app.js.gz</c>), when the browser accepts it. Compressing
/// once at build time costs nothing per request and allows the strongest settings.
/// </summary>
internal static class PrecompressedFiles
{
    private static readonly (string Encoding, string Suffix)[] Variants = [("br", ".br"), ("gzip", ".gz")];

    /// <summary>Rewrites the request to the best precompressed variant that exists.</summary>
    public static IApplicationBuilder UsePrecompressedFiles(this IApplicationBuilder app, IFileProvider files) =>
        app.Use((context, next) =>
        {
            var request = context.Request;
            var path = request.Path.Value;
            if ((HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
                && path is not null
                && Path.HasExtension(path)
                && !IsCompressedName(path))
            {
                var variant = Choose(request.GetTypedHeaders().AcceptEncoding, path, files);
                if (variant is { } chosen)
                {
                    request.Path = path + chosen.Suffix;
                    context.Response.Headers.Append(HeaderNames.Vary, HeaderNames.AcceptEncoding);
                }
                else if (Variants.Any(v => files.GetFileInfo(path + v.Suffix).Exists))
                {
                    context.Response.Headers.Append(HeaderNames.Vary, HeaderNames.AcceptEncoding);
                }
            }

            return next(context);
        });

    /// <summary>The original path of a precompressed file (<c>/a.js.br</c> → <c>/a.js</c>).</summary>
    public static string OriginalPath(string path)
    {
        foreach (var (_, suffix) in Variants)
        {
            if (path.EndsWith(suffix, StringComparison.Ordinal))
            {
                return path[..^suffix.Length];
            }
        }

        return path;
    }

    /// <summary>The Content-Encoding of a precompressed file, or null for an ordinary one.</summary>
    public static string? EncodingOf(string path)
    {
        foreach (var (encoding, suffix) in Variants)
        {
            if (path.EndsWith(suffix, StringComparison.Ordinal))
            {
                return encoding;
            }
        }

        return null;
    }

    private static bool IsCompressedName(string path) => EncodingOf(path) is not null;

    private static (string Encoding, string Suffix)? Choose(
        IList<StringWithQualityHeaderValue> accepted, string path, IFileProvider files)
    {
        foreach (var variant in Variants)
        {
            var acceptable = accepted.Any(a =>
                (a.Quality ?? 1) > 0
                && (a.Value.Equals(variant.Encoding, StringComparison.OrdinalIgnoreCase) || a.Value == "*"));
            if (acceptable && files.GetFileInfo(path + variant.Suffix).Exists)
            {
                return variant;
            }
        }

        return null;
    }

    /// <summary>Content types by the original extension, so that <c>app.js.br</c> is JavaScript.</summary>
    public sealed class ContentTypes : IContentTypeProvider
    {
        private readonly FileExtensionContentTypeProvider _inner = new();

        public bool TryGetContentType(string subpath, out string contentType) =>
            _inner.TryGetContentType(OriginalPath(subpath), out contentType!);
    }
}

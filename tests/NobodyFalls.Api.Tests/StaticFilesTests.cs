using System.IO.Compression;
using System.Net;
using System.Text;
using NobodyFalls.Api.Tests.Support;
using NobodyFalls.Testing.Postgres;

namespace NobodyFalls.Api.Tests;

/// <summary>The built front end: precompressed variants, content types and caching.</summary>
public sealed class StaticFilesTests(PostgresFixture postgres) : IDisposable
{
    private const string Script = "console.log('nobody falls');";
    private readonly DirectoryInfo _webRoot = Directory.CreateTempSubdirectory("nf-wwwroot-");

    [Fact]
    public async Task Brotli_is_served_when_the_browser_accepts_it()
    {
        await using var app = await StartAsync();
        using var response = await GetAsync(app, "/assets/app-abc123.js", "gzip, deflate, br");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["br"], response.Content.Headers.ContentEncoding);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Accept-Encoding", response.Headers.Vary);
        Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl?.ToString());
        await using var body = new BrotliStream(await response.Content.ReadAsStreamAsync(), CompressionMode.Decompress);
        Assert.Equal(Script, await new StreamReader(body).ReadToEndAsync());
    }

    [Fact]
    public async Task Gzip_is_the_fallback_and_identity_the_last_resort()
    {
        await using var app = await StartAsync();

        using var gzip = await GetAsync(app, "/assets/app-abc123.js", "gzip");
        Assert.Equal(["gzip"], gzip.Content.Headers.ContentEncoding);
        await using var body = new GZipStream(await gzip.Content.ReadAsStreamAsync(), CompressionMode.Decompress);
        Assert.Equal(Script, await new StreamReader(body).ReadToEndAsync());

        using var refused = await GetAsync(app, "/assets/app-abc123.js", "br;q=0, gzip;q=0");
        Assert.Empty(refused.Content.Headers.ContentEncoding);
        Assert.Equal(Script, await refused.Content.ReadAsStringAsync());
        Assert.Contains("Accept-Encoding", refused.Headers.Vary);
    }

    [Fact]
    public async Task The_service_worker_stays_fresh_even_compressed()
    {
        await using var app = await StartAsync();
        using var response = await GetAsync(app, "/sw.js", "br");

        Assert.Equal(["br"], response.Content.Headers.ContentEncoding);
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
    }

    public void Dispose() => _webRoot.Delete(recursive: true);

    private async Task<ApiApp> StartAsync()
    {
        var assets = Directory.CreateDirectory(Path.Combine(_webRoot.FullName, "assets"));
        foreach (var path in new[] { Path.Combine(assets.FullName, "app-abc123.js"), Path.Combine(_webRoot.FullName, "sw.js") })
        {
            var bytes = Encoding.UTF8.GetBytes(Script);
            await File.WriteAllBytesAsync(path, bytes);
            await File.WriteAllBytesAsync(path + ".br", Compress(bytes, s => new BrotliStream(s, CompressionLevel.SmallestSize)));
            await File.WriteAllBytesAsync(path + ".gz", Compress(bytes, s => new GZipStream(s, CompressionLevel.SmallestSize)));
        }

        return await ApiApp.StartAsync(postgres, s => s["webroot"] = _webRoot.FullName);
    }

    private static async Task<HttpResponseMessage> GetAsync(ApiApp app, string path, string acceptEncoding)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.TryAddWithoutValidation("Accept-Encoding", acceptEncoding);
        return await app.Browser().SendAsync(request);
    }

    private static byte[] Compress(byte[] content, Func<Stream, Stream> compressor)
    {
        using var output = new MemoryStream();
        using (var stream = compressor(output))
        {
            stream.Write(content);
        }

        return output.ToArray();
    }
}

using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

namespace Fitzobit.Web.Infrastructure;

/// <summary>
/// Browser caching for the web app's files, so a normal reload always shows the newest version after a deploy. The start page
/// (index.html) is never cached: it names the current script and style files. Those files in /assets/ carry a content hash in their
/// names (a new deploy gives new names), so they are cached for a year. Other files (logo, icons) are re-checked after an hour.
/// </summary>
public static class StaticFileCaching
{
    public const string NoStore = "no-cache, no-store, must-revalidate";
    public const string Immutable = "public, max-age=31536000, immutable";
    public const string Short = "public, max-age=3600";

    /// <summary>Options for <c>UseStaticFiles</c> and <c>MapFallbackToFile</c> (both serve index.html).</summary>
    public static StaticFileOptions Options { get; } = new()
    {
        OnPrepareResponse = context =>
        {
            context.Context.Response.Headers[HeaderNames.CacheControl] = CacheControlFor(context.Context.Request.Path, context.File.Name);
            if (context.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase))
                context.Context.Response.Headers[HeaderNames.Expires] = "0";
        }
    };

    /// <summary>The Cache-Control value for a served file.</summary>
    public static string CacheControlFor(string? requestPath, string fileName) =>
        fileName.Equals("index.html", StringComparison.OrdinalIgnoreCase) ? NoStore
        : requestPath?.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase) == true ? Immutable
        : Short;
}

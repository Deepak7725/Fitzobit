using Fitzobit.Web.Infrastructure;

namespace Fitzobit.Tests.Web;

/// <summary>After a deploy a normal reload must show the new version: the start page is never cached, hashed assets are.</summary>
public class StaticFileCachingTests
{
    [Theory]
    [InlineData("/", "index.html", StaticFileCaching.NoStore)]
    [InlineData("/data", "index.html", StaticFileCaching.NoStore)] // client-side routes fall back to index.html
    [InlineData("/index.html", "index.html", StaticFileCaching.NoStore)]
    [InlineData("/assets/index-BgyelDIo.js", "index-BgyelDIo.js", StaticFileCaching.Immutable)]
    [InlineData("/assets/index-Ck38vhke.css", "index-Ck38vhke.css", StaticFileCaching.Immutable)]
    [InlineData("/logo.png", "logo.png", StaticFileCaching.Short)]
    [InlineData("/favicon-32.png", "favicon-32.png", StaticFileCaching.Short)]
    public void Each_file_gets_the_right_cache_rule(string path, string file, string expected) =>
        Assert.Equal(expected, StaticFileCaching.CacheControlFor(path, file));
}

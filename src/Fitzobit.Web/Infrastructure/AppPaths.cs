namespace Fitzobit.Web.Infrastructure;

public sealed record AppPaths(string BasePath)
{
    /// <summary>
    /// "Fitzobit:BasePath" if configured, otherwise the nearest folder above the content root that holds Fitzobit.sln,
    /// otherwise the content root itself (e.g. a published deployment).
    /// </summary>
    public static string ResolveBasePath(IConfiguration configuration, string contentRoot)
    {
        var configured = configuration["Fitzobit:BasePath"];
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(contentRoot, configured));

        for (var dir = new DirectoryInfo(contentRoot); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Fitzobit.sln"))) return dir.FullName;

        return contentRoot;
    }
}

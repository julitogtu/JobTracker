namespace JobTracker.Domain.Jobs;

internal static class AbsoluteUrl
{
    public static bool IsHttpOrHttps(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}

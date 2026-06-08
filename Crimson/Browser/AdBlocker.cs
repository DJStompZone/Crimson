using System;
using System.Collections.Generic;
using System.Linq;

namespace Crimson.Browser
{
    public sealed class AdBlocker
    {
        private static readonly IReadOnlyList<string> BlockedHostParts = new[]
        {
            "doubleclick.net",
            "googlesyndication.com",
            "googleadservices.com",
            "adservice.google.com",
            "pagead2.googlesyndication.com",
            "static.doubleclick.net",
            "www.google-analytics.com",
            "ssl.google-analytics.com"
        };

        private static readonly IReadOnlyList<string> BlockedUrlParts = new[]
        {
            "/pagead/",
            "/ptracking",
            "/api/stats/ads",
            "/api/stats/qoe",
            "/youtubei/v1/log_event",
            "/youtubei/v1/att/get",
            "adformat=",
            "adunit",
            "ad_type",
            "googleads"
        };

        public bool ShouldBlock(string? rawUri)
        {
            if (string.IsNullOrWhiteSpace(rawUri))
            {
                return false;
            }

            if (!Uri.TryCreate(rawUri, UriKind.Absolute, out Uri? uri))
            {
                return false;
            }

            string host = uri.Host;
            string url = rawUri;

            if (IsVideoPlaybackUrl(host, url))
            {
                return false;
            }

            return ContainsAny(host, BlockedHostParts) || ContainsAny(url, BlockedUrlParts);
        }

        private static bool IsVideoPlaybackUrl(string host, string url)
        {
            return host.Contains("googlevideo.com", StringComparison.OrdinalIgnoreCase) ||
                   url.Contains("/videoplayback", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsAny(string value, IReadOnlyList<string> needles)
        {
            return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }
    }
}

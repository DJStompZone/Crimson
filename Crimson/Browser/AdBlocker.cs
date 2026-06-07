using System;
using System.Collections.Generic;
using System.Linq;

namespace Crimson.Browser
{
    /// <summary>
    /// Performs conservative request blocking for obvious ad/tracker endpoints while preserving
    /// YouTube media playback URLs. Most video-ad handling lives in the injected page scripts.
    /// </summary>
    public sealed class AdBlocker
    {
        private readonly IReadOnlyList<string> blockedHostParts = new[]
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

        private readonly IReadOnlyList<string> blockedUrlParts = new[]
        {
            "/pagead/",
            "/ptracking",
            "/api/stats/ads",
            "/api/stats/qoe",
            "/youtubei/v1/log_event",
            "/youtubei/v1/att/get",
            "/youtubei/v1/player/ad_break",
            "adformat=",
            "adunit",
            "ad_type",
            "googleads"
        };

        public bool ShouldBlock(string rawUri)
        {
            if (!Uri.TryCreate(rawUri, UriKind.Absolute, out Uri uri))
            {
                return false;
            }

            string host = uri.Host.ToLowerInvariant();
            string url = rawUri.ToLowerInvariant();

            if (IsVideoPlaybackUrl(host, url))
            {
                return false;
            }

            return blockedHostParts.Any(host.Contains) || blockedUrlParts.Any(url.Contains);
        }

        private static bool IsVideoPlaybackUrl(string host, string url)
        {
            return host.EndsWith("googlevideo.com") ||
                   host.Contains("googlevideo.com") ||
                   url.Contains("/videoplayback");
        }
    }
}

(() => {
    "use strict";

    const apiBase = "https://sponsor.ajay.app/api/skipSegments";
    const categories = [
        "sponsor",
        "selfpromo",
        "interaction",
        "intro",
        "outro",
        "preview",
        "music_offtopic",
        "filler"
    ];

    let currentVideoId = null;
    let segments = [];
    let lastSkipAt = 0;
    let enabled = true;

    function readSettings() {
        const settings = window.__crimsonSettings;

        if (!settings) {
            enabled = true;
            return;
        }

        enabled = settings.sponsorBlockEnabled !== false;
    }

    function getVideoId() {
        const url = new URL(window.location.href);

        if (url.hostname.includes("youtube.com")) {
            return url.searchParams.get("v");
        }

        if (url.hostname === "youtu.be") {
            return url.pathname.replace("/", "");
        }

        return null;
    }

    async function fetchSegments(videoId) {
        const categoryParam = encodeURIComponent(JSON.stringify(categories));
        const url = `${apiBase}?videoID=${encodeURIComponent(videoId)}&categories=${categoryParam}`;

        const response = await fetch(url, {
            method: "GET",
            cache: "no-store"
        });

        if (response.status === 404) {
            return [];
        }

        if (!response.ok) {
            console.warn("[Crimson:SponsorBlock] request failed", response.status);
            return [];
        }

        const data = await response.json();
        return Array.isArray(data) ? data : [];
    }

    async function refreshSegments() {
        readSettings();

        if (!enabled) {
            segments = [];
            return;
        }

        const videoId = getVideoId();

        if (!videoId) {
            currentVideoId = null;
            segments = [];
            return;
        }

        if (videoId === currentVideoId) {
            return;
        }

        currentVideoId = videoId;
        segments = [];

        try {
            segments = await fetchSegments(videoId);
            console.info("[Crimson:SponsorBlock] segments loaded", segments);
        } catch (error) {
            console.warn("[Crimson:SponsorBlock] failed", error);
            segments = [];
        }
    }

    function findVideoElement() {
        const videos = Array.from(document.querySelectorAll("video"));

        if (videos.length === 0) {
            return null;
        }

        return videos.find(video => video.duration > 0) || videos[0];
    }

    function maybeSkip() {
        readSettings();

        if (!enabled || segments.length === 0) {
            return;
        }

        const video = findVideoElement();

        if (!video || video.paused || Number.isNaN(video.currentTime)) {
            return;
        }

        const now = Date.now();

        if (now - lastSkipAt < 750) {
            return;
        }

        const currentTime = video.currentTime;

        for (const item of segments) {
            if (!item || !Array.isArray(item.segment)) {
                continue;
            }

            const start = item.segment[0];
            const end = item.segment[1];

            if (typeof start !== "number" || typeof end !== "number") {
                continue;
            }

            if (currentTime >= start && currentTime < end) {
                video.currentTime = end + 0.05;
                lastSkipAt = now;
                console.info(`[Crimson:SponsorBlock] skipped ${item.category}: ${start} -> ${end}`);
                return;
            }
        }
    }

    function hookNavigation() {
        const originalPushState = history.pushState;
        const originalReplaceState = history.replaceState;

        history.pushState = function crimsonPushState() {
            const result = originalPushState.apply(this, arguments);
            window.setTimeout(refreshSegments, 100);
            return result;
        };

        history.replaceState = function crimsonReplaceState() {
            const result = originalReplaceState.apply(this, arguments);
            window.setTimeout(refreshSegments, 100);
            return result;
        };

        window.addEventListener("popstate", () => window.setTimeout(refreshSegments, 100));
        document.addEventListener("yt-navigate-finish", () => window.setTimeout(refreshSegments, 100));
        window.addEventListener("crimson-settings-changed", () => window.setTimeout(refreshSegments, 100));
    }

    hookNavigation();

    window.setInterval(refreshSegments, 1000);
    window.setInterval(maybeSkip, 250);

    refreshSegments();
})();

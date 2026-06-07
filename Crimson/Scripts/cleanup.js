(() => {
    "use strict";

    const styleId = "crimson-cleanup-style";

    const css = `
        ytd-promoted-sparkles-web-renderer,
        ytd-display-ad-renderer,
        ytd-companion-slot-renderer,
        ytd-action-companion-ad-renderer,
        ytd-ad-slot-renderer,
        ytd-in-feed-ad-layout-renderer,
        ytd-promoted-video-renderer,
        #player-ads,
        #masthead-ad,
        #feedmodule-PRO,
        .ytp-ad-module,
        .video-ads {
            display: none !important;
            visibility: hidden !important;
            pointer-events: none !important;
        }
    `;

    function installStyle() {
        if (document.getElementById(styleId)) {
            return;
        }

        const style = document.createElement("style");
        style.id = styleId;
        style.textContent = css;

        const root = document.documentElement || document.head || document.body;

        if (root) {
            root.appendChild(style);
        }
    }

    function removeKnownAdNodes() {
        const selectors = [
            "ytd-promoted-sparkles-web-renderer",
            "ytd-display-ad-renderer",
            "ytd-companion-slot-renderer",
            "ytd-action-companion-ad-renderer",
            "ytd-ad-slot-renderer",
            "ytd-in-feed-ad-layout-renderer",
            "ytd-promoted-video-renderer",
            "#player-ads",
            "#masthead-ad",
            ".ytp-ad-module",
            ".video-ads"
        ];

        for (const selector of selectors) {
            for (const node of document.querySelectorAll(selector)) {
                node.remove();
            }
        }
    }

    function runCleanup() {
        installStyle();
        removeKnownAdNodes();
    }

    function startObserver() {
        const target = document.documentElement || document.body;

        if (!target) {
            window.setTimeout(startObserver, 250);
            return;
        }

        const observer = new MutationObserver(runCleanup);

        observer.observe(target, {
            childList: true,
            subtree: true
        });
    }

    runCleanup();
    startObserver();

    document.addEventListener("yt-navigate-finish", runCleanup);
    window.setInterval(runCleanup, 1500);
})();

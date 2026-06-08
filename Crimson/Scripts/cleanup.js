(() => {
    "use strict";

    const styleId = "crimson-cleanup-style";

    const adNodeSelectors = [
        "ytd-promoted-sparkles-web-renderer",
        "ytd-display-ad-renderer",
        "ytd-companion-slot-renderer",
        "ytd-action-companion-ad-renderer",
        "ytd-ad-slot-renderer",
        "ytd-in-feed-ad-layout-renderer",
        "ytd-promoted-video-renderer",
        "ytd-statement-banner-renderer",
        "ytd-banner-promo-renderer",
        "ytd-mealbar-promo-renderer",
        "#player-ads",
        "#masthead-ad",
        "#feedmodule-PRO",
        ".ytp-ad-module",
        ".video-ads"
    ];

    const adNodeSelector = adNodeSelectors.join(",");

    const removableCardSelectors = [
        "ytd-rich-item-renderer",
        "ytd-rich-section-renderer",
        "ytd-video-renderer",
        "ytd-compact-video-renderer",
        "ytd-grid-video-renderer",
        "ytd-reel-item-renderer",
        "ytd-item-section-renderer",
        "ytd-shelf-renderer"
    ];

    const ghostCandidateSelectors = [
        "ytd-rich-item-renderer",
        "ytd-rich-section-renderer",
        "ytd-video-renderer",
        "ytd-compact-video-renderer",
        "ytd-grid-video-renderer",
        "ytd-reel-item-renderer"
    ];

    const realContentSelector = [
        "a#thumbnail[href*='/watch']",
        "a.yt-simple-endpoint[href*='/watch']",
        "a[href*='/shorts/']",
        "#video-title",
        "h3",
        "yt-formatted-string[aria-label]",
        "img[src]",
        "yt-img-shadow img[src]",
        "ytd-thumbnail",
        "ytd-channel-name"
    ].join(",");

    const css = `
        ${adNodeSelector},
        ytd-rich-item-renderer:has(ytd-ad-slot-renderer),
        ytd-rich-item-renderer:has(ytd-display-ad-renderer),
        ytd-rich-item-renderer:has(ytd-in-feed-ad-layout-renderer),
        ytd-rich-item-renderer:has(ytd-promoted-video-renderer),
        ytd-video-renderer:has(ytd-ad-slot-renderer),
        ytd-video-renderer:has(ytd-display-ad-renderer),
        ytd-video-renderer:has(ytd-promoted-video-renderer),
        ytd-rich-section-renderer:has(ytd-statement-banner-renderer),
        ytd-rich-section-renderer:has(ytd-banner-promo-renderer),
        ytd-rich-section-renderer:has(ytd-mealbar-promo-renderer),
        ytd-compact-video-renderer:has(ytd-ad-slot-renderer),
        ytd-compact-video-renderer:has(ytd-display-ad-renderer),
        ytd-compact-video-renderer:has(ytd-promoted-video-renderer) {
            display: none !important;
            visibility: hidden !important;
            pointer-events: none !important;
            contain: strict !important;
            height: 0 !important;
            min-height: 0 !important;
            margin: 0 !important;
            padding: 0 !important;
            overflow: hidden !important;
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

    function closestRemovableCard(node) {
        if (!node || typeof node.closest !== "function") {
            return null;
        }

        return node.closest(removableCardSelectors.join(","));
    }

    function removeNode(node) {
        if (!node || !node.parentNode) {
            return;
        }

        node.remove();
    }

    function removeKnownAdNodes() {
        for (const node of document.querySelectorAll(adNodeSelector)) {
            const card = closestRemovableCard(node);

            if (card) {
                removeNode(card);
                continue;
            }

            removeNode(node);
        }
    }

    function isProbablyGhostCard(card) {
        if (!card || !card.isConnected) {
            return false;
        }

        if (card.matches(adNodeSelector) || card.querySelector(adNodeSelector)) {
            return true;
        }

        if (card.querySelector(realContentSelector)) {
            return false;
        }

        const text = (card.textContent || "").replace(/\s+/g, "").trim();

        if (text.length > 12) {
            return false;
        }

        const rect = card.getBoundingClientRect();
        const hasLayoutFootprint = rect.width > 24 || rect.height > 24;

        return hasLayoutFootprint;
    }

    function removeGhostCards() {
        for (const card of document.querySelectorAll(ghostCandidateSelectors.join(","))) {
            if (isProbablyGhostCard(card)) {
                removeNode(card);
            }
        }
    }

    function normalizeFeedLayout() {
        const grids = [
            "ytd-rich-grid-renderer #contents",
            "ytd-rich-grid-row #contents",
            "ytd-section-list-renderer #contents",
            "ytd-item-section-renderer #contents"
        ];

        for (const selector of grids) {
            for (const node of document.querySelectorAll(selector)) {
                node.style.removeProperty("min-height");
            }
        }
    }

    function runCleanup() {
        installStyle();
        removeKnownAdNodes();
        removeGhostCards();
        normalizeFeedLayout();
    }

    function startObserver() {
        const target = document.documentElement || document.body;

        if (!target) {
            window.setTimeout(startObserver, 250);
            return;
        }

        const observer = new MutationObserver(() => {
            window.requestAnimationFrame(runCleanup);
        });

        observer.observe(target, {
            childList: true,
            subtree: true
        });
    }

    runCleanup();
    startObserver();

    document.addEventListener("yt-navigate-finish", () => {
        window.setTimeout(runCleanup, 0);
        window.setTimeout(runCleanup, 250);
        window.setTimeout(runCleanup, 1000);
    });

    window.setInterval(runCleanup, 1500);
})();

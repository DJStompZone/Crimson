(() => {
    "use strict";

    const logPrefix = "[Crimson:Prune]";
    const youtubeApiMarkers = [
        "/youtubei/v1/player",
        "/youtubei/v1/next",
        "/youtubei/v1/browse",
        "/youtubei/v1/search",
        "/youtubei/v1/reel/reel_watch_sequence"
    ];

    const adKeyNames = new Set([
        "playerAds",
        "adPlacements",
        "adSlots",
        "adBreakHeartbeatParams",
        "adBreakService",
        "adSafetyReason",
        "adSignalsInfo",
        "adTagParameters",
        "adTrackingParams",
        "adVideoId",
        "adParams",
        "adClientParams",
        "adPlacementConfig",
        "adSlotMetadata",
        "companionAd",
        "companionAdRenderer",
        "displayAdRenderer",
        "promotedSparklesWebRenderer",
        "promotedVideoRenderer",
        "statementBannerRenderer",
        "mealbarPromoRenderer",
        "playerLegacyDesktopWatchAdsRenderer",
        "instreamVideoAdRenderer",
        "adPreviewRenderer"
    ]);

    const rendererAdKeys = new Set([
        "promotedSparklesWebRenderer",
        "promotedVideoRenderer",
        "displayAdRenderer",
        "companionAdRenderer",
        "inFeedAdLayoutRenderer",
        "adSlotRenderer",
        "playerLegacyDesktopWatchAdsRenderer",
        "instreamVideoAdRenderer"
    ]);

    function shouldPruneUrl(rawUrl) {
        if (!rawUrl || typeof rawUrl !== "string") {
            return false;
        }

        return youtubeApiMarkers.some(marker => rawUrl.includes(marker));
    }

    function isPlainObject(value) {
        return value !== null && typeof value === "object" && !Array.isArray(value);
    }

    function pruneJson(value, depth = 0) {
        if (depth > 80 || value === null || typeof value !== "object") {
            return value;
        }

        if (Array.isArray(value)) {
            for (let index = value.length - 1; index >= 0; index -= 1) {
                const item = value[index];

                if (containsRendererAdKey(item)) {
                    value.splice(index, 1);
                    continue;
                }

                pruneJson(item, depth + 1);
            }

            return value;
        }

        for (const key of Object.keys(value)) {
            if (adKeyNames.has(key)) {
                delete value[key];
                continue;
            }

            const child = value[key];

            if (containsRendererAdKey(child)) {
                delete value[key];
                continue;
            }

            pruneJson(child, depth + 1);
        }

        return value;
    }

    function containsRendererAdKey(value) {
        if (!isPlainObject(value)) {
            return false;
        }

        return Object.keys(value).some(key => rendererAdKeys.has(key));
    }

    function tryPruneText(text, source) {
        if (!text || typeof text !== "string") {
            return text;
        }

        const trimmed = text.trim();

        if (!trimmed.startsWith("{") && !trimmed.startsWith("[")) {
            return text;
        }

        try {
            const json = JSON.parse(text);
            pruneJson(json);
            console.info(`${logPrefix} pruned ${source}`);
            return JSON.stringify(json);
        } catch {
            return text;
        }
    }

    function patchFetch() {
        const originalFetch = window.fetch;

        window.fetch = async function crimsonFetch(input, init) {
            const response = await originalFetch.apply(this, arguments);

            try {
                const url = typeof input === "string" ? input : input?.url;

                if (!shouldPruneUrl(url)) {
                    return response;
                }

                const contentType = response.headers.get("content-type") || "";

                if (!contentType.includes("application/json")) {
                    return response;
                }

                const originalText = await response.clone().text();
                const prunedText = tryPruneText(originalText, url);

                if (prunedText === originalText) {
                    return response;
                }

                return new Response(prunedText, {
                    status: response.status,
                    statusText: response.statusText,
                    headers: response.headers
                });
            } catch (error) {
                console.warn(`${logPrefix} fetch patch failed`, error);
                return response;
            }
        };
    }

    function patchXhr() {
        const originalOpen = XMLHttpRequest.prototype.open;
        const originalSend = XMLHttpRequest.prototype.send;

        XMLHttpRequest.prototype.open = function crimsonOpen(method, url) {
            this.__crimsonUrl = url;
            return originalOpen.apply(this, arguments);
        };

        XMLHttpRequest.prototype.send = function crimsonSend() {
            if (!shouldPruneUrl(this.__crimsonUrl)) {
                return originalSend.apply(this, arguments);
            }

            this.addEventListener("readystatechange", function crimsonReadyStateChanged() {
                if (this.readyState !== 4) {
                    return;
                }

                tryPatchXhrResponse(this);
            });

            return originalSend.apply(this, arguments);
        };
    }

    function tryPatchXhrResponse(xhr) {
        const originalText = xhr.responseText;

        if (!originalText) {
            return;
        }

        const prunedText = tryPruneText(originalText, xhr.__crimsonUrl);

        if (prunedText === originalText) {
            return;
        }

        redefineReadonlyProperty(xhr, "responseText", prunedText);
        redefineReadonlyProperty(xhr, "response", prunedText);
    }

    function redefineReadonlyProperty(target, propertyName, value) {
        try {
            Object.defineProperty(target, propertyName, {
                configurable: true,
                get: () => value
            });
        } catch (error) {
            console.warn(`${logPrefix} failed to redefine ${propertyName}`, error);
        }
    }

    function patchInitialGlobal(name) {
        try {
            let currentValue = window[name];

            Object.defineProperty(window, name, {
                configurable: true,
                get() {
                    if (currentValue && typeof currentValue === "object") {
                        pruneJson(currentValue);
                    }

                    return currentValue;
                },
                set(value) {
                    if (value && typeof value === "object") {
                        pruneJson(value);
                    }

                    currentValue = value;
                }
            });

            if (currentValue && typeof currentValue === "object") {
                pruneJson(currentValue);
            }
        } catch (error) {
            console.warn(`${logPrefix} failed to patch ${name}`, error);
        }
    }

    function patchInitialGlobals() {
        patchInitialGlobal("ytInitialPlayerResponse");
        patchInitialGlobal("ytInitialData");
    }

    function installAdAccelerator() {
        let savedRate = null;
        let savedMuted = null;
        let lastTouched = 0;

        window.setInterval(() => {
            const player = document.querySelector(".html5-video-player");
            const video = document.querySelector("video");

            if (!player || !video) {
                return;
            }

            const adShowing = player.classList.contains("ad-showing") ||
                document.querySelector(".ytp-ad-player-overlay") ||
                document.querySelector(".ytp-ad-text") ||
                document.querySelector(".ytp-ad-skip-button, .ytp-skip-ad-button");

            if (adShowing) {
                if (savedRate === null) {
                    savedRate = video.playbackRate;
                }

                if (savedMuted === null) {
                    savedMuted = video.muted;
                }

                video.muted = true;
                video.playbackRate = 16;
                lastTouched = Date.now();

                const skipButton = document.querySelector(".ytp-ad-skip-button, .ytp-skip-ad-button, button.ytp-ad-skip-button-modern");

                if (skipButton) {
                    skipButton.click();
                }

                return;
            }

            if (savedRate !== null && Date.now() - lastTouched > 1000) {
                video.playbackRate = savedRate;
                video.muted = savedMuted === true;
                savedRate = null;
                savedMuted = null;
            }
        }, 200);
    }

    patchInitialGlobals();
    patchFetch();
    patchXhr();
    installAdAccelerator();

    console.info(`${logPrefix} installed`);
})();

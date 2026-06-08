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

    const adSelectors = [
        ".html5-video-player.ad-showing",
        ".html5-video-player.ad-interrupting",
        ".ytp-ad-player-overlay",
        ".ytp-ad-text",
        ".ytp-ad-preview-container",
        ".ytp-ad-image-overlay",
        ".ytp-ad-module",
        ".video-ads"
    ];

    const skipSelectors = [
        ".ytp-ad-skip-button",
        ".ytp-skip-ad-button",
        "button.ytp-ad-skip-button-modern",
        ".ytp-ad-skip-button-modern",
        "button[class*='skip']"
    ];

    const playSelectors = [
        ".ytp-large-play-button",
        ".ytp-play-button",
        "button[aria-label='Play']",
        "button[aria-label^='Play']",
        "button[title='Play']",
        "button[title^='Play']"
    ];

    const startTimeThresholdSeconds = 0.45;
    const playKickCooldownMs = 450;
    const userPauseCooldownMs = 5000;
    const xboxAdReloadMaxAttempts = 3;
    const xboxAdReloadDelayMs = 1100;
    const xboxAdReloadStartThresholdSeconds = 2.5;
    const forcedAdSeekDelayMs = 1200;
    const forcedAdSeekCooldownMs = 700;
    const forcedAdSeekStepSeconds = 8;

    let savedRate = null;
    let savedMuted = null;
    let lastAdTouchAt = 0;
    let lastPlayKickAt = 0;
    let lastUserInputAt = 0;
    let lastUserPauseAt = 0;
    let suppressPauseTrackingUntil = 0;
    let lastVideoElement = null;
    let lastMainVideoKey = null;
    let lastForcedSeekAt = 0;
    let adDetectedAt = 0;
    let mainVideoHasPlayed = false;
    let scheduledXboxReloadKey = null;
    let enabledPlatform = "auto";
    let autoPlayedMainVideoKeys = new Set();
    let autoPlayedAdKeys = new Set();

    function readSettings() {
        const settings = window.__crimsonSettings;

        if (!settings) {
            enabledPlatform = "auto";
            return;
        }

        enabledPlatform = settings.platform || "auto";
    }

    function isXboxLike() {
        readSettings();

        if (enabledPlatform === "xbox") {
            return true;
        }

        return /Xbox/i.test(navigator.userAgent || "") || /Xbox/i.test(navigator.platform || "");
    }

    function shouldPruneUrl(rawUrl) {
        if (!rawUrl || typeof rawUrl !== "string") {
            return false;
        }

        return youtubeApiMarkers.some(marker => rawUrl.includes(marker));
    }

    function isPlainObject(value) {
        return value !== null && typeof value === "object" && !Array.isArray(value);
    }

    function containsRendererAdKey(value) {
        if (!isPlainObject(value)) {
            return false;
        }

        return Object.keys(value).some(key => rendererAdKeys.has(key));
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

    function queryFirst(selectors) {
        for (const selector of selectors) {
            const node = document.querySelector(selector);

            if (node) {
                return node;
            }
        }

        return null;
    }

    function findVideoElement() {
        const videos = Array.from(document.querySelectorAll("video"));

        if (videos.length === 0) {
            return null;
        }

        return videos.find(video => Number.isFinite(video.duration) && video.duration > 0) || videos[0];
    }

    function findPlayerElement() {
        return document.querySelector(".html5-video-player");
    }

    function getVideoId() {
        try {
            const url = new URL(window.location.href);

            if (url.hostname.includes("youtube.com")) {
                return url.searchParams.get("v") || "unknown-video";
            }

            if (url.hostname === "youtu.be") {
                return url.pathname.replace("/", "") || "unknown-video";
            }
        } catch {
            return "unknown-video";
        }

        return "unknown-video";
    }

    function getMainVideoKey() {
        return `${getVideoId()}@${location.pathname}${location.search.replace(/([?&])crimson_reload=[^&]*/g, "")}`;
    }

    function getAdKey(video) {
        const videoKey = getMainVideoKey();
        const duration = Number.isFinite(video.duration) ? video.duration.toFixed(2) : "unknown-duration";

        return `${videoKey}:ad:${duration}`;
    }

    function getReloadStorageKey() {
        return `crimson:xbox-ad-reload:${getVideoId()}`;
    }

    function getReloadAttemptCount() {
        return Number(sessionStorage.getItem(getReloadStorageKey()) || "0");
    }

    function setReloadAttemptCount(value) {
        sessionStorage.setItem(getReloadStorageKey(), String(value));
    }

    function isNearStart(video) {
        return Number.isFinite(video.currentTime) && video.currentTime <= startTimeThresholdSeconds;
    }

    function isAdShowing() {
        const player = findPlayerElement();

        if (player?.classList?.contains("ad-showing")) {
            return true;
        }

        if (player?.classList?.contains("ad-interrupting")) {
            return true;
        }

        return adSelectors.some(selector => document.querySelector(selector));
    }

    function clickFirst(selectors) {
        const button = queryFirst(selectors);

        if (!button) {
            return false;
        }

        try {
            button.click();
            return true;
        } catch {
            return false;
        }
    }

    async function tryPlay(video, reason) {
        const now = Date.now();

        if (now - lastPlayKickAt < playKickCooldownMs) {
            return false;
        }

        lastPlayKickAt = now;
        suppressPauseTrackingUntil = now + 750;

        clickFirst(playSelectors);

        try {
            await video.play();
            console.info(`${logPrefix} autoplay kick succeeded: ${reason}`);
            return true;
        } catch (error) {
            console.debug(`${logPrefix} autoplay kick rejected: ${reason}`, error);
            return false;
        }
    }

    function speedUpAd(video) {
        if (savedRate === null) {
            savedRate = video.playbackRate || 1;
        }

        if (savedMuted === null) {
            savedMuted = video.muted;
        }

        video.muted = true;

        try {
            video.playbackRate = 16;
        } catch {
            try {
                video.playbackRate = 8;
            } catch {
                video.playbackRate = 4;
            }
        }

        lastAdTouchAt = Date.now();

        if (adDetectedAt === 0) {
            adDetectedAt = lastAdTouchAt;
        }
    }

    function forceSeekAd(video) {
        const now = Date.now();

        if (now - adDetectedAt < forcedAdSeekDelayMs || now - lastForcedSeekAt < forcedAdSeekCooldownMs) {
            return;
        }

        if (!Number.isFinite(video.currentTime) || !Number.isFinite(video.duration) || video.duration <= 0) {
            return;
        }

        const nextTime = Math.min(video.duration - 0.2, video.currentTime + forcedAdSeekStepSeconds);

        if (nextTime <= video.currentTime) {
            return;
        }

        try {
            video.currentTime = nextTime;
            lastForcedSeekAt = now;
            console.info(`${logPrefix} forced ad seek to ${nextTime}`);
        } catch (error) {
            console.debug(`${logPrefix} forced ad seek rejected`, error);
        }
    }

    function restoreVideo(video) {
        adDetectedAt = 0;

        if (savedRate === null || Date.now() - lastAdTouchAt <= 1000) {
            return;
        }

        try {
            video.playbackRate = savedRate;
        } catch {
            video.playbackRate = 1;
        }

        video.muted = savedMuted === true;

        savedRate = null;
        savedMuted = null;
    }

    function shouldRespectRecentUserPause(video) {
        if (isNearStart(video)) {
            return false;
        }

        return Date.now() - lastUserPauseAt < userPauseCooldownMs;
    }

    function handlePausedAdAtStart(video) {
        if (!video.paused || !isNearStart(video)) {
            return;
        }

        const adKey = getAdKey(video);

        if (autoPlayedAdKeys.has(adKey)) {
            return;
        }

        autoPlayedAdKeys.add(adKey);
        tryPlay(video, "paused ad at start");
    }

    function handlePausedMainVideoAtStart(video) {
        if (!video.paused || !isNearStart(video)) {
            return;
        }

        if (!location.href.includes("/watch?")) {
            return;
        }

        if (shouldRespectRecentUserPause(video)) {
            return;
        }

        const mainVideoKey = getMainVideoKey();

        if (autoPlayedMainVideoKeys.has(mainVideoKey)) {
            return;
        }

        autoPlayedMainVideoKeys.add(mainVideoKey);
        tryPlay(video, "paused main video at start");
    }

    function maybeReloadXboxPreRoll(video) {
        if (!isXboxLike()) {
            return;
        }

        if (!location.href.includes("/watch?")) {
            return;
        }

        if (mainVideoHasPlayed) {
            return;
        }

        if (!isAdShowing()) {
            return;
        }

        if (Number.isFinite(video.currentTime) && video.currentTime > xboxAdReloadStartThresholdSeconds) {
            return;
        }

        const attempts = getReloadAttemptCount();

        if (attempts >= xboxAdReloadMaxAttempts) {
            return;
        }

        const reloadKey = `${getMainVideoKey()}:${attempts}`;

        if (scheduledXboxReloadKey === reloadKey) {
            return;
        }

        scheduledXboxReloadKey = reloadKey;
        setReloadAttemptCount(attempts + 1);

        window.setTimeout(() => {
            if (!isAdShowing() || mainVideoHasPlayed) {
                return;
            }

            console.info(`${logPrefix} requesting Xbox pre-roll hard reload attempt ${attempts + 1}`);
            requestHostHardReload();
        }, xboxAdReloadDelayMs);
    }

    function requestHostHardReload() {
        try {
            if (window.chrome?.webview?.postMessage) {
                window.chrome.webview.postMessage("crimson-hard-reload");
                return;
            }
        } catch (error) {
            console.debug(`${logPrefix} host reload message failed`, error);
        }

        try {
            const url = new URL(location.href);
            url.searchParams.set("crimson_reload", String(Date.now()));
            location.replace(url.toString());
        } catch {
            location.reload();
        }
    }

    function resetPerVideoStateIfNeeded() {
        const mainVideoKey = getMainVideoKey();

        if (mainVideoKey === lastMainVideoKey) {
            return;
        }

        lastMainVideoKey = mainVideoKey;
        lastUserPauseAt = 0;
        adDetectedAt = 0;
        mainVideoHasPlayed = false;
        scheduledXboxReloadKey = null;

        if (autoPlayedMainVideoKeys.size > 50) {
            autoPlayedMainVideoKeys = new Set();
        }

        if (autoPlayedAdKeys.size > 100) {
            autoPlayedAdKeys = new Set();
        }
    }

    function attachVideoPauseTracking(video) {
        if (video === lastVideoElement) {
            return;
        }

        lastVideoElement = video;

        video.addEventListener("pause", () => {
            const now = Date.now();

            if (now < suppressPauseTrackingUntil) {
                return;
            }

            if (now - lastUserInputAt <= 1500 && !isNearStart(video)) {
                lastUserPauseAt = now;
                console.info(`${logPrefix} user pause detected`);
            }
        }, true);
    }

    function installInteractionTracker() {
        const markInteraction = () => {
            lastUserInputAt = Date.now();
        };

        window.addEventListener("pointerdown", markInteraction, true);
        window.addEventListener("mousedown", markInteraction, true);
        window.addEventListener("touchstart", markInteraction, true);
        window.addEventListener("keydown", markInteraction, true);
        window.addEventListener("gamepadconnected", markInteraction, true);
    }

    function installAdAndAutoplayController() {
        window.setInterval(() => {
            readSettings();

            const video = findVideoElement();

            if (!video) {
                return;
            }

            resetPerVideoStateIfNeeded();
            attachVideoPauseTracking(video);

            const adShowing = isAdShowing();

            if (adShowing) {
                speedUpAd(video);
                clickFirst(skipSelectors);
                handlePausedAdAtStart(video);
                maybeReloadXboxPreRoll(video);

                if (!isXboxLike()) {
                    forceSeekAd(video);
                }

                return;
            }

            if (!video.paused && Number.isFinite(video.currentTime) && video.currentTime > 0.5 && location.href.includes("/watch?")) {
                mainVideoHasPlayed = true;
            }

            restoreVideo(video);
            handlePausedMainVideoAtStart(video);
        }, 200);
    }

    patchInitialGlobals();
    patchFetch();
    patchXhr();
    installInteractionTracker();
    installAdAndAutoplayController();

    console.info(`${logPrefix} installed`);
})();

(function () {
    const themeKey = "dotmatter-theme";
    const sidebarKey = "dotmatter-sidebar";
    const maxLiveFeedItems = 14;
    const maxLiveFeedToasts = 3;
    const feedDrawerTransitionMs = 180;
    const liveFeedToastLifetimeMs = 5200;
    let liveMessageCount = 0;
    let liveFeedSource = null;
    let liveFeedItems = [];
    let liveFeedToasts = [];
    let liveFeedToastSeed = 0;
    let feedDrawerHideTimer = 0;
    const liveFeedToastTimers = new Map();

    function applyTheme() {
        document.documentElement.dataset.theme = "dark";
        localStorage.removeItem(themeKey);
        return "dark";
    }

    function applySidebarState(collapsed) {
        const frame = document.getElementById("shell-frame");
        const label = document.getElementById("shell-sidebar-toggle-label");
        if (frame) {
            frame.classList.toggle("shell-frame-collapsed", collapsed);
        }

        if (label) {
            label.textContent = collapsed ? ">>" : "<<";
        }

        localStorage.setItem(sidebarKey, collapsed ? "collapsed" : "expanded");
    }

    function setLiveFeedStatus(kind, text, meta) {
        const badge = document.getElementById("shell-live-feed-badge");
        const details = document.getElementById("shell-live-feed-meta");
        if (badge) {
            badge.textContent = text;
            badge.classList.remove("status-badge-online", "status-badge-error", "status-badge-neutral");
            badge.classList.add(`status-badge-${kind}`);
        }

        if (details) {
            details.textContent = meta;
        }
    }

    function humanizeToken(value) {
        const normalized = `${value ?? ""}`
            .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
            .replace(/[_-]+/g, " ")
            .replace(/:/g, " -> ")
            .replace(/\s+/g, " ")
            .trim();

        if (!normalized) {
            return "";
        }

        return normalized.charAt(0).toUpperCase() + normalized.slice(1);
    }

    function normalizeLiveFeedEvent(raw) {
        try {
            const parsed = JSON.parse(raw);
            if (parsed && typeof parsed === "object") {
                return {
                    device: parsed.device || "controller",
                    type: parsed.type || "event",
                    value: parsed.value || "update",
                    time: parsed.time || new Date().toISOString()
                };
            }
        }
        catch {
        }

        return {
            device: "controller",
            type: "event",
            value: raw || "update",
            time: new Date().toISOString()
        };
    }

    function formatLiveFeedTime(value) {
        const stamp = new Date(value);
        if (Number.isNaN(stamp.getTime())) {
            return "--";
        }

        return stamp.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" });
    }

    function getLiveFeedTone(item) {
        const text = `${item?.type ?? ""} ${item?.value ?? ""}`.toLowerCase();
        if (/fail|error|offline|denied|reject|timeout/.test(text)) {
            return "danger";
        }

        if (/online|connected|complete|success|added|bound|enabled|open/.test(text)) {
            return "signal";
        }

        if (/pending|unknown|retry|commission|binding|network|group|scene|acl|admin|command/.test(text)) {
            return "accent";
        }

        return "neutral";
    }

    function getLiveFeedSummary(item) {
        return humanizeToken(item?.value) || "Update";
    }

    function getLiveFeedPreview(item) {
        return item
            ? `${humanizeToken(item.type)} | ${item.device} | ${getLiveFeedSummary(item)}`
            : "No recent controller events";
    }

    function isFeedDrawerOpen() {
        const drawer = document.getElementById("shell-feed-drawer");
        return !!drawer && !drawer.hidden;
    }

    function clearLiveFeedToastTimer(id) {
        const timer = liveFeedToastTimers.get(id);
        if (timer) {
            clearTimeout(timer);
            liveFeedToastTimers.delete(id);
        }
    }

    function dismissLiveFeedToast(id) {
        clearLiveFeedToastTimer(id);
        liveFeedToasts = liveFeedToasts.filter(item => item.id !== id);
        renderLiveFeedToasts();
    }

    function clearAllLiveFeedToasts() {
        for (const toast of liveFeedToasts) {
            clearLiveFeedToastTimer(toast.id);
        }

        liveFeedToasts = [];
        renderLiveFeedToasts();
    }

    function queueLiveFeedToast(item) {
        if (isFeedDrawerOpen()) {
            return;
        }

        const toast = {
            ...item,
            id: `live-feed-toast-${++liveFeedToastSeed}`,
            tone: getLiveFeedTone(item)
        };

        liveFeedToasts.unshift(toast);
        while (liveFeedToasts.length > maxLiveFeedToasts) {
            const removed = liveFeedToasts.pop();
            if (removed) {
                clearLiveFeedToastTimer(removed.id);
            }
        }

        const timer = setTimeout(() => dismissLiveFeedToast(toast.id), liveFeedToastLifetimeMs);
        liveFeedToastTimers.set(toast.id, timer);
        renderLiveFeedToasts();
    }

    function setFeedDrawerOpen(open) {
        const drawer = document.getElementById("shell-feed-drawer");
        const backdrop = document.getElementById("shell-feed-backdrop");
        const toggle = document.getElementById("shell-feed-toggle");
        clearTimeout(feedDrawerHideTimer);

        if (toggle) {
            toggle.setAttribute("aria-expanded", open ? "true" : "false");
        }

        document.body.classList.toggle("shell-feed-open", open);

        if (open) {
            clearAllLiveFeedToasts();

            if (drawer) {
                drawer.hidden = false;
                drawer.setAttribute("aria-hidden", "false");
                requestAnimationFrame(() => drawer.classList.add("is-open"));
            }

            if (backdrop) {
                backdrop.hidden = false;
                requestAnimationFrame(() => backdrop.classList.add("is-open"));
            }

            return;
        }

        if (drawer) {
            drawer.setAttribute("aria-hidden", "true");
            drawer.classList.remove("is-open");
        }

        if (backdrop) {
            backdrop.classList.remove("is-open");
        }

        feedDrawerHideTimer = setTimeout(() => {
            if (drawer) {
                drawer.hidden = true;
            }

            if (backdrop) {
                backdrop.hidden = true;
            }
        }, feedDrawerTransitionMs);
    }

    function renderLiveFeedToasts() {
        const stack = document.getElementById("shell-live-feed-toasts");
        if (!stack) {
            return;
        }

        stack.replaceChildren();
        if (isFeedDrawerOpen()) {
            return;
        }

        for (const item of liveFeedToasts) {
            const toast = document.createElement("button");
            toast.type = "button";
            toast.className = `shell-feed-toast shell-feed-toast-${item.tone}`;
            toast.setAttribute("data-shell-toggle", "feed-drawer");

            const head = document.createElement("div");
            head.className = "shell-feed-toast-head";

            const type = document.createElement("span");
            type.className = "shell-feed-toast-type";
            type.textContent = humanizeToken(item.type);

            const time = document.createElement("time");
            time.className = "shell-feed-toast-time";
            time.textContent = formatLiveFeedTime(item.time);

            head.append(type, time);

            const summary = document.createElement("div");
            summary.className = "shell-feed-toast-summary";
            summary.textContent = getLiveFeedSummary(item);

            const device = document.createElement("div");
            device.className = "shell-feed-toast-device";
            device.textContent = item.device || "controller";

            toast.append(head, summary, device);
            stack.append(toast);
        }
    }

    function renderLiveFeed() {
        const latest = document.getElementById("shell-live-feed-latest");
        const drawerMeta = document.getElementById("shell-feed-drawer-meta");
        const empty = document.getElementById("shell-feed-empty");
        const list = document.getElementById("shell-feed-list");

        if (latest) {
            const newest = liveFeedItems[0];
            latest.textContent = getLiveFeedPreview(newest);
        }

        if (drawerMeta) {
            drawerMeta.textContent = liveFeedItems.length > 0
                ? `${liveFeedItems.length} recent controller events · newest first`
                : "Waiting for controller activity";
        }

        if (!list || !empty) {
            return;
        }

        list.replaceChildren();
        empty.hidden = liveFeedItems.length > 0;

        for (const item of liveFeedItems) {
            const article = document.createElement("article");
            article.className = `shell-feed-item shell-feed-item-${getLiveFeedTone(item)}`;

            const head = document.createElement("div");
            head.className = "shell-feed-item-head";

            const type = document.createElement("span");
            type.className = "shell-feed-item-type";
            type.textContent = humanizeToken(item.type);

            const time = document.createElement("time");
            time.className = "shell-feed-item-time";
            time.textContent = formatLiveFeedTime(item.time);

            head.append(type, time);

            const device = document.createElement("div");
            device.className = "shell-feed-item-device";
            device.textContent = item.device || "controller";

            const value = document.createElement("div");
            value.className = "shell-feed-item-value";
            value.textContent = getLiveFeedSummary(item);

            article.append(head, device, value);
            list.append(article);
        }
    }

    function recordLiveFeedMessage(raw) {
        liveFeedItems.unshift(normalizeLiveFeedEvent(raw));
        if (liveFeedItems.length > maxLiveFeedItems) {
            liveFeedItems = liveFeedItems.slice(0, maxLiveFeedItems);
        }

        renderLiveFeed();
        queueLiveFeedToast(liveFeedItems[0]);
    }

    function connectLiveFeed() {
        if (liveFeedSource) {
            return;
        }

        setLiveFeedStatus("neutral", "pending", "connecting");
        liveFeedSource = new EventSource("/ui/live/controller");

        liveFeedSource.onopen = () => {
            setLiveFeedStatus("online", "online", liveMessageCount > 0 ? `${liveMessageCount} msgs` : "stream open");
        };

        liveFeedSource.onmessage = event => {
            const message = event?.data ?? "";
            liveMessageCount += 1;
            const stamp = new Date().toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" });
            setLiveFeedStatus("online", "online", `${liveMessageCount} msgs · ${stamp}`);
            recordLiveFeedMessage(message);
        };

        liveFeedSource.onerror = () => {
            setLiveFeedStatus("error", "failed", "retrying");
        };
    }

    function initializeShell() {
        applyTheme();
        applySidebarState(localStorage.getItem(sidebarKey) === "collapsed");

        document.addEventListener("click", event => {
            const target = event.target instanceof Element ? event.target.closest("[data-shell-toggle]") : null;
            if (!target) {
                return;
            }

            const action = target.getAttribute("data-shell-toggle");
            if (action === "sidebar") {
                const frame = document.getElementById("shell-frame");
                const collapsed = frame?.classList.contains("shell-frame-collapsed") ?? false;
                applySidebarState(!collapsed);
            }
            else if (action === "feed-drawer") {
                setFeedDrawerOpen(!isFeedDrawerOpen());
            }
            else if (action === "feed-drawer-close") {
                setFeedDrawerOpen(false);
            }
        }, { passive: true });

        document.addEventListener("keydown", event => {
            if (event.key === "Escape" && isFeedDrawerOpen()) {
                setFeedDrawerOpen(false);
            }
        });

        renderLiveFeed();
        renderLiveFeedToasts();
        connectLiveFeed();
    }

    applyTheme();

    window.dotMatterShell = {
        getPreferences() {
            return {
                theme: "dark",
                sidebarCollapsed: localStorage.getItem(sidebarKey) === "collapsed"
            };
        },
        applyTheme,
        setSidebarCollapsed(collapsed) {
            applySidebarState(collapsed);
        }
    };

    window.addEventListener("DOMContentLoaded", initializeShell, { once: true });
})();

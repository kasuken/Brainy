/**
 * brainySearch.js
 * Lightweight helper for the GlobalSearchBar Blazor component.
 * Registers the Ctrl+K / Cmd+K shortcut and click-outside dismissal,
 * delegating back to the component via a DotNetObjectReference.
 */
window.brainySearch = {
    /** @type {DotNet.DotNetObject|null} */
    _ref: null,
    _registered: false,
    _usageKey: 'brainy-command-usage',
    _maxRecent: 8,

    /**
     * Called once by the GlobalSearchBar component after first render.
     * Stores the dotnet object reference and wires up global listeners.
     * @param {DotNet.DotNetObject} dotnetRef
     */
    init(dotnetRef) {
        this._ref = dotnetRef;

        if (this._registered) return;
        this._registered = true;

        // Ctrl+K / Cmd+K → open search bar
        document.addEventListener('keydown', (e) => {
            if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
                e.preventDefault();
                this._ref?.invokeMethodAsync('OpenFromKeyboardAsync');
            }
        });

        // Click outside the .gsb root → close the overlay
        document.addEventListener('mousedown', (e) => {
            const root = document.querySelector('.gsb');
            if (root && !root.contains(e.target)) {
                this._ref?.invokeMethodAsync('CloseFromOutsideAsync');
            }
        });
    },

    /**
     * Focus the given ElementReference (the search <input>).
     * @param {HTMLElement} el
     */
    focus(el) {
        if (el) el.focus();
    },

    /**
     * Reads the current user's per-browser command palette usage (issue #318):
     * how many times each command has run, and the most-recently-run ids first.
     * Stored client-side only — never sent to the server as a record — so a
     * cleared/private browser simply falls back to the palette's default order.
     * @returns {{counts: Object<string, number>, recentIds: string[]}}
     */
    getCommandUsage() {
        try {
            const raw = localStorage.getItem(this._usageKey);
            if (!raw) return { counts: {}, recentIds: [] };
            const parsed = JSON.parse(raw);
            return {
                counts: (parsed && typeof parsed.counts === 'object' && parsed.counts) || {},
                recentIds: (parsed && Array.isArray(parsed.recentIds)) ? parsed.recentIds : []
            };
        } catch {
            return { counts: {}, recentIds: [] };
        }
    },

    /**
     * Records that a palette command just ran, bumping its frequency count and
     * moving it to the front of the recent list.
     * @param {string} commandId
     */
    recordCommandUse(commandId) {
        try {
            const usage = this.getCommandUsage();
            usage.counts[commandId] = (usage.counts[commandId] || 0) + 1;
            usage.recentIds = [commandId, ...usage.recentIds.filter((id) => id !== commandId)]
                .slice(0, this._maxRecent);
            localStorage.setItem(this._usageKey, JSON.stringify(usage));
        } catch {
            // localStorage unavailable (private browsing, disabled storage, quota) —
            // the palette just falls back to its default ranking next time it opens.
        }
    },

    /**
     * Call before the component is disposed to prevent stale callbacks.
     */
    dispose() {
        this._ref = null;
    }
};

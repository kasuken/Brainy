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
     * Call before the component is disposed to prevent stale callbacks.
     */
    dispose() {
        this._ref = null;
    }
};

// Lightweight localStorage helper used by the page help wizard to remember
// whether the user has already seen the onboarding guide for a given screen.
window.brainyHelp = {
    _prefix: 'brainy.help.',

    hasSeen(key) {
        try {
            return localStorage.getItem(this._prefix + key) === '1';
        } catch {
            // If storage is blocked, treat the guide as seen so we never nag the user.
            return true;
        }
    },

    markSeen(key) {
        try {
            localStorage.setItem(this._prefix + key, '1');
        } catch {
            // Ignore: auto-show is a non-critical convenience.
        }
    },

    reset(key) {
        try {
            localStorage.removeItem(this._prefix + key);
        } catch {
            // Ignore.
        }
    }
};

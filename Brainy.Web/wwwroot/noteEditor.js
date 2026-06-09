window.brainyNoteEditor = {
    /** Insert markdown wrapping around the current selection in a textarea. Returns the new full value. */
    insertMarkdown(elementId, prefix, suffix, placeholder) {
        const textarea = document.getElementById(elementId);
        if (!textarea) return null;

        const start = textarea.selectionStart;
        const end = textarea.selectionEnd;
        const selected = textarea.value.substring(start, end) || placeholder;
        const before = textarea.value.substring(0, start);
        const after = textarea.value.substring(end);

        const newValue = before + prefix + selected + suffix + after;
        textarea.value = newValue;

        const newStart = start + prefix.length;
        const newEnd = newStart + selected.length;
        textarea.setSelectionRange(newStart, newEnd);
        textarea.focus();

        return newValue;
    },

    _beforeunloadHandler: null,

    addBeforeunloadWarning() {
        if (this._beforeunloadHandler) return;
        this._beforeunloadHandler = (e) => {
            e.preventDefault();
            e.returnValue = '';
        };
        window.addEventListener('beforeunload', this._beforeunloadHandler);
    },

    removeBeforeunloadWarning() {
        if (this._beforeunloadHandler) {
            window.removeEventListener('beforeunload', this._beforeunloadHandler);
            this._beforeunloadHandler = null;
        }
    }
};

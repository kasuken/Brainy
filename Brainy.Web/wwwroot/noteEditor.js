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

    /** Insert arbitrary text at the current caret position in a textarea. Returns the new full value. */
    insertTextAtCursor(elementId, text) {
        const textarea = document.getElementById(elementId);
        if (!textarea) return null;

        const start = textarea.selectionStart;
        const end = textarea.selectionEnd;
        const before = textarea.value.substring(0, start);
        const after = textarea.value.substring(end);

        const newValue = before + text + after;
        textarea.value = newValue;

        const pos = start + text.length;
        textarea.setSelectionRange(pos, pos);
        textarea.focus();

        return newValue;
    },

    // ── Clipboard image paste ──────────────────────────────────────────────
    _pendingImage: null,

    /** Attach a paste handler that captures clipboard images and notifies .NET. */
    registerPasteHandler(elementId, dotNetRef) {
        const textarea = document.getElementById(elementId);
        if (!textarea) return;

        // Remove any previous handler before attaching a fresh one.
        if (textarea._brainyPasteHandler) {
            textarea.removeEventListener('paste', textarea._brainyPasteHandler);
        }

        const handler = async (e) => {
            const items = e.clipboardData && e.clipboardData.items;
            if (!items) return;

            for (const item of items) {
                if (item.kind === 'file' && item.type && item.type.indexOf('image/') === 0) {
                    e.preventDefault();
                    const file = item.getAsFile();
                    if (!file) continue;

                    const buffer = await file.arrayBuffer();
                    // Resolve a reliable content type: the file's type, then the item's
                    // type, then infer from the file name, defaulting to PNG.
                    let contentType = file.type || item.type || '';
                    let fileName = file.name || '';
                    if (!contentType) {
                        const ext = (fileName.split('.').pop() || '').toLowerCase();
                        const map = {
                            png: 'image/png', jpg: 'image/jpeg', jpeg: 'image/jpeg',
                            gif: 'image/gif', webp: 'image/webp', bmp: 'image/bmp'
                        };
                        contentType = map[ext] || 'image/png';
                    }
                    if (!fileName) {
                        const ext = contentType.split('/')[1] || 'png';
                        fileName = 'pasted-image.' + ext;
                    }

                    this._pendingImage = {
                        bytes: new Uint8Array(buffer),
                        contentType: contentType,
                        fileName: fileName
                    };

                    await dotNetRef.invokeMethodAsync(
                        'OnImagePastedAsync',
                        contentType,
                        fileName);
                    break;
                }
            }
        };

        textarea._brainyPasteHandler = handler;
        textarea.addEventListener('paste', handler);
    },

    /** Detach the paste handler from a textarea. */
    unregisterPasteHandler(elementId) {
        const textarea = document.getElementById(elementId);
        if (textarea && textarea._brainyPasteHandler) {
            textarea.removeEventListener('paste', textarea._brainyPasteHandler);
            textarea._brainyPasteHandler = null;
        }
        this._pendingImage = null;
    },

    /** Return the bytes of the last pasted image as a stream reference for .NET. */
    getPastedImageBytes() {
        return this._pendingImage ? this._pendingImage.bytes : null;
    },

    /** Clear the buffered pasted image once .NET has consumed it. */
    clearPastedImage() {
        this._pendingImage = null;
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

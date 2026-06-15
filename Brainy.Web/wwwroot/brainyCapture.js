/**
 * brainyCapture.js
 * Helper for the frictionless Quick Capture dialog.
 * Provides autofocus, Enter-to-save keyboard handling, clipboard image paste,
 * and whole-surface drag-and-drop, delegating back to the Blazor component.
 *
 * Pasted and dropped files are routed into the hidden Blazor <InputFile> so they
 * stream over the existing upload channel instead of crossing the JS interop
 * boundary as a single (size-limited) message.
 */
window.brainyCapture = {
    /** @type {DotNet.DotNetObject|null} */
    _ref: null,
    _textarea: null,
    _root: null,
    _fileInputId: null,
    _onKeyDown: null,
    _onPaste: null,
    _onDragOver: null,
    _onDragLeave: null,
    _onDrop: null,
    _recognition: null,
    _listening: false,
    _focusBeforeNextResult: false,

    /**
     * Wires up the capture surface.
     * @param {DotNet.DotNetObject} dotnetRef
     * @param {HTMLTextAreaElement} textarea
     * @param {HTMLElement} root
     * @param {string} fileInputId id of the hidden Blazor InputFile element
     */
    init(dotnetRef, textarea, root, fileInputId) {
        this._ref = dotnetRef;
        this._textarea = textarea;
        this._root = root;
        this._fileInputId = fileInputId;

        if (textarea) {
            // Focus so the user can start typing immediately.
            setTimeout(() => textarea.focus(), 0);

            // Enter saves; Shift+Enter inserts a newline; Ctrl/Cmd+Enter also saves.
            this._onKeyDown = (e) => {
                if (e.key !== 'Enter') return;
                if (e.shiftKey) return; // allow newline
                e.preventDefault();
                this._ref?.invokeMethodAsync('SaveFromKeyboardAsync');
            };
            textarea.addEventListener('keydown', this._onKeyDown);

            // Paste a screenshot or copied image straight into the capture.
            this._onPaste = (e) => {
                const items = e.clipboardData && e.clipboardData.items;
                if (!items) return;
                for (const item of items) {
                    if (item.type && item.type.indexOf('image/') === 0) {
                        const file = item.getAsFile();
                        if (file) {
                            this._routeFile(file);
                            e.preventDefault();
                        }
                        return;
                    }
                }
            };
            textarea.addEventListener('paste', this._onPaste);
        }

        if (root) {
            this._onDragOver = (e) => {
                e.preventDefault();
                root.classList.add('qcd__body--drag');
            };
            this._onDragLeave = (e) => {
                if (!root.contains(e.relatedTarget)) {
                    root.classList.remove('qcd__body--drag');
                }
            };
            this._onDrop = (e) => {
                e.preventDefault();
                root.classList.remove('qcd__body--drag');
                const files = e.dataTransfer && e.dataTransfer.files;
                if (files && files.length) {
                    this._routeFile(files[0]);
                }
            };
            root.addEventListener('dragover', this._onDragOver);
            root.addEventListener('dragleave', this._onDragLeave);
            root.addEventListener('drop', this._onDrop);
        }
    },

    /**
     * Pushes a File into the hidden Blazor InputFile and notifies it via a change
     * event so Blazor streams the bytes through its normal pipeline.
     * @param {File} file
     */
    _routeFile(file) {
        const input = document.getElementById(this._fileInputId);
        if (!input) return;

        // Clipboard screenshots often arrive as the generic "image.png"; give them a
        // unique, friendlier name so saved notes are easy to tell apart.
        let toAdd = file;
        if (!file.name || file.name === 'image.png') {
            toAdd = new File([file], 'screenshot-' + Date.now() + '.png', { type: file.type || 'image/png' });
        }

        const dt = new DataTransfer();
        dt.items.add(toAdd);
        input.files = dt.files;
        input.dispatchEvent(new Event('change', { bubbles: true }));
    },

    /** True when the browser exposes the Web Speech API. */
    isSpeechSupported() {
        return !!(window.SpeechRecognition || window.webkitSpeechRecognition);
    },

    /**
     * Starts voice dictation. Final transcript segments are pushed back to the
     * component via OnSpeechResultAsync. Returns false when unsupported/already running.
     * @param {string|null} lang BCP-47 language tag, or null to use the browser default
     */
    startDictation(lang) {
        if (this._listening) return false;
        const Ctor = window.SpeechRecognition || window.webkitSpeechRecognition;
        if (!Ctor) return false;

        this._focusBeforeNextResult = false;

        const rec = new Ctor();
        rec.lang = lang || navigator.language || 'en-US';
        rec.continuous = true;
        rec.interimResults = false;

        rec.onresult = (e) => {
            let finalText = '';
            for (let i = e.resultIndex; i < e.results.length; i++) {
                if (e.results[i].isFinal) {
                    finalText += e.results[i][0].transcript;
                }
            }
            finalText = finalText.trim();
            if (finalText) {
                if (this._focusBeforeNextResult) {
                    this.focusTextInput();
                    this._focusBeforeNextResult = false;
                }
                this._ref?.invokeMethodAsync('OnSpeechResultAsync', finalText);
            }
        };
        rec.onerror = (e) => {
            this._listening = false;
            this._ref?.invokeMethodAsync('OnSpeechErrorAsync', (e && e.error) || 'error');
        };
        rec.onend = () => {
            this._listening = false;
            this._focusBeforeNextResult = false;
            this._ref?.invokeMethodAsync('OnSpeechEndAsync');
        };

        this._recognition = rec;
        this._listening = true;
        try {
            rec.start();
        } catch {
            this._listening = false;
            return false;
        }
        return true;
    },

    /** Returns focus to the capture textarea. */
    focusTextInput() {
        if (this._textarea) {
            this._textarea.focus();
        }
    },

    /** Stops an in-progress dictation, if any. */
    stopDictation() {
        if (this._recognition && this._listening) {
            // Browsers may deliver one final result after stop(); ensure focus is
            // restored first so the transcript lands back in the capture field.
            this._focusBeforeNextResult = true;
            this.focusTextInput();
            try { this._recognition.stop(); } catch { /* already stopped */ }
        }
    },

    /** Removes listeners before the component is disposed. */
    dispose() {
        this.stopDictation();
        this._recognition = null;
        this._listening = false;
        this._focusBeforeNextResult = false;
        if (this._textarea) {
            if (this._onKeyDown) this._textarea.removeEventListener('keydown', this._onKeyDown);
            if (this._onPaste) this._textarea.removeEventListener('paste', this._onPaste);
        }
        if (this._root) {
            if (this._onDragOver) this._root.removeEventListener('dragover', this._onDragOver);
            if (this._onDragLeave) this._root.removeEventListener('dragleave', this._onDragLeave);
            if (this._onDrop) this._root.removeEventListener('drop', this._onDrop);
        }
        this._ref = null;
        this._textarea = null;
        this._root = null;
        this._fileInputId = null;
        this._onKeyDown = null;
        this._onPaste = null;
        this._onDragOver = null;
        this._onDragLeave = null;
        this._onDrop = null;
    }
};

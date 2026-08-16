// BlogIt Admin — JS Interop
window.blogitInterop = {
    initEditor: function (elementId, dotNetRef) {
        const el = document.getElementById(elementId);
        if (!el) return;
        // Avoid double-init
        if (el._easyMDE) return;
        const easyMDE = new EasyMDE({
            element: el,
            spellChecker: false,
            autosave: { enabled: false },
            toolbar: [
                'bold', 'italic', 'heading', '|',
                'quote', 'unordered-list', 'ordered-list', '|',
                'link', 'image', '|',
                'preview', 'side-by-side', 'fullscreen', '|',
                'guide'
            ]
        });
        easyMDE.codemirror.on('change', function () {
            dotNetRef.invokeMethodAsync('OnContentChanged', easyMDE.value());
        });
        el._easyMDE = easyMDE;
    },

    getEditorValue: function (elementId) {
        const el = document.getElementById(elementId);
        return el?._easyMDE?.value() ?? '';
    },

    setEditorValue: function (elementId, value) {
        const el = document.getElementById(elementId);
        if (el?._easyMDE) el._easyMDE.value(value || '');
    },

    destroyEditor: function (elementId) {
        const el = document.getElementById(elementId);
        if (el?._easyMDE) {
            el._easyMDE.toTextArea();
            el._easyMDE = null;
        }
    },

    // Focus bookkeeping for ModalDialog.razor. It lives here rather than in the component because
    // both halves need the document's list of focusable elements, and Blazor has no way to
    // enumerate that: ElementReference can focus one element it already holds, not discover which
    // descendants are focusable or read document.activeElement.
    //
    // The native <dialog> element with showModal() would give the trap, the Escape handling and the
    // focus restore for free, and it was the first choice. It was dropped because it moves the
    // backdrop into ::backdrop and takes over positioning, which means rewriting .modal-overlay and
    // .modal — a visual change to three dialogs, on a finding that is supposed to be about
    // semantics. Worth revisiting as its own change.
    modal: {
        // Only ever one dialog is open at a time in this app: the pickers are opened from an editor
        // and the confirmations from a list, and neither opens another on top. A single slot rather
        // than a stack, so a leaked open() cannot quietly strand an entry.
        _previous: null,
        _container: null,
        _onKeyDown: null,

        // [tabindex="-1"] is excluded deliberately: that is how the dialog itself is made
        // programmatically focusable, and it must not become a Tab stop inside its own trap.
        _selector: 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',

        open: function (element) {
            if (!element) return;
            // Release first: a second open() without a close() would otherwise lose the original
            // return target and leave the previous listener attached.
            this.close();

            this._previous = document.activeElement;
            this._container = element;
            this._onKeyDown = (e) => {
                if (e.key !== 'Tab') return;

                const focusable = Array.from(element.querySelectorAll(this._selector))
                    // Queried live on each Tab: the media picker swaps its whole body between the
                    // browse and upload tabs, so a list captured at open() would go stale.
                    .filter(el => el.offsetParent !== null || el === document.activeElement);
                if (focusable.length === 0) {
                    // Nothing to cycle through, but the caret still must not escape to the page
                    // behind the overlay.
                    e.preventDefault();
                    return;
                }

                const first = focusable[0];
                const last = focusable[focusable.length - 1];
                // Wrapping is only needed at the two ends. Anywhere else the browser's own order is
                // already correct and preventing the default would break it.
                if (e.shiftKey && (document.activeElement === first || document.activeElement === element)) {
                    e.preventDefault();
                    last.focus();
                } else if (!e.shiftKey && document.activeElement === last) {
                    e.preventDefault();
                    first.focus();
                }
            };
            element.addEventListener('keydown', this._onKeyDown);
        },

        close: function () {
            if (this._container && this._onKeyDown) {
                this._container.removeEventListener('keydown', this._onKeyDown);
            }
            this._container = null;
            this._onKeyDown = null;

            // Guarded on isConnected: the element that had focus can have been removed while the
            // dialog was open — deleting the selected media file is exactly that — and focusing a
            // detached node silently sends focus to <body>, losing the user's place either way.
            const previous = this._previous;
            this._previous = null;
            if (previous && previous.isConnected && typeof previous.focus === 'function') {
                previous.focus();
            }
        }
    },

    // Rejects when the copy did not happen, so the caller can offer the URL to copy by hand
    // instead of claiming success. navigator.clipboard is undefined outside a secure context,
    // and even where it exists writeText can be denied by permissions policy.
    copyToClipboard: function (text) {
        if (navigator.clipboard) {
            return navigator.clipboard.writeText(text);
        }

        const ta = document.createElement('textarea');
        ta.value = text;
        // Kept off-screen rather than appended as-is: a visible textarea appearing and vanishing
        // reads as a glitch, and scrolling to a focused element would jump the page.
        ta.setAttribute('readonly', '');
        ta.style.position = 'fixed';
        ta.style.top = '-1000px';
        document.body.appendChild(ta);
        ta.select();
        try {
            if (!document.execCommand('copy')) {
                throw new Error('Clipboard write was refused.');
            }
        } finally {
            document.body.removeChild(ta);
        }
    }
};

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

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

    copyToClipboard: function (text) {
        if (navigator.clipboard) {
            return navigator.clipboard.writeText(text);
        }
        // Fallback
        const ta = document.createElement('textarea');
        ta.value = text;
        document.body.appendChild(ta);
        ta.select();
        document.execCommand('copy');
        document.body.removeChild(ta);
    }
};

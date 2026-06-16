// Small JS interop helpers for KotoNeko.
window.kotoneko = {
    // Focus an element by reference (used to keep focus on the answer box).
    focus: function (element) {
        if (element && typeof element.focus === "function") {
            element.focus();
        }
    },

    // Focus and select all text in an element.
    focusSelect: function (element) {
        if (element && typeof element.focus === "function") {
            element.focus();
            if (typeof element.select === "function") {
                element.select();
            }
        }
    }
};

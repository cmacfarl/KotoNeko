// Small JS interop helpers for KotoNeko.
window.kotoneko = window.kotoneko || {};

(function (k) {
    "use strict";

    // ---------------------------------------------------------------------
    // Romaji -> hiragana conversion (client-side mirror of
    // KotoNeko.Core.Text.RomajiConverter). Running this in the browser is what
    // makes the kana inputs reliable: the input's displayed value is converted
    // synchronously on each keystroke and never round-trips to the server, so a
    // late server "value" patch can no longer race the user's typing and drop /
    // duplicate characters. The server stays the source of truth for *grading*
    // (it re-runs the C# converter, which is idempotent on kana).
    // ---------------------------------------------------------------------
    const VOWELS = "aeiou";

    // Longest-first lookup of romaji token -> hiragana. Keep this in sync with
    // RomajiConverter.BuildMap() in C#.
    const MAP = {
        // Vowels
        "a": "あ", "i": "い", "u": "う", "e": "え", "o": "お",
        // K
        "ka": "か", "ki": "き", "ku": "く", "ke": "け", "ko": "こ",
        "kya": "きゃ", "kyu": "きゅ", "kyo": "きょ",
        // G
        "ga": "が", "gi": "ぎ", "gu": "ぐ", "ge": "げ", "go": "ご",
        "gya": "ぎゃ", "gyu": "ぎゅ", "gyo": "ぎょ",
        // S
        "sa": "さ", "shi": "し", "si": "し", "su": "す", "se": "せ", "so": "そ",
        "sha": "しゃ", "shu": "しゅ", "sho": "しょ",
        "sya": "しゃ", "syu": "しゅ", "syo": "しょ",
        // Z / J
        "za": "ざ", "ji": "じ", "zi": "じ", "zu": "ず", "ze": "ぜ", "zo": "ぞ",
        "ja": "じゃ", "ju": "じゅ", "jo": "じょ",
        "jya": "じゃ", "jyu": "じゅ", "jyo": "じょ",
        // T
        "ta": "た", "chi": "ち", "ti": "ち", "tsu": "つ", "tu": "つ", "te": "て", "to": "と",
        "cha": "ちゃ", "chu": "ちゅ", "cho": "ちょ",
        "tya": "ちゃ", "tyu": "ちゅ", "tyo": "ちょ",
        // D
        "da": "だ", "di": "ぢ", "du": "づ", "de": "で", "do": "ど",
        "dya": "ぢゃ", "dyu": "ぢゅ", "dyo": "ぢょ",
        // N
        "na": "な", "ni": "に", "nu": "ぬ", "ne": "ね", "no": "の",
        "nya": "にゃ", "nyu": "にゅ", "nyo": "にょ",
        // H / F
        "ha": "は", "hi": "ひ", "fu": "ふ", "hu": "ふ", "he": "へ", "ho": "ほ",
        "hya": "ひゃ", "hyu": "ひゅ", "hyo": "ひょ",
        "fa": "ふぁ", "fi": "ふぃ", "fe": "ふぇ", "fo": "ふぉ",
        // B
        "ba": "ば", "bi": "び", "bu": "ぶ", "be": "べ", "bo": "ぼ",
        "bya": "びゃ", "byu": "びゅ", "byo": "びょ",
        // P
        "pa": "ぱ", "pi": "ぴ", "pu": "ぷ", "pe": "ぺ", "po": "ぽ",
        "pya": "ぴゃ", "pyu": "ぴゅ", "pyo": "ぴょ",
        // M
        "ma": "ま", "mi": "み", "mu": "む", "me": "め", "mo": "も",
        "mya": "みゃ", "myu": "みゅ", "myo": "みょ",
        // Y
        "ya": "や", "yu": "ゆ", "yo": "よ",
        // R
        "ra": "ら", "ri": "り", "ru": "る", "re": "れ", "ro": "ろ",
        "rya": "りゃ", "ryu": "りゅ", "ryo": "りょ",
        // W
        "wa": "わ", "wo": "を", "wi": "うぃ", "we": "うぇ",
        // V
        "va": "ゔぁ", "vi": "ゔぃ", "vu": "ゔ", "ve": "ゔぇ", "vo": "ゔぉ",
        // Small kana via x/l prefix
        "xa": "ぁ", "xi": "ぃ", "xu": "ぅ", "xe": "ぇ", "xo": "ぉ",
        "la": "ぁ", "li": "ぃ", "lu": "ぅ", "le": "ぇ", "lo": "ぉ",
        "xya": "ゃ", "xyu": "ゅ", "xyo": "ょ",
        "xtsu": "っ", "xtu": "っ", "ltu": "っ",
        "-": "ー"
    };

    const KEYS = Object.keys(MAP);
    let maxKeyLength = 0;
    for (const key of KEYS) {
        if (key.length > maxKeyLength) {
            maxKeyLength = key.length;
        }
    }

    function isLatinLetter(c) {
        return c >= "a" && c <= "z";
    }

    function isVowel(c) {
        return c.length === 1 && VOWELS.indexOf(c) >= 0;
    }

    // 'n' is syllabic ん when followed by a consonant (not y), another 'n', an
    // apostrophe, or the end of input. Returns characters consumed, or 0 if the
    // 'n' is part of na/ni/.../nya and should fall through to token matching.
    function tryConsumeN(s, i) {
        const next = i + 1 < s.length ? s[i + 1] : "";

        if (next === "") {
            return 0;
        }
        
        if (next === "'") {
            return 2;
        }
        
        if (next === "n") {
            const after = i + 2 < s.length ? s[i + 2] : "";
            const afterFormsSyllable = isVowel(after) || after === "y";
            return afterFormsSyllable ? 1 : 2;
        }
        
        if (isLatinLetter(next) && !isVowel(next) && next !== "y") {
            return 1;
        }
        
        return 0;
    }

    function tryMatchToken(s, start) {
        const max = Math.min(maxKeyLength, s.length - start);
        for (let len = max; len >= 1; len--) {
            const candidate = s.substring(start, start + len);
            if (Object.prototype.hasOwnProperty.call(MAP, candidate)) {
                return { kana: MAP[candidate], consumed: len };
            }
        }
        return null;
    }

    function isPrefixOfSomeKey(s, start) {
        const tail = s.substring(start);
        for (const key of KEYS) {
            if (key.length > tail.length && key.startsWith(tail)) {
                return true;
            }
        }
        return false;
    }

    function toHiragana(input) {
        if (!input) {
            return input || "";
        }

        const lower = input.toLowerCase();
        let result = "";
        let i = 0;

        while (i < lower.length) {
            const c = lower[i];

            // Pass through anything that is not a latin letter (kana, spaces, etc.).
            if (!isLatinLetter(c)) {
                result += input[i];
                i++;
                continue;
            }

            // Syllabic ん.
            if (c === "n") {
                const nConsumed = tryConsumeN(lower, i);
                if (nConsumed > 0) {
                    result += "ん";
                    i += nConsumed;
                    continue;
                }
            }

            // Sokuon (small っ) from a doubled consonant, e.g. "kk" -> っk.
            if (c !== "n" && !isVowel(c) && i + 1 < lower.length && lower[i + 1] === c) {
                result += "っ";
                i++;
                continue;
            }

            // Longest token match.
            const m = tryMatchToken(lower, i);
            if (m) {
                result += m.kana;
                i += m.consumed;
                continue;
            }

            // Tail could still become a valid kana: leave it as romaji for now.
            if (isPrefixOfSomeKey(lower, i)) {
                result += input.substring(i);
                break;
            }

            // Nothing matched: emit verbatim.
            result += input[i];
            i++;
        }

        return result;
    }

    k.romaji = { toHiragana: toHiragana };

    // ---------------------------------------------------------------------
    // Kana input wiring. attach() owns the element's value during typing.
    //   options.interceptKeys  - if true, swallow Tab/Enter and forward them to
    //                            the component via OnInterceptKey(key) instead of
    //                            letting them move focus / submit.
    // The element may carry data-kn-convert="false" to disable conversion
    // dynamically (used by the quiz, where some questions are English).
    // ---------------------------------------------------------------------
    k.kanaInput = {
        attach: function (element, dotNetRef, options) {
            if (!element || element._kotonekoKana) {
                return;
            }
            options = options || {};
            const interceptKeys = !!options.interceptKeys;

            const onInput = function () {
                const raw = element.value;
                const convert = element.dataset.knConvert !== "false";
                const converted = convert ? toHiragana(raw) : raw;
                if (converted !== raw) {
                    element.value = converted;
                    // Keep the caret at the end (romaji typing is left-to-right).
                    const len = converted.length;
                    try { element.setSelectionRange(len, len); } catch (e) { /* ignore */ }
                }
                dotNetRef.invokeMethodAsync("OnKanaInput", converted);
            };
            element.addEventListener("input", onInput);

            let onKeyDown = null;
            if (interceptKeys) {
                onKeyDown = function (e) {
                    if (e.key === "Tab" || e.key === "Enter") {
                        e.preventDefault();
                        dotNetRef.invokeMethodAsync("OnInterceptKey", e.key);
                    }
                };
                element.addEventListener("keydown", onKeyDown);
            }

            element._kotonekoKana = { onInput: onInput, onKeyDown: onKeyDown };
        },

        // Push a value into the element from the server side (initial load,
        // jisho fill, kana-only prefill). Does not fire an input event.
        setValue: function (element, value) {
            if (!element) {
                return;
            }
            element.value = value || "";
        },

        detach: function (element) {
            if (!element || !element._kotonekoKana) {
                return;
            }
            element.removeEventListener("input", element._kotonekoKana.onInput);
            if (element._kotonekoKana.onKeyDown) {
                element.removeEventListener("keydown", element._kotonekoKana.onKeyDown);
            }
            element._kotonekoKana = null;
        }
    };

    // Focus an element by reference (used to keep focus on the answer box).
    k.focus = function (element) {
        if (element && typeof element.focus === "function") {
            element.focus();
        }
    };

    // Focus and select all text in an element.
    k.focusSelect = function (element) {
        if (element && typeof element.focus === "function") {
            element.focus();
            if (typeof element.select === "function") {
                element.select();
            }
        }
    };

    // Focus (and select) an element found by id.
    k.focusById = function (id) {
        const el = document.getElementById(id);
        if (el && typeof el.focus === "function") {
            el.focus();
            if (typeof el.select === "function") {
                el.select();
            }
        }
    };
})(window.kotoneko);

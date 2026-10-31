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
    //   options.replayKey      - if true, Tab is swallowed and forwarded to the
    //                            component via OnReplayKey() whenever the element
    //                            carries data-kn-replay="true" (listening questions).
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
            const replayKey = !!options.replayKey;

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
            if (interceptKeys || replayKey) {
                onKeyDown = function (e) {
                    if (replayKey && e.key === "Tab" && element.dataset.knReplay === "true") {
                        e.preventDefault();
                        dotNetRef.invokeMethodAsync("OnReplayKey");
                        return;
                    }
                    if (interceptKeys && (e.key === "Tab" || e.key === "Enter")) {
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

    // ---------------------------------------------------------------------
    // Japanese input: a small in-page IME, so the Windows IME can stay off.
    // Romaji becomes kana as it is typed (the "composition", always at the end
    // of the text). Space converts the composition with the server's offline
    // dictionary into segments, each with kanji candidates (shown nine at a
    // time, the page following the selection; segments and candidates can also
    // be clicked):
    //   Space / ↓ next candidate, ↑ previous, 1–9 pick, ← / → change segment,
    //   Shift+← / → shrink or grow the segment, F7 katakana, F6 hiragana,
    //   Enter commit, Esc back to kana (or drop the kana), Backspace back to kana.
    // Enter with nothing being composed submits. All state lives here; the
    // component only hears OnImeChanged(text, kana) and OnImeSubmit().
    // options.learn (default true): remember spellings the user picks, and put
    // past picks first. Off where kanji choice is being tested (dictation).
    // options.replay: Tab, with nothing being composed, calls OnImeReplay().
    // setLocked(input, true) ignores typing (e.g. while a result is shown); Enter
    // still submits and Tab still replays.
    // ---------------------------------------------------------------------
    function hiraganaToKatakana(s) {
        return s.replace(/[ぁ-ゖ]/g, c => String.fromCharCode(c.charCodeAt(0) + 0x60));
    }

    function katakanaToHiragana(s) {
        return s.replace(/[ァ-ヶ]/g, c => String.fromCharCode(c.charCodeAt(0) - 0x60));
    }

    function isAllKana(s) {
        return /^[ぁ-ゖァ-ヶー]+$/.test(s);
    }

    async function postJson(url, body) {
        const response = await fetch(url, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(body)
        });
        if (!response.ok) {
            throw new Error("Conversion failed: " + response.status);
        }
        return response.status === 204 ? null : response.json();
    }

    k.japaneseInput = {
        attach: function (input, strip, dotNetRef, options) {
            if (!input || input._kotonekoIme) {
                return;
            }
            const learn = !(options && options.learn === false);
            const replay = !!(options && options.replay);

            const s = {
                committed: "",   // text before the composition
                chunks: [],      // committed pieces: { text, kana } (kana null if unknown)
                comp: "",        // kana being composed (may end in unfinished romaji)
                segs: null,      // while converting: [{ reading, candidates, index }]
                focus: 0,        // segment being edited
                request: 0,      // latest conversion request, to ignore stale replies
                osIme: false,    // the Windows IME was seen composing
                locked: false    // typing is ignored (see setLocked)
            };

            // Candidates show a page at a time; the page holds the selected one.
            const PageSize = 9;
            const pageOf = g => Math.floor(g.index / PageSize);

            const display = () => s.segs
                ? s.segs.map(g => g.candidates[g.index]).join("")
                : s.comp;

            function committedKana() {
                return s.chunks.every(c => c.kana !== null) ? s.chunks.map(c => c.kana).join("") : null;
            }

            function notify() {
                dotNetRef.invokeMethodAsync("OnImeChanged", s.committed, committedKana());
            }

            function render() {
                input.value = s.committed + display();
                const end = input.value.length;
                try { input.setSelectionRange(end, end); } catch (e) { /* ignore */ }
                renderStrip();
            }

            function renderStrip() {
                strip.textContent = "";
                if (s.osIme) {
                    const warn = document.createElement("div");
                    warn.className = "kn-ime-warning";
                    warn.textContent = "Turn the Windows IME off — KotoNeko converts for you.";
                    strip.appendChild(warn);
                }
                if (s.segs) {
                    const row = document.createElement("div");
                    row.className = "kn-ime-segments";
                    s.segs.forEach((g, i) => {
                        const span = document.createElement("span");
                        span.className = "kn-ime-seg" + (i === s.focus ? " active" : "");
                        span.textContent = g.candidates[g.index];
                        span.title = "Click to change this word";
                        // mousedown, so the input keeps its focus.
                        span.addEventListener("mousedown", e => { e.preventDefault(); s.focus = i; render(); });
                        row.appendChild(span);
                    });
                    strip.appendChild(row);

                    const focused = s.segs[s.focus];
                    const page = pageOf(focused);
                    const list = document.createElement("ol");
                    list.className = "kn-ime-candidates";
                    focused.candidates.slice(page * PageSize, page * PageSize + PageSize).forEach((c, i) => {
                        const index = page * PageSize + i;
                        const li = document.createElement("li");
                        li.className = index === focused.index ? "active" : "";
                        const n = document.createElement("span");
                        n.className = "kn-ime-num";
                        n.textContent = String(i + 1);
                        li.appendChild(n);
                        li.appendChild(document.createTextNode(c));
                        li.addEventListener("mousedown", e => { e.preventDefault(); focused.index = index; render(); });
                        list.appendChild(li);
                    });
                    strip.appendChild(list);

                    const hint = document.createElement("div");
                    hint.className = "kn-ime-hint";
                    const count = focused.candidates.length > PageSize
                        ? `${focused.index + 1} of ${focused.candidates.length} · ` : "";
                    hint.textContent = count + "Space next spelling"
                        + (s.segs.length > 1 ? " · ← → other word" : "")
                        + " · Shift+← → shorter/longer word · Enter done";
                    strip.appendChild(hint);
                } else if (s.comp) {
                    const hint = document.createElement("div");
                    hint.className = "kn-ime-hint";
                    hint.textContent = "Space converts · Enter keeps the kana";
                    strip.appendChild(hint);
                }
            }

            // Finish any trailing romaji: a final n is ん.
            function finishKana(text) {
                return toHiragana(text.endsWith("n") ? text.slice(0, -1) + "nn" : text);
            }

            function commitChunk(text, kana) {
                if (text.length === 0) {
                    return;
                }
                s.chunks.push({ text: text, kana: kana });
                s.committed += text;
            }

            // Remember the segments where the user picked something other than the
            // default; accepting a default teaches nothing.
            function rememberChoices() {
                const picked = s.segs
                    .filter(g => g.index !== 0)
                    .map(g => ({ reading: g.reading, written: g.candidates[g.index] }));
                if (learn && picked.length > 0) {
                    postJson("/api/ime/learn", { choices: picked }).catch(e => console.warn(e));
                }
            }

            function commit() {
                if (s.segs) {
                    rememberChoices();
                    commitChunk(display(), s.segs.map(g => g.reading).join(""));
                } else if (s.comp) {
                    const kana = finishKana(s.comp);
                    commitChunk(kana, isAllKana(kana) ? kana : null);
                }
                s.comp = "";
                s.segs = null;
                s.focus = 0;
                render();
                notify();
            }

            async function convert() {
                const kana = finishKana(s.comp);
                s.comp = kana;
                const id = ++s.request;
                try {
                    const segs = await postJson("/api/ime/convert", { kana: kana, learn: learn });
                    if (id !== s.request || segs.length === 0) {
                        return;
                    }
                    s.segs = segs.map(g => ({ reading: g.reading, candidates: g.candidates, index: 0 }));
                    s.focus = 0;
                } catch (e) {
                    console.warn(e);
                }
                render();
            }

            // Move the boundary after the focused segment by `delta` kana.
            async function resize(delta) {
                const rest = s.segs.slice(s.focus).map(g => g.reading).join("");
                const length = s.segs[s.focus].reading.length + delta;
                if (length < 1 || length > rest.length) {
                    return;
                }
                const head = rest.slice(0, length);
                const tail = rest.slice(length);
                const id = ++s.request;
                try {
                    const [candidates, tailSegs] = await Promise.all([
                        postJson("/api/ime/candidates", { kana: head, learn: learn }),
                        tail ? postJson("/api/ime/convert", { kana: tail, learn: learn }) : Promise.resolve([])
                    ]);
                    if (id !== s.request || !s.segs) {
                        return;
                    }
                    s.segs = s.segs.slice(0, s.focus)
                        .concat([{ reading: head, candidates: candidates, index: 0 }])
                        .concat(tailSegs.map(g => ({ reading: g.reading, candidates: g.candidates, index: 0 })));
                } catch (e) {
                    console.warn(e);
                }
                render();
            }

            function cycle(step) {
                const g = s.segs[s.focus];
                g.index = (g.index + step + g.candidates.length) % g.candidates.length;
                render();
            }

            // Put a specific spelling on the focused segment (used by F6/F7).
            function useSpelling(text) {
                const g = s.segs[s.focus];
                let i = g.candidates.indexOf(text);
                if (i < 0) {
                    g.candidates = g.candidates.concat([text]);
                    i = g.candidates.length - 1;
                }
                g.index = i;
                render();
            }

            const onKeyDown = function (e) {
                if (e.isComposing || e.keyCode === 229) {
                    // The Windows IME is handling this key.
                    if (!s.osIme) { s.osIme = true; renderStrip(); }
                    return;
                }

                const handled = () => { e.preventDefault(); e.stopPropagation(); };
                const plain = !e.ctrlKey && !e.metaKey && !e.altKey;

                if (s.locked) {
                    if (e.key === "Enter") { handled(); dotNetRef.invokeMethodAsync("OnImeSubmit"); return; }
                    if (e.key === "Tab" && replay) { handled(); dotNetRef.invokeMethodAsync("OnImeReplay"); return; }
                    if (e.key.length === 1 || e.key === "Backspace" || e.key === "Delete") { handled(); }
                    return;
                }

                if (s.segs) {
                    if (e.key === " " || e.key === "ArrowDown") { handled(); cycle(1); return; }
                    if (e.key === "ArrowUp") { handled(); cycle(-1); return; }
                    if (e.key === "ArrowRight" && e.shiftKey) { handled(); resize(1); return; }
                    if (e.key === "ArrowLeft" && e.shiftKey) { handled(); resize(-1); return; }
                    if (e.key === "ArrowRight") { handled(); s.focus = Math.min(s.focus + 1, s.segs.length - 1); render(); return; }
                    if (e.key === "ArrowLeft") { handled(); s.focus = Math.max(s.focus - 1, 0); render(); return; }
                    if (e.key === "Enter") { handled(); commit(); return; }
                    if (e.key === "Escape" || e.key === "Backspace") { handled(); s.segs = null; render(); return; }
                    if (e.key === "F7") { handled(); useSpelling(hiraganaToKatakana(s.segs[s.focus].reading)); return; }
                    if (e.key === "F6") { handled(); useSpelling(katakanaToHiragana(s.segs[s.focus].reading)); return; }
                    if (plain && /^[1-9]$/.test(e.key)) {
                        const g = s.segs[s.focus];
                        const n = pageOf(g) * PageSize + Number(e.key) - 1;
                        if (n < g.candidates.length) { handled(); g.index = n; render(); }
                        return;
                    }
                    // Any other typing keeps the conversion and starts afresh.
                    if (plain && e.key.length === 1) { commit(); }
                    return;
                }

                if (s.comp) {
                    if (e.key === " ") { handled(); convert(); return; }
                    if (e.key === "Enter") { handled(); commit(); return; }
                    if (e.key === "Escape") { handled(); s.comp = ""; render(); return; }
                    if (e.key === "F7") { handled(); s.comp = hiraganaToKatakana(finishKana(s.comp)); render(); return; }
                    if (e.key === "F6") { handled(); s.comp = katakanaToHiragana(finishKana(s.comp)); render(); return; }
                    return;
                }

                if (e.key === "Enter") { handled(); dotNetRef.invokeMethodAsync("OnImeSubmit"); return; }
                if (e.key === "Tab" && replay) { handled(); dotNetRef.invokeMethodAsync("OnImeReplay"); return; }
                if (e.key === " ") { handled(); commitChunk(" ", " "); render(); notify(); return; }
            };

            const onInput = function () {
                const value = input.value;
                if (s.segs) {
                    // Typing during a conversion is handled on keydown; just redraw.
                    render();
                    return;
                }

                if (value.startsWith(s.committed)) {
                    s.comp = toHiragana(value.slice(s.committed.length));
                    render();
                    return;
                }

                // The committed text itself was edited (e.g. Backspace past the
                // composition): keep the chunks that are still intact.
                let kept = 0;
                let length = 0;
                while (kept < s.chunks.length && value.startsWith(s.committed.slice(0, length + s.chunks[kept].text.length))) {
                    length += s.chunks[kept].text.length;
                    kept++;
                }
                s.chunks = s.chunks.slice(0, kept);
                const rest = value.slice(length);
                if (rest) {
                    s.chunks.push({ text: rest, kana: isAllKana(rest) ? rest : null });
                }
                s.committed = value;
                s.comp = "";
                renderStrip();
                notify();
            };

            const onCompositionStart = function () {
                s.osIme = true;
                renderStrip();
            };

            // Leaving the field keeps what was being composed.
            const onBlur = function () {
                if (s.comp || s.segs) {
                    commit();
                }
            };

            input.addEventListener("keydown", onKeyDown);
            input.addEventListener("input", onInput);
            input.addEventListener("compositionstart", onCompositionStart);
            input.addEventListener("blur", onBlur);
            input._kotonekoIme = {
                state: s,
                render: render,
                notify: notify,
                remove: function () {
                    input.removeEventListener("keydown", onKeyDown);
                    input.removeEventListener("input", onInput);
                    input.removeEventListener("compositionstart", onCompositionStart);
                    input.removeEventListener("blur", onBlur);
                }
            };
        },

        // Ignore typing (true) or accept it again (false).
        setLocked: function (input, locked) {
            const ime = input && input._kotonekoIme;
            if (ime) {
                ime.state.locked = !!locked;
            }
        },

        // Empty the field (e.g. for the next dictation sentence).
        clear: function (input) {
            const ime = input && input._kotonekoIme;
            if (!ime) {
                return;
            }
            Object.assign(ime.state, { committed: "", chunks: [], comp: "", segs: null, focus: 0, request: ime.state.request + 1 });
            ime.render();
            ime.notify();
        },

        detach: function (input) {
            if (input && input._kotonekoIme) {
                input._kotonekoIme.remove();
                input._kotonekoIme = null;
            }
        }
    };

    // Play audio from a URL (used for review autoplay), always at the speed it
    // was generated at. Resolves when the clip ends; rejects if it cannot load or
    // play, so callers never wait forever (e.g. when the browser blocks autoplay).
    k.playAudio = function (url) {
        return new Promise((resolve, reject) => {
            const audio = new Audio(url);
            audio.addEventListener('ended', () => resolve());
            audio.addEventListener('error', (e) => reject(e));
            audio.play().catch(reject);
        });
    };

    // Play a sequence of audio URLs in order. After each clip ends, wait
    // pauseMs milliseconds before starting the next one.
    k.playAudioSequence = function (urls, pauseMs) {
        function playNext(index) {
            if (index >= urls.length) return;
            const audio = new Audio(urls[index]);
            audio.onended = function () {
                setTimeout(function () { playNext(index + 1); }, pauseMs);
            };
            audio.play().catch(function () { playNext(index + 1); });
        }
        playNext(0);
    };

    // The listening player: plays clips in order with a pause between, one run at a
    // time page-wide (a new play stops the old one), and stop() ends it. Resolves
    // with the seconds of audio actually heard, for the daily listening minutes.
    let listeningRun = null;

    function finishListening(run) {
        if (run.done) return;
        run.done = true;
        if (run.audio) {
            run.seconds += run.audio.currentTime || 0;
            run.audio.pause();
        }
        clearTimeout(run.timer);
        if (listeningRun === run) listeningRun = null;
        run.resolve(run.seconds);
    }

    k.listening = {
        play: function (urls, pauseMs) {
            if (listeningRun) finishListening(listeningRun);
            return new Promise(function (resolve) {
                const run = { done: false, audio: null, timer: null, seconds: 0, resolve: resolve };
                listeningRun = run;
                function next(index) {
                    if (run.done) return;
                    if (index >= urls.length) { finishListening(run); return; }
                    const audio = new Audio(urls[index]);
                    let moved = false;
                    const skip = function () {
                        if (moved) return;
                        moved = true;
                        run.audio = null;
                        next(index + 1);
                    };
                    run.audio = audio;
                    audio.onended = function () {
                        if (moved) return;
                        moved = true;
                        run.seconds += audio.duration || 0;
                        run.audio = null;
                        run.timer = setTimeout(function () { next(index + 1); }, pauseMs);
                    };
                    audio.onerror = skip;
                    audio.play().catch(skip);
                }
                next(0);
            });
        },

        stop: function () {
            if (listeningRun) finishListening(listeningRun);
        }
    };

    // Play audio from a raw byte array passed by Blazor (used for edit-page preview).
    k.playAudioBytes = function (bytes) {
        const blob = new Blob([bytes], { type: "audio/mpeg" });
        const url = URL.createObjectURL(blob);
        const audio = new Audio(url);
        audio.onended = function () { URL.revokeObjectURL(url); };
        audio.play().catch(function () { URL.revokeObjectURL(url); });
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

    // The Manage menu is a <details>, which only closes on its own summary: close it
    // on a click anywhere outside it, or on Escape.
    function closeMenus(except) {
        document.querySelectorAll("details.kn-nav-menu[open]").forEach(function (menu) {
            if (!except || !menu.contains(except)) {
                menu.removeAttribute("open");
            }
        });
    }
    document.addEventListener("click", function (e) { closeMenus(e.target); });
    document.addEventListener("keydown", function (e) {
        if (e.key === "Escape") closeMenus(null);
    });
})(window.kotoneko);

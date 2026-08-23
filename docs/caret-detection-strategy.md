# ShowLang caret detection strategy

Goal: avoid site/app-specific fixes. Detect the caret by capability/provider family, validate the result, and prefer a safe screen-corner fallback over a wrong position.

## Default pipeline

1. Win32 `GetGUIThreadInfo` caret — highest confidence.
2. MSAA `OBJID_CARET` — high confidence.
3. Native UI Automation TextPattern2 `GetCaretRange` — high confidence when supported.
4. Managed UI Automation collapsed `TextPattern.GetSelection()` rectangle — medium/high confidence.
5. Provider-family fallback only when the direct caret rectangle is unavailable or proven broken.
6. If no candidate passes validation, use the active monitor's bottom-right working area.

## Provider-family fallbacks

- Terminal-like providers: expand the collapsed text range to the adjacent character only when the provider exposes no caret rectangle.
- Browser omnibox/address-field providers: use the document-prefix edge workaround only for browser address controls whose collapsed rectangle is pinned to the control's left edge.
- Normal web inputs, textareas and contenteditable controls: never use the browser-address workaround.
## Candidate validation

A UIA caret candidate must pass all of these checks:

- It is inside the foreground window and on-screen.
- It is inside, or within a small tolerance of, the focused editable element's bounding rectangle when that rectangle is specific enough to be useful.
- The focused element is editable by control type, writable ValuePattern, or non-read-only text attributes.
- Cached caret data is tied to the same focused element/runtime id and is discarded when focus changes.
- A rectangle from an unrelated child, button, suggestion popup or newline is rejected.

## Confidence policy

Use one automatic mode rather than per-app settings. High-confidence sources win. Low-confidence inferred positions never override a valid direct caret. When confidence is insufficient, show the language at the screen corner instead of guessing.

Do not add domain names such as Facebook, YouTube or Shopee to detection code. A workaround may key on a provider/control class only when the API behavior is reproducible across that provider family.
## Regression matrix before promotion

Every caret-engine change must be checked against these families:

- Win32/WPF normal text boxes
- Windows Terminal
- Raycast/Electron-style editable controls
- Browser address bar
- Normal web `<input>` / search fields (YouTube, Shopee are current examples)
- Web contenteditable / rich-text composer (Facebook is the current example)
- Windows Search
- No editable target -> screen-corner fallback

Only promote the branch to stable when all baseline cases pass. New failures stay in experimental and must not move the stable tag.
## Current engine-v2 rules

- UI Automation candidates are accepted only when they intersect the focused editable control (10 px tolerance).
- Focus changes invalidate the cached caret immediately; late background queries from an older focus generation cannot overwrite the new cache.
- Empty editable controls whose provider exposes no caret rectangle anchor to the focused control's left edge instead of borrowing a virtual adjacent range.
- Adjacent-character geometry remains a fallback only after direct collapsed geometry fails and must still stay inside the focused control.
- TextPattern2 remains optional: some Chromium providers report the pattern query as successful while returning no usable pattern object, so it must never be the only path.

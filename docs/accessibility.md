# Accessibility checks and interaction

ChessLens provides coordinate entry alongside board dragging/tapping in game review, puzzles and mistake practice. **Current pieces and legal moves** describes the displayed position by colour, piece and square, includes check status and the authoritative active legal coordinate moves, and explains promotion entry. It does not expose an engine evaluation or a suggested training answer. Completed attempts and solution playback describe the position currently displayed.

The draggable pieces retain the library's artwork and behavior, with names such as **Black queen on d8**. Keyboard users can also use the coordinate field. In review, replay controls support Left/Right, Home and End; controls remain focusable at the ends with `aria-disabled` so the next navigation key still works. Replay shortcuts do not consume the board's keyboard-drag keys or typing in input controls.

Promotion opens an inline, labelled choice of Queen, Rook, Bishop and Knight. Focus enters the first choice, Tab reaches each choice, and Escape cancels. Cancelling returns focus to the initiating control when it remains present. Keyboard/touch checks include a legal rook mating alternative, rather than assuming promotion must be to a queen.

Navigation gives the main content a page name, moves focus there, and updates the document title. A visible skip link, native form labels, explicit group labels for notation/candidates/metrics and text move classifications support navigation without colour or dragging. Status text is kept in a paragraph inside a persistent polite status region: inspection with Orca/Chromium found that direct text changes on a native status-bar node were being treated as silent UI updates. The revised markup is exercised with actual saved practice work and inspected Orca announcements.

The sidebar can scroll when enlarged text increases its height. Long names, notation and original PGN text wrap; wide report tables, notation/graph lists and mobile navigation have their own bounded scrolling areas. Reduced-motion media preferences remove CSS animation/transition and set the board animation duration to zero.

## Automated checks

`npm run test:e2e` runs the real application in desktop Chromium and Pixel 7 touch emulation. The connected learning journey scans registration, empty overview, import, completed import, library, review, opened weakness evidence, the exact saved decision, puzzle preparation/play/review, mistake practice play/review, progress filters, saved progress, empty weekly reports and settings. Other journeys scan live candidates, supported comparison charts, weekly reports, promotion choices and open position text. Separate checks exercise 320-pixel width, long unbroken player names, 31 legal saved half-moves, a long original annotation, and 200% text sizing. Every scanned state checks document content width against viewport width.

The pinned **axe-core/Playwright 4.13.0** integration uses all default rules without excluding elements or rules, saves the complete results, fails on reported violations and also rejects unresolved `aria-prohibited-attr` notes. See [Playwright's accessibility guide](https://playwright.dev/docs/accessibility-testing) for the integration and the limits of automated checks. Each saved scan retains its inconclusive results; a zero-violation count alone is not a complete accessibility assessment.

Axe leaves single-letter board coordinates for manual review. Their rendered colours are also checked numerically against the [WCAG contrast formula and threshold](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html). The coordinate foreground `#101010` has an unrounded ratio above **4.56:1** on the dark warm square and **14.79:1** on the light square. Evaluation text now has an opaque dark label background. SVG comparison text, dashboard gradient endpoints, decorative icons and visible focus were additionally inspected; text and numeric labels convey the information represented by charts and colours.

## Keyboard and assistive inspection

On 14 October 2026, tool-operated manual inspection used **Orca 50.2, AT-SPI 2.60.4, Chromium 153.0.8010.12 and Xvfb 21.1.22** in a separate X11/private D-Bus session. The user's normal desktop and existing screen-reader session were not replaced. Native X keyboard events exercised registration/login, PGN entry, library/review, opened supported weakness evidence, the exact saved decision, a solved personal mate puzzle, mistake practice, saved progress, an empty weekly report and preferences. The same application used real Stockfish and persisted training results.

Native browser zoom reached **200%** (observed `devicePixelRatio=2`, 720 CSS pixels within the 1440-pixel browser viewport), separately from the automated text-size check. Width remained within the client viewport through review, findings, puzzle/practice, progress, reports and settings. Native framebuffer screenshots were inspected because a Playwright full-page screenshot under native zoom did not capture the whole physical viewport accurately. Actual mobile touch events and promotion focus are covered by the real-browser tests.

Orca's AT-SPI events and generated speech output were inspected for entry labels/required state, headings, named pieces and squares, coordinate controls, links, preferences saved, the active practice turn, engine-check progress and the completed objective. An actual practice retry preserved its original source and completed a new attempt with `d8h4`; the saved attempt was checked independently. Earlier failed audit/control attempts remain in the verification evidence and are not counted as passing checks.

This is an engineering inspection of the actual interface and screen-reader output, not a user study or an audio-listening session. No braille display, physical mobile device, NVDA/Windows or VoiceOver/macOS inspection is claimed. Orca was allowed time to process focus-mode changes before native typing. The independent quality review must still inspect representative desktop/mobile interactions. Dated commands, counts, screenshots and limitations are recorded in [verification](verification.md).

## Enlarged overview follow-up — 15 October 2026

The 320-pixel, 200%-text overview now wraps its heading and stacks the library
card instead of overflowing its grid. This is part of the desktop/mobile
controls regression, alongside promotions, saved notation and the existing
real-engine practice check. The footer's licence link is permanently underlined
and opens the bundled notices. Four focused cases/18 full axe scans pass after
fixing the initially observed width and link-identification defects. This
follow-up does not replace the earlier real Orca/browser-zoom inspection or
claim new assistive technologies/physical devices were tested.

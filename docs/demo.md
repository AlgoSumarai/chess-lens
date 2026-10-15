# Demonstration: a complete learning loop

Start the local application using the README, then create a fresh account.
The files below contain original artificial games, never a real player's
performance record. No model key is needed. The same sequence is exercised with
real Stockfish on desktop and mobile in `web/e2e/reports.spec.ts`.

1. Open **Import**. Upload `samples/learning-loop.pgn` and enter **Learner** as
   your player name. Confirm one game was imported, then open the library.
2. Open the game and choose **Deep review**. Wait for its completed six
   half-moves. Quick reviews are useful previews; this demonstration uses
   independently checked evidence for training.
3. Open **Weaknesses**. Select 15 October 2026 in the UTC date range and choose
   **Find my patterns**. Expand **Tactical opportunities**. One game gives an
   early signal; it does not establish a recurring real-player weakness.
4. Follow **Review move 2 · black**. The board, notation and explanation select
   the saved `Nc6` decision. Read what happened and the legal better
   continuation. Change positions and return to confirm the explanation follows
   the selected decision.
5. Choose **Turn this decision into a puzzle**, wait for validation, then
   **Start or resume puzzle**. Enter `d8h4` in **Coordinate move** and play it.
   The attempt is solved by checkmate; its review links to the original mistake.
6. Follow that original-decision link and choose **Play from before this
   mistake**. Play `d8h4` again. The terminal objective ends the attempt early
   and records **Objective met · unassisted attempt**. On another attempt, try
   a hint/undo and observe the assistance label. Reload to inspect saved state.
7. Open **Progress**. Set both played-date fields to 15 October 2026, source
   **PGN**, colour **black**, group **blitz**, exact control **300+0**, and unknown
   openings. Save progress. The report shows the actual game/evidence
   denominators, training attempts and exact source decisions. A single game
   correctly produces **Insufficient comparable data**.
8. To demonstrate a supported descriptive comparison, use another fresh
   account and import `samples/comparable-progress.pgn` as **Learner**. Choose
   **Review this batch**, ten games and deep review. After all ten finish, open
   Progress for 15 October 2026 only, with PGN/black/blitz/unknown-opening filters.
   The preceding day has five independent original game records. The intended
   real-engine result is **Previous: 5/15 · 33.3%; Current: 0/10 · 0.0%** and
   **An observed change**. These constructed samples illustrate the comparison
   rules; they do not show that practising improved someone's chess. The browser
   regression independently checks that result and immutable reload behavior.
9. Open **Weekly reports** and choose a completed local-calendar week.
   Reviews completed today cannot create retrospective activity in an earlier
   week. **No new completed review activity** is an actionable empty state,
   with a progress link. Saved historical-week behavior is tested separately
   using explicitly labelled completion-time fixtures.
10. Open **Settings** to inspect coaching level/timezone, the no-key model
    setting and data-deletion control. **Licences & artwork** opens the bundled
    attribution texts. Repeat the main flow on a small screen or using keyboard
    focus and coordinate move input.

Provider imports are a separate small public integration demonstration; see
[the bounded import verifier](provider-imports.md). Their source profiles remain
unverified. Do not use a fabricated provider response as live integration proof.

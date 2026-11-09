# Live evaluation and review variations

Enable **Analyse this position** once to follow the board through the imported game and explored lines. The board-height evaluation bar uses White's perspective; flipping the board moves its white portion and score to White's end. Candidate scores retain the labelled side-to-move perspective. Scores and candidates disappear when analysis is stopped, including numeric labels in the saved game's evaluation graph.

The panel shows up to three ranked Stockfish candidates with up to ten SAN half-moves each. Selecting a candidate plays its first move. Board moves and coordinate entry use the same move tree. An already explored move follows its existing continuation instead of creating a duplicate.

The move list keeps the original game and every explored continuation. Select a move to visit it, or a line heading to change the continuation followed by the navigation controls. Selecting another line at a shared position leaves the board there. Back, Forward, Home and End retain that selected line, including when Back reaches the imported game. Exploring a different move preserves the previous continuation. Lines live only in the mounted review; leaving and reopening the review clears them. Refreshing the game's player identity keeps the review open.

`reviewTree.ts` stores parent/child relationships, the current node and the selected line's endpoint separately. Every alternative node carries the imported-game anchor and its full variation history, so the existing server replays the correct history for legal moves and engine analysis. The existing limit of 100 variation half-moves applies.

`usePositionAnalysis` shares one toggle and one result between the bar and candidate panel. Responses must match the request's game, imported-game anchor, full variation, FEN and sequence. Navigation hides the old result during render, before effects run. Aborted polling, job/version checks and the existing persisted sequence barrier prevent late responses from replacing the selected position. Pending positions show an ellipsis and empty candidates. Positions without legal moves show an empty candidate list and do not queue an engine search.

## Verification

From `web/`, with the local API, database and Stockfish worker running:

```sh
npm test
npm run build
npm run test:e2e -- e2e/live-review.spec.ts e2e/foundation.spec.ts e2e/accessibility-controls.spec.ts
```

The component tests cover delayed requests and polls, stopping in-flight analysis, SAN limits/castling/promotion/mate, terminal positions, nested branches, duplicate moves, selected-line replay and game-data refreshes. The new browser journey uses real Stockfish on desktop and mobile, checks nested candidate branches and line switching, compares the live score with the engine result for the final board after rapid navigation, validates every displayed SAN continuation, checks flipped bar geometry, runs an accessibility scan, and confirms review reopening clears variations. The existing journeys also cover import/identity/replay, saved analysis, keyboard/touch promotion, long notation, narrow screens and enlarged text.

Verified on 9 November 2026: **21 unit/component tests passed**, **8 desktop/mobile browser cases passed**, the production build passed, and `git diff --check` passed. The browser cases used the updated Vite frontend with the existing API and Stockfish 19 worker in an isolated Docker project/database. All included accessibility scans passed. Desktop and mobile screenshots were inspected; generated screenshots and the browser report are under `web/test-results/` and `web/playwright-report/`.

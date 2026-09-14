# Personal puzzle validation and review

Puzzle generation starts from the full source history immediately before a selected, completed, non-provisional mistake belonging to the chosen player. The source game and analysis remain unchanged. Generation is a separate durable worker job; no engine runs in an HTTP handler.

## Publication gate

The current `chesslens-puzzles-v1` validator publishes short forcing mates and material-winning tactics. A positional error or a move with many equivalent choices may be useful to review but will not necessarily produce a puzzle. Rejection includes a reason and does not claim the original analysis was wrong.

Each decision and defensive position is searched at full skill with two independent budgets: 250,000 nodes / five seconds and 750,000 nodes / eight seconds. Player decisions use up to eight principal variations. Defensive replies and the restricted set of omitted moves need only their strongest line. Complete, exact, legal evidence, consistent engine/settings (apart from this explicit MultiPV choice), and stable scores are required. Centipawns may differ by at most 75; mate distances must match. Mate scores remain separate.

For material objectives, all moves within 50 centipawns of the best move must remain acceptable in both searches. The next inferior move must be at least 100 centipawns worse; a fragile boundary rejects the puzzle. Mating alternatives must achieve mate within three player moves. Additional positive mating lines beyond that horizon reject the candidate rather than being labelled incorrect. Any legal move omitted from MultiPV is included in a restricted search set at both budgets. If its best continuation is sound or too close, the puzzle is rejected. No unlisted equivalent move is deliberately marked wrong.

The tree stores every accepted player choice and a deeply checked strongest defensive reply for that choice. Equivalent defences can change order without changing the objective. Mates finish at legal checkmate. Material objectives require a gain of at least one pawn unit, surviving two consecutive player decision points in both continuation searches, with evaluation support against compensation. Values are pawn 1, minor piece 3, rook 5, queen 9; these material units are not centipawn evaluation. A player completes the objective after the checked defence when the gain is secured; winning a whole game is unnecessary.

The validation tree is limited to three player decisions per branch, four acceptable moves at each decision, 12 decision nodes, 48 searches, three minutes wall time, and two million JSON characters of evidence. Exceeding a bound never publishes a partial solution. Full initial FEN and source history are retained for draw-sensitive engine searches and authoritative move validation.

These are conservative finite-search checks, not a proof of perfect chess or a calibrated puzzle rating. The UI labels difficulty as an estimate. Current factual explanations describe the supported objective and provide legal solution playback; named tactical motifs are not invented.

## Durable jobs and privacy

The API permits two active candidates and 20 new candidates per owner per rolling day. A unique owner/run/ply/validator key reuses an existing candidate. Jobs use five-minute leases and at most three attempts. Expired leases can be reclaimed, and terminal writes require the current running state and lease token. Cancellation or data deletion prevents a late search result from reviving a job. Already-running searches finish under the bounded validation deadline; cancellation does not instantly stop them through a database watcher.

Only ready puzzles can start attempts. Basic puzzle and active-attempt responses omit the original error, source link, evaluation, solution tree and validation searches. Hints reveal progressively more guidance by request. After completion or revealing the answer, the response supplies the checked tree, factual explanation and exact saved-review link. All HTTP and SignalR access is owner-scoped.

## Attempts and spaced review

The server stores accepted player moves, validated defensive replies, submitted errors, cumulative hints, timestamps, outcome, elapsed seconds and the next review date. Elapsed time includes pauses and is capped at 24 hours; it is not a performance rating. One active attempt per owner/puzzle is enforced in PostgreSQL. Starting again resumes it until it expires after 24 hours. Every mutation requires the current attempt version; duplicate or stale commands return 409 without double-counting a hint, move or schedule update. Illegal moves return validation errors and do not finish an attempt.

Hints progress from a general calculation prompt to a starting square and then an accepted coordinate move. Each new decision resets the hint stage, while total hint use remains recorded. A legal move outside the stable accepted set finishes the attempt as failed. Revealing the solution finishes it as revealed.

The documented review intervals are:

| Outcome | Next review | Success streak |
| --- | --- | --- |
| Failed, revealed or expired | Four hours | Reset |
| Solved with hints | One day | Reset |
| Solved without hints | 1, 3, 7, 14, then 30 days | Increment, capped at five |

The schedule is saved atomically with completion. Review intervals are learning heuristics. Completing puzzles alone does not demonstrate improved game performance. The library orders ready puzzles by due date, and earlier practice remains available.

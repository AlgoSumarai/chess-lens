# Completed-game provider imports

Contract checked 13 October 2026 against the [Chess.com Published-Data API](https://www.chess.com/news/view/published-data-api), [Lichess export specification](https://github.com/lichess-org/api/blob/master/doc/specs/tags/games/api-games-user-username.yaml), and [Lichess rate-limit guidance](https://lichess.org/page/api-tips).

The public username is an **unverified identity**. No provider password, account token, ongoing-game endpoint or scraping is used. Choose an explicit UTC interval (`from` inclusive, `to` exclusive), at most 366 days, and 1–200 records. The interface includes both selected calendar dates by converting the through-date to the next UTC midnight. The default is 50 recent games. Unknown metadata remains null.

Chess.com enumerates supported monthly completed-game archives from newest to oldest, using at most 13 overlapping months. It filters by the archive's completion timestamp. Returned archive URLs are used only to extract and validate a month belonging to the requested username; every fetched URL is reconstructed locally. Lichess uses the documented completed-game NDJSON export (`ongoing=false`, `finished=true`, bounded `max`, clocks and opening enabled, provider evaluations disabled). Its date interval follows the provider's game-creation timestamp. Responses are read incrementally under a 20 MB decompressed limit, then parsed as a bounded selection. We do not claim that one request processes the user's entire archive.

Each selected PGN goes through the existing authoritative legal mainline parser. Unsupported variants, illegal games, missing completion dates that cannot be matched to a Chess.com interval, malformed NDJSON records, and immutable-source conflicts are reported individually. An unavailable username/export, invalid archive response or oversized selection fails the job with an actionable message. Missing ratings, opening names and clocks are not fabricated. Chess.com opening URLs are not converted into guessed opening names; PGN opening tags and Lichess opening metadata are retained when supplied.

Source metadata includes a safe source URL, provider ID, ratings, provider rating pool, rated flag, timestamps, time control, termination, opening/ECO, raw PGN and legal mainline/clock annotations. Chess.com live/daily IDs include their namespace; Lichess IDs are eight-character game IDs. Without a provider ID, the fallback uses the documented legal PGN fingerprint in a provider namespace. Annotation changes do not replace an existing source. If a repeated provider ID changes its legal moves, initial FEN or result, the conflicting delivery is excluded and the original is retained.

PostgreSQL constraints, a source-identity transaction lock, and an owner/game key prevent duplicate sources and owner associations under concurrent delivery. Every valid record, including a duplicate already owned, creates one batch membership. Public profiles are owner-scoped. Data deletion removes jobs, memberships, profiles, analyses and derived entities, and a late provider response cannot recreate them.

## Requests, cache and recovery

Only fixed HTTPS provider hosts and completed-game paths can be fetched. Usernames accept only bounded letters, digits, underscores and hyphens. Redirects are disabled. Source URLs are never fetched. Requests run in the worker, with a 30-second total deadline and a 20 MB decompressed body limit. Selected PGNs together are capped at 5 MB; games remain limited to 1,000 half-moves each.

A bounded worker cache (eight responses, 64 MB total) observes `Cache-Control`, `Age`, ETag and Last-Modified, including 304 revalidation. It is an in-memory optimization, not an authoritative or persistent import checkpoint. Cache entries never appear in an HTTP response to a user. Restarts may refetch a page but cannot duplicate its committed records.

Database advisory locks serialize requests per provider across workers. A durable provider cooldown applies to all owners after rate limits or temporary outages. HTTP 429 respects Retry-After and waits at least a full minute, as Lichess requires. Transient failures use bounded exponential backoff; no worker sleeps while holding the queue. Four consecutive failed/interrupted claims stop the import. Successful page/validation checkpoints reset this consecutive-failure count. Two-minute leases exceed one bounded provider request. Cancellation is observed before fetching and at commit checkpoints; already saved valid games remain available.

An archive plan and each fetched page are committed before legal validation. The processor yields after one provider page or 16 validated records, so a large import cannot monopolize the worker lane. Restart uses the saved month index and staged records. An expired lease may be reclaimed; exhausted leases fail visibly. Resync creates a new bounded job and preserves the earlier source and evidence. One owner may have three active imports and 20 new import jobs per rolling day.

## Bounded batch review

After an import finishes, including cancellation/failure with valid saved games, choose 1–20 batch games and a quick/deep review. One batch may be active per owner. The first selected valid records are included. Existing completed reviews with the same profile/current analysis version, and already active game reviews, are reused and displayed as their saved evidence; they are not recomputed or averaged. A new game review can still be requested from its own screen.

New reviews reserve their entire requested half-move count on submission. All reserved work counts against the existing 5,000-half-move daily budget. The scheduler admits at most three queued/running mainline reviews per owner; other reserved reviews remain `waiting` and start automatically as capacity becomes available. Repeated owner/import/profile submission reuses the saved batch. Reload and reconnect reconcile saved progress through HTTP and owner-scoped SignalR notifications. Cancelling a batch cancels only the new reviews it owns, preserving reused reviews and committed partial results. Historical imports predating batch membership remain available through the game library; their memberships cannot be inferred reliably after the original upload payload was discarded.

Provider contract and pipeline fixtures are explicitly labelled in tests. Live verification is a separate opt-in command against the running loopback application:

```sh
python3 scripts/check-provider-imports.py --url http://localhost:8088
```

It creates a labelled local test account, imports two public completed games from each provider, checks resync and immutable records, and deletes its imported test data. A missing provider is recorded as an unverified gate and exits unsuccessfully. See [verification](verification.md) for dated evidence.

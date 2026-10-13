# Private PGN annotations and shared game identity

PGN deduplication matches canonical headers, starting position, result and legal mainline. `Annotator`, `PlyCount`, comments, variations and clock annotations do not distinguish a completed game. That permits two owners to share the same immutable legal source while uploading different private notes.

Each `UserGame` now retains that owner's **first** parsed original PGN and per-ply clock snapshot. Reimporting different annotations preserves the first owner copy. Shared PGN `SourceGame` records contain a canonical mainline without private annotations, annotator headers or clocks. Game IDs, fingerprints, legal history, engine caches and derived evidence remain shared where compatible. Public provider source annotations remain the immutable public version originally fetched; an offline PGN is never inferred to be public from its Site tag.

Game responses return only the authenticated owner's original PGN and clock view. Insight generation uses that same clock view. Report v2 captures the owner's clock JSON along with its original run/evidence references, so later input changes cannot rewrite queued or completed report coverage. The optional coaching provider continues to receive no PGN comments or uploaded names.

## Upgrade from the earlier shared-annotation layout

Earlier completed import jobs discarded their upload payload and did not retain the creator of a shared PGN. Neither the earliest remaining association, a username nor an `Annotator` header proves who supplied its comments. The migration deliberately makes no attribution guess.

`PrivatePgnAnnotations` relocates each earlier PGN's exact stored original bytes into `LegacyPgnAnnotations`, a restricted preservation table with **no HTTP endpoint**. The shared row becomes canonical and its private clocks are removed. Starting positions, game IDs/fingerprints, legal moves, review IDs, scores/settings/classifications and completed snapshots stay unchanged. The migration is forward-only because dropping the new storage could discard owner originals; restore the pre-upgrade database backup for rollback.

Until the owner reimports their original file, the game remains replayable and reviewable with `annotationState:"unattributed"`, a canonical PGN and missing clocks. The interface explains this state. Reimport restores only the supplied owner's version, reuses the same saved game/reviews and does not expose the restricted legacy copy. A pre-upgrade partial job whose payload is still present can restore its own committed prefix during resume without changing import counts.

Deleting an owner's imported data deletes their private originals and clocks. Any unattributed legacy preservation payload associated with that deletion is also removed; another owner's **attributed** first upload and legal game remain intact. This conservative cleanup avoids retaining an unidentified person's private notes after a deletion request. Preserve a private pre-upgrade backup before changing the layout. Legacy originals are not automatically assigned or made downloadable to an owner, because the earlier schema cannot establish their provenance.

Existing completed report v1 results remain readable and unchanged. Pending v1 jobs finish with a clear unsupported-history error; retry cannot reinterpret their old inputs under v2. Choose a fresh capture instead. Completed-report retry returns the same saved result. See [report rules](reports-progress-rules.md) and the migration/actual two-owner evidence in [verification](verification.md).

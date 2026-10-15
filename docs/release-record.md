# Local release record — 15 October 2026

## Corrected candidate — 2026.10.15-r2

Runtime application source is **3b3ae4a808cc2dda5e1ce789ccab2da8558cbf86**. The native corrected images pass clean-checkout startup, 227 backend/10 component/34 desktop-mobile checks, 64 clean automated accessibility scans, live provider resync, preserved-data/key upgrades, restricted-role HTTPS/copy engine checks and the repeat mixed-workload benchmark. The corresponding ARM64 images pass exact source/UCI and OS/runtime notice checks, fresh whole-stack startup and six desktop/mobile cases with 40 clean accessibility scans. **Round 1 remains failed; independent Round 2 is still required.**

The new identities below come from the exact verified per-image notice inventories. The corrected packet is prepared at `.local/releases/2026.10.15-r2/`; its `release.json` and `SHA256SUMS` authoritatively record the committed source, image archives and complete file hashes. Artifact loading/fresh-start receipts are recorded after packaging; the historical candidate below must not be described as passing independent review.

| Platform | Service | Observed image/config ID | Available manifest/index digest |
| --- | --- | --- | --- |
| linux/amd64 | api | `sha256:013fb86139c0fa907dc87c901c01d6fecf5eefc3fdb82c80244cf3c894ffda06` | `sha256:0c37f9b2deba1a8a41a0552da67c6c4d9e3f71f345c843a5e77d8c9d9f04c6e8` |
| linux/amd64 | worker | `sha256:2f0e1703ebf3a62730b442e0f4ddc0034dbbc152306d02c422e288f84a764e97` | `sha256:7897521a30de501d8d07e38f8aa301a1b47cf0a83bd48eb1d0f6f8bd009e5c6b` |
| linux/amd64 | frontend | `sha256:fb63cf2bb0b5405c9f58bf14f7212f35333623a3fd204498e664ce2aa60d0df5` | `sha256:050aa28249fecffb31d1d13bb24ba262407cf7d3c3880fc569cea2866e250fa7` |
| linux/amd64 | database | `sha256:885953109528ad3dfc90362b1a6f50a78620b5315be19f187753d267e484dc5b` | `sha256:74935e72241653ca55e0414067e6d8763aceb8a810eb51b452253ec3dcfc4336` |
| linux/arm64 | api | `sha256:0272a235ce4549db47c7dc5784cf11c9d5955cc67db3fbe0ee5dffa78e3129b4` | `sha256:0bb9234578b6921bc69a4701fef7fc8c87ef1c6233cfc631a11ad200d02af166` |
| linux/arm64 | worker | `sha256:82c1f713b9e908a45433abfd4702ba9693c8a03d81e385a0d43ebf7d9e817721` | `sha256:643b8cff14d894b980690d7bc7fc67252e1d3c95f4429b371ae23bd349ef03c5` |
| linux/arm64 | frontend | `sha256:0cd8cddfe21742666046ca806b74d40c79e8f072484cabfe3962956f44a6c135` | `sha256:291e48f988875b43cc54c9b448b8ddaa1df3f7796a84bded1755f50c355394f8` |
| linux/arm64 | database | `sha256:a63b24165ea0eacced7585766d790ac08bbaefe10a1841f87783a4505d29a69c` | `sha256:74935e72241653ca55e0414067e6d8763aceb8a810eb51b452253ec3dcfc4336` |

## Historical candidate — failed Round 1

Candidate **2026.10.15-local** uses application commit `bee92d0c659a9476ec315820467193f394968be0` and source archive commit `880808b200f200638199324833fac41eb508b54d`. It is local preparation; nothing is published externally. The independent review record determines final readiness.

The authoritative manifest and all 1,191 file checksums are `.local/releases/2026.10.15-local/{release.json,SHA256SUMS}`. Both exported image archives have been loaded and their four platform-specific image IDs verified. Fresh native startup uses the extracted source and `--no-build --pull never` at localhost:8095. The complete ARM64 stack starts on native Engine/QEMU and passes both connected desktop/mobile learning journeys. See the verification ledger for exact scope.

| Platform | Service | Observed image/config ID | Available manifest/index digest |
| --- | --- | --- | --- |
| linux/amd64 | api | `sha256:fa96c1b514813247d2bd41732bc20842c838624798c81e822bcbd431bcbd78a0` | `sha256:cd03abbf11613ff426ccf8b2d1af614b5d02f17675075ab9598096e5b2a4b610` |
| linux/amd64 | worker | `sha256:a6a4c5401b272ac26cd8df4544d0bbed7a696a663e5c9f05f0145a361440ea8d` | `sha256:b3236bf62c62cbda1163d4cd057a64129e82291da54111f852393325b5961f2c` |
| linux/amd64 | frontend | `sha256:738df34be84a5069491b5d6be51d9b7ad9d6c0707ea609a35b5f81ed7173e55f` | `sha256:bbb8c51f31b64f7e64e54fab596a17b51ddc54080332dc1b1ace43833660e71f` |
| linux/amd64 | database | `sha256:885953109528ad3dfc90362b1a6f50a78620b5315be19f187753d267e484dc5b` | `sha256:74935e72241653ca55e0414067e6d8763aceb8a810eb51b452253ec3dcfc4336` |
| linux/arm64 | api | `sha256:95b1785657c305099292b64de64dec4e70fea4dcee6722fdf370530326472e3b` | `sha256:4a923a7ed4df97a4ebf9220a67f9cd78e356ffc195728df8f7b383eccb052421` |
| linux/arm64 | worker | `sha256:102ecdf6c07e68886733eb7ec91d9df3afaac54a118262fb506a5c8b72754a64` | `sha256:6378255c97fe411d73216fb954b69c1e14f83c90d7fd0a84255cc536120d8ae8` |
| linux/arm64 | frontend | `sha256:0cd8cddfe21742666046ca806b74d40c79e8f072484cabfe3962956f44a6c135` | `sha256:ecc9f35a21562fd38cb4c243157fdb7ef20935e566723cb4d8ad6e738277f334` |
| linux/arm64 | database | `sha256:a63b24165ea0eacced7585766d790ac08bbaefe10a1841f87783a4505d29a69c` | `sha256:74935e72241653ca55e0414067e6d8763aceb8a810eb51b452253ec3dcfc4336`, `sha256:74935e72241653ca55e0414067e6d8763aceb8a810eb51b452253ec3dcfc4336` |

Docker Engine/platform selection can expose a config ID or manifest ID depending on the inspection path; these are distinct from an image archive checksum. The manifest retains the observed platform-specific IDs and available content digests. The verifier checks the selected platform after loading, without requiring registry access.

| Artifact | SHA-256 |
| --- | --- |
| images-amd64.tar.gz | `98b07efbdb505a432de4df1e9cd5677b7ae88a115b823a479819e866e521c4d6` |
| images-arm64.tar.gz | `4eb2087b59ac8c7026cb61d69a21c66575bfa596ce9b57bb9f6b7112c179dedd` |
| source.tar.gz | `e630d629588b5efb5ba53201048741a2dfbbe0aceaea4fa6cf5dda7b0e0eecfd` |

Complete runtime copyright/common-licence texts and available .NET notices are in both `notices-*` directories. Application texts and twelve-piece artwork provenance are in the source/API/worker/frontend distributions as documented. Each worker includes the exact complete Stockfish source, NNUE network and build/licence materials. No database, key volume, credential, TLS private key or user PGN backup is packaged.

The initial exported-source snapshot predates the final operational evidence/review documentation. Its application inputs are identical. Final handover records any later documentation/source archive separately, with the same verified runtime images.

Use [offline startup](release.md), [HTTPS/cutover instructions](deployment.md), [backup/restore](backup-restore.md) and [the demonstration](demo.md). The normal development stack runs on loopback HTTP; the restricted-role HTTPS example is independently validated with a private local CA.

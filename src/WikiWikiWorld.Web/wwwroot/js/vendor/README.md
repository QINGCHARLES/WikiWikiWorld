# Datastar

Pinned version: v1.0.4 (MIT; see datastar-LICENSE.txt).
Source: https://cdn.jsdelivr.net/gh/starfederation/datastar@v1.0.4/bundles/datastar.js
License: https://raw.githubusercontent.com/starfederation/datastar/v1.0.4/LICENSE.md
SHA-256: `727844adfc825ee651fb93c544a2a739986f9a21820a94524b35f0cac470cf91`
Size: 33,553 bytes uncompressed; approximately 13,365 bytes gzip.

Served locally as an ES module only on article views. No npm/build pipeline.
When upgrading, verify form antiforgery, partial morphing, authored-content isolation,
and no retries after failed toggle requests. In this version network errors bypass
`retry: 'never'`, so bookmark actions also set `retryMaxCount: 0`.

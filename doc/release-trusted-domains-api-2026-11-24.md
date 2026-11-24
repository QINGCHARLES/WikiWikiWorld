# Trusted-domains API for MaggieTheBot — release 2026-11-24

The owner asked to push and deploy.

## Change

- `ab1c6e4`: `/api/trusted-domains` (Bearer token).
  - `GET` lists the trusted domains and the caller's proposals, with their outcomes and administrator notes.
  - `POST` proposes a domain with a reason. It returns 200 (pending), 400 (invalid input) or 409 (already trusted
    or already waiting). Messages are English on every culture.
  - Proposals join the same review queue as `/wiki:trusted-domains`.
  - Common prompt v4: after publishing, MaggieTheBot proposes each official domain the article links to that is not
    yet trusted or proposed, and lists its proposals in the Notes for the editor.
  - See [api.md](api.md#trusted-domains-api).
- `50691a6`, `753da78`: release records only.

No database change. Nothing under `deploy/` changed. `git log 242e490..main` had no schema scripts.

## Source and deployment

- Source: `ab1c6e406a4e` on `main`, pushed to `origin/main` (`753da78..ab1c6e4`) before publishing.
- Release: `release-20261124T061038Z-ab1c6e406a4e`, published with `latest`, family `resolute-chiseled-extra`.
- Image ID on the host: `sha256:b4bbaa79a4b594748a91d231bf82e2d58ffbf7e0f70eee112ce18ca0d3524636`. Run through
  `/home/deploy/update.sh` with the release tag, started `2026-11-24T06:11:07Z`.

## Verification

- All 436 tests passed.
- Development, with a DevvyTheBot token:
  - A new domain got 200 and the domain was reduced from a full address.
  - An already trusted domain and a repeat proposal each got 409.
  - A bare `gov` got 400.
  - GET listed the trusted domains and the caller's proposals.
  - A request sent through the Japanese site still got an English message.
- Production:
  - 200 for the en, ja and ar home pages, `gq-hype`, Cinepedia and the sitemap.
  - Zero restarts, and no `fail:`, `crit:` or exception log lines.
  - With MaggieTheBot's saved token, `GET api/trusted-domains` returned 200 with the seven trusted domains and no
    proposals.
  - No POST was made in production, so the queue was left untouched.

## Rollback

Pass `ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261124T061038Z-ab1c6e406a4e` (the delete-article
release) to `/home/deploy/update.sh`, then point `latest` at it. No database restore is needed.

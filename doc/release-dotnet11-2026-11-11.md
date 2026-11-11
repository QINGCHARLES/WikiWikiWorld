# .NET 11 upgrade and private-message page restyle — release 2026-11-11

The owner asked to push everything and deploy. This is the first production release on .NET 11.

## Source and deployment

- Source: `dae5d261ea47273297b0c022f450a285202eacc1` on `main`, pushed to
  `origin/main` before publishing. It deploys three changes since the previous release:
  - `a41403a` (`Upgrade to .NET 11 and EF Core 11`): all projects target `net11.0`, Microsoft
    packages at 11.0.0, the SQLitePCLRaw and `System.Security.Cryptography.Xml` pins removed, and
    the OpenAPI document pinned to 3.1.
  - `cb29b3f` (`Restyle private message pages and move recipient autocomplete to its own script`).
  - `14bf415` and `dae5d26`, which change only `AGENTS.md` and `PROJECT.md`.
- Release: `release-20261111T172401Z-dae5d261ea47`.
- Production image:
  `ghcr.io/qingcharles/wikiwikiworld@sha256:840728d12e03515c507d049b40116b7be65ffb2f2e983c9209e976650bf1c128`
  (linux/amd64, user 1654). SDK container publishing pushed the immutable release tag and `latest`
  straight to GHCR on `mcr.microsoft.com/dotnet/aspnet:11.0-resolute-chiseled-extra`, with
  `InvariantGlobalization=false`. The host pulled the release tag before
  `/home/deploy/update.sh` replaced the container by digest.
- Production started at `2026-11-11T17:25:09.203497907Z`, keeping the
  `/opt/wiki/data -> /app/data` and `/opt/wiki/certs -> /app/certs` mounts.

No database change was needed: nothing under `src/WikiWikiWorld.Data` changed except the project
file. Nothing under `deploy/` changed, and the host's `update.sh` matches the repository copy.

## Verification

- Release build passed with only the existing ImageSharp NU1902/NU1903 advisories. All 395 .NET
  tests and all 27 browser-script tests passed on `net11.0`.
- **Before the swap, on the host.** `dotnet --list-runtimes` ran inside the new image and reported
  `Microsoft.NETCore.App 11.0.0` and `Microsoft.AspNetCore.App 11.0.0`, so the runtime starts on
  the host CPU (x86-64-v2 minimum; the host reports v2, v3 and v4).
- **Production checks.** These were anonymous only, over validated TLS.
  - 200 from English, root, Japanese and Arabic Magazedia, English Cinepedia, Fanfare, CRTR, the
    header helper, a representative article, search, and the sitemap.
  - The culture selector shows English, العربية and 日本語, so ICU is present in the image.
  - An article image is served as WebP through ImageSharp.
  - `/openapi/v1.json` reports OpenAPI 3.1.2 with 6 paths.
  - `js/recipient-autocomplete.js` and the new `css/pages/profile.css` are served. The owner-only
    messages page redirects anonymous readers.
  - The container reported zero restarts and no exception, `fail:` or `crit:` log lines.
- **Not verified.** Signed-in flows were not verified in production, because that needs a person
  signed in. That includes the restyled inbox, conversation and compose pages, which were also
  not browser-checked in development for this release; only the autocomplete script's tests ran.
  The Scalar API docs page was checked by HTTP request only, not rendered in a browser.

## Rollback

Previous image retained on the host:
`ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261111T172401Z-dae5d261ea47`.
Previous digest:
`ghcr.io/qingcharles/wikiwikiworld@sha256:dab097adcb235dca08f55511510553236b719267409d3cc07123f0bf18284bea`
(`release-20261108T182305Z-aa617f79ed75`, .NET 10). Pass it to `/home/deploy/update.sh` to roll
back. No database restore is needed: the schema is unchanged, and the .NET 10 build reads the
same database file.

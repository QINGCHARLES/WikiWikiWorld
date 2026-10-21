"""Publish wiki: documentation pages from seed_wiki_docs.py to a wiki database as new revisions.

Run locally with --export to write a payload of selected pages, copy the payload and this script
to the host, then run a read-only dry run followed by --apply. Existing revisions are preserved:
a changed page gets a new current revision under its existing canonical identity, an unchanged
page is skipped, and a missing page is created.
"""

import argparse
import json
import sqlite3
import uuid
from datetime import datetime, timezone
from pathlib import Path

SITE_ID = 1
CULTURE = "en"
TYPE_INFO = 4  # ArticleType.Info


def export(slugs, payload_path):
    """Write the named pages from seed_wiki_docs.ARTICLES to a JSON payload file."""
    from seed_wiki_docs import ARTICLES

    articles = {article["slug"]: article for article in ARTICLES}
    missing = [slug for slug in slugs if slug not in articles]
    if missing:
        raise SystemExit(f"Not defined in seed_wiki_docs.py: {', '.join(missing)}")
    Path(payload_path).write_text(json.dumps([articles[slug] for slug in slugs], ensure_ascii=False, indent=1), encoding="utf-8")
    print(f"Wrote {len(slugs)} pages to {payload_path}")


def publish(database_path, payload_path, author, reason, output_dir, apply):
    """Plan, and with apply=True commit, one transaction that brings every payload page up to date."""
    pages = json.loads(Path(payload_path).read_text(encoding="utf-8"))
    mode = "rw" if apply else "ro"
    connection = sqlite3.connect(f"{Path(database_path).resolve().as_uri()}?mode={mode}", uri=True, timeout=10)
    connection.row_factory = sqlite3.Row
    try:
        connection.execute("PRAGMA foreign_keys=ON")
        if apply:
            connection.execute("PRAGMA synchronous=FULL")
            connection.execute("BEGIN IMMEDIATE")
        author_row = connection.execute(
            "SELECT Id FROM Users WHERE NormalizedUserName=? AND DateDeleted IS NULL", (author.upper(),)
        ).fetchone()
        if author_row is None:
            raise RuntimeError(f"Author {author} does not exist.")
        now = datetime.now(timezone.utc).isoformat(sep=" ")
        backup = []
        for page in pages:
            rows = connection.execute(
                "SELECT * FROM ArticleRevisions WHERE SiteId=? AND Culture=? AND UrlSlug=?",
                (SITE_ID, CULTURE, page["slug"]),
            ).fetchall()
            current = [row for row in rows if row["IsCurrent"] == 1 and row["DateDeleted"] is None]
            if rows and len(current) != 1:
                raise RuntimeError(f"{page['slug']}: expected one current revision, found {len(current)} of {len(rows)} rows.")
            if current:
                existing = current[0]
                unchanged = (existing["Text"], existing["Title"], existing["DisplayTitle"]) == (
                    page["text"], page["title"], page["display_title"])
                if unchanged:
                    print(f"{page['slug']}: unchanged (revision {existing['Id']}), skipped")
                    continue
                print(f"{page['slug']}: new revision replacing {existing['Id']} ({len(existing['Text'])} -> {len(page['text'])} chars)")
                backup.append(dict(existing))
                canonical_id = existing["CanonicalArticleId"]
                if apply:
                    connection.execute("UPDATE ArticleRevisions SET IsCurrent=0 WHERE Id=?", (existing["Id"],))
            else:
                print(f"{page['slug']}: created ({len(page['text'])} chars)")
                canonical_id = str(uuid.uuid4())
            if apply:
                connection.execute(
                    """INSERT INTO ArticleRevisions
                    (CanonicalArticleId, SiteId, Culture, Title, DisplayTitle, UrlSlug, Type, Text,
                     CanonicalFileId, IsCurrent, RevisionReason, CreatedByUserId, DateCreated, DateDeleted)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, NULL, 1, ?, ?, ?, NULL)""",
                    (canonical_id, SITE_ID, CULTURE, page["title"], page["display_title"], page["slug"],
                     TYPE_INFO, page["text"], reason, author_row["Id"], now),
                )
        if not apply:
            print("Dry run: nothing written. Re-run with --apply to commit.")
            return
        if output_dir:
            stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
            backup_path = Path(output_dir) / f"wiki-docs-replaced-{stamp}.json"
            backup_path.write_text(json.dumps(backup, ensure_ascii=False, indent=1), encoding="utf-8")
            print(f"Replaced revisions saved to {backup_path}")
        connection.commit()
        print("Committed.")
    except Exception:
        if connection.in_transaction:
            connection.rollback()
        raise
    finally:
        connection.close()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--export", nargs="+", metavar="SLUG", help="Write these pages to --payload and exit.")
    parser.add_argument("--payload", required=True, help="JSON payload file to write (--export) or publish.")
    parser.add_argument("--database", help="Wiki SQLite database to publish into.")
    parser.add_argument("--author", default="QINGCHARLES", help="User name recorded as the revision author.")
    parser.add_argument("--reason", default="Update wiki documentation", help="Revision reason.")
    parser.add_argument("--output-dir", help="Directory for a JSON copy of replaced revisions.")
    parser.add_argument("--apply", action="store_true", help="Commit the changes; without it nothing is written.")
    arguments = parser.parse_args()
    if arguments.export:
        export(arguments.export, arguments.payload)
    elif not arguments.database:
        parser.error("--database is required unless --export is given.")
    else:
        publish(arguments.database, arguments.payload, arguments.author, arguments.reason, arguments.output_dir, arguments.apply)

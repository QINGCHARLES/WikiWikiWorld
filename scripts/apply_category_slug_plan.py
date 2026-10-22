"""Apply an audited category-slug plan as new revisions, with optimistic concurrency checks.

Plans contain Id, SiteId, Culture, Article, OriginalSha256 and Text, generated from
the actual Markdown parser. Run without --apply to validate without writing.
Back up the database and deploy the explicit-slug parser before applying.
"""

import argparse
import hashlib
import json
import sqlite3
from datetime import datetime, timezone
from pathlib import Path


REPLACEMENTS = {
    "Men’s magazines": "mens-magazines",
    "Men's Fashion and Lifestyle": "mens-fashion-and-lifestyle",
    "GQ Style (USA) magazines": "gq-style-usa-magazines",
    "Publication issues with covers featuring Robert Downey Jr.":
        "publication-issues-with-covers-featuring-robert-downey-jr",
}
REASON = "Adopt cleaner category slugs with explicit identities; preserve display labels."


def apply_plan(database_path, plan_path, author_id, apply):
    """Validate every planned edit, then atomically append revisions without replacing history."""
    plan = json.loads(Path(plan_path).read_text(encoding="utf-8-sig"))
    if not plan or len({item["Id"] for item in plan}) != len(plan):
        raise ValueError("Expected a nonempty plan with distinct source revisions.")
    connection = sqlite3.connect(
        f"{Path(database_path).resolve().as_uri()}?mode={'rw' if apply else 'ro'}",
        uri=True, timeout=30,
    )
    connection.row_factory = sqlite3.Row
    try:
        connection.execute("PRAGMA foreign_keys=ON")
        if apply:
            connection.execute("PRAGMA synchronous=FULL")
        connection.execute("BEGIN IMMEDIATE" if apply else "BEGIN")
        if not connection.execute(
            "SELECT Id FROM Users WHERE Id=? AND DateDeleted IS NULL", (author_id,)
        ).fetchone():
            raise ValueError("The maintenance author must be an existing active user.")
        pending = []
        for item in plan:
            rows = connection.execute(
                "SELECT Id, Text FROM ArticleRevisions WHERE SiteId=? AND Culture=? "
                "AND UrlSlug=? AND IsCurrent=1 AND DateDeleted IS NULL",
                (item["SiteId"], item["Culture"], item["Article"]),
            ).fetchall()
            if len(rows) != 1:
                raise ValueError(f"Expected one current revision: {item['Article']}")
            row = rows[0]
            if row["Text"] == item["Text"]:
                continue
            digest = hashlib.sha256(row["Text"].encode("utf-8")).hexdigest().upper()
            if row["Id"] != item["Id"] or digest != item["OriginalSha256"]:
                raise ValueError(f"Article changed since audit: {item['Article']}")
            expected = row["Text"]
            for label, slug in REPLACEMENTS.items():
                expected = expected.replace(
                    "{{Category " + label + "}}",
                    "{{Category " + label + "|#|" + slug + "}}",
                )
            if expected == row["Text"] or expected != item["Text"]:
                raise ValueError(f"Plan exceeds approved replacements: {item['Article']}")
            pending.append(item)
        results = []
        if apply:
            timestamp = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%f") + "0+00:00"
            for item in pending:
                connection.execute("UPDATE ArticleRevisions SET IsCurrent=0 WHERE Id=?", (item["Id"],))
                cursor = connection.execute(
                    """INSERT INTO ArticleRevisions
                    (CanonicalArticleId, SiteId, Culture, Title, DisplayTitle, UrlSlug,
                     IsCurrent, Type, Text, CanonicalFileId, RevisionReason,
                     CreatedByUserId, DateCreated, DateDeleted)
                    SELECT CanonicalArticleId, SiteId, Culture, Title, DisplayTitle, UrlSlug,
                           1, Type, ?, CanonicalFileId, ?, ?, ?, NULL
                    FROM ArticleRevisions WHERE Id=?""",
                    (item["Text"], REASON, author_id, timestamp, item["Id"]),
                )
                results.append({"article": item["Article"], "oldRevision": item["Id"], "newRevision": cursor.lastrowid})
            connection.commit()
        else:
            connection.rollback()
        print(json.dumps({"applied": apply, "pending": len(pending), "alreadyApplied": len(plan) - len(pending), "revisions": results}, ensure_ascii=False))
    except Exception:
        connection.rollback()
        raise
    finally:
        connection.close()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--db", required=True)
    parser.add_argument("--plan", required=True)
    parser.add_argument("--author", required=True)
    parser.add_argument("--apply", action="store_true")
    arguments = parser.parse_args()
    apply_plan(arguments.db, arguments.plan, arguments.author, arguments.apply)

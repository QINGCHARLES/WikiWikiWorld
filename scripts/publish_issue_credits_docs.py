"""Add the credits guide and a revisioned formatting-index link without replacing existing writing."""

import argparse
import sqlite3
import uuid
from datetime import datetime, timezone
from pathlib import Path

from seed_wiki_docs import CULTURE, DB_PATH, SITE_ID, TYPE_INFO, USER_ID


def publish(database_path):
    """Publish only the new guide and an additive index revision in one transaction."""
    guide = (Path(__file__).resolve().parents[1] / "doc" / "issue-credits.md").read_text(encoding="utf-8")
    connection = sqlite3.connect(f"{Path(database_path).resolve().as_uri()}?mode=rw", uri=True, timeout=10)
    connection.row_factory = sqlite3.Row
    try:
        connection.execute("PRAGMA foreign_keys=ON")
        connection.execute("PRAGMA synchronous=FULL")
        connection.execute("BEGIN IMMEDIATE")
        if not connection.execute("SELECT Id FROM Users WHERE Id=?", (USER_ID,)).fetchone():
            raise RuntimeError("The documentation author configured in seed_wiki_docs.py does not exist.")
        index_rows = connection.execute(
            "SELECT * FROM ArticleRevisions WHERE SiteId=? AND Culture=? AND UrlSlug=? AND IsCurrent=1 AND DateDeleted IS NULL",
            (SITE_ID, CULTURE, "wiki:formatting"),
        ).fetchall()
        if len(index_rows) != 1:
            raise RuntimeError("Expected exactly one current formatting guide in the configured site/culture.")
        index = index_rows[0]
        existing = connection.execute(
            "SELECT Id FROM ArticleRevisions WHERE SiteId=? AND Culture=? AND UrlSlug=?",
            (SITE_ID, CULTURE, "wiki:issue-credits"),
        ).fetchone()
        created = False
        if existing is None:
            insert_revision(connection, dict(
                CanonicalArticleId=str(uuid.uuid4()), SiteId=SITE_ID, Culture=CULTURE,
                Title="Wiki: Issue Credits Directive", DisplayTitle="Wiki: Issue Credits Directive",
                UrlSlug="wiki:issue-credits", Type=TYPE_INFO, Text=guide, CanonicalFileId=None,
            ))
            created = True
        linked = "/wiki:issue-credits" not in index["Text"]
        if linked:
            updated = dict(index)
            updated["Text"] += (
                "\n\n## Issue credits\n\n"
                "* [**Issue Credits** `{{IssueCredits}}`](/wiki:issue-credits)"
                " — Featured and collapsible contributor groups with roles and linked names.\n"
            )
            connection.execute("UPDATE ArticleRevisions SET IsCurrent=0 WHERE Id=?", (index["Id"],))
            insert_revision(connection, updated)
        connection.commit()
        print(f"wiki:issue-credits created={created}; formatting index revision added={linked}")
    except Exception:
        connection.rollback()
        raise
    finally:
        connection.close()


def insert_revision(connection, values):
    """Insert an information-page revision while preserving its canonical identity and metadata."""
    columns = (
        "CanonicalArticleId", "SiteId", "Culture", "Title", "DisplayTitle", "UrlSlug",
        "Type", "Text", "CanonicalFileId",
    )
    connection.execute(
        """INSERT INTO ArticleRevisions
        (CanonicalArticleId, SiteId, Culture, Title, DisplayTitle, UrlSlug, Type, Text,
         CanonicalFileId, IsCurrent, RevisionReason, CreatedByUserId, DateCreated, DateDeleted)
        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, 1, ?, ?, ?, NULL)""",
        tuple(values[column] for column in columns) + (
            "Document IssueCredits Markdown extension", USER_ID,
            datetime.now(timezone.utc).isoformat(sep=" "),
        ),
    )


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--db", default=DB_PATH, help="Existing local wiki database.")
    publish(parser.parse_args().db)

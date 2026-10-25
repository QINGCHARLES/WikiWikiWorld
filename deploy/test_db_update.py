"""Exercise the maintenance runner against disposable real SQLite databases."""

from contextlib import closing, redirect_stdout
import io
from pathlib import Path
import sqlite3
import subprocess
import sys
import tempfile
import unittest

from db_update import Apply


class DatabaseUpdateTests(unittest.TestCase):
    """Cover backup contents, transaction failures, assertions and existing SQL."""

    def setUp(self) -> None:
        """Create an isolated database with a committed row for each test."""
        self.Directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.Directory.cleanup)
        self.Root = Path(self.Directory.name)
        self.Database = self.Root / "app.db"
        self.Sql = self.Root / "update.sql"
        self.Verify = self.Root / "verify.sql"
        self.Verify.write_text("SELECT 1;", encoding="utf-8")
        with closing(sqlite3.connect(self.Database)) as Connection:
            Connection.executescript("CREATE TABLE Items(Id INTEGER PRIMARY KEY); INSERT INTO Items VALUES (1);")

    def RunUpdate(self, Sql: str, Precheck: Path | None = None) -> Path:
        """Run the supplied script while suppressing routine success output."""
        self.Sql.write_text(Sql, encoding="utf-8")
        with redirect_stdout(io.StringIO()):
            return Apply(self.Database, self.Sql, self.Verify, Precheck)

    def test_backup_contains_committed_wal_data(self) -> None:
        """Backups include committed WAL rows and precede migration changes."""
        with closing(sqlite3.connect(self.Database)) as Writer:
            Writer.execute("PRAGMA journal_mode=WAL")
            Writer.execute("INSERT INTO Items VALUES (2)")
            Writer.commit()
            Backup = self.RunUpdate("BEGIN IMMEDIATE; INSERT INTO Items VALUES (3); COMMIT;")
            with closing(sqlite3.connect(Backup)) as Snapshot:
                self.assertEqual(Snapshot.execute("SELECT Id FROM Items ORDER BY Id").fetchall(), [(1,), (2,)])
            self.assertEqual(Writer.execute("SELECT COUNT(*) FROM Items").fetchone(), (3,))

    def test_sql_failure_rolls_back_open_transaction(self) -> None:
        """A statement error stops subsequent statements and rolls back pending writes."""
        with self.assertRaises(sqlite3.Error):
            self.RunUpdate("BEGIN IMMEDIATE; INSERT INTO Items VALUES (2); INVALID SQL; COMMIT;")
        with closing(sqlite3.connect(self.Database)) as Connection:
            self.assertEqual(Connection.execute("SELECT Id FROM Items").fetchall(), [(1,)])
        self.assertEqual(len(list((self.Root / ".db-backups").glob("*.db"))), 1)

    def test_open_transaction_is_not_reported_as_success(self) -> None:
        """Missing COMMIT fails and the connection close undoes pending changes."""
        with self.assertRaisesRegex(RuntimeError, "uncommitted"):
            self.RunUpdate("BEGIN; INSERT INTO Items VALUES (2);")
        with closing(sqlite3.connect(self.Database)) as Connection:
            self.assertEqual(Connection.execute("SELECT COUNT(*) FROM Items").fetchone(), (1,))

    def test_failed_verification_does_not_restore_over_commits(self) -> None:
        """A failed postcondition reports failure without discarding committed changes."""
        self.Verify.write_text("SELECT 0;", encoding="utf-8")
        with self.assertRaisesRegex(RuntimeError, "Verification failed"):
            self.RunUpdate("BEGIN; INSERT INTO Items VALUES (2); COMMIT;")
        with closing(sqlite3.connect(self.Database)) as Connection:
            self.assertEqual(Connection.execute("SELECT COUNT(*) FROM Items").fetchone(), (2,))

    def test_precheck_stops_before_backup_or_write(self) -> None:
        """A false precondition prevents the migration from being attempted."""
        Precheck = self.Root / "precheck.sql"
        Precheck.write_text("SELECT 0;", encoding="utf-8")
        with self.assertRaisesRegex(RuntimeError, "Precheck failed"):
            self.RunUpdate("INSERT INTO Items VALUES (2);", Precheck)
        self.assertFalse((self.Root / ".db-backups").exists())

    def test_missing_database_is_not_created(self) -> None:
        """A misspelled database path cannot create an empty database."""
        self.Database = self.Root / "missing.db"
        with self.assertRaises(FileNotFoundError):
            self.RunUpdate("CREATE TABLE Wrong(Id);")
        self.assertFalse(self.Database.exists())

    def test_verification_cannot_write(self) -> None:
        """An accidentally supplied mutation fails instead of changing data during checks."""
        self.Verify.write_text("DELETE FROM Items RETURNING 1;", encoding="utf-8")
        with self.assertRaises(sqlite3.Error):
            self.RunUpdate("BEGIN; COMMIT;")
        with closing(sqlite3.connect(self.Database)) as Connection:
            self.assertEqual(Connection.execute("SELECT COUNT(*) FROM Items").fetchone(), (1,))

    def test_cli_failure_returns_nonzero_and_reports_backup(self) -> None:
        """Operators receive a failed process status and a usable recovery location."""
        self.Sql.write_text("BEGIN; INVALID SQL; COMMIT;", encoding="utf-8")
        Result = subprocess.run(
            [sys.executable, "-B", str(Path(__file__).with_name("db_update.py")),
             "test", str(self.Database), str(self.Sql), str(self.Verify)],
            capture_output=True, text=True, check=False)
        self.assertEqual(Result.returncode, 1)
        self.assertIn("Backup complete:", Result.stdout)
        self.assertIn("FAILED:", Result.stderr)
        self.assertNotIn("SUCCESS:", Result.stdout)

    def test_existing_bookmark_upgrade_and_repeat_failure(self) -> None:
        """The actual repository migration preserves private lists and fails on repeat."""
        with closing(sqlite3.connect(self.Database)) as Connection:
            Connection.executescript("""
                CREATE TABLE BookmarkLists(Id INTEGER PRIMARY KEY, Name TEXT);
                CREATE TABLE Bookmarks(Id INTEGER PRIMARY KEY, BookmarkListId INTEGER,
                    DateCreated TEXT, DateDeleted TEXT);
                INSERT INTO BookmarkLists VALUES (1, 'Keep me');
                INSERT INTO Bookmarks VALUES (1, 1, '2026-10-24', NULL);
            """)
        self.Verify.write_text("SELECT COUNT(*) = 1 FROM BookmarkLists WHERE Name = 'Keep me' AND IsPublic = 0;",
                               encoding="utf-8")
        Migration = Path(__file__).resolve().parents[1] / "src/WikiWikiWorld.Data/SqliteScripts/AddBookmarkListVisibility.sql"
        Sql = Migration.read_text(encoding="utf-8-sig")
        self.RunUpdate(Sql)
        with self.assertRaises(sqlite3.Error):
            self.RunUpdate(Sql)


if __name__ == "__main__":
    unittest.main()

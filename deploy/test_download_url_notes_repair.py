"""Verify the shipped notes repair on disposable populated and empty SQLite databases."""

from contextlib import closing, redirect_stdout
import io
from pathlib import Path
import sqlite3
import tempfile
import unittest

from db_update import Apply


class DownloadUrlNotesRepairTests(unittest.TestCase):
    """Exercise data preservation, corrected relationships, rollback and fresh schema behavior."""

    def setUp(self) -> None:
        """Create an isolated full application schema with the historical foreign-key defect."""
        self.Directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.Directory.cleanup)
        self.Database = Path(self.Directory.name) / "app.db"
        self.Scripts = Path(__file__).resolve().parents[1] / "src/WikiWikiWorld.Data/SqliteScripts"
        self.Repair = self.Scripts / "RepairDownloadUrlNotesForeignKey.sql"
        self.Precheck = self.Scripts / "CheckDownloadUrlNotesForeignKeyBefore.sql"
        self.Verify = self.Scripts / "VerifyDownloadUrlNotesForeignKey.sql"
        self.CreateFixture(Legacy=True)

    def CreateFixture(self, Legacy: bool) -> None:
        """Build the real creation script and add valid parents for relationship tests."""
        Sql = (self.Scripts / "Create.sql").read_text(encoding="utf-8-sig")
        if Legacy:
            Sql = Sql.replace("REFERENCES DownloadUrls (Id)", "REFERENCES DownloadUrls_old (Id)")
        with closing(sqlite3.connect(self.Database)) as Connection:
            Connection.executescript(Sql)
            Connection.executescript("""
                INSERT INTO Users (Id, EmailConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount, DateCreated)
                VALUES ('author', 0, 0, 0, 0, '2026-11-07T00:00:00Z');
                INSERT INTO DownloadUrls (Id, SiteId, HashSha256, Filename, OriginalFilename, MimeType,
                    FileSizeBytes, FilenameChanged, NeedsRedeployment, UploadedByUserId, DateCreated)
                VALUES (1, 1, 'first', 'first.pdf', 'first.pdf', 'application/pdf', 100, 0, 0, 'author', '2026-11-07T00:00:00Z'),
                    (2, 1, 'second', 'second.pdf', 'second.pdf', 'application/pdf', 200, 0, 0, 'author', '2026-11-07T00:00:00Z');
            """)

    def PopulateLegacyNotes(self, DownloadUrlId: int = 1) -> None:
        """Simulate retained legacy notes and a previously issued, deleted higher identity."""
        with closing(sqlite3.connect(self.Database)) as Connection:
            Connection.execute("PRAGMA foreign_keys=OFF")
            Connection.execute("INSERT INTO DownloadUrlNotes VALUES (7, ?, 'author', 'ja', 'Keep café / 日本語', '2026-11-07T00:00:00Z')",
                               (DownloadUrlId,))
            Connection.execute("INSERT INTO DownloadUrlNotes VALUES (12, 1, 'author', 'en', 'Deleted note', '2026-11-07T00:00:00Z')")
            Connection.execute("DELETE FROM DownloadUrlNotes WHERE Id = 12")
            Connection.commit()

    def ApplyRepair(self, Precheck: bool = True) -> Path:
        """Use the production runner implementation and its exact shipped assertions."""
        with redirect_stdout(io.StringIO()):
            return Apply(self.Database, self.Repair, self.Verify, self.Precheck if Precheck else None)

    def AssertRelationships(self, Connection: sqlite3.Connection) -> None:
        """Require valid notes, rejection of missing parents, and both deletion cascades."""
        Connection.execute("PRAGMA foreign_keys=ON")
        Connection.execute("INSERT INTO DownloadUrlNotes (DownloadUrlId, UserId, Culture, Text, DateCreated) VALUES (2, 'author', 'en', 'New note', '2026-11-07T00:00:00Z')")
        for DownloadUrlId, UserId in [(999, "author"), (1, "missing-user")]:
            with self.assertRaises(sqlite3.IntegrityError):
                Connection.execute("INSERT INTO DownloadUrlNotes (DownloadUrlId, UserId, Culture, Text, DateCreated) VALUES (?, ?, 'en', 'Invalid', '2026-11-07T00:00:00Z')",
                                   (DownloadUrlId, UserId))
        Connection.execute("DELETE FROM DownloadUrls WHERE Id = 1")
        self.assertEqual(Connection.execute("SELECT COUNT(*) FROM DownloadUrlNotes WHERE DownloadUrlId = 1").fetchone(), (0,))
        self.assertEqual(Connection.execute("SELECT COUNT(*) FROM DownloadUrlNotes").fetchone(), (1,))
        Connection.execute("DELETE FROM Users WHERE Id = 'author'")
        self.assertEqual(Connection.execute("SELECT COUNT(*) FROM DownloadUrlNotes").fetchone(), (0,))
        self.assertEqual(Connection.execute("PRAGMA foreign_key_check").fetchall(), [])

    def test_populated_repair_preserves_rows_identity_backup_and_relationships(self) -> None:
        """Existing note bytes and deleted identity history survive the real maintenance path."""
        self.PopulateLegacyNotes()
        with closing(sqlite3.connect(self.Database)) as Connection:
            Before = Connection.execute("SELECT * FROM DownloadUrlNotes ORDER BY Id").fetchall()
        Backup = self.ApplyRepair()
        with closing(sqlite3.connect(Backup)) as Snapshot:
            self.assertEqual(Snapshot.execute("SELECT * FROM DownloadUrlNotes ORDER BY Id").fetchall(), Before)
            self.assertEqual(Snapshot.execute(self.Precheck.read_text()).fetchall(), [(1,)])
        with closing(sqlite3.connect(self.Database)) as Connection:
            self.assertEqual(Connection.execute("SELECT * FROM DownloadUrlNotes ORDER BY Id").fetchall(), Before)
            self.assertEqual(Connection.execute("SELECT seq FROM sqlite_sequence WHERE name = 'DownloadUrlNotes'").fetchone(), (12,))
            self.assertEqual(Connection.execute(self.Verify.read_text()).fetchall(), [(1,)])
            self.AssertRelationships(Connection)
            self.assertEqual(Connection.execute("SELECT seq FROM sqlite_sequence WHERE name = 'DownloadUrlNotes'").fetchone(), (13,))

    def test_empty_repair_restores_user_deletion_and_rejects_repeat(self) -> None:
        """The live empty-table case becomes usable and cannot accidentally be reapplied."""
        self.ApplyRepair()
        with self.assertRaisesRegex(RuntimeError, "Precheck failed"):
            self.ApplyRepair()
        self.assertEqual(len(list((self.Database.parent / ".db-backups").glob("*.db"))), 1)
        with closing(sqlite3.connect(self.Database)) as Connection:
            Connection.execute("PRAGMA foreign_keys=ON")
            Connection.execute("DELETE FROM Users WHERE Id = 'author'")
            self.assertEqual(Connection.execute("SELECT COUNT(*) FROM Users").fetchone(), (0,))

    def test_orphan_notes_refuse_precheck_and_roll_back_if_precheck_is_skipped(self) -> None:
        """Unresolvable notes are retained, never silently discarded to make the repair pass."""
        self.PopulateLegacyNotes(DownloadUrlId=999)
        with self.assertRaisesRegex(RuntimeError, "Precheck failed"):
            self.ApplyRepair()
        self.assertFalse((self.Database.parent / ".db-backups").exists())
        with self.assertRaises(sqlite3.IntegrityError):
            self.ApplyRepair(Precheck=False)
        with closing(sqlite3.connect(self.Database)) as Connection:
            self.assertEqual(Connection.execute("SELECT Id, DownloadUrlId FROM DownloadUrlNotes").fetchall(), [(7, 999)])
            self.assertEqual(Connection.execute("SELECT COUNT(*) FROM sqlite_schema WHERE name = 'DownloadUrlNotes_Repaired'").fetchone(), (0,))
            self.assertIn("DownloadUrls_old", [Row[2] for Row in Connection.execute("PRAGMA foreign_key_list('DownloadUrlNotes')")])

    def test_fresh_schema_supports_notes_and_both_cascades(self) -> None:
        """New databases have the corrected relationships without requiring a repair."""
        self.Database = self.Database.with_name("fresh.db")
        self.CreateFixture(Legacy=False)
        with closing(sqlite3.connect(self.Database)) as Connection:
            self.assertEqual(Connection.execute(self.Verify.read_text()).fetchall(), [(1,)])
            Connection.execute("PRAGMA foreign_keys=ON")
            Connection.execute("INSERT INTO DownloadUrlNotes VALUES (7, 1, 'author', 'en', 'Existing note', '2026-11-07T00:00:00Z')")
            self.AssertRelationships(Connection)

    def test_original_copyright_migration_does_not_recreate_the_defect(self) -> None:
        """The historical upgrade creates notes after replacing their parent table."""
        with closing(sqlite3.connect(":memory:")) as Connection:
            Connection.executescript("""
                PRAGMA foreign_keys=ON;
                CREATE TABLE Users (Id TEXT PRIMARY KEY);
                INSERT INTO Users VALUES ('author');
                CREATE TABLE DownloadUrls (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT, SiteId INTEGER NOT NULL,
                    HashSha256 TEXT NOT NULL, Filename TEXT NOT NULL, MimeType TEXT NOT NULL,
                    FileSizeBytes INTEGER NOT NULL, DownloadUrls TEXT NULL, Quality INTEGER NULL,
                    NeedsOcr INTEGER NULL, IsComplete INTEGER NULL, CreatedByUserId TEXT NOT NULL,
                    DateCreated TEXT NOT NULL, DateModified TEXT NULL, DateDeleted TEXT NULL
                );
                CREATE INDEX IX_DownloadUrls_SiteId_HashSha256 ON DownloadUrls (SiteId, HashSha256);
                INSERT INTO DownloadUrls VALUES
                    (1, 1, 'first', 'first.pdf', 'application/pdf', 100, NULL, NULL, NULL, NULL, 'author', '2026-11-07T00:00:00Z', NULL, NULL),
                    (2, 1, 'second', 'second.pdf', 'application/pdf', 200, NULL, NULL, NULL, NULL, 'author', '2026-11-07T00:00:00Z', NULL, NULL);
            """)
            Connection.executescript((self.Scripts / "MigrateDownloadUrlsCopyrightStatus.sql").read_text(encoding="utf-8-sig"))
            self.assertEqual(Connection.execute(self.Verify.read_text()).fetchall(), [(1,)])
            self.assertEqual(Connection.execute("SELECT Id, Filename, OriginalFilename, UploadedByUserId FROM DownloadUrls ORDER BY Id").fetchall(),
                             [(1, "first.pdf", "first.pdf", "author"), (2, "second.pdf", "second.pdf", "author")])
            Connection.execute("INSERT INTO DownloadUrlNotes VALUES (7, 1, 'author', 'en', 'Preserve me', '2026-11-07T00:00:00Z')")
            self.AssertRelationships(Connection)


if __name__ == "__main__":
    unittest.main()

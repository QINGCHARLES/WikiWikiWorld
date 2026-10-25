"""Apply reviewed SQLite maintenance SQL with a backup and explicit assertions."""

import argparse
from contextlib import closing
from datetime import datetime, timezone
from pathlib import Path
import sqlite3
import sys
import tempfile


def AssertQuery(Connection: sqlite3.Connection, Sql: str, Label: str) -> None:
    """Require one read-only SQL query to return exactly one integer true value."""
    Connection.execute("PRAGMA query_only=ON")
    try:
        if Connection.execute(Sql).fetchall() != [(1,)]:
            raise RuntimeError(f"{Label} failed: expected one row containing 1.")
    finally:
        Connection.execute("PRAGMA query_only=OFF")


def Apply(Database: Path, SqlFile: Path, VerifyFile: Path, PrecheckFile: Path | None) -> Path:
    """Back up an existing database, apply SQL, and verify; never restore automatically.

    The SQL owns transaction boundaries. Closing the connection rolls back any
    uncommitted transaction on failure; earlier explicit commits remain applied.
    """
    Database = Database.resolve(strict=True)
    Sql = SqlFile.read_text(encoding="utf-8-sig")
    Verify = VerifyFile.read_text(encoding="utf-8-sig")
    Precheck = PrecheckFile.read_text(encoding="utf-8-sig") if PrecheckFile else None
    with closing(sqlite3.connect(Database.as_uri() + "?mode=rw", uri=True,
                                 isolation_level=None, timeout=30)) as Connection:
        Connection.execute("PRAGMA foreign_keys=ON")
        Connection.execute("PRAGMA synchronous=FULL")
        if Precheck is not None:
            AssertQuery(Connection, Precheck, "Precheck")

        BackupDirectory = Database.parent / ".db-backups"
        BackupDirectory.mkdir(mode=0o700, exist_ok=True)
        Stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
        with tempfile.NamedTemporaryFile(prefix=f"{Database.name}-{Stamp}-", suffix=".db",
                                         dir=BackupDirectory, delete=False) as BackupFile:
            Backup = Path(BackupFile.name)
        with closing(sqlite3.connect(Backup)) as Destination:
            Connection.backup(Destination)
            if Destination.execute("PRAGMA integrity_check").fetchall() != [("ok",)]:
                raise RuntimeError(f"Backup integrity check failed: {Backup}")
        print(f"Backup complete: {Backup}", flush=True)
        print(f"Applying: {SqlFile}", flush=True)
        Connection.executescript(Sql)
        if Connection.in_transaction:
            raise RuntimeError("SQL left an uncommitted transaction; rolling it back on close.")
        if Connection.execute("PRAGMA integrity_check").fetchall() != [("ok",)]:
            raise RuntimeError("Database integrity check failed.")
        if Connection.execute("PRAGMA foreign_key_check").fetchone() is not None:
            raise RuntimeError("Database foreign-key check failed.")
        AssertQuery(Connection, Verify, "Verification")
        print("SUCCESS: SQL applied; integrity, foreign keys and verification passed.", flush=True)
        return Backup


def Main() -> int:
    """Parse explicit target arguments and report failures with a nonzero exit code."""
    Parser = argparse.ArgumentParser(description=__doc__)
    Parser.add_argument("environment", choices=("production", "development", "test"))
    Parser.add_argument("database", type=Path)
    Parser.add_argument("sql", type=Path)
    Parser.add_argument("verify", type=Path)
    Parser.add_argument("--precheck", type=Path)
    Arguments = Parser.parse_args()
    print(f"Environment: {Arguments.environment}; database: {Arguments.database}", flush=True)
    try:
        Apply(Arguments.database, Arguments.sql, Arguments.verify, Arguments.precheck)
        return 0
    except (OSError, sqlite3.Error, RuntimeError) as Error:
        print(f"FAILED: {Error}\nStop and inspect the database before retrying. "
              "Explicitly committed changes may remain; no backup was restored.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(Main())

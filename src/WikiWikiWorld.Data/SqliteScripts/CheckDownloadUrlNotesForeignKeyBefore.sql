SELECT EXISTS (
    SELECT 1 FROM pragma_foreign_key_list('DownloadUrlNotes')
    WHERE "table" = 'DownloadUrls_old' AND "from" = 'DownloadUrlId' AND "to" = 'Id' AND on_delete = 'CASCADE'
) AND EXISTS (
    SELECT 1 FROM pragma_foreign_key_list('DownloadUrlNotes')
    WHERE "table" = 'Users' AND "from" = 'UserId' AND "to" = 'Id' AND on_delete = 'CASCADE'
) AND (SELECT COUNT(*) FROM pragma_foreign_key_list('DownloadUrlNotes')) = 2
  AND (SELECT COUNT(*) FROM pragma_table_info('DownloadUrlNotes')) = 6
  AND (SELECT COUNT(*) FROM sqlite_schema WHERE tbl_name = 'DownloadUrlNotes' AND type IN ('index', 'trigger')) = 1
  AND EXISTS (SELECT 1 FROM sqlite_schema WHERE type = 'index' AND name = 'IX_DownloadUrlNotes_DownloadUrlId')
  AND NOT EXISTS (SELECT 1 FROM sqlite_schema WHERE name IN ('DownloadUrls_old', 'DownloadUrlNotes_Repaired'))
  AND NOT EXISTS (
    SELECT 1 FROM sqlite_schema AS Tables, pragma_foreign_key_list(Tables.name) AS ForeignKeys
    WHERE Tables.type = 'table' AND ForeignKeys."table" = 'DownloadUrlNotes'
) AND NOT EXISTS (
    SELECT 1 FROM DownloadUrlNotes AS Notes
    LEFT JOIN DownloadUrls AS Downloads ON Downloads.Id = Notes.DownloadUrlId
    LEFT JOIN Users ON Users.Id = Notes.UserId
    WHERE Downloads.Id IS NULL OR Users.Id IS NULL
);

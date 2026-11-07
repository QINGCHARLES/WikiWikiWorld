SELECT EXISTS (
    SELECT 1 FROM pragma_foreign_key_list('DownloadUrlNotes')
    WHERE "table" = 'DownloadUrls' AND "from" = 'DownloadUrlId' AND "to" = 'Id' AND on_delete = 'CASCADE'
) AND EXISTS (
    SELECT 1 FROM pragma_foreign_key_list('DownloadUrlNotes')
    WHERE "table" = 'Users' AND "from" = 'UserId' AND "to" = 'Id' AND on_delete = 'CASCADE'
) AND (SELECT COUNT(*) FROM pragma_foreign_key_list('DownloadUrlNotes')) = 2
  AND (SELECT COUNT(*) FROM pragma_table_info('DownloadUrlNotes')) = 6
  AND EXISTS (
    SELECT 1 FROM pragma_index_list('DownloadUrlNotes')
    WHERE name = 'IX_DownloadUrlNotes_DownloadUrlId' AND "unique" = 0 AND partial = 0
) AND (SELECT COUNT(*) FROM pragma_index_info('IX_DownloadUrlNotes_DownloadUrlId')) = 1
  AND EXISTS (SELECT 1 FROM pragma_index_info('IX_DownloadUrlNotes_DownloadUrlId') WHERE name = 'DownloadUrlId')
  AND NOT EXISTS (SELECT 1 FROM sqlite_schema WHERE name = 'DownloadUrlNotes_Repaired')
  AND NOT EXISTS (
    SELECT 1 FROM sqlite_schema AS Tables, pragma_foreign_key_list(Tables.name) AS ForeignKeys
    WHERE Tables.type = 'table' AND ForeignKeys."table" = 'DownloadUrls_old'
) AND NOT EXISTS (SELECT 1 FROM pragma_foreign_key_check);

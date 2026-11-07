SELECT EXISTS (
    SELECT 1 FROM pragma_table_info('FileRevisions')
    WHERE name = 'StagingUploadId' AND type = 'TEXT' AND "notnull" = 0
) AND EXISTS (
    SELECT 1 FROM pragma_index_list('FileRevisions')
    WHERE name = 'UX_FileRevisions_StagingUploadId' AND "unique" = 1 AND partial = 1
) AND (SELECT COUNT(*) FROM pragma_index_info('UX_FileRevisions_StagingUploadId')) = 1
  AND EXISTS (SELECT 1 FROM pragma_index_info('UX_FileRevisions_StagingUploadId') WHERE name = 'StagingUploadId')
  AND NOT EXISTS (SELECT 1 FROM pragma_foreign_key_check);

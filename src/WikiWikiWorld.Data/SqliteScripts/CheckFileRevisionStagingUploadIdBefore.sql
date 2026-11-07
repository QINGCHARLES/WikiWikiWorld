SELECT EXISTS (SELECT 1 FROM pragma_table_info('FileRevisions') WHERE name = 'CanonicalFileId')
    AND NOT EXISTS (SELECT 1 FROM pragma_table_info('FileRevisions') WHERE name = 'StagingUploadId')
    AND NOT EXISTS (SELECT 1 FROM pragma_index_list('FileRevisions') WHERE name = 'UX_FileRevisions_StagingUploadId');

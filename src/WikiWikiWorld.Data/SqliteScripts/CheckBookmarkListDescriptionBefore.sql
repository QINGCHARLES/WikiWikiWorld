SELECT EXISTS (SELECT 1 FROM sqlite_schema WHERE type = 'table' AND name = 'BookmarkLists')
    AND EXISTS (SELECT 1 FROM pragma_table_info('BookmarkLists') WHERE name = 'IsPublic')
    AND NOT EXISTS (SELECT 1 FROM pragma_table_info('BookmarkLists') WHERE name = 'Description');

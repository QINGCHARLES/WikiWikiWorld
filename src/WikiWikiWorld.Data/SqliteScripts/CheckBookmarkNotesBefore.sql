SELECT EXISTS (SELECT 1 FROM sqlite_schema WHERE type = 'table' AND name = 'Bookmarks')
    AND EXISTS (SELECT 1 FROM pragma_table_info('Bookmarks') WHERE name = 'BookmarkListId')
    AND EXISTS (SELECT 1 FROM pragma_table_info('BookmarkLists') WHERE name = 'Description')
    AND NOT EXISTS (SELECT 1 FROM pragma_table_info('Bookmarks') WHERE name = 'Notes');

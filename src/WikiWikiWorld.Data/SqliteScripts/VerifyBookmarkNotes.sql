SELECT EXISTS (
    SELECT 1 FROM pragma_table_info('Bookmarks')
    WHERE name = 'Notes' AND type = 'TEXT' AND "notnull" = 0 AND dflt_value IS NULL AND pk = 0
) AND NOT EXISTS (SELECT 1 FROM pragma_foreign_key_check);

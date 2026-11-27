SELECT EXISTS (SELECT 1 FROM sqlite_schema WHERE type = 'table' AND name = 'Users')
    AND NOT EXISTS (SELECT 1 FROM pragma_table_info('Users') WHERE name = 'IsDisabled');

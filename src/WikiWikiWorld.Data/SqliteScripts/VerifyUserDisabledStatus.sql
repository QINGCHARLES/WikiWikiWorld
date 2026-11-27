SELECT EXISTS (
    SELECT 1 FROM pragma_table_info('Users')
    WHERE name = 'IsDisabled' AND type = 'INTEGER' AND "notnull" = 1 AND dflt_value = '0'
) AND NOT EXISTS (SELECT 1 FROM Users WHERE IsDisabled <> 0);

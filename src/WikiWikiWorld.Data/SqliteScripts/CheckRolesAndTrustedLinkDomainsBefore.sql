SELECT EXISTS (SELECT 1 FROM sqlite_schema WHERE type = 'table' AND name = 'Roles')
    AND EXISTS (SELECT 1 FROM sqlite_schema WHERE type = 'table' AND name = 'Users')
    AND NOT EXISTS (SELECT 1 FROM sqlite_schema WHERE name = 'TrustedLinkDomains')
    AND NOT EXISTS (
        SELECT 1 FROM Roles
        WHERE NormalizedName IN ('ADMINISTRATOR', 'SENIOR EDITOR', 'MODERATOR', 'BOT')
           OR Id IN ('3b6f5a0e-6d1c-4c59-9a43-0d5f7c1e2a01', '3b6f5a0e-6d1c-4c59-9a43-0d5f7c1e2a02',
                     '3b6f5a0e-6d1c-4c59-9a43-0d5f7c1e2a03', '3b6f5a0e-6d1c-4c59-9a43-0d5f7c1e2a04')
    );

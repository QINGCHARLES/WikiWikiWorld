SELECT (SELECT COUNT(*) FROM Roles
        WHERE NormalizedName IN ('ADMINISTRATOR', 'SENIOR EDITOR', 'MODERATOR', 'BOT') AND DateDeleted IS NULL) = 4
    AND (SELECT COUNT(*) FROM TrustedLinkDomains WHERE Status = 1 AND ProposedByUserId IS NULL) = 7
    AND EXISTS (SELECT 1 FROM sqlite_schema WHERE type = 'index' AND name = 'UX_TrustedLinkDomains_Domain_Open')
    AND EXISTS (SELECT 1 FROM sqlite_schema WHERE type = 'index' AND name = 'IX_TrustedLinkDomains_Status_DateProposed')
    AND EXISTS (SELECT 1 FROM sqlite_schema WHERE type = 'index' AND name = 'IX_TrustedLinkDomains_ProposedByUserId')
    AND (SELECT COUNT(*) FROM pragma_table_info('TrustedLinkDomains')) = 12
    AND (SELECT COUNT(*) FROM pragma_foreign_key_list('TrustedLinkDomains')) = 3
    AND NOT EXISTS (SELECT 1 FROM pragma_foreign_key_check);

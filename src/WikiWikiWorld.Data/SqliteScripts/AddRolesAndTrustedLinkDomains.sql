-- Apply once through the maintenance runner with CheckRolesAndTrustedLinkDomainsBefore.sql and
-- VerifyRolesAndTrustedLinkDomains.sql. Apply BEFORE deploying the release that reads TrustedLinkDomains:
-- that release loads the trusted list at startup and will not start without the table.
-- Adds the four application roles (no one is given a role here; use the grant-role command), and the
-- TrustedLinkDomains table seeded with the seven domains from the retired ApprovedLinkDomains setting.
BEGIN IMMEDIATE;

INSERT INTO Roles (Id, Name, NormalizedName, ConcurrencyStamp, DateCreated, DateDeleted) VALUES
    ('3b6f5a0e-6d1c-4c59-9a43-0d5f7c1e2a01', 'Administrator', 'ADMINISTRATOR', '9d0c6e52-1b7a-4f6e-8d3e-5a2b7c4f1e01', strftime('%Y-%m-%d %H:%M:%f', 'now') || '+00:00', NULL),
    ('3b6f5a0e-6d1c-4c59-9a43-0d5f7c1e2a02', 'Senior Editor', 'SENIOR EDITOR', '9d0c6e52-1b7a-4f6e-8d3e-5a2b7c4f1e02', strftime('%Y-%m-%d %H:%M:%f', 'now') || '+00:00', NULL),
    ('3b6f5a0e-6d1c-4c59-9a43-0d5f7c1e2a03', 'Moderator', 'MODERATOR', '9d0c6e52-1b7a-4f6e-8d3e-5a2b7c4f1e03', strftime('%Y-%m-%d %H:%M:%f', 'now') || '+00:00', NULL),
    ('3b6f5a0e-6d1c-4c59-9a43-0d5f7c1e2a04', 'Bot', 'BOT', '9d0c6e52-1b7a-4f6e-8d3e-5a2b7c4f1e04', strftime('%Y-%m-%d %H:%M:%f', 'now') || '+00:00', NULL);

CREATE TABLE TrustedLinkDomains (
    Id INTEGER NOT NULL CONSTRAINT PK_TrustedLinkDomains PRIMARY KEY AUTOINCREMENT,
    Domain TEXT NOT NULL,
    Status INTEGER NOT NULL CHECK (Status IN (0, 1, 2, 3)),
    ProposalNote TEXT NOT NULL,
    ProposedByUserId TEXT NULL,
    DateProposed TEXT NOT NULL,
    ReviewedByUserId TEXT NULL,
    DateReviewed TEXT NULL,
    ReviewNote TEXT NULL,
    RevokedByUserId TEXT NULL,
    DateRevoked TEXT NULL,
    RevocationNote TEXT NULL,
    CONSTRAINT FK_TrustedLinkDomains_Users_ProposedByUserId
        FOREIGN KEY (ProposedByUserId) REFERENCES Users (Id) ON DELETE SET NULL,
    CONSTRAINT FK_TrustedLinkDomains_Users_ReviewedByUserId
        FOREIGN KEY (ReviewedByUserId) REFERENCES Users (Id) ON DELETE SET NULL,
    CONSTRAINT FK_TrustedLinkDomains_Users_RevokedByUserId
        FOREIGN KEY (RevokedByUserId) REFERENCES Users (Id) ON DELETE SET NULL
) STRICT;

-- At most one open (pending or trusted) row per domain; denied and revoked rows are history.
CREATE UNIQUE INDEX UX_TrustedLinkDomains_Domain_Open
    ON TrustedLinkDomains (Domain) WHERE Status IN (0, 1);
CREATE INDEX IX_TrustedLinkDomains_Status_DateProposed
    ON TrustedLinkDomains (Status, DateProposed);
CREATE INDEX IX_TrustedLinkDomains_ProposedByUserId
    ON TrustedLinkDomains (ProposedByUserId);

INSERT INTO TrustedLinkDomains (Domain, Status, ProposalNote, ProposedByUserId, DateProposed) VALUES
    ('gq.com', 1, 'Carried over from the ApprovedLinkDomains configuration list.', NULL, strftime('%Y-%m-%d %H:%M:%f', 'now') || '+00:00'),
    ('rollingstone.com', 1, 'Carried over from the ApprovedLinkDomains configuration list.', NULL, strftime('%Y-%m-%d %H:%M:%f', 'now') || '+00:00'),
    ('theverge.com', 1, 'Carried over from the ApprovedLinkDomains configuration list.', NULL, strftime('%Y-%m-%d %H:%M:%f', 'now') || '+00:00'),
    ('vogue.com', 1, 'Carried over from the ApprovedLinkDomains configuration list.', NULL, strftime('%Y-%m-%d %H:%M:%f', 'now') || '+00:00'),
    ('vanityfair.com', 1, 'Carried over from the ApprovedLinkDomains configuration list.', NULL, strftime('%Y-%m-%d %H:%M:%f', 'now') || '+00:00'),
    ('theatlantic.com', 1, 'Carried over from the ApprovedLinkDomains configuration list.', NULL, strftime('%Y-%m-%d %H:%M:%f', 'now') || '+00:00'),
    ('wired.com', 1, 'Carried over from the ApprovedLinkDomains configuration list.', NULL, strftime('%Y-%m-%d %H:%M:%f', 'now') || '+00:00');

COMMIT;

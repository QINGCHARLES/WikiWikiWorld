SELECT (SELECT count(*) FROM pragma_table_list WHERE name IN ('PrivateConversations', 'PrivateConversationParticipants', 'PrivateMessages') AND strict = 1) = 3
   AND NOT EXISTS (SELECT 1 FROM pragma_table_info('ArticleTalkSubjects') WHERE name IN ('HasBeenEdited', 'DateModified'))
   AND (SELECT count(*) FROM pragma_table_info('PrivateConversationParticipants') WHERE (name = 'ConversationId' AND pk = 1) OR (name = 'UserId' AND pk = 2)) = 2
   AND (SELECT count(*) FROM pragma_foreign_key_list('PrivateMessages') WHERE "table" = 'PrivateConversationParticipants' AND on_delete = 'RESTRICT'
        AND ((seq = 0 AND "from" = 'ConversationId' AND "to" = 'ConversationId') OR (seq = 1 AND "from" = 'AuthorUserId' AND "to" = 'UserId'))) = 2
   AND (SELECT count(*) FROM sqlite_schema WHERE type = 'index' AND name IN (
       'IX_PrivateConversations_CreatedByUserId', 'IX_PrivateConversationParticipants_UserId_ConversationId',
       'IX_PrivateMessages_ConversationId_Id', 'IX_PrivateMessages_ConversationId_AuthorUserId')) = 4
   AND NOT EXISTS (SELECT 1 FROM ArticleTalkSubjects)
   AND NOT EXISTS (SELECT 1 FROM ArticleTalkSubjectPosts)
   AND NOT EXISTS (SELECT 1 FROM PrivateConversations)
   AND NOT EXISTS (SELECT 1 FROM PrivateConversationParticipants)
   AND NOT EXISTS (SELECT 1 FROM PrivateMessages)
   AND NOT EXISTS (SELECT 1 FROM pragma_foreign_key_check);

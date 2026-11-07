SELECT NOT EXISTS (SELECT 1 FROM ArticleTalkSubjects)
   AND NOT EXISTS (SELECT 1 FROM ArticleTalkSubjectPosts)
   AND (SELECT count(*) FROM pragma_table_info('ArticleTalkSubjects') WHERE name IN ('HasBeenEdited', 'DateModified')) = 2
   AND NOT EXISTS (SELECT 1 FROM sqlite_schema WHERE name IN ('PrivateConversations', 'PrivateConversationParticipants', 'PrivateMessages'));

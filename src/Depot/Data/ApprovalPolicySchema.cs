// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.Data;

public static class ApprovalPolicySchema
{
	public static void Ensure(IDatabaseConnectionFactory connectionFactory)
	{
		ArgumentNullException.ThrowIfNull(connectionFactory);
		using var connection = connectionFactory.CreateConnection();
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = connectionFactory.Provider switch
		{
			DatabaseProvider.Local => Sqlite,
			DatabaseProvider.SqlServer => SqlServer,
			DatabaseProvider.MySql => MySql,
			_ => throw new NotSupportedException($"Approval-policy persistence is not supported for provider '{connectionFactory.Provider}'.")
		};
		command.ExecuteNonQuery();
	}

	private const string Sqlite = """
		CREATE TABLE IF NOT EXISTS ApprovalPolicies
		(
			PolicyId TEXT NOT NULL PRIMARY KEY,
			Name TEXT NOT NULL,
			Description TEXT NULL,
			SubjectKind INTEGER NOT NULL,
			Priority INTEGER NOT NULL,
			IsActive INTEGER NOT NULL,
			EffectiveFromUtc TEXT NULL,
			EffectiveToUtc TEXT NULL,
			Version INTEGER NOT NULL,
			DefinitionJson TEXT NOT NULL,
			CreatedByUserId INTEGER NULL,
			CreatedByUserDisplay TEXT NULL,
			CreatedAtUtc TEXT NOT NULL,
			UpdatedByUserId INTEGER NULL,
			UpdatedByUserDisplay TEXT NULL,
			UpdatedAtUtc TEXT NOT NULL
		);
		CREATE INDEX IF NOT EXISTS IX_ApprovalPolicies_Resolution
			ON ApprovalPolicies (SubjectKind, IsActive, Priority, EffectiveFromUtc, EffectiveToUtc);
		CREATE TABLE IF NOT EXISTS ApprovalPolicyRevisions
		(
			PolicyId TEXT NOT NULL,
			Version INTEGER NOT NULL,
			DefinitionJson TEXT NOT NULL,
			ChangedByUserId INTEGER NULL,
			ChangedByUserDisplay TEXT NULL,
			ChangedAtUtc TEXT NOT NULL,
			PRIMARY KEY (PolicyId, Version)
		);
		CREATE TABLE IF NOT EXISTS ApprovalInstances
		(
			InstanceId TEXT NOT NULL PRIMARY KEY,
			SubjectKind INTEGER NOT NULL,
			SubjectId TEXT NOT NULL,
			PolicyId TEXT NOT NULL,
			PolicyVersion INTEGER NOT NULL,
			PolicyName TEXT NOT NULL,
			SnapshotJson TEXT NOT NULL,
			CurrentStageOrder INTEGER NOT NULL,
			Status INTEGER NOT NULL,
			Version INTEGER NOT NULL,
			CreatedAtUtc TEXT NOT NULL
		);
		CREATE INDEX IF NOT EXISTS IX_ApprovalInstances_Subject
			ON ApprovalInstances (SubjectKind, SubjectId, Status);
		CREATE TABLE IF NOT EXISTS ApprovalDecisions
		(
			DecisionId TEXT NOT NULL PRIMARY KEY,
			InstanceId TEXT NOT NULL,
			StageOrder INTEGER NOT NULL,
			Decision INTEGER NOT NULL,
			UserId INTEGER NOT NULL,
			UserDisplay TEXT NOT NULL,
			Comment TEXT NULL,
			DecidedAtUtc TEXT NOT NULL,
			UNIQUE (InstanceId, StageOrder)
		);
		CREATE TABLE IF NOT EXISTS ApprovalDelegations
		(
			DelegationId TEXT NOT NULL PRIMARY KEY,
			FromUserId INTEGER NOT NULL,
			ToUserId INTEGER NOT NULL,
			SubjectKind INTEGER NULL,
			EffectiveFromUtc TEXT NOT NULL,
			EffectiveToUtc TEXT NOT NULL,
			CreatedByUserId INTEGER NOT NULL,
			CreatedByUserDisplay TEXT NOT NULL,
			CreatedAtUtc TEXT NOT NULL
		);
		CREATE INDEX IF NOT EXISTS IX_ApprovalDelegations_Effective
			ON ApprovalDelegations (FromUserId, SubjectKind, EffectiveFromUtc, EffectiveToUtc);
		""";

	private const string SqlServer = """
		IF OBJECT_ID(N'ApprovalPolicies', N'U') IS NULL
		BEGIN
			CREATE TABLE ApprovalPolicies
			(
				PolicyId nvarchar(36) NOT NULL PRIMARY KEY,
				Name nvarchar(200) NOT NULL,
				Description nvarchar(2000) NULL,
				SubjectKind int NOT NULL,
				Priority int NOT NULL,
				IsActive bit NOT NULL,
				EffectiveFromUtc nvarchar(40) NULL,
				EffectiveToUtc nvarchar(40) NULL,
				Version int NOT NULL,
				DefinitionJson nvarchar(max) NOT NULL,
				CreatedByUserId bigint NULL,
				CreatedByUserDisplay nvarchar(200) NULL,
				CreatedAtUtc nvarchar(40) NOT NULL,
				UpdatedByUserId bigint NULL,
				UpdatedByUserDisplay nvarchar(200) NULL,
				UpdatedAtUtc nvarchar(40) NOT NULL
			);
		END;
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_ApprovalPolicies_Resolution' AND object_id=OBJECT_ID(N'ApprovalPolicies'))
			CREATE INDEX IX_ApprovalPolicies_Resolution ON ApprovalPolicies (SubjectKind, IsActive, Priority, EffectiveFromUtc, EffectiveToUtc);
		IF OBJECT_ID(N'ApprovalPolicyRevisions', N'U') IS NULL
		BEGIN
			CREATE TABLE ApprovalPolicyRevisions
			(
				PolicyId nvarchar(36) NOT NULL,
				Version int NOT NULL,
				DefinitionJson nvarchar(max) NOT NULL,
				ChangedByUserId bigint NULL,
				ChangedByUserDisplay nvarchar(200) NULL,
				ChangedAtUtc nvarchar(40) NOT NULL,
				CONSTRAINT PK_ApprovalPolicyRevisions PRIMARY KEY (PolicyId, Version)
			);
		END;
		IF OBJECT_ID(N'ApprovalInstances', N'U') IS NULL
		BEGIN
			CREATE TABLE ApprovalInstances
			(
				InstanceId nvarchar(36) NOT NULL PRIMARY KEY,
				SubjectKind int NOT NULL,
				SubjectId nvarchar(100) NOT NULL,
				PolicyId nvarchar(36) NOT NULL,
				PolicyVersion int NOT NULL,
				PolicyName nvarchar(200) NOT NULL,
				SnapshotJson nvarchar(max) NOT NULL,
				CurrentStageOrder int NOT NULL,
				Status int NOT NULL,
				Version int NOT NULL,
				CreatedAtUtc nvarchar(40) NOT NULL
			);
		END;
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_ApprovalInstances_Subject' AND object_id=OBJECT_ID(N'ApprovalInstances'))
			CREATE INDEX IX_ApprovalInstances_Subject ON ApprovalInstances (SubjectKind, SubjectId, Status);
		IF OBJECT_ID(N'ApprovalDecisions', N'U') IS NULL
		BEGIN
			CREATE TABLE ApprovalDecisions
			(
				DecisionId nvarchar(36) NOT NULL PRIMARY KEY,
				InstanceId nvarchar(36) NOT NULL,
				StageOrder int NOT NULL,
				Decision int NOT NULL,
				UserId bigint NOT NULL,
				UserDisplay nvarchar(200) NOT NULL,
				Comment nvarchar(2000) NULL,
				DecidedAtUtc nvarchar(40) NOT NULL,
				CONSTRAINT UQ_ApprovalDecisions_InstanceStage UNIQUE (InstanceId, StageOrder)
			);
		END;
		IF OBJECT_ID(N'ApprovalDelegations', N'U') IS NULL
		BEGIN
			CREATE TABLE ApprovalDelegations
			(
				DelegationId nvarchar(36) NOT NULL PRIMARY KEY,
				FromUserId bigint NOT NULL,
				ToUserId bigint NOT NULL,
				SubjectKind int NULL,
				EffectiveFromUtc nvarchar(40) NOT NULL,
				EffectiveToUtc nvarchar(40) NOT NULL,
				CreatedByUserId bigint NOT NULL,
				CreatedByUserDisplay nvarchar(200) NOT NULL,
				CreatedAtUtc nvarchar(40) NOT NULL
			);
		END;
		IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_ApprovalDelegations_Effective' AND object_id=OBJECT_ID(N'ApprovalDelegations'))
			CREATE INDEX IX_ApprovalDelegations_Effective ON ApprovalDelegations (FromUserId, SubjectKind, EffectiveFromUtc, EffectiveToUtc);
		""";

	private const string MySql = """
		CREATE TABLE IF NOT EXISTS ApprovalPolicies
		(
			PolicyId CHAR(36) NOT NULL PRIMARY KEY,
			Name VARCHAR(200) NOT NULL,
			Description TEXT NULL,
			SubjectKind INT NOT NULL,
			Priority INT NOT NULL,
			IsActive TINYINT(1) NOT NULL,
			EffectiveFromUtc VARCHAR(40) NULL,
			EffectiveToUtc VARCHAR(40) NULL,
			Version INT NOT NULL,
			DefinitionJson LONGTEXT NOT NULL,
			CreatedByUserId BIGINT NULL,
			CreatedByUserDisplay VARCHAR(200) NULL,
			CreatedAtUtc VARCHAR(40) NOT NULL,
			UpdatedByUserId BIGINT NULL,
			UpdatedByUserDisplay VARCHAR(200) NULL,
			UpdatedAtUtc VARCHAR(40) NOT NULL,
			INDEX IX_ApprovalPolicies_Resolution (SubjectKind, IsActive, Priority, EffectiveFromUtc, EffectiveToUtc)
		) ENGINE=InnoDB;
		CREATE TABLE IF NOT EXISTS ApprovalPolicyRevisions
		(
			PolicyId CHAR(36) NOT NULL,
			Version INT NOT NULL,
			DefinitionJson LONGTEXT NOT NULL,
			ChangedByUserId BIGINT NULL,
			ChangedByUserDisplay VARCHAR(200) NULL,
			ChangedAtUtc VARCHAR(40) NOT NULL,
			PRIMARY KEY (PolicyId, Version)
		) ENGINE=InnoDB;
		CREATE TABLE IF NOT EXISTS ApprovalInstances
		(
			InstanceId CHAR(36) NOT NULL PRIMARY KEY,
			SubjectKind INT NOT NULL,
			SubjectId VARCHAR(100) NOT NULL,
			PolicyId CHAR(36) NOT NULL,
			PolicyVersion INT NOT NULL,
			PolicyName VARCHAR(200) NOT NULL,
			SnapshotJson LONGTEXT NOT NULL,
			CurrentStageOrder INT NOT NULL,
			Status INT NOT NULL,
			Version INT NOT NULL,
			CreatedAtUtc VARCHAR(40) NOT NULL,
			INDEX IX_ApprovalInstances_Subject (SubjectKind, SubjectId, Status)
		) ENGINE=InnoDB;
		CREATE TABLE IF NOT EXISTS ApprovalDecisions
		(
			DecisionId CHAR(36) NOT NULL PRIMARY KEY,
			InstanceId CHAR(36) NOT NULL,
			StageOrder INT NOT NULL,
			Decision INT NOT NULL,
			UserId BIGINT NOT NULL,
			UserDisplay VARCHAR(200) NOT NULL,
			Comment TEXT NULL,
			DecidedAtUtc VARCHAR(40) NOT NULL,
			UNIQUE KEY UQ_ApprovalDecisions_InstanceStage (InstanceId, StageOrder)
		) ENGINE=InnoDB;
		CREATE TABLE IF NOT EXISTS ApprovalDelegations
		(
			DelegationId CHAR(36) NOT NULL PRIMARY KEY,
			FromUserId BIGINT NOT NULL,
			ToUserId BIGINT NOT NULL,
			SubjectKind INT NULL,
			EffectiveFromUtc VARCHAR(40) NOT NULL,
			EffectiveToUtc VARCHAR(40) NOT NULL,
			CreatedByUserId BIGINT NOT NULL,
			CreatedByUserDisplay VARCHAR(200) NOT NULL,
			CreatedAtUtc VARCHAR(40) NOT NULL,
			INDEX IX_ApprovalDelegations_Effective (FromUserId, SubjectKind, EffectiveFromUtc, EffectiveToUtc)
		) ENGINE=InnoDB;
		""";
}

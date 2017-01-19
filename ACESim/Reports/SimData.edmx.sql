
-- --------------------------------------------------
-- Entity Designer DDL Script for SQL Server 2005, 2008, 2012 and Azure
-- --------------------------------------------------
-- Date Created: 01/19/2017 12:55:15
-- Generated from EDMX file: C:\GitHub\ACESim\ACESim\Reports\SimData.edmx
-- --------------------------------------------------

SET QUOTED_IDENTIFIER OFF;
GO
USE [ACESIM];
GO
IF SCHEMA_ID(N'dbo') IS NULL EXECUTE(N'CREATE SCHEMA [dbo]');
GO

-- --------------------------------------------------
-- Dropping existing FOREIGN KEY constraints
-- --------------------------------------------------

IF OBJECT_ID(N'[dbo].[FK_ColumnListToColumn_Column]', 'F') IS NOT NULL
    ALTER TABLE [dbo].[ColumnListToColumns] DROP CONSTRAINT [FK_ColumnListToColumn_Column];
GO
IF OBJECT_ID(N'[dbo].[FK_ColumnListToColumn_ColumnList]', 'F') IS NOT NULL
    ALTER TABLE [dbo].[ColumnListToColumns] DROP CONSTRAINT [FK_ColumnListToColumn_ColumnList];
GO
IF OBJECT_ID(N'[dbo].[FK_DataPoint_Column]', 'F') IS NOT NULL
    ALTER TABLE [dbo].[DataPoints] DROP CONSTRAINT [FK_DataPoint_Column];
GO
IF OBJECT_ID(N'[dbo].[FK_DataPoint_Row]', 'F') IS NOT NULL
    ALTER TABLE [dbo].[DataPoints] DROP CONSTRAINT [FK_DataPoint_Row];
GO
IF OBJECT_ID(N'[dbo].[FK_ExecutionResultSet_DataPoint]', 'F') IS NOT NULL
    ALTER TABLE [dbo].[DataPoints] DROP CONSTRAINT [FK_ExecutionResultSet_DataPoint];
GO
IF OBJECT_ID(N'[dbo].[FK__aspnet_Me__Appli__02084FDA]', 'F') IS NOT NULL
    ALTER TABLE [dbo].[aspnet_Membership] DROP CONSTRAINT [FK__aspnet_Me__Appli__02084FDA];
GO
IF OBJECT_ID(N'[dbo].[FK__aspnet_Me__UserI__02FC7413]', 'F') IS NOT NULL
    ALTER TABLE [dbo].[aspnet_Membership] DROP CONSTRAINT [FK__aspnet_Me__UserI__02FC7413];
GO
IF OBJECT_ID(N'[dbo].[FK__aspnet_Pr__UserI__17036CC0]', 'F') IS NOT NULL
    ALTER TABLE [dbo].[aspnet_Profile] DROP CONSTRAINT [FK__aspnet_Pr__UserI__17036CC0];
GO
IF OBJECT_ID(N'[dbo].[FK__aspnet_Ro__Appli__208CD6FA]', 'F') IS NOT NULL
    ALTER TABLE [dbo].[aspnet_Roles] DROP CONSTRAINT [FK__aspnet_Ro__Appli__208CD6FA];
GO
IF OBJECT_ID(N'[dbo].[FK__aspnet_Us__Appli__71D1E811]', 'F') IS NOT NULL
    ALTER TABLE [dbo].[aspnet_Users] DROP CONSTRAINT [FK__aspnet_Us__Appli__71D1E811];
GO
IF OBJECT_ID(N'[dbo].[FK__aspnet_Us__RoleI__25518C17]', 'F') IS NOT NULL
    ALTER TABLE [dbo].[aspnet_UsersInRoles] DROP CONSTRAINT [FK__aspnet_Us__RoleI__25518C17];
GO
IF OBJECT_ID(N'[dbo].[FK__aspnet_Us__UserI__245D67DE]', 'F') IS NOT NULL
    ALTER TABLE [dbo].[aspnet_UsersInRoles] DROP CONSTRAINT [FK__aspnet_Us__UserI__245D67DE];
GO
IF OBJECT_ID(N'[dbo].[FK_ReportSnippet_ColumnList]', 'F') IS NOT NULL
    ALTER TABLE [dbo].[ReportSnippets] DROP CONSTRAINT [FK_ReportSnippet_ColumnList];
GO
IF OBJECT_ID(N'[dbo].[FK_ReportSnippet_RowList]', 'F') IS NOT NULL
    ALTER TABLE [dbo].[ReportSnippets] DROP CONSTRAINT [FK_ReportSnippet_RowList];
GO
IF OBJECT_ID(N'[dbo].[FK_ReportSnippetToReportDefinitio_ReportDefinition]', 'F') IS NOT NULL
    ALTER TABLE [dbo].[ReportSnippetToReportDefinitions] DROP CONSTRAINT [FK_ReportSnippetToReportDefinitio_ReportDefinition];
GO
IF OBJECT_ID(N'[dbo].[FK_ReportSnippetToReportDefinitio_ReportSnippet]', 'F') IS NOT NULL
    ALTER TABLE [dbo].[ReportSnippetToReportDefinitions] DROP CONSTRAINT [FK_ReportSnippetToReportDefinitio_ReportSnippet];
GO
IF OBJECT_ID(N'[dbo].[FK_RowListToRow_Row]', 'F') IS NOT NULL
    ALTER TABLE [dbo].[RowListToRows] DROP CONSTRAINT [FK_RowListToRow_Row];
GO
IF OBJECT_ID(N'[dbo].[FK_RowListToRow_RowList]', 'F') IS NOT NULL
    ALTER TABLE [dbo].[RowListToRows] DROP CONSTRAINT [FK_RowListToRow_RowList];
GO
IF OBJECT_ID(N'[dbo].[FK_SettingChoice_SettingCategory]', 'F') IS NOT NULL
    ALTER TABLE [dbo].[SettingChoices] DROP CONSTRAINT [FK_SettingChoice_SettingCategory];
GO
IF OBJECT_ID(N'[dbo].[FK_SettingChoiceToExecutionResult_ExecutionResultSet]', 'F') IS NOT NULL
    ALTER TABLE [dbo].[SettingChoiceToExecutionResultSets] DROP CONSTRAINT [FK_SettingChoiceToExecutionResult_ExecutionResultSet];
GO
IF OBJECT_ID(N'[dbo].[FK_SettingChoiceToExecutionResult_SettingChoice]', 'F') IS NOT NULL
    ALTER TABLE [dbo].[SettingChoiceToExecutionResultSets] DROP CONSTRAINT [FK_SettingChoiceToExecutionResult_SettingChoice];
GO
IF OBJECT_ID(N'[dbo].[FK_SettingChoiceToSettingChoiceSe_SettingChoice]', 'F') IS NOT NULL
    ALTER TABLE [dbo].[SettingChoiceToSettingChoiceSets] DROP CONSTRAINT [FK_SettingChoiceToSettingChoiceSe_SettingChoice];
GO
IF OBJECT_ID(N'[dbo].[FK_SettingChoiceToSettingChoiceSe_SettingChoiceSet]', 'F') IS NOT NULL
    ALTER TABLE [dbo].[SettingChoiceToSettingChoiceSets] DROP CONSTRAINT [FK_SettingChoiceToSettingChoiceSe_SettingChoiceSet];
GO

-- --------------------------------------------------
-- Dropping existing tables
-- --------------------------------------------------

IF OBJECT_ID(N'[dbo].[aspnet_Applications]', 'U') IS NOT NULL
    DROP TABLE [dbo].[aspnet_Applications];
GO
IF OBJECT_ID(N'[dbo].[aspnet_Membership]', 'U') IS NOT NULL
    DROP TABLE [dbo].[aspnet_Membership];
GO
IF OBJECT_ID(N'[dbo].[aspnet_Profile]', 'U') IS NOT NULL
    DROP TABLE [dbo].[aspnet_Profile];
GO
IF OBJECT_ID(N'[dbo].[aspnet_Roles]', 'U') IS NOT NULL
    DROP TABLE [dbo].[aspnet_Roles];
GO
IF OBJECT_ID(N'[dbo].[aspnet_SchemaVersions]', 'U') IS NOT NULL
    DROP TABLE [dbo].[aspnet_SchemaVersions];
GO
IF OBJECT_ID(N'[dbo].[aspnet_Users]', 'U') IS NOT NULL
    DROP TABLE [dbo].[aspnet_Users];
GO
IF OBJECT_ID(N'[dbo].[aspnet_UsersInRoles]', 'U') IS NOT NULL
    DROP TABLE [dbo].[aspnet_UsersInRoles];
GO
IF OBJECT_ID(N'[dbo].[ColumnLists]', 'U') IS NOT NULL
    DROP TABLE [dbo].[ColumnLists];
GO
IF OBJECT_ID(N'[dbo].[ColumnListToColumns]', 'U') IS NOT NULL
    DROP TABLE [dbo].[ColumnListToColumns];
GO
IF OBJECT_ID(N'[dbo].[Columns]', 'U') IS NOT NULL
    DROP TABLE [dbo].[Columns];
GO
IF OBJECT_ID(N'[dbo].[DataPoints]', 'U') IS NOT NULL
    DROP TABLE [dbo].[DataPoints];
GO
IF OBJECT_ID(N'[dbo].[ExecutionResultSets]', 'U') IS NOT NULL
    DROP TABLE [dbo].[ExecutionResultSets];
GO
IF OBJECT_ID(N'[dbo].[ReportDefinitions]', 'U') IS NOT NULL
    DROP TABLE [dbo].[ReportDefinitions];
GO
IF OBJECT_ID(N'[dbo].[ReportSnippets]', 'U') IS NOT NULL
    DROP TABLE [dbo].[ReportSnippets];
GO
IF OBJECT_ID(N'[dbo].[ReportSnippetToReportDefinitions]', 'U') IS NOT NULL
    DROP TABLE [dbo].[ReportSnippetToReportDefinitions];
GO
IF OBJECT_ID(N'[dbo].[RolePermissions]', 'U') IS NOT NULL
    DROP TABLE [dbo].[RolePermissions];
GO
IF OBJECT_ID(N'[dbo].[RowLists]', 'U') IS NOT NULL
    DROP TABLE [dbo].[RowLists];
GO
IF OBJECT_ID(N'[dbo].[RowListToRows]', 'U') IS NOT NULL
    DROP TABLE [dbo].[RowListToRows];
GO
IF OBJECT_ID(N'[dbo].[Rows]', 'U') IS NOT NULL
    DROP TABLE [dbo].[Rows];
GO
IF OBJECT_ID(N'[dbo].[SettingCategories]', 'U') IS NOT NULL
    DROP TABLE [dbo].[SettingCategories];
GO
IF OBJECT_ID(N'[dbo].[SettingChoices]', 'U') IS NOT NULL
    DROP TABLE [dbo].[SettingChoices];
GO
IF OBJECT_ID(N'[dbo].[SettingChoiceSets]', 'U') IS NOT NULL
    DROP TABLE [dbo].[SettingChoiceSets];
GO
IF OBJECT_ID(N'[dbo].[SettingChoiceToExecutionResultSets]', 'U') IS NOT NULL
    DROP TABLE [dbo].[SettingChoiceToExecutionResultSets];
GO
IF OBJECT_ID(N'[dbo].[SettingChoiceToSettingChoiceSets]', 'U') IS NOT NULL
    DROP TABLE [dbo].[SettingChoiceToSettingChoiceSets];
GO
IF OBJECT_ID(N'[ACESIMModelStoreContainer].[vw_aspnet_Applications]', 'U') IS NOT NULL
    DROP TABLE [ACESIMModelStoreContainer].[vw_aspnet_Applications];
GO
IF OBJECT_ID(N'[ACESIMModelStoreContainer].[vw_aspnet_MembershipUsers]', 'U') IS NOT NULL
    DROP TABLE [ACESIMModelStoreContainer].[vw_aspnet_MembershipUsers];
GO
IF OBJECT_ID(N'[ACESIMModelStoreContainer].[vw_aspnet_Profiles]', 'U') IS NOT NULL
    DROP TABLE [ACESIMModelStoreContainer].[vw_aspnet_Profiles];
GO
IF OBJECT_ID(N'[ACESIMModelStoreContainer].[vw_aspnet_Roles]', 'U') IS NOT NULL
    DROP TABLE [ACESIMModelStoreContainer].[vw_aspnet_Roles];
GO
IF OBJECT_ID(N'[ACESIMModelStoreContainer].[vw_aspnet_Users]', 'U') IS NOT NULL
    DROP TABLE [ACESIMModelStoreContainer].[vw_aspnet_Users];
GO
IF OBJECT_ID(N'[ACESIMModelStoreContainer].[vw_aspnet_UsersInRoles]', 'U') IS NOT NULL
    DROP TABLE [ACESIMModelStoreContainer].[vw_aspnet_UsersInRoles];
GO

-- --------------------------------------------------
-- Creating all tables
-- --------------------------------------------------

-- Creating table 'aspnet_Applications'
CREATE TABLE [dbo].[aspnet_Applications] (
    [ApplicationName] nvarchar(256)  NOT NULL,
    [LoweredApplicationName] nvarchar(256)  NOT NULL,
    [ApplicationId] uniqueidentifier  NOT NULL,
    [Description] nvarchar(256)  NULL
);
GO

-- Creating table 'aspnet_Membership'
CREATE TABLE [dbo].[aspnet_Membership] (
    [ApplicationId] uniqueidentifier  NOT NULL,
    [UserId] uniqueidentifier  NOT NULL,
    [Password] nvarchar(128)  NOT NULL,
    [PasswordFormat] int  NOT NULL,
    [PasswordSalt] nvarchar(128)  NOT NULL,
    [MobilePIN] nvarchar(16)  NULL,
    [Email] nvarchar(256)  NULL,
    [LoweredEmail] nvarchar(256)  NULL,
    [PasswordQuestion] nvarchar(256)  NULL,
    [PasswordAnswer] nvarchar(128)  NULL,
    [IsApproved] bit  NOT NULL,
    [IsLockedOut] bit  NOT NULL,
    [CreateDate] datetime  NOT NULL,
    [LastLoginDate] datetime  NOT NULL,
    [LastPasswordChangedDate] datetime  NOT NULL,
    [LastLockoutDate] datetime  NOT NULL,
    [FailedPasswordAttemptCount] int  NOT NULL,
    [FailedPasswordAttemptWindowStart] datetime  NOT NULL,
    [FailedPasswordAnswerAttemptCount] int  NOT NULL,
    [FailedPasswordAnswerAttemptWindowStart] datetime  NOT NULL,
    [Comment] nvarchar(max)  NULL
);
GO

-- Creating table 'aspnet_Profile'
CREATE TABLE [dbo].[aspnet_Profile] (
    [UserId] uniqueidentifier  NOT NULL,
    [PropertyNames] nvarchar(max)  NOT NULL,
    [PropertyValuesString] nvarchar(max)  NOT NULL,
    [PropertyValuesBinary] varbinary(max)  NOT NULL,
    [LastUpdatedDate] datetime  NOT NULL
);
GO

-- Creating table 'aspnet_Roles'
CREATE TABLE [dbo].[aspnet_Roles] (
    [ApplicationId] uniqueidentifier  NOT NULL,
    [RoleId] uniqueidentifier  NOT NULL,
    [RoleName] nvarchar(256)  NOT NULL,
    [LoweredRoleName] nvarchar(256)  NOT NULL,
    [Description] nvarchar(256)  NULL
);
GO

-- Creating table 'aspnet_SchemaVersions'
CREATE TABLE [dbo].[aspnet_SchemaVersions] (
    [Feature] nvarchar(128)  NOT NULL,
    [CompatibleSchemaVersion] nvarchar(128)  NOT NULL,
    [IsCurrentVersion] bit  NOT NULL
);
GO

-- Creating table 'aspnet_Users'
CREATE TABLE [dbo].[aspnet_Users] (
    [ApplicationId] uniqueidentifier  NOT NULL,
    [UserId] uniqueidentifier  NOT NULL,
    [UserName] nvarchar(256)  NOT NULL,
    [LoweredUserName] nvarchar(256)  NOT NULL,
    [MobileAlias] nvarchar(16)  NULL,
    [IsAnonymous] bit  NOT NULL,
    [LastActivityDate] datetime  NOT NULL
);
GO

-- Creating table 'ColumnLists'
CREATE TABLE [dbo].[ColumnLists] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [RowVersion] binary(8)  NOT NULL,
    [Name] nvarchar(255)  NOT NULL
);
GO

-- Creating table 'ColumnListToColumns'
CREATE TABLE [dbo].[ColumnListToColumns] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [RowVersion] binary(8)  NOT NULL,
    [OrderInList] int  NULL,
    [ColumnListToColumn_Column] int  NOT NULL,
    [ColumnListToColumn_ColumnList] int  NOT NULL
);
GO

-- Creating table 'Columns'
CREATE TABLE [dbo].[Columns] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [RowVersion] binary(8)  NOT NULL,
    [Name] nvarchar(255)  NOT NULL
);
GO

-- Creating table 'DataPoints'
CREATE TABLE [dbo].[DataPoints] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [RowVersion] binary(8)  NOT NULL,
    [Value] float  NULL,
    [DataPoint_Row] int  NOT NULL,
    [DataPoint_Column] int  NOT NULL,
    [ExecutionResultSet_DataPoint] int  NOT NULL
);
GO

-- Creating table 'ExecutionResultSets'
CREATE TABLE [dbo].[ExecutionResultSets] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [RowVersion] binary(8)  NOT NULL,
    [Time] datetime  NOT NULL,
    [SettingChoiceSummary] nvarchar(255)  NOT NULL,
    [FullSettingsList] nvarchar(255)  NOT NULL,
    [FullVariableList] nvarchar(255)  NOT NULL
);
GO

-- Creating table 'ReportDefinitions'
CREATE TABLE [dbo].[ReportDefinitions] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [RowVersion] binary(8)  NOT NULL,
    [Name] nvarchar(255)  NOT NULL
);
GO

-- Creating table 'ReportSnippets'
CREATE TABLE [dbo].[ReportSnippets] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [RowVersion] binary(8)  NOT NULL,
    [Name] nvarchar(255)  NOT NULL,
    [ReportSnippet_RowList] int  NOT NULL,
    [ReportSnippet_ColumnList] int  NOT NULL
);
GO

-- Creating table 'ReportSnippetToReportDefinitions'
CREATE TABLE [dbo].[ReportSnippetToReportDefinitions] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [RowVersion] binary(8)  NOT NULL,
    [ReportSnippetToReportDefinitio_ReportDefinition] int  NOT NULL,
    [ReportSnippetToReportDefinitio_ReportSnippet] int  NOT NULL
);
GO

-- Creating table 'RolePermissions'
CREATE TABLE [dbo].[RolePermissions] (
    [RoleName] nvarchar(128)  NOT NULL,
    [PermissionId] nvarchar(322)  NOT NULL
);
GO

-- Creating table 'RowLists'
CREATE TABLE [dbo].[RowLists] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [RowVersion] binary(8)  NOT NULL,
    [Name] nvarchar(255)  NOT NULL
);
GO

-- Creating table 'RowListToRows'
CREATE TABLE [dbo].[RowListToRows] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [RowVersion] binary(8)  NOT NULL,
    [OrderInList] int  NULL,
    [RowListToRow_Row] int  NOT NULL,
    [RowListToRow_RowList] int  NOT NULL
);
GO

-- Creating table 'Rows'
CREATE TABLE [dbo].[Rows] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [RowVersion] binary(8)  NOT NULL,
    [Name] nvarchar(255)  NOT NULL
);
GO

-- Creating table 'SettingCategories'
CREATE TABLE [dbo].[SettingCategories] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [RowVersion] binary(8)  NOT NULL,
    [Name] nvarchar(255)  NOT NULL
);
GO

-- Creating table 'SettingChoices'
CREATE TABLE [dbo].[SettingChoices] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [RowVersion] binary(8)  NOT NULL,
    [Name] nvarchar(255)  NOT NULL,
    [SettingChoice_SettingCategory] int  NOT NULL
);
GO

-- Creating table 'SettingChoiceSets'
CREATE TABLE [dbo].[SettingChoiceSets] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [RowVersion] binary(8)  NOT NULL,
    [Name] nvarchar(255)  NOT NULL
);
GO

-- Creating table 'SettingChoiceToExecutionResultSets'
CREATE TABLE [dbo].[SettingChoiceToExecutionResultSets] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [RowVersion] binary(8)  NOT NULL,
    [SettingChoiceToExecutionResult_ExecutionResultSet] int  NOT NULL,
    [SettingChoiceToExecutionResult_SettingChoice] int  NOT NULL
);
GO

-- Creating table 'SettingChoiceToSettingChoiceSets'
CREATE TABLE [dbo].[SettingChoiceToSettingChoiceSets] (
    [Id] int IDENTITY(1,1) NOT NULL,
    [RowVersion] binary(8)  NOT NULL,
    [SettingChoiceToSettingChoiceSe_SettingChoice] int  NOT NULL,
    [SettingChoiceToSettingChoiceSe_SettingChoiceSet] int  NOT NULL
);
GO

-- Creating table 'vw_aspnet_Applications'
CREATE TABLE [dbo].[vw_aspnet_Applications] (
    [ApplicationName] nvarchar(256)  NOT NULL,
    [LoweredApplicationName] nvarchar(256)  NOT NULL,
    [ApplicationId] uniqueidentifier  NOT NULL,
    [Description] nvarchar(256)  NULL
);
GO

-- Creating table 'vw_aspnet_MembershipUsers'
CREATE TABLE [dbo].[vw_aspnet_MembershipUsers] (
    [UserId] uniqueidentifier  NOT NULL,
    [PasswordFormat] int  NOT NULL,
    [MobilePIN] nvarchar(16)  NULL,
    [Email] nvarchar(256)  NULL,
    [LoweredEmail] nvarchar(256)  NULL,
    [PasswordQuestion] nvarchar(256)  NULL,
    [PasswordAnswer] nvarchar(128)  NULL,
    [IsApproved] bit  NOT NULL,
    [IsLockedOut] bit  NOT NULL,
    [CreateDate] datetime  NOT NULL,
    [LastLoginDate] datetime  NOT NULL,
    [LastPasswordChangedDate] datetime  NOT NULL,
    [LastLockoutDate] datetime  NOT NULL,
    [FailedPasswordAttemptCount] int  NOT NULL,
    [FailedPasswordAttemptWindowStart] datetime  NOT NULL,
    [FailedPasswordAnswerAttemptCount] int  NOT NULL,
    [FailedPasswordAnswerAttemptWindowStart] datetime  NOT NULL,
    [Comment] nvarchar(max)  NULL,
    [ApplicationId] uniqueidentifier  NOT NULL,
    [UserName] nvarchar(256)  NOT NULL,
    [MobileAlias] nvarchar(16)  NULL,
    [IsAnonymous] bit  NOT NULL,
    [LastActivityDate] datetime  NOT NULL
);
GO

-- Creating table 'vw_aspnet_Profiles'
CREATE TABLE [dbo].[vw_aspnet_Profiles] (
    [UserId] uniqueidentifier  NOT NULL,
    [LastUpdatedDate] datetime  NOT NULL,
    [DataSize] int  NULL
);
GO

-- Creating table 'vw_aspnet_Roles'
CREATE TABLE [dbo].[vw_aspnet_Roles] (
    [ApplicationId] uniqueidentifier  NOT NULL,
    [RoleId] uniqueidentifier  NOT NULL,
    [RoleName] nvarchar(256)  NOT NULL,
    [LoweredRoleName] nvarchar(256)  NOT NULL,
    [Description] nvarchar(256)  NULL
);
GO

-- Creating table 'vw_aspnet_Users'
CREATE TABLE [dbo].[vw_aspnet_Users] (
    [ApplicationId] uniqueidentifier  NOT NULL,
    [UserId] uniqueidentifier  NOT NULL,
    [UserName] nvarchar(256)  NOT NULL,
    [LoweredUserName] nvarchar(256)  NOT NULL,
    [MobileAlias] nvarchar(16)  NULL,
    [IsAnonymous] bit  NOT NULL,
    [LastActivityDate] datetime  NOT NULL
);
GO

-- Creating table 'vw_aspnet_UsersInRoles'
CREATE TABLE [dbo].[vw_aspnet_UsersInRoles] (
    [UserId] uniqueidentifier  NOT NULL,
    [RoleId] uniqueidentifier  NOT NULL
);
GO

-- Creating table 'aspnet_UsersInRoles'
CREATE TABLE [dbo].[aspnet_UsersInRoles] (
    [aspnet_Roles_RoleId] uniqueidentifier  NOT NULL,
    [aspnet_Users_UserId] uniqueidentifier  NOT NULL
);
GO

-- --------------------------------------------------
-- Creating all PRIMARY KEY constraints
-- --------------------------------------------------

-- Creating primary key on [ApplicationId] in table 'aspnet_Applications'
ALTER TABLE [dbo].[aspnet_Applications]
ADD CONSTRAINT [PK_aspnet_Applications]
    PRIMARY KEY CLUSTERED ([ApplicationId] ASC);
GO

-- Creating primary key on [UserId] in table 'aspnet_Membership'
ALTER TABLE [dbo].[aspnet_Membership]
ADD CONSTRAINT [PK_aspnet_Membership]
    PRIMARY KEY CLUSTERED ([UserId] ASC);
GO

-- Creating primary key on [UserId] in table 'aspnet_Profile'
ALTER TABLE [dbo].[aspnet_Profile]
ADD CONSTRAINT [PK_aspnet_Profile]
    PRIMARY KEY CLUSTERED ([UserId] ASC);
GO

-- Creating primary key on [RoleId] in table 'aspnet_Roles'
ALTER TABLE [dbo].[aspnet_Roles]
ADD CONSTRAINT [PK_aspnet_Roles]
    PRIMARY KEY CLUSTERED ([RoleId] ASC);
GO

-- Creating primary key on [Feature], [CompatibleSchemaVersion] in table 'aspnet_SchemaVersions'
ALTER TABLE [dbo].[aspnet_SchemaVersions]
ADD CONSTRAINT [PK_aspnet_SchemaVersions]
    PRIMARY KEY CLUSTERED ([Feature], [CompatibleSchemaVersion] ASC);
GO

-- Creating primary key on [UserId] in table 'aspnet_Users'
ALTER TABLE [dbo].[aspnet_Users]
ADD CONSTRAINT [PK_aspnet_Users]
    PRIMARY KEY CLUSTERED ([UserId] ASC);
GO

-- Creating primary key on [Id] in table 'ColumnLists'
ALTER TABLE [dbo].[ColumnLists]
ADD CONSTRAINT [PK_ColumnLists]
    PRIMARY KEY CLUSTERED ([Id] ASC);
GO

-- Creating primary key on [Id] in table 'ColumnListToColumns'
ALTER TABLE [dbo].[ColumnListToColumns]
ADD CONSTRAINT [PK_ColumnListToColumns]
    PRIMARY KEY CLUSTERED ([Id] ASC);
GO

-- Creating primary key on [Id] in table 'Columns'
ALTER TABLE [dbo].[Columns]
ADD CONSTRAINT [PK_Columns]
    PRIMARY KEY CLUSTERED ([Id] ASC);
GO

-- Creating primary key on [Id] in table 'DataPoints'
ALTER TABLE [dbo].[DataPoints]
ADD CONSTRAINT [PK_DataPoints]
    PRIMARY KEY CLUSTERED ([Id] ASC);
GO

-- Creating primary key on [Id] in table 'ExecutionResultSets'
ALTER TABLE [dbo].[ExecutionResultSets]
ADD CONSTRAINT [PK_ExecutionResultSets]
    PRIMARY KEY CLUSTERED ([Id] ASC);
GO

-- Creating primary key on [Id] in table 'ReportDefinitions'
ALTER TABLE [dbo].[ReportDefinitions]
ADD CONSTRAINT [PK_ReportDefinitions]
    PRIMARY KEY CLUSTERED ([Id] ASC);
GO

-- Creating primary key on [Id] in table 'ReportSnippets'
ALTER TABLE [dbo].[ReportSnippets]
ADD CONSTRAINT [PK_ReportSnippets]
    PRIMARY KEY CLUSTERED ([Id] ASC);
GO

-- Creating primary key on [Id] in table 'ReportSnippetToReportDefinitions'
ALTER TABLE [dbo].[ReportSnippetToReportDefinitions]
ADD CONSTRAINT [PK_ReportSnippetToReportDefinitions]
    PRIMARY KEY CLUSTERED ([Id] ASC);
GO

-- Creating primary key on [RoleName], [PermissionId] in table 'RolePermissions'
ALTER TABLE [dbo].[RolePermissions]
ADD CONSTRAINT [PK_RolePermissions]
    PRIMARY KEY CLUSTERED ([RoleName], [PermissionId] ASC);
GO

-- Creating primary key on [Id] in table 'RowLists'
ALTER TABLE [dbo].[RowLists]
ADD CONSTRAINT [PK_RowLists]
    PRIMARY KEY CLUSTERED ([Id] ASC);
GO

-- Creating primary key on [Id] in table 'RowListToRows'
ALTER TABLE [dbo].[RowListToRows]
ADD CONSTRAINT [PK_RowListToRows]
    PRIMARY KEY CLUSTERED ([Id] ASC);
GO

-- Creating primary key on [Id] in table 'Rows'
ALTER TABLE [dbo].[Rows]
ADD CONSTRAINT [PK_Rows]
    PRIMARY KEY CLUSTERED ([Id] ASC);
GO

-- Creating primary key on [Id] in table 'SettingCategories'
ALTER TABLE [dbo].[SettingCategories]
ADD CONSTRAINT [PK_SettingCategories]
    PRIMARY KEY CLUSTERED ([Id] ASC);
GO

-- Creating primary key on [Id] in table 'SettingChoices'
ALTER TABLE [dbo].[SettingChoices]
ADD CONSTRAINT [PK_SettingChoices]
    PRIMARY KEY CLUSTERED ([Id] ASC);
GO

-- Creating primary key on [Id] in table 'SettingChoiceSets'
ALTER TABLE [dbo].[SettingChoiceSets]
ADD CONSTRAINT [PK_SettingChoiceSets]
    PRIMARY KEY CLUSTERED ([Id] ASC);
GO

-- Creating primary key on [Id] in table 'SettingChoiceToExecutionResultSets'
ALTER TABLE [dbo].[SettingChoiceToExecutionResultSets]
ADD CONSTRAINT [PK_SettingChoiceToExecutionResultSets]
    PRIMARY KEY CLUSTERED ([Id] ASC);
GO

-- Creating primary key on [Id] in table 'SettingChoiceToSettingChoiceSets'
ALTER TABLE [dbo].[SettingChoiceToSettingChoiceSets]
ADD CONSTRAINT [PK_SettingChoiceToSettingChoiceSets]
    PRIMARY KEY CLUSTERED ([Id] ASC);
GO

-- Creating primary key on [ApplicationName], [LoweredApplicationName], [ApplicationId] in table 'vw_aspnet_Applications'
ALTER TABLE [dbo].[vw_aspnet_Applications]
ADD CONSTRAINT [PK_vw_aspnet_Applications]
    PRIMARY KEY CLUSTERED ([ApplicationName], [LoweredApplicationName], [ApplicationId] ASC);
GO

-- Creating primary key on [UserId], [PasswordFormat], [IsApproved], [IsLockedOut], [CreateDate], [LastLoginDate], [LastPasswordChangedDate], [LastLockoutDate], [FailedPasswordAttemptCount], [FailedPasswordAttemptWindowStart], [FailedPasswordAnswerAttemptCount], [FailedPasswordAnswerAttemptWindowStart], [ApplicationId], [UserName], [IsAnonymous], [LastActivityDate] in table 'vw_aspnet_MembershipUsers'
ALTER TABLE [dbo].[vw_aspnet_MembershipUsers]
ADD CONSTRAINT [PK_vw_aspnet_MembershipUsers]
    PRIMARY KEY CLUSTERED ([UserId], [PasswordFormat], [IsApproved], [IsLockedOut], [CreateDate], [LastLoginDate], [LastPasswordChangedDate], [LastLockoutDate], [FailedPasswordAttemptCount], [FailedPasswordAttemptWindowStart], [FailedPasswordAnswerAttemptCount], [FailedPasswordAnswerAttemptWindowStart], [ApplicationId], [UserName], [IsAnonymous], [LastActivityDate] ASC);
GO

-- Creating primary key on [UserId], [LastUpdatedDate] in table 'vw_aspnet_Profiles'
ALTER TABLE [dbo].[vw_aspnet_Profiles]
ADD CONSTRAINT [PK_vw_aspnet_Profiles]
    PRIMARY KEY CLUSTERED ([UserId], [LastUpdatedDate] ASC);
GO

-- Creating primary key on [ApplicationId], [RoleId], [RoleName], [LoweredRoleName] in table 'vw_aspnet_Roles'
ALTER TABLE [dbo].[vw_aspnet_Roles]
ADD CONSTRAINT [PK_vw_aspnet_Roles]
    PRIMARY KEY CLUSTERED ([ApplicationId], [RoleId], [RoleName], [LoweredRoleName] ASC);
GO

-- Creating primary key on [ApplicationId], [UserId], [UserName], [LoweredUserName], [IsAnonymous], [LastActivityDate] in table 'vw_aspnet_Users'
ALTER TABLE [dbo].[vw_aspnet_Users]
ADD CONSTRAINT [PK_vw_aspnet_Users]
    PRIMARY KEY CLUSTERED ([ApplicationId], [UserId], [UserName], [LoweredUserName], [IsAnonymous], [LastActivityDate] ASC);
GO

-- Creating primary key on [UserId], [RoleId] in table 'vw_aspnet_UsersInRoles'
ALTER TABLE [dbo].[vw_aspnet_UsersInRoles]
ADD CONSTRAINT [PK_vw_aspnet_UsersInRoles]
    PRIMARY KEY CLUSTERED ([UserId], [RoleId] ASC);
GO

-- Creating primary key on [aspnet_Roles_RoleId], [aspnet_Users_UserId] in table 'aspnet_UsersInRoles'
ALTER TABLE [dbo].[aspnet_UsersInRoles]
ADD CONSTRAINT [PK_aspnet_UsersInRoles]
    PRIMARY KEY CLUSTERED ([aspnet_Roles_RoleId], [aspnet_Users_UserId] ASC);
GO

-- --------------------------------------------------
-- Creating all FOREIGN KEY constraints
-- --------------------------------------------------

-- Creating foreign key on [ApplicationId] in table 'aspnet_Membership'
ALTER TABLE [dbo].[aspnet_Membership]
ADD CONSTRAINT [FK__aspnet_Me__Appli__02084FDA]
    FOREIGN KEY ([ApplicationId])
    REFERENCES [dbo].[aspnet_Applications]
        ([ApplicationId])
    ON DELETE NO ACTION ON UPDATE NO ACTION;
GO

-- Creating non-clustered index for FOREIGN KEY 'FK__aspnet_Me__Appli__02084FDA'
CREATE INDEX [IX_FK__aspnet_Me__Appli__02084FDA]
ON [dbo].[aspnet_Membership]
    ([ApplicationId]);
GO

-- Creating foreign key on [ApplicationId] in table 'aspnet_Roles'
ALTER TABLE [dbo].[aspnet_Roles]
ADD CONSTRAINT [FK__aspnet_Ro__Appli__208CD6FA]
    FOREIGN KEY ([ApplicationId])
    REFERENCES [dbo].[aspnet_Applications]
        ([ApplicationId])
    ON DELETE NO ACTION ON UPDATE NO ACTION;
GO

-- Creating non-clustered index for FOREIGN KEY 'FK__aspnet_Ro__Appli__208CD6FA'
CREATE INDEX [IX_FK__aspnet_Ro__Appli__208CD6FA]
ON [dbo].[aspnet_Roles]
    ([ApplicationId]);
GO

-- Creating foreign key on [ApplicationId] in table 'aspnet_Users'
ALTER TABLE [dbo].[aspnet_Users]
ADD CONSTRAINT [FK__aspnet_Us__Appli__71D1E811]
    FOREIGN KEY ([ApplicationId])
    REFERENCES [dbo].[aspnet_Applications]
        ([ApplicationId])
    ON DELETE NO ACTION ON UPDATE NO ACTION;
GO

-- Creating non-clustered index for FOREIGN KEY 'FK__aspnet_Us__Appli__71D1E811'
CREATE INDEX [IX_FK__aspnet_Us__Appli__71D1E811]
ON [dbo].[aspnet_Users]
    ([ApplicationId]);
GO

-- Creating foreign key on [UserId] in table 'aspnet_Membership'
ALTER TABLE [dbo].[aspnet_Membership]
ADD CONSTRAINT [FK__aspnet_Me__UserI__02FC7413]
    FOREIGN KEY ([UserId])
    REFERENCES [dbo].[aspnet_Users]
        ([UserId])
    ON DELETE NO ACTION ON UPDATE NO ACTION;
GO

-- Creating foreign key on [UserId] in table 'aspnet_Profile'
ALTER TABLE [dbo].[aspnet_Profile]
ADD CONSTRAINT [FK__aspnet_Pr__UserI__17036CC0]
    FOREIGN KEY ([UserId])
    REFERENCES [dbo].[aspnet_Users]
        ([UserId])
    ON DELETE NO ACTION ON UPDATE NO ACTION;
GO

-- Creating foreign key on [ColumnListToColumn_ColumnList] in table 'ColumnListToColumns'
ALTER TABLE [dbo].[ColumnListToColumns]
ADD CONSTRAINT [FK_ColumnListToColumn_ColumnList]
    FOREIGN KEY ([ColumnListToColumn_ColumnList])
    REFERENCES [dbo].[ColumnLists]
        ([Id])
    ON DELETE CASCADE ON UPDATE NO ACTION;
GO

-- Creating non-clustered index for FOREIGN KEY 'FK_ColumnListToColumn_ColumnList'
CREATE INDEX [IX_FK_ColumnListToColumn_ColumnList]
ON [dbo].[ColumnListToColumns]
    ([ColumnListToColumn_ColumnList]);
GO

-- Creating foreign key on [ReportSnippet_ColumnList] in table 'ReportSnippets'
ALTER TABLE [dbo].[ReportSnippets]
ADD CONSTRAINT [FK_ReportSnippet_ColumnList]
    FOREIGN KEY ([ReportSnippet_ColumnList])
    REFERENCES [dbo].[ColumnLists]
        ([Id])
    ON DELETE CASCADE ON UPDATE NO ACTION;
GO

-- Creating non-clustered index for FOREIGN KEY 'FK_ReportSnippet_ColumnList'
CREATE INDEX [IX_FK_ReportSnippet_ColumnList]
ON [dbo].[ReportSnippets]
    ([ReportSnippet_ColumnList]);
GO

-- Creating foreign key on [ColumnListToColumn_Column] in table 'ColumnListToColumns'
ALTER TABLE [dbo].[ColumnListToColumns]
ADD CONSTRAINT [FK_ColumnListToColumn_Column]
    FOREIGN KEY ([ColumnListToColumn_Column])
    REFERENCES [dbo].[Columns]
        ([Id])
    ON DELETE CASCADE ON UPDATE NO ACTION;
GO

-- Creating non-clustered index for FOREIGN KEY 'FK_ColumnListToColumn_Column'
CREATE INDEX [IX_FK_ColumnListToColumn_Column]
ON [dbo].[ColumnListToColumns]
    ([ColumnListToColumn_Column]);
GO

-- Creating foreign key on [DataPoint_Column] in table 'DataPoints'
ALTER TABLE [dbo].[DataPoints]
ADD CONSTRAINT [FK_DataPoint_Column]
    FOREIGN KEY ([DataPoint_Column])
    REFERENCES [dbo].[Columns]
        ([Id])
    ON DELETE CASCADE ON UPDATE NO ACTION;
GO

-- Creating non-clustered index for FOREIGN KEY 'FK_DataPoint_Column'
CREATE INDEX [IX_FK_DataPoint_Column]
ON [dbo].[DataPoints]
    ([DataPoint_Column]);
GO

-- Creating foreign key on [DataPoint_Row] in table 'DataPoints'
ALTER TABLE [dbo].[DataPoints]
ADD CONSTRAINT [FK_DataPoint_Row]
    FOREIGN KEY ([DataPoint_Row])
    REFERENCES [dbo].[Rows]
        ([Id])
    ON DELETE CASCADE ON UPDATE NO ACTION;
GO

-- Creating non-clustered index for FOREIGN KEY 'FK_DataPoint_Row'
CREATE INDEX [IX_FK_DataPoint_Row]
ON [dbo].[DataPoints]
    ([DataPoint_Row]);
GO

-- Creating foreign key on [ExecutionResultSet_DataPoint] in table 'DataPoints'
ALTER TABLE [dbo].[DataPoints]
ADD CONSTRAINT [FK_ExecutionResultSet_DataPoint]
    FOREIGN KEY ([ExecutionResultSet_DataPoint])
    REFERENCES [dbo].[ExecutionResultSets]
        ([Id])
    ON DELETE CASCADE ON UPDATE NO ACTION;
GO

-- Creating non-clustered index for FOREIGN KEY 'FK_ExecutionResultSet_DataPoint'
CREATE INDEX [IX_FK_ExecutionResultSet_DataPoint]
ON [dbo].[DataPoints]
    ([ExecutionResultSet_DataPoint]);
GO

-- Creating foreign key on [SettingChoiceToExecutionResult_ExecutionResultSet] in table 'SettingChoiceToExecutionResultSets'
ALTER TABLE [dbo].[SettingChoiceToExecutionResultSets]
ADD CONSTRAINT [FK_SettingChoiceToExecutionResult_ExecutionResultSet]
    FOREIGN KEY ([SettingChoiceToExecutionResult_ExecutionResultSet])
    REFERENCES [dbo].[ExecutionResultSets]
        ([Id])
    ON DELETE CASCADE ON UPDATE NO ACTION;
GO

-- Creating non-clustered index for FOREIGN KEY 'FK_SettingChoiceToExecutionResult_ExecutionResultSet'
CREATE INDEX [IX_FK_SettingChoiceToExecutionResult_ExecutionResultSet]
ON [dbo].[SettingChoiceToExecutionResultSets]
    ([SettingChoiceToExecutionResult_ExecutionResultSet]);
GO

-- Creating foreign key on [ReportSnippetToReportDefinitio_ReportDefinition] in table 'ReportSnippetToReportDefinitions'
ALTER TABLE [dbo].[ReportSnippetToReportDefinitions]
ADD CONSTRAINT [FK_ReportSnippetToReportDefinitio_ReportDefinition]
    FOREIGN KEY ([ReportSnippetToReportDefinitio_ReportDefinition])
    REFERENCES [dbo].[ReportDefinitions]
        ([Id])
    ON DELETE CASCADE ON UPDATE NO ACTION;
GO

-- Creating non-clustered index for FOREIGN KEY 'FK_ReportSnippetToReportDefinitio_ReportDefinition'
CREATE INDEX [IX_FK_ReportSnippetToReportDefinitio_ReportDefinition]
ON [dbo].[ReportSnippetToReportDefinitions]
    ([ReportSnippetToReportDefinitio_ReportDefinition]);
GO

-- Creating foreign key on [ReportSnippet_RowList] in table 'ReportSnippets'
ALTER TABLE [dbo].[ReportSnippets]
ADD CONSTRAINT [FK_ReportSnippet_RowList]
    FOREIGN KEY ([ReportSnippet_RowList])
    REFERENCES [dbo].[RowLists]
        ([Id])
    ON DELETE CASCADE ON UPDATE NO ACTION;
GO

-- Creating non-clustered index for FOREIGN KEY 'FK_ReportSnippet_RowList'
CREATE INDEX [IX_FK_ReportSnippet_RowList]
ON [dbo].[ReportSnippets]
    ([ReportSnippet_RowList]);
GO

-- Creating foreign key on [ReportSnippetToReportDefinitio_ReportSnippet] in table 'ReportSnippetToReportDefinitions'
ALTER TABLE [dbo].[ReportSnippetToReportDefinitions]
ADD CONSTRAINT [FK_ReportSnippetToReportDefinitio_ReportSnippet]
    FOREIGN KEY ([ReportSnippetToReportDefinitio_ReportSnippet])
    REFERENCES [dbo].[ReportSnippets]
        ([Id])
    ON DELETE CASCADE ON UPDATE NO ACTION;
GO

-- Creating non-clustered index for FOREIGN KEY 'FK_ReportSnippetToReportDefinitio_ReportSnippet'
CREATE INDEX [IX_FK_ReportSnippetToReportDefinitio_ReportSnippet]
ON [dbo].[ReportSnippetToReportDefinitions]
    ([ReportSnippetToReportDefinitio_ReportSnippet]);
GO

-- Creating foreign key on [RowListToRow_RowList] in table 'RowListToRows'
ALTER TABLE [dbo].[RowListToRows]
ADD CONSTRAINT [FK_RowListToRow_RowList]
    FOREIGN KEY ([RowListToRow_RowList])
    REFERENCES [dbo].[RowLists]
        ([Id])
    ON DELETE CASCADE ON UPDATE NO ACTION;
GO

-- Creating non-clustered index for FOREIGN KEY 'FK_RowListToRow_RowList'
CREATE INDEX [IX_FK_RowListToRow_RowList]
ON [dbo].[RowListToRows]
    ([RowListToRow_RowList]);
GO

-- Creating foreign key on [RowListToRow_Row] in table 'RowListToRows'
ALTER TABLE [dbo].[RowListToRows]
ADD CONSTRAINT [FK_RowListToRow_Row]
    FOREIGN KEY ([RowListToRow_Row])
    REFERENCES [dbo].[Rows]
        ([Id])
    ON DELETE CASCADE ON UPDATE NO ACTION;
GO

-- Creating non-clustered index for FOREIGN KEY 'FK_RowListToRow_Row'
CREATE INDEX [IX_FK_RowListToRow_Row]
ON [dbo].[RowListToRows]
    ([RowListToRow_Row]);
GO

-- Creating foreign key on [SettingChoice_SettingCategory] in table 'SettingChoices'
ALTER TABLE [dbo].[SettingChoices]
ADD CONSTRAINT [FK_SettingChoice_SettingCategory]
    FOREIGN KEY ([SettingChoice_SettingCategory])
    REFERENCES [dbo].[SettingCategories]
        ([Id])
    ON DELETE CASCADE ON UPDATE NO ACTION;
GO

-- Creating non-clustered index for FOREIGN KEY 'FK_SettingChoice_SettingCategory'
CREATE INDEX [IX_FK_SettingChoice_SettingCategory]
ON [dbo].[SettingChoices]
    ([SettingChoice_SettingCategory]);
GO

-- Creating foreign key on [SettingChoiceToExecutionResult_SettingChoice] in table 'SettingChoiceToExecutionResultSets'
ALTER TABLE [dbo].[SettingChoiceToExecutionResultSets]
ADD CONSTRAINT [FK_SettingChoiceToExecutionResult_SettingChoice]
    FOREIGN KEY ([SettingChoiceToExecutionResult_SettingChoice])
    REFERENCES [dbo].[SettingChoices]
        ([Id])
    ON DELETE CASCADE ON UPDATE NO ACTION;
GO

-- Creating non-clustered index for FOREIGN KEY 'FK_SettingChoiceToExecutionResult_SettingChoice'
CREATE INDEX [IX_FK_SettingChoiceToExecutionResult_SettingChoice]
ON [dbo].[SettingChoiceToExecutionResultSets]
    ([SettingChoiceToExecutionResult_SettingChoice]);
GO

-- Creating foreign key on [SettingChoiceToSettingChoiceSe_SettingChoice] in table 'SettingChoiceToSettingChoiceSets'
ALTER TABLE [dbo].[SettingChoiceToSettingChoiceSets]
ADD CONSTRAINT [FK_SettingChoiceToSettingChoiceSe_SettingChoice]
    FOREIGN KEY ([SettingChoiceToSettingChoiceSe_SettingChoice])
    REFERENCES [dbo].[SettingChoices]
        ([Id])
    ON DELETE CASCADE ON UPDATE NO ACTION;
GO

-- Creating non-clustered index for FOREIGN KEY 'FK_SettingChoiceToSettingChoiceSe_SettingChoice'
CREATE INDEX [IX_FK_SettingChoiceToSettingChoiceSe_SettingChoice]
ON [dbo].[SettingChoiceToSettingChoiceSets]
    ([SettingChoiceToSettingChoiceSe_SettingChoice]);
GO

-- Creating foreign key on [SettingChoiceToSettingChoiceSe_SettingChoiceSet] in table 'SettingChoiceToSettingChoiceSets'
ALTER TABLE [dbo].[SettingChoiceToSettingChoiceSets]
ADD CONSTRAINT [FK_SettingChoiceToSettingChoiceSe_SettingChoiceSet]
    FOREIGN KEY ([SettingChoiceToSettingChoiceSe_SettingChoiceSet])
    REFERENCES [dbo].[SettingChoiceSets]
        ([Id])
    ON DELETE CASCADE ON UPDATE NO ACTION;
GO

-- Creating non-clustered index for FOREIGN KEY 'FK_SettingChoiceToSettingChoiceSe_SettingChoiceSet'
CREATE INDEX [IX_FK_SettingChoiceToSettingChoiceSe_SettingChoiceSet]
ON [dbo].[SettingChoiceToSettingChoiceSets]
    ([SettingChoiceToSettingChoiceSe_SettingChoiceSet]);
GO

-- Creating foreign key on [aspnet_Roles_RoleId] in table 'aspnet_UsersInRoles'
ALTER TABLE [dbo].[aspnet_UsersInRoles]
ADD CONSTRAINT [FK_aspnet_UsersInRoles_aspnet_Roles]
    FOREIGN KEY ([aspnet_Roles_RoleId])
    REFERENCES [dbo].[aspnet_Roles]
        ([RoleId])
    ON DELETE NO ACTION ON UPDATE NO ACTION;
GO

-- Creating foreign key on [aspnet_Users_UserId] in table 'aspnet_UsersInRoles'
ALTER TABLE [dbo].[aspnet_UsersInRoles]
ADD CONSTRAINT [FK_aspnet_UsersInRoles_aspnet_Users]
    FOREIGN KEY ([aspnet_Users_UserId])
    REFERENCES [dbo].[aspnet_Users]
        ([UserId])
    ON DELETE NO ACTION ON UPDATE NO ACTION;
GO

-- Creating non-clustered index for FOREIGN KEY 'FK_aspnet_UsersInRoles_aspnet_Users'
CREATE INDEX [IX_FK_aspnet_UsersInRoles_aspnet_Users]
ON [dbo].[aspnet_UsersInRoles]
    ([aspnet_Users_UserId]);
GO

-- --------------------------------------------------
-- Script has ended
-- --------------------------------------------------
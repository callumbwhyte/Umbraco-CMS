﻿using System;
using System.Data;
using System.Linq;
using Umbraco.Core.Configuration;
using Umbraco.Core.Logging;
using Umbraco.Core.Models;
using Umbraco.Core.Models.Rdbms;
using Umbraco.Core.Persistence.SqlSyntax;

namespace Umbraco.Core.Persistence.Migrations.Upgrades.TargetVersionSevenSevenZero
{
    [Migration("7.7.0", 1, Constants.System.UmbracoMigrationName)]
    public class AddUserGroupTables : MigrationBase
    {
        public AddUserGroupTables(ISqlSyntaxProvider sqlSyntax, ILogger logger)
            : base(sqlSyntax, logger)
        { }

        public override void Up()
        {
            var tables = SqlSyntax.GetTablesInSchema(Context.Database).ToArray();
            var constraints = SqlSyntax.GetConstraintsPerColumn(Context.Database).Distinct().ToArray();

            if (AddNewTables(tables))
            {
                MigrateUserPermissions();
                MigrateUserTypesToGroups();
                DeleteOldTables(tables, constraints);
                SetDefaultIcons();
            }
            else
            {
                //if we aren't adding the tables, make sure that the umbracoUserGroup table has the correct FKs - these
                //were added after the beta release so we need to do some cleanup
                //if the FK doesn't exist
                if (constraints.Any(x => x.Item1.InvariantEquals("umbracoUserGroup") 
                    && x.Item2.InvariantEquals("startContentId") 
                    && x.Item3.InvariantEquals("FK_startContentId_umbracoNode_id")) == false)
                {
                    //before we add any foreign key we need to make sure there's no stale data in there which would  have happened in the beta
                    //release if a start node was assigned and then that start node was deleted.
                    Execute.Sql(@"UPDATE umbracoUserGroup SET startContentId = NULL WHERE startContentId NOT IN (SELECT id FROM umbracoNode)");

                    Create.ForeignKey("FK_startContentId_umbracoNode_id")
                        .FromTable("umbracoUserGroup")
                        .ForeignColumn("startContentId")
                        .ToTable("umbracoNode")
                        .PrimaryColumn("id")
                        .OnDelete(Rule.None)
                        .OnUpdate(Rule.None);
                }

                if (constraints.Any(x => x.Item1.InvariantEquals("umbracoUserGroup") 
                    && x.Item2.InvariantEquals("startMediaId") 
                    && x.Item3.InvariantEquals("FK_startMediaId_umbracoNode_id")) == false)
                {
                    //before we add any foreign key we need to make sure there's no stale data in there which would  have happened in the beta
                    //release if a start node was assigned and then that start node was deleted.
                    Execute.Sql(@"UPDATE umbracoUserGroup SET startMediaId = NULL WHERE startMediaId NOT IN (SELECT id FROM umbracoNode)");

                    Create.ForeignKey("FK_startMediaId_umbracoNode_id")
                        .FromTable("umbracoUserGroup")
                        .ForeignColumn("startMediaId")
                        .ToTable("umbracoNode")
                        .PrimaryColumn("id")
                        .OnDelete(Rule.None)
                        .OnUpdate(Rule.None);
                }
            }
        }

        private void SetDefaultIcons()
        {
            Execute.Sql(string.Format("UPDATE umbracoUserGroup SET icon = '{0}' WHERE userGroupAlias = '{1}'", "", Constants.Security.AdminGroupAlias));
            Execute.Sql(string.Format("UPDATE umbracoUserGroup SET icon = '{0}' WHERE userGroupAlias = '{1}'", "icon-edit", "writer"));
            Execute.Sql(string.Format("UPDATE umbracoUserGroup SET icon = '{0}' WHERE userGroupAlias = '{1}'", "icon-tools", "editor"));
            Execute.Sql(string.Format("UPDATE umbracoUserGroup SET icon = '{0}' WHERE userGroupAlias = '{1}'", "icon-globe", "translator"));
        }

        private bool AddNewTables(string[] tables)
        {
            var updated = false;
            if (tables.InvariantContains("umbracoUserGroup") == false)
            {
                Create.Table<UserGroupDto>();
                updated = true;
            }

            if (tables.InvariantContains("umbracoUser2UserGroup") == false)
            {
                Create.Table<User2UserGroupDto>();
                updated = true;
            }

            if (tables.InvariantContains("umbracoUserGroup2App") == false)
            {
                Create.Table<UserGroup2AppDto>();
                updated = true;
            }

            if (tables.InvariantContains("umbracoUserGroup2NodePermission") == false)
            {
                Create.Table<UserGroup2NodePermissionDto>();
                updated = true;
            }

            

            return updated;
        }       

        private void MigrateUserTypesToGroups()
        {
            // Create a user group for each user type
            Execute.Sql(@"INSERT INTO umbracoUserGroup (userGroupAlias, userGroupName, userGroupDefaultPermissions)
                SELECT userTypeAlias, userTypeName, userTypeDefaultPermissions
                FROM umbracoUserType");

            // Add each user to the group created from their type
            Execute.Sql(@"INSERT INTO umbracoUser2UserGroup (userId, userGroupId)
                SELECT u.id, ug.id
                FROM umbracoUser u
                INNER JOIN umbracoUserType ut ON ut.id = u.userType
                INNER JOIN umbracoUserGroup ug ON ug.userGroupAlias = ut.userTypeAlias");

            // Add the built-in administrator account to all apps
            Execute.Sql(@"INSERT INTO umbracoUserGroup2app (userGroupId,app)
                SELECT ug.id, app
                FROM umbracoUserGroup ug
                INNER JOIN umbracoUser2UserGroup u2ug ON u2ug.userGroupId = ug.id
                INNER JOIN umbracoUser u ON u.id = u2ug.userId
                INNER JOIN umbracoUser2app u2a ON u2a." + SqlSyntax.GetQuotedColumnName("user") + @" = u.id
				WHERE u.id = 0");

            // Rename some groups for consistency (plural form)
            Execute.Sql("UPDATE umbracoUserGroup SET userGroupName = 'Writers' WHERE userGroupAlias = 'writer'");
            Execute.Sql("UPDATE umbracoUserGroup SET userGroupName = 'Translators' WHERE userGroupAlias = 'translator'");

            //Ensure all built in groups have a start node of -1
            Execute.Sql("UPDATE umbracoUserGroup SET startContentId = -1 WHERE userGroupAlias = 'editor'");
            Execute.Sql("UPDATE umbracoUserGroup SET startMediaId = -1 WHERE userGroupAlias = 'editor'");
            Execute.Sql("UPDATE umbracoUserGroup SET startContentId = -1 WHERE userGroupAlias = 'writer'");
            Execute.Sql("UPDATE umbracoUserGroup SET startMediaId = -1 WHERE userGroupAlias = 'writer'");
            Execute.Sql("UPDATE umbracoUserGroup SET startContentId = -1 WHERE userGroupAlias = 'translator'");
            Execute.Sql("UPDATE umbracoUserGroup SET startMediaId = -1 WHERE userGroupAlias = 'translator'");
            Execute.Sql("UPDATE umbracoUserGroup SET startContentId = -1 WHERE userGroupAlias = 'admin'");
            Execute.Sql("UPDATE umbracoUserGroup SET startMediaId = -1 WHERE userGroupAlias = 'admin'");
        }

        private void MigrateUserPermissions()
        {
            // Create user group records for all non-admin users that have specific permissions set
            Execute.Sql(@"INSERT INTO umbracoUserGroup(userGroupAlias, userGroupName)
                SELECT userName + 'Group', 'Group for ' + userName
                FROM umbracoUser
                WHERE (id IN (
	                SELECT " + SqlSyntax.GetQuotedColumnName("user") + @"
	                FROM umbracoUser2app
                ) OR id IN (
	                SELECT userid
	                FROM umbracoUser2NodePermission
                ))
                AND id > 0");

            // Associate those groups with the users
            Execute.Sql(@"INSERT INTO umbracoUser2UserGroup (userId, userGroupId)
                SELECT u.id, ug.id
                FROM umbracoUser u
                INNER JOIN umbracoUserGroup ug ON ug.userGroupAlias = userName + 'Group'");

            // Create node permissions on the groups
            Execute.Sql(@"INSERT INTO umbracoUserGroup2NodePermission (userGroupId,nodeId,permission)
                SELECT ug.id, nodeId, permission
                FROM umbracoUserGroup ug
                INNER JOIN umbracoUser2UserGroup u2ug ON u2ug.userGroupId = ug.id
                INNER JOIN umbracoUser u ON u.id = u2ug.userId
                INNER JOIN umbracoUser2NodePermission u2np ON u2np.userId = u.id
				WHERE ug.userGroupAlias NOT IN (
					SELECT userTypeAlias
					FROM umbracoUserType
				)");

            // Create app permissions on the groups
            Execute.Sql(@"INSERT INTO umbracoUserGroup2app (userGroupId,app)
                SELECT ug.id, app
                FROM umbracoUserGroup ug
                INNER JOIN umbracoUser2UserGroup u2ug ON u2ug.userGroupId = ug.id
                INNER JOIN umbracoUser u ON u.id = u2ug.userId
                INNER JOIN umbracoUser2app u2a ON u2a." + SqlSyntax.GetQuotedColumnName("user") + @" = u.id
				WHERE ug.userGroupAlias NOT IN (
					SELECT userTypeAlias
					FROM umbracoUserType
				)");
        }

        private void DeleteOldTables(string[] tables, Tuple<string, string, string>[] constraints)
        {
            if (tables.InvariantContains("umbracoUser2App"))
            {
                Delete.Table("umbracoUser2App");
            }

            if (tables.InvariantContains("umbracoUser2NodePermission"))
            {
                Delete.Table("umbracoUser2NodePermission");
            }

            if (tables.InvariantContains("umbracoUserType") && tables.InvariantContains("umbracoUser"))
            {
                if (constraints.Any(x => x.Item1.InvariantEquals("umbracoUser") && x.Item3.InvariantEquals("FK_umbracoUser_umbracoUserType_id")))
                {
                    Delete.ForeignKey("FK_umbracoUser_umbracoUserType_id").OnTable("umbracoUser");
                }

                Delete.Column("userType").FromTable("umbracoUser");
                Delete.Table("umbracoUserType");
            }
        }

        public override void Down()
        { }
    }
}
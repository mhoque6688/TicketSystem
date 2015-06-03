﻿// TicketDesk - Attribution notice
// Contributor(s):
//
//      Stephen Redd (stephen@reddnet.net, http://www.reddnet.net)
//
// This file is distributed under the terms of the Microsoft Public 
// License (Ms-PL). See http://opensource.org/licenses/MS-PL
// for the complete terms of use. 
//
// For any distribution that contains code from this file, this notice of 
// attribution must remain intact, and a copy of the license must be 
// provided to the recipient.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Microsoft.AspNet.Identity;
using TicketDesk.Domain.Model;
using TicketDesk.Web.Identity;

namespace TicketDesk.Web.Client.Models
{
    public class FilterBarViewModel
    {
        private UserTicketListSetting CurrentListSetting { get; set; }
        private TicketDeskRoleManager RoleManager { get; set; }
        private TicketDeskUserManager UserManager { get; set; }
        public FilterBarViewModel(UserTicketListSetting currentListSetting)
        {
            CurrentListSetting = currentListSetting;
            RoleManager = DependencyResolver.Current.GetService<TicketDeskRoleManager>();
            UserManager = DependencyResolver.Current.GetService<TicketDeskUserManager>();
        }


        //TODO: Refactor all selectlists here to use the select list extensions B

        public SelectList ItemsPerPageSelectList
        {
            get
            {
                return new SelectList(
                    new[]
                    {
                        new {Text = "10", Value = "10"},
                        new {Text = "20", Value = "20"},
                        new {Text = "30", Value = "30"},
                        new {Text = "50", Value = "50"},
                        new {Text = "100", Value = "100"}
                    },
                    "Value",
                    "Text",
                    CurrentListSetting.ItemsPerPage
                    );
            }
        }

        public SelectList TicketStatusSelectList
        {
            get
            {

                string selectedStatus = "any";
                var fColumn = CurrentListSetting.FilterColumns.SingleOrDefault(fc => fc.ColumnName == "TicketStatus");
                if (fColumn != null)
                {
                    if (!fColumn.ColumnValue.ToString().Equals("Closed", StringComparison.InvariantCultureIgnoreCase)) 
                    {
                        selectedStatus = fColumn.ColumnValue.ToString().ToLowerInvariant();
                    }
                    else
                    {
                        selectedStatus = (fColumn.UseEqualityComparison.HasValue && fColumn.UseEqualityComparison.Value) ? "Closed" : "Open";
                    }
                }

                return new SelectList(
                    new[] 
                    {   
                        new { Text="-- any --", Value="Any"}, 
                        new { Text="-- open --", Value="Open"},
                        new { Text="Active", Value="Active"},
                        new { Text="More Info", Value="MoreInfo"},
                        new { Text="Resolved", Value="Resolved"},
                        new { Text="Closed", Value="Closed"}
                    },
                "Value",
                "Text",
                selectedStatus
                );
            }
        }


        public Dictionary<string, object> OwnerFilterHtmlAttributes
        {
            get
            {
                var isOwnerFilterDisabled = CurrentListSetting.DisabledFilterColumnNames.Contains("Owner");
                var cVal = String.Format("postback{0}", (isOwnerFilterDisabled) ? " disabled" : string.Empty);
                var ownerHtmlAttributes = new Dictionary<string, object> { { "Class", cVal } };
                if (isOwnerFilterDisabled)
                {
                    ownerHtmlAttributes.Add("Disabled", true);
                }
                return ownerHtmlAttributes;
            }
        }

       

        public SelectList SubmittersSelectList
        {
            get
            {
                string selectedUserName = "anyone";
                var fColumn = CurrentListSetting.FilterColumns.SingleOrDefault(fc => fc.ColumnName == "Owner");
                if (fColumn != null)
                {
                    selectedUserName = fColumn.ColumnValue.ToString();
                }
               
                var lusers = GetUsersInRole("TdInternalUsers");
                lusers.Insert(0, new UserItem { Name = "anyone", DisplayName = "-- anyone --" });

                return new SelectList(lusers, "Name", "DisplayName", selectedUserName);
            }
        }

       

        public SelectList AssignedToSelectList
        {
            get
            {
                string selectedUserName = "anyone";
                var fColumn = CurrentListSetting.FilterColumns.SingleOrDefault(fc => fc.ColumnName == "AssignedTo");
                if (fColumn != null)
                {
                    //when filter for column exists, but the value is null it means the selection was unassigned
                    selectedUserName = (fColumn.ColumnValue ?? "unassigned").ToString();
                        //(string.IsNullOrEmpty(fColumn.ColumnValue.ToString())) ? "unassigned" : fColumn.ColumnValue.ToString();
                }

                var lusers = GetUsersInRole("TdHelpDeskUsers");
                lusers.Insert(0, new UserItem { Name = "anyone", DisplayName = "-- anyone --" });
                lusers.Insert(1, new UserItem { Name = "unassigned", DisplayName = "-- unassigned --" });

                return new SelectList(lusers, "Name", "DisplayName", selectedUserName);
            }
        }

        public Dictionary<string, object> AssignedFilterHtmlAttributes
        {
            get
            {
                var isAssignedToFilterDisabled = CurrentListSetting.DisabledFilterColumnNames.Contains("AssignedTo");
                var cVal = String.Format("postback{0}", (isAssignedToFilterDisabled) ? " disabled" : string.Empty);
                var assignedHtmlAttributes = new Dictionary<string, object> { { "Class", cVal } };
                if (isAssignedToFilterDisabled)
                {
                    assignedHtmlAttributes.Add("Disabled", true);
                }
                return assignedHtmlAttributes;
            }
        }

        public Dictionary<string, object> StatusFilterHtmlAttributes
        {
            get
            {
                var isTicketStatusFilterDisabled = CurrentListSetting.DisabledFilterColumnNames.Contains("TicketStatus");
                var cVal = String.Format("postback{0}", (isTicketStatusFilterDisabled) ? " disabled" : string.Empty);
                var statusHtmlAttributes = new Dictionary<string, object> { { "Class", cVal } };
                if (isTicketStatusFilterDisabled)
                {
                    statusHtmlAttributes.Add("Disabled", true);
                }
                
                return statusHtmlAttributes;
            }
        }

        internal class UserItem
        {
            //TODO: add caching and reuse UserDisplayInfo from UserDisplayInfoCache
            public string Name { get; set; }
            public string DisplayName { get; set; }
        }

        private List<UserItem> GetUsersInRole(string roleName)
        {


            //TODO: Add caching for this in role manager
            return RoleManager
                .FindByName(roleName)
                .Users
                .GetUsersInRole(UserManager)
                .OrderBy(u => u.DisplayName)
                .Select(u => new UserItem { Name = u.Id, DisplayName = u.DisplayName })
                .ToList();
        }
    }
}
// TicketDesk - Attribution notice
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
using System.Data.Entity.Core.Objects;
using System.Data.Entity.Infrastructure;
using System.Data.Entity.Validation;
using System.Linq;
using System.Threading.Tasks;
using TicketDesk.Domain.Localization;
using TicketDesk.Domain.Model;
using System.Data.Entity;
using TicketDesk.Domain.Search;


namespace TicketDesk.Domain
{
    public class TicketDeskContext : DbContext
    {
        public TicketDeskContextSecurityProviderBase SecurityProvider { get; private set; }
        public TicketActionManager TicketActions { get; set; }
        public TicketDeskSearchProvider SearchProvider
        {
            get
            {
                return TicketDeskSearchProvider.GetInstance(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME")));
            }
        }
        
        /// <summary>
        /// Initializes a new instance of the <see cref="TicketDeskContext"/> class.
        /// </summary>
        /// <remarks>
        /// The securityProvider parameter can be left null; however, this should 
        /// be reserved only for back-end and automated functionality that runs
        /// outside of a user's context (e.g. migrations)
        /// </remarks>
        /// <param name="securityProvider">The security provider.</param>
        public TicketDeskContext(TicketDeskContextSecurityProviderBase securityProvider)
            : this()
        {
            SecurityProvider = securityProvider;
            TicketActions = TicketActionManager.GetInstance(SecurityProvider);
        }


        /// <summary>
        /// Initializes a new instance of the <see cref="TicketDeskContext"/> class.
        /// </summary>
        /// <remarks>
        /// Some functions related to migrations still expect to be able to construct the
        /// DbContext from a parameterless ctor. 
        /// 
        /// Initializers were fixed in EF 6.1 so they
        /// can be use the context from which they were called instead of constructing a new
        /// instance internally, but a few obscure bits were not similarly updated (e.g. 
        /// DbMigrator.GetPendingMigrations). 
        /// </remarks>
        public TicketDeskContext()
            : base("name=TicketDesk")
        {
            //TODO: still looking for a way to remove the public parameterless ctor without degrading migrations and startup ops
        }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
           
            modelBuilder.Entity<TicketEvent>()
                .Property(e => e.Version)
                .IsFixedLength();

            modelBuilder.Entity<TicketEvent>()
                .HasMany(e => e.TicketEventNotifications)
                .WithRequired(e => e.TicketEvent)
                .HasForeignKey(e => new { e.TicketId, e.CommentId })
                .WillCascadeOnDelete(false);

            modelBuilder.Entity<Ticket>()
                .Property(e => e.Version)
                .IsFixedLength();

            modelBuilder.ComplexType<UserTicketListSettingsCollection>()
                .Property(p => p.Serialized)
                .HasColumnName("ListSettingsJson");

            modelBuilder.ComplexType<ApplicationSelectListSetting>()
                .Property(p => p.Serialized)
                .HasColumnName("SelectListSettingsJson");

            modelBuilder.ComplexType<ApplicationPermissionsSetting>()
                .Property(p => p.Serialized)
                .HasColumnName("PermissionsSettingsJson");

        }

        public virtual DbSet<TicketAttachment> TicketAttachments { get; set; }
        public virtual DbSet<TicketEvent> TicketEvents { get; set; }
        public virtual DbSet<TicketEventNotification> TicketEventNotifications { get; set; }
        public virtual DbSet<Ticket> Tickets { get; set; }
        public virtual DbSet<TicketTag> TicketTags { get; set; }
        public virtual DbSet<UserSetting> UserSettings { get; set; }
        /// <summary>
        /// Use TicketDeskSettings for more convienient access to settings instead. Gets or sets the application settings.
        /// </summary>
        /// <value>The application settings.</value>
        public virtual DbSet<ApplicationSetting> ApplicationSettings { get; set; }

        /// <summary>
        /// Gets or sets the application settings specific to ticketdesk.
        /// </summary>
        /// <value>The application settings.</value>
        public ApplicationSetting TicketDeskSettings
        {
            get { return ApplicationSettings.GetTicketDeskSettings(); }
            set
            {
                var oldSettings = ApplicationSettings.GetTicketDeskSettings();
                if (oldSettings != null)
                {
                    ApplicationSettings.Remove(oldSettings);
                }
                ApplicationSettings.Add(value);
            }
        }


        public ObjectQuery<T> GetObjectQueryFor<T>(IDbSet<T> entity) where T : class
        {
            var oc = ((IObjectContextAdapter)this).ObjectContext;
            return oc.CreateObjectSet<T>();
        }

        protected override DbEntityValidationResult ValidateEntity(DbEntityEntry entityEntry, IDictionary<object, object> items)
        {
            var result = new DbEntityValidationResult(entityEntry, new List<DbValidationError>());

            //skip the custom validation if a security provider wasn't supplied
            if (SecurityProvider != null && entityEntry.Entity is Ticket && entityEntry.State == EntityState.Added)
            {
                var ticket = entityEntry.Entity as Ticket;
                if (!TicketActions.IsTicketActivityValid(ticket, TicketActivity.Create))
                {
                    result.ValidationErrors.Add(new
                        DbValidationError("authorization",
                        TicketDeskDomainText.ExceptionSecurityUserCannotCreateNewTicket));
                }
            }
            return result.ValidationErrors.Count > 0 ? result : base.ValidateEntity(entityEntry, items);
        }

        public override async Task<int> SaveChangesAsync()
        {
            var pendingTicketChanges = GetTicketChanges().ToArray();
            if (SecurityProvider != null)
            {
                PreProcessNewTickets();
                PreProcessModifiedTickets(pendingTicketChanges);
            }

            var result = await base.SaveChangesAsync();

            if (result > 0)
            {
                await PostProcessTicketChangesAsync(pendingTicketChanges);
            }
            return result;
        }

        public override int SaveChanges()
        {
            var pendingTicketChanges = GetTicketChanges().ToArray();
            if (SecurityProvider != null)
            {
                PreProcessNewTickets();
                PreProcessModifiedTickets(pendingTicketChanges);
            }

            var result = base.SaveChanges();

            if (result > 0)
            {
                PostProcessTicketChanges(pendingTicketChanges);
            }
            return result;
        }

        private async Task PostProcessTicketChangesAsync(IEnumerable<Ticket> ticketChanges)
        {
            // ReSharper disable once EmptyGeneralCatchClause
            try
            {
                //queue up for search index update
                var queueItems = ticketChanges.ToSeachQueueItems();
                await SearchProvider.QueueItemsForIndexingAsync(queueItems);

            }
            catch
            {
                //TODO: Log this somewhere
            }
        }

        private void PostProcessTicketChanges(IEnumerable<Ticket> ticketChanges)
        {
            // ReSharper disable once EmptyGeneralCatchClause
            try
            {
                //queue up for search index update
                var queueItems = ticketChanges.ToSeachQueueItems();
                AsyncHelpers.RunSync(() => SearchProvider.QueueItemsForIndexingAsync(queueItems));
            }
            catch
            {
                //TODO: Log this somewhere
            }
        }

        private void PreProcessModifiedTickets(IEnumerable<Ticket> ticketChanges)
        {
            foreach (var change in ticketChanges)
            {
                PrePopulateModifiedTicket(change);
            }
        }

        private void PreProcessNewTickets()
        {
            var ticketChanges = ChangeTracker.Entries<Ticket>().Where(t => t.State == EntityState.Added).Select(t => t.Entity);

            foreach (var change in ticketChanges)
            {
                PrePopulateNewTicket(change);
            }
        }

        private void PrePopulateModifiedTicket(Ticket modifiedTicket)
        {
            var o = ChangeTracker.Entries<Ticket>().Single(e => e.Entity.TicketId == modifiedTicket.TicketId);
            var now = DateTime.Now;

            modifiedTicket.LastUpdateBy = SecurityProvider.CurrentUserId;
            modifiedTicket.LastUpdateDate = now;

            if (o.State != EntityState.Added)//can't access orig values for new entities
            {
                var origTicket = (Ticket)o.OriginalValues.ToObject();
                if (modifiedTicket.TicketStatus != origTicket.TicketStatus)
                //if status change, force update to status by/date
                {
                    modifiedTicket.CurrentStatusDate = now;
                    modifiedTicket.CurrentStatusSetBy = SecurityProvider.CurrentUserId;
                }
            }
        }

        private void PrePopulateNewTicket(Ticket newTicket)
        {
            //TODO: Move this somewhere else?

            //TODO: double check owner if populated, make sure submitter can set this field if it isn't their id already
            //TODO: double check assigned if populated, make sure submitter can set this field.

            var now = DateTime.Now;
            newTicket.Owner = newTicket.Owner ?? SecurityProvider.CurrentUserId;
            newTicket.CreatedBy = SecurityProvider.CurrentUserId;
            newTicket.CreatedDate = now;
            newTicket.TicketStatus = TicketStatus.Active;
            newTicket.CurrentStatusDate = now;
            newTicket.CurrentStatusSetBy = SecurityProvider.CurrentUserId;

            //last update info will be set by PrePopulateModifiedTicket method, no need to set it here too
            //newTicket.LastUpdateBy = SecurityProvider.CurrentUserId;
            //newTicket.LastUpdateDate = now;

            if (newTicket.TagList != null && newTicket.TagList.Any())
            {
                newTicket.TicketTags.AddRange(newTicket.TagList.Split(',').Select(tag =>
                    new TicketTag
                    {
                        TagName = tag.Trim()
                    }));
            }

            var act = (newTicket.Owner != SecurityProvider.CurrentUserId)
                ? TicketActivity.CreateOnBehalfOf
                : TicketActivity.Create;

            newTicket.TicketEvents.AddActivityEvent(
                SecurityProvider.CurrentUserId,
                act,
                null,
                null,
                SecurityProvider.GetUserDisplayName(newTicket.Owner));


            //TODO: What with attachments?
        }

        private IEnumerable<Ticket> GetTicketChanges()
        {
            var pendingCommentChanges =
                ChangeTracker.Entries<TicketEvent>().Where(t => t.State != EntityState.Unchanged)
                    .Select(t => t.Entity.TicketId).ToArray();

            var pendingTicketChanges = ChangeTracker.Entries<Ticket>()
                .Where(t => t.State != EntityState.Unchanged || pendingCommentChanges.Contains(t.Entity.TicketId))
                .Select(t => t.Entity)
                .ToArray(); //execute now, because after save changes this query will return no results
            return pendingTicketChanges;
        }
    }
}

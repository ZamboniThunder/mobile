﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Toggl.Phoebe.Analytics;
using Toggl.Phoebe.Data;
using Toggl.Phoebe.Data.DataObjects;
using Toggl.Phoebe.Data.Models;
using Toggl.Phoebe.Logging;
using Toggl.Phoebe.Net;
using XPlatUtils;

namespace Toggl.Phoebe
{
    public class WidgetSyncManager : IDisposable
    {
        private const string Tag = "WidgetSyncManager";
        private const string DefaultDurationText = " 00:00:00 ";

        private ActiveTimeEntryManager timeEntryManager;
        private TimeEntryModel currentTimeEntry;
        private AuthManager authManager;
        private IWidgetUpdateService widgetUpdateService;
        private Subscription<SyncStartedMessage> subscriptionSyncStarted;
        private Subscription<SyncFinishedMessage> subscriptionSyncFinished;

        private bool isActing;
        private bool isLoading;
        private int rebindCounter;
        private MessageBus messageBus;

        public WidgetSyncManager ()
        {
            authManager = ServiceContainer.Resolve<AuthManager>();
            authManager.PropertyChanged += OnAuthPropertyChanged;

            widgetUpdateService = ServiceContainer.Resolve<IWidgetUpdateService>();
            timeEntryManager = ServiceContainer.Resolve<ActiveTimeEntryManager> ();
            timeEntryManager.PropertyChanged += OnTimeEntryManagerPropertyChanged;
            ResetModelToRunning ();

            messageBus = ServiceContainer.Resolve<MessageBus> ();
            subscriptionSyncStarted = messageBus.Subscribe<SyncStartedMessage> (OnSync);
            subscriptionSyncFinished = messageBus.Subscribe<SyncFinishedMessage> (OnSync);
        }

        public async void StartStopTimeEntry()
        {
            if (isActing) {
                return;
            }
            isActing = true;

            try {
                if (currentTimeEntry != null && currentTimeEntry.State == TimeEntryState.Running) {
                    await currentTimeEntry.StopAsync ();

                    // Ping analytics
                    ServiceContainer.Resolve<ITracker>().SendTimerStopEvent (TimerStopSource.Widget);
                } else if (timeEntryManager != null) {
                    currentTimeEntry = (TimeEntryModel)timeEntryManager.Draft;
                    if (currentTimeEntry == null) {
                        return;
                    }
                    await currentTimeEntry.StartAsync ();

                    // Show new screen on platform
                    widgetUpdateService.ShowNewTimeEntryScreen (currentTimeEntry);

                    // Ping analytics
                    ServiceContainer.Resolve<ITracker>().SendTimerStartEvent (TimerStartSource.WidgetNew);
                }
            } finally {
                isActing = false;
            }
        }

        public async void ContinueTimeEntry ()
        {
            TimeEntryModel entryModel;
            Guid stringGuid = widgetUpdateService.EntryIdStarted;

            // Query local data:
            var store = ServiceContainer.Resolve<IDataStore> ();
            var userId = ServiceContainer.Resolve<AuthManager> ().GetUserId ();

            var baseQuery = store.Table<TimeEntryData> ()
                            .Where (r => r.DeletedAt == null
                                    && r.UserId == userId
                                    && r.Id == stringGuid)
                            .Take (1);

            var entries = await baseQuery.ToListAsync ().ConfigureAwait (false);
            if (entries.Count > 0) {
                entryModel = (TimeEntryModel)entries.FirstOrDefault();
            } else {
                return;
            }

            if (entryModel == null) {
                return;
            }
            await entryModel.ContinueAsync ();

            // Ping analytics
            ServiceContainer.Resolve<ITracker>().SendTimerStartEvent (TimerStartSource.WidgetStart);
        }

        public void Dispose ()
        {
            var bus = ServiceContainer.Resolve<MessageBus> ();

            if (subscriptionSyncStarted != null) {
                bus.Unsubscribe (subscriptionSyncStarted);
                subscriptionSyncStarted = null;
            }

            if (subscriptionSyncFinished != null) {
                bus.Unsubscribe (subscriptionSyncFinished);
                subscriptionSyncFinished = null;
            }

            authManager.PropertyChanged -= OnAuthPropertyChanged;
            timeEntryManager.PropertyChanged -= OnTimeEntryManagerPropertyChanged;
        }

        public void SyncWidgetData()
        {
            OnSync (null);
        }

        private async void OnSync (Message msg)
        {
            if (isLoading) {
                return;
            }

            isLoading = true;

            try {
                // Dispathc started message.
                messageBus.Send<SyncWidgetMessage> (new SyncWidgetMessage (this,true));

                var queryStartDate = Time.UtcNow - TimeSpan.FromDays (9);

                // Query local data:
                var store = ServiceContainer.Resolve<IDataStore> ();
                var userId = ServiceContainer.Resolve<AuthManager> ().GetUserId ();

                var baseQuery = store.Table<TimeEntryData> ()
                                .OrderByDescending (r => r.StartTime)
                                .Where (r => r.DeletedAt == null
                                        && r.UserId == userId
                                        && r.State != TimeEntryState.New
                                        && r.StartTime >= queryStartDate)
                                .Take (4);

                var entries = await baseQuery.ToListAsync ().ConfigureAwait (false);
                var widgetEntries = new List<WidgetEntryData>();

                foreach (var entry in entries) {

                    ProjectData project;

                    if (entry.ProjectId != null) {
                        var q = store.Table<ProjectData>().Where (p => p.Id == entry.ProjectId.Value);
                        var l = await q.ToListAsync ().ConfigureAwait (false);
                        project = l.FirstOrDefault();
                    } else {
                        project = new ProjectData {
                            Name = string.Empty,
                            Color = ProjectModel.HexColors.Length - 1,
                        };
                    }

                    widgetEntries.Add (new WidgetEntryData {
                        Id = entry.Id.ToString(),
                        ProjectName = project.Name,
                        Description = entry.Description,
                        Color = ProjectModel.HexColors [ project.Color % ProjectModel.HexColors.Length],
                        IsRunning = entry.State == TimeEntryState.Running,
                        TimeValue = (entry.StopTime - entry.StartTime).ToString(),
                        Duration = (entry.StopTime.HasValue ? entry.StopTime.Value : Time.UtcNow) - entry.StartTime,
                    });

                }

                widgetUpdateService.LastEntries = widgetEntries;

            } catch (Exception exc) {
                var log = ServiceContainer.Resolve<ILogger> ();
                log.Error (Tag, exc, "Failed to fetch time entries");
            } finally {
                isLoading = false;

                // Dispathc ended message.
                messageBus.Send<SyncWidgetMessage> (new SyncWidgetMessage (this));
            }
        }

        private void OnAuthPropertyChanged (object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == AuthManager.PropertyIsAuthenticated) {
                widgetUpdateService.IsUserLogged = authManager.IsAuthenticated;
            }
        }

        private void OnTimeEntryManagerPropertyChanged (object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == ActiveTimeEntryManager.PropertyRunning) {
                ResetModelToRunning ();
                SyncWidgetData ();
            }
        }

        private void ResetModelToRunning ()
        {
            if (timeEntryManager == null) {
                return;
            }

            if (currentTimeEntry == null) {
                currentTimeEntry = (TimeEntryModel)timeEntryManager.Running;
            } else if (timeEntryManager.Running != null) {
                currentTimeEntry.Data = timeEntryManager.Running;
            } else {
                currentTimeEntry = null;
            }
        }

        public class WidgetEntryData
        {
            public string Id { get; set; }

            public string ProjectName { get; set; }

            public string Description { get; set; }

            public string TimeValue { get; set; }

            public string Color { get; set; }

            public bool IsRunning { get; set; }

            public TimeSpan Duration { get; set; }
        }
    }
}


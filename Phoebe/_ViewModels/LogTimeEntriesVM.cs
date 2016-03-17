﻿using System;
using System.Linq;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using GalaSoft.MvvmLight;
using PropertyChanged;
using Toggl.Phoebe.Analytics;
using Toggl.Phoebe._Data;
using Toggl.Phoebe._Data.Models;
using Toggl.Phoebe._Helpers;
using Toggl.Phoebe._Reactive;
using Toggl.Phoebe._ViewModels.Timer;
using XPlatUtils;

namespace Toggl.Phoebe._ViewModels
{
    [ImplementPropertyChanged]
    public class LogTimeEntriesVM : ViewModelBase, IDisposable
    {
        public class LoadInfoType
        {
            public bool IsSyncing { get; private set; }
            public bool HasMore { get; private set; }
            public bool HadErrors { get; private set; }

            public LoadInfoType (bool isSyncing, bool hasMore, bool hadErrors)
            {
                IsSyncing = isSyncing;
                HasMore = hasMore;
                HadErrors = hadErrors;
            }
        }

        private TimeEntryCollectionVM timeEntryCollection;
        private Subscription<Data.SettingChangedMessage> subscriptionSettingChanged;
        private readonly System.Timers.Timer durationTimer;
        private readonly IDisposable subscriptionState;

        public bool IsGroupedMode { get; private set; }
        public string Duration { get; private set; }
        public bool IsEntryRunning { get; private set; }
        public bool StartedByFAB { get; private set; }
        public LoadInfoType LoadInfo { get; private set; }
        public RichTimeEntry ActiveEntry { get; private set; }
        public ObservableCollection<IHolder> Collection { get { return timeEntryCollection; } }

        public LogTimeEntriesVM (TimerState timerState)
        {
            // durationTimer will update the Duration value if ActiveTimeEntry is running
            durationTimer = new System.Timers.Timer ();
            durationTimer.Elapsed += DurationTimerCallback;

            ServiceContainer.Resolve<ITracker> ().CurrentScreen = "TimeEntryList Screen";

            // RX TODO: Remove MessageBus
            var bus = ServiceContainer.Resolve<MessageBus> ();
            subscriptionSettingChanged = bus.Subscribe<Data.SettingChangedMessage> (msg => {
                if (msg.Name == "GroupedTimeEntries") {
                    ResetCollection ();
                }
            });

            ResetCollection ();
            subscriptionState =
                StoreManager.Singleton
                .Observe (app => app.TimerState)
                .StartWith (timerState)
                .Scan<TimerState, Tuple<TimerState, DownloadResult>> (
                    null, (tuple, state) => Tuple.Create (state, tuple != null ? tuple.Item2 : null))
                .Subscribe (tuple => UpdateState (tuple.Item1, tuple.Item2));
        }

        private void ResetCollection ()
        {
            DisposeCollection ();
            IsGroupedMode = ServiceContainer.Resolve<Data.ISettingsStore> ().GroupedTimeEntries;

            timeEntryCollection = new TimeEntryCollectionVM (
                IsGroupedMode ? TimeEntryGroupMethod.Single : TimeEntryGroupMethod.ByDateAndTask);
        }

        public void Dispose ()
        {
            var bus = ServiceContainer.Resolve<MessageBus> ();
            if (subscriptionSettingChanged != null) {
                bus.Unsubscribe (subscriptionSettingChanged);
                subscriptionSettingChanged = null;
            }
            if (subscriptionState != null) {
                subscriptionState.Dispose ();
            }
            durationTimer.Elapsed -= DurationTimerCallback;
            DisposeCollection ();
        }

        private void DisposeCollection ()
        {
            if (timeEntryCollection != null) {
                timeEntryCollection.Dispose ();
            }
        }

        // TODO RX: What's the difference between LoadMore and TriggerFullSync?
        //public void TriggerFullSync ()

        public void LoadMore ()
        {
            ServiceContainer.Resolve<IPlatformUtils> ().DispatchOnUIThread (() => {
                LoadInfo = new LoadInfoType (true, true, false);
                RxChain.Send (new DataMsg.TimeEntriesLoad ());
            });
        }

        public void ContinueTimeEntry (int index)
        {
            var timeEntryHolder = timeEntryCollection.Data.ElementAt (index) as ITimeEntryHolder;
            if (timeEntryHolder == null) {
                return;
            }

            if (timeEntryHolder.Entry.Data.State == TimeEntryState.Running) {
                RxChain.Send (new DataMsg.TimeEntryStop (timeEntryHolder.Entry.Data));
                ServiceContainer.Resolve<ITracker> ().SendTimerStopEvent (TimerStopSource.App);
            } else {
                RxChain.Send (new DataMsg.TimeEntryContinue (timeEntryHolder.Entry.Data));
                ServiceContainer.Resolve<ITracker> ().SendTimerStartEvent (TimerStartSource.AppContinue);
            }
        }

        public void StartStopTimeEntry (bool startedByFAB = false)
        {
            // TODO RX: Protect from requests in short time (double click...)?

            var entry = ActiveEntry.Data;
            if (entry.State == TimeEntryState.Running) {
                RxChain.Send (new DataMsg.TimeEntryStop (entry));
                ServiceContainer.Resolve<ITracker> ().SendTimerStartEvent (TimerStartSource.AppNew);
            } else {
                RxChain.Send (new DataMsg.TimeEntryContinue (entry, startedByFAB));
                ServiceContainer.Resolve<ITracker> ().SendTimerStopEvent (TimerStopSource.App);
            }
        }

        public void RemoveTimeEntry (int index)
        {
            var te = Collection.ElementAt (index) as ITimeEntryHolder;
            RxChain.Send (new DataMsg.TimeEntryDelete (te.Entry.Data));
            // TODO: Add analytic event?
        }

        private void UpdateState (TimerState timerState, DownloadResult prevDownloadResult)
        {
            ServiceContainer.Resolve<IPlatformUtils> ().DispatchOnUIThread (() => {
                // Check if DownloadResult has changed
                if (LoadInfo == null || prevDownloadResult != timerState.DownloadResult) {
                    LoadInfo = new LoadInfoType (
                        timerState.DownloadResult.IsSyncing,
                        timerState.DownloadResult.HasMore,
                        timerState.DownloadResult.HadErrors
                    );
                }

                // Check if ActiveTimeEntry has changed
                if (ActiveEntry == null || ActiveEntry.Data.Id != timerState.ActiveEntry.Id) {
                    if (timerState.ActiveEntry.Id == Guid.Empty) {
                        ActiveEntry = new RichTimeEntry (timerState, new TimeEntryData ());
                    } else {
                        StartedByFAB = timerState.ActiveEntry.StartedByFAB;
                        ActiveEntry = timerState.TimeEntries[timerState.ActiveEntry.Id];

                        // Check if an entry is running.
                        if (IsEntryRunning = ActiveEntry.Data.State == TimeEntryState.Running) {
                            durationTimer.Start ();
                        } else {
                            durationTimer.Stop ();
                            Duration = TimeSpan.FromSeconds (0).ToString ().Substring (0, 8);
                        }
                    }
                }
            });
        }

        private void DurationTimerCallback (object sender, System.Timers.ElapsedEventArgs e)
        {
            // Update on UI Thread
            ServiceContainer.Resolve<IPlatformUtils> ().DispatchOnUIThread (() => {
                var duration = ActiveEntry.Data.GetDuration ();
                durationTimer.Interval = 1000 - duration.Milliseconds;
                Duration = TimeSpan.FromSeconds (duration.TotalSeconds).ToString ().Substring (0, 8);
            });
        }
    }
}
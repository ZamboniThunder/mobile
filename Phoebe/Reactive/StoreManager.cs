﻿using System;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Toggl.Phoebe.Logging;
using Toggl.Phoebe.Data;
using XPlatUtils;

namespace Toggl.Phoebe.Reactive
{
    public class StoreManager
    {
        public static StoreManager Singleton { get; private set; }

        public static void Init (AppState initState, Reducer<AppState> reducer)
        {
            Singleton = Singleton ?? new StoreManager (initState, reducer);
        }

        private AppState appStateUnsafe;
        private readonly object appStateLock = new object ();

        public AppState AppState
        {
            get {
                lock (appStateLock) {
                    return appStateUnsafe;
                }
            }
            private set {
                lock (appStateLock) {
                    appStateUnsafe = value;
                }
            }
        }

        public static void Cleanup ()
        {
            Singleton = null;
        }

        readonly Subject<Tuple<DataMsg,SyncTestOptions>> subject1 = new Subject<Tuple<DataMsg,SyncTestOptions>> ();
        readonly Subject<DataSyncMsg<AppState>> subject2 = new Subject<DataSyncMsg<AppState>> ();

        StoreManager (AppState initState, Reducer<AppState> reducer)
        {
            AppState = initState;
            var initSyncMsg = DataSyncMsg.Create (initState);

            subject1
            .Synchronize (Scheduler.Default)
            .Scan (initSyncMsg, (acc, tuple) => {
                DataSyncMsg<AppState> msg;
                try {
                    msg = reducer.Reduce (acc.State, tuple.Item1);
                } catch (Exception ex) {
                    var log = ServiceContainer.Resolve<ILogger> ();
                    log.Error (GetType ().Name, ex, "Failed to handle DataMsg: {0}", tuple.Item1.GetType ().Name);
                    msg = DataSyncMsg.Create (acc.State);
                }
                AppState = msg.State;
                return tuple.Item2 == null ? msg : msg.With (tuple.Item2);
            })
            .Subscribe (subject2.OnNext);
        }

        public void Send (DataMsg msg, SyncTestOptions syncTest)
        {
            subject1.OnNext (Tuple.Create (msg, syncTest));
        }

        public IObservable<DataSyncMsg<AppState>> Observe ()
        {
            return subject2;
        }

        public IObservable<T> Observe<T> (Func<DataSyncMsg<AppState>, T> selector)
        {
            return subject2.Select (syncMsg => selector (syncMsg));
        }
    }
}
﻿using System;
using Foundation;
using NotificationCenter;
using Toggl.Phoebe;
using UIKit;
using XPlatUtils;

namespace Toggl.Ross.Data
{
    public class WidgetUpdateService //: IWidgetUpdateService
    {
        public static string MillisecondsKey = "milliseconds_key";
        public static string TimeEntriesKey = "time_entries_key";
        public static string StartedEntryKey = "started_entry_key";
        public static string AppActiveEntryKey = "app_active_entry_key";
        public static string AppBackgroundEntryKey = "app_bg_entry_key";
        public static string IsUserLoggedKey = "is_logged_key";

        public static string TodayUrlPrefix = "today";
        public static string StartEntryUrlPrefix = "start";
        public static string ContinueEntryUrlPrefix = "continue";

        private NSUserDefaults nsUserDefaults;
        private UIViewController rootController;

        public NSUserDefaults UserDefaults
        {
            get
            {
                if (nsUserDefaults == null)
                {
                    nsUserDefaults = new NSUserDefaults("group." + NSBundle.MainBundle.BundleIdentifier, NSUserDefaultsType.SuiteName);
                }
                return nsUserDefaults;
            }
        }

        public WidgetUpdateService()
        {
            // Update auth state from platform service.
            //
            // Phoebe services are initializated first,
            // if we try to update auth state from Phoebe
            // WidgetUpdateService still doesn't exists.

            //var authManager = ServiceContainer.Resolve<AuthManager>();

            //IsUserLogged = authManager.IsAuthenticated;
            SetAppActivated(true);
            SetAppOnBackground(false);
            rootController = UIApplication.SharedApplication.KeyWindow.RootViewController;
        }

        #region IWidgetUpdateService implementation

        /*
        List<WidgetSyncManager.WidgetEntryData> lastEntries;

        public List<WidgetSyncManager.WidgetEntryData> LastEntries
        {
            get {
                return lastEntries;
            } set {
                if (value != null) {
                    lastEntries = value;
                    var json = JsonConvert.SerializeObject (lastEntries);
                    UserDefaults.SetString (json, TimeEntriesKey);
                    UpdateWidgetContent();
                }
            }
        }
        */
        private string duration;

        public string RunningEntryDuration
        {
            get
            {
                return duration;
            }
            set
            {
                duration = value;
                UserDefaults.SetString(duration, MillisecondsKey);
            }
        }

        private bool isLogged;

        public bool IsUserLogged
        {
            get
            {
                return isLogged;
            }
            set
            {
                isLogged = value;
                UserDefaults.SetBool(isLogged, IsUserLoggedKey);
                UpdateWidgetContent();
            }
        }

        private Guid entryIdStarted;

        public Guid EntryIdStarted
        {
            get
            {
                Guid entryId;
                Guid.TryParse(UserDefaults.StringForKey(StartedEntryKey), out entryId);
                return entryId;
            }
            set
            {
                entryIdStarted = value;
            }
        }

        #endregion

        public void SetAppActivated(bool isActivated)
        {
            UserDefaults.SetBool(isActivated, AppActiveEntryKey);
            UpdateWidgetContent();
        }

        public void SetAppOnBackground(bool isBackground)
        {
            UserDefaults.SetBool(isBackground, AppBackgroundEntryKey);
            UpdateWidgetContent();
        }

        /*
        public async void ShowNewTimeEntryScreen (TimeEntryModel currentTimeEntry)
        {
            var topVCList = new List<UIViewController> (rootController.ChildViewControllers);
            if (topVCList.Count > 0) {
                // Get current VC's navigation
                var controllers = new List<UIViewController> (topVCList[0].NavigationController.ViewControllers);
                var tags = await ServiceContainer.Resolve<IDataStore> ().GetTimeEntryTags (currentTimeEntry.Data.Id);
                var editController = new EditTimeEntryViewController (currentTimeEntry.Data, tags);
                controllers.Add (editController);
                if (ServiceContainer.Resolve<SettingsStore> ().ChooseProjectForNew) {
                    controllers.Add (new ProjectSelectionViewController (currentTimeEntry.Workspace.Id, editController));
                }
                topVCList[0].NavigationController.SetViewControllers (controllers.ToArray (), true);
            }
        }
        */

        public void UpdateWidgetContent()
        {
            if (ServiceContainer.Resolve<IPlatformUtils> ().IsWidgetAvailable)
            {
                var controller = NCWidgetController.GetWidgetController();
                controller.SetHasContent(true, NSBundle.MainBundle.BundleIdentifier + ".today");
            }
        }
    }
}
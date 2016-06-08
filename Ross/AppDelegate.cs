using System;
using Foundation;
using GalaSoft.MvvmLight.Views;
using Google.Core;
using Google.GoogleCloudMessaging;
using Google.SignIn;
using SQLite.Net.Interop;
using SQLite.Net.Platform.XamarinIOS;
using TestFairyLib;
using Toggl.Phoebe;
using Toggl.Phoebe.Analytics;
using Toggl.Phoebe.Data;
using Toggl.Phoebe.Logging;
using Toggl.Phoebe.Misc;
using Toggl.Phoebe.Net;
using Toggl.Phoebe.Reactive;
using Toggl.Ross.Analytics;
using Toggl.Ross.Net;
using Toggl.Ross.ViewControllers;
using Toggl.Ross.Views;
using Toggl.Ross.Widget;
using UIKit;
using Xamarin;
using XPlatUtils;

namespace Toggl.Ross
{
    // The UIApplicationDelegate for the application. This class is responsible for launching the
    // User Interface of the application, as well as listening (and optionally responding) to
    // application events from iOS.
    [Register("AppDelegate")]
    public class AppDelegate : UIApplicationDelegate, IPlatformUtils, IReceiverDelegate
    {
        private TogglWindow window;
        private int systemVersion;
        private const int minVersionWidget = 7;

        public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
        {
            var versionString = UIDevice.CurrentDevice.SystemVersion;
            systemVersion = Convert.ToInt32(versionString.Split(new [] {"."}, StringSplitOptions.None)[0]);

            // Attach bug tracker
#if (!DEBUG)
            TestFairy.Begin(Build.TestFairyApiToken);
#endif

            // Component initialisation.
            // Register platform info first.
            ServiceContainer.Register<IPlatformUtils> (this);

            // Used for migration
            ServiceContainer.Register<IOldSettingsStore>(() => new OldSettingsStore());

            // Register Phoebe services
            Services.Register();

            // Override default implementation
            ServiceContainer.Register<ITimeProvider> (() => new NSTimeProvider());

            // Register Ross components:
            ServiceContainer.Register<ILogger> (() => new BaseLogger());
            ServiceContainer.Register<ExperimentManager> (() => new ExperimentManager(
                typeof(Phoebe.Analytics.Experiments),
                typeof(Analytics.Experiments)));
            ServiceContainer.Register<ILoggerClient> (initialiseLogClient);
            ServiceContainer.Register<ITracker> (() => new Tracker());
            ServiceContainer.Register<INetworkPresence> (() => new NetworkPresence());
            ServiceContainer.Register<NetworkIndicatorManager> ();
            ServiceContainer.Register<TagChipCache> ();
            ServiceContainer.Register<IDialogService> (() => new DialogService());
            ConfigureRemoteNotifications();
            Theme.Style.Initialize();

            // Make sure critical services are running are running:
            ServiceContainer.Resolve<UpgradeManger> ().TryUpgrade();
            ServiceContainer.Resolve<ILoggerClient> ();
            ServiceContainer.Resolve<ITracker> ();

            // This needs some services, like ITimeProvider, so run it at the end
            RxChain.Init(AppState.Init());

            // Order matters
            if (systemVersion > minVersionWidget)
            {
                ServiceContainer.Register(() => new WidgetService());
            }

            // Setup Google sign in
            SetupGoogleServices();

            // Start app
            window = new TogglWindow(UIScreen.MainScreen.Bounds);
            window.RootViewController = new MainViewController();
            window.MakeKeyAndVisible();

            return true;
        }

        #region GCM Configuration (see Google Cloud Messaging for iOS component Readme)
        public void ConfigureRemoteNotifications()
        {
            // Configure and Start GCM
            var gcmConfig = Config.DefaultConfig;
            gcmConfig.ReceiverDelegate = this;
            Service.SharedInstance.Start(gcmConfig);

            // Register for remote notifications
            var notTypes = UIUserNotificationType.Sound | UIUserNotificationType.Alert | UIUserNotificationType.Badge;
            var settings = UIUserNotificationSettings.GetSettingsForTypes(notTypes, null);
            UIApplication.SharedApplication.RegisterUserNotificationSettings(settings);
            UIApplication.SharedApplication.RegisterForRemoteNotifications();
        }

        public override void RegisteredForRemoteNotifications(UIApplication application, NSData deviceToken)
        {
            // Configure and start Instance ID
            var config = Google.InstanceID.Config.DefaultConfig;
            Google.InstanceID.InstanceId.SharedInstance.Start(config);

            RxChain.Send(new DataMsg.RegisterPush(deviceToken.ToString()));
        }

        public override void DidReceiveRemoteNotification(UIApplication application, NSDictionary userInfo, Action<UIBackgroundFetchResult> completionHandler)
        {
            // Your own notification handling logic here

            // Notify GCM we received the message
            Service.SharedInstance.AppDidReceiveMessage(userInfo);
        }

        // TODO: Disconnect in background?
        //public override void OnActivated(UIApplication application)
        //{
        //    Service.SharedInstance.Connect(error =>
        //    {
        //        if (error != null)
        //            Console.WriteLine("Could not connect to GCM: {0}", error.LocalizedDescription);
        //        else
        //            Console.WriteLine("Connected to GCM");
        //    });
        //}

        //public override void DidEnterBackground(UIApplication application)
        //{
        //    Service.SharedInstance.Disconnect();
        //}
        #endregion

        #region Widget management

        public override void OnActivated(UIApplication application)
        {
            // Make sure the user data is refreshed when the application becomes active
            // TODO Rx Removed full sync from here.
            // RxChain.Send (new DataMsg.FullSync ());
            ServiceContainer.Resolve<NetworkIndicatorManager> ();

            if (systemVersion > minVersionWidget)
            {
                ServiceContainer.Resolve<WidgetService>().SetAppOnBackground(false);
            }
        }

        public override bool OpenUrl(UIApplication application, NSUrl url, string sourceApplication, NSObject annotation)
        {
            if (systemVersion > minVersionWidget)
            {
                if (url.AbsoluteString.Contains(WidgetService.TodayUrlPrefix))
                {
                    if (url.AbsoluteString.Contains(WidgetService.StartEntryUrlPrefix))
                    {
                        RxChain.Send(new DataMsg.TimeEntryStart());
                    }
                    else
                    {
                        try
                        {
                            var nsUserDefaults = new NSUserDefaults("group.com.toggl.timer", NSUserDefaultsType.SuiteName);
                            var guidStr = nsUserDefaults.StringForKey(WidgetService.StartedEntryKey);
                            var guid = Guid.Parse(guidStr);
                            var te = StoreManager.Singleton.AppState.TimeEntries [guid];
                            RxChain.Send(new DataMsg.TimeEntryContinue(te.Data));
                        }
                        catch (Exception ex)
                        {
                            var logger = ServiceContainer.Resolve<ILogger> ();
                            logger.Error("iOS Widget", ex, "Error starting unexisting time entry.");
                        }
                    }
                    return true;
                }
            }
            return SignIn.SharedInstance.HandleUrl(url, sourceApplication, annotation);
        }

        public override void DidEnterBackground(UIApplication application)
        {
            if (systemVersion > minVersionWidget)
            {
                ServiceContainer.Resolve<WidgetService>().SetAppOnBackground(true);
            }
        }

        public override void WillTerminate(UIApplication application)
        {
            if (systemVersion > minVersionWidget)
            {
                ServiceContainer.Resolve<WidgetService>().SetAppActivated(false);
            }
        }

        #endregion

        private static ILoggerClient initialiseLogClient()
        {
#if DEBUG
            Insights.Initialize(Insights.DebugModeKey);
#else
            Insights.Initialize(Build.XamarinInsightsApiKey);
#endif
            return new LogClient();
        }

        private void SetupGoogleServices()
        {
            NSError configureError;
            Context.SharedInstance.Configure(out configureError);
            if (configureError != null)
            {
                // Set up Google Analytics
                // the tracker ID isn't detected automatically from GoogleService-info.plist
                // so, it's passed manually. Waiting for new versions of the library.
                var gaiInstance = Google.Analytics.Gai.SharedInstance;
                gaiInstance.DefaultTracker = gaiInstance.GetTracker(Build.GoogleAnalyticsId);

                var log = ServiceContainer.Resolve<ILogger> ();
                SignIn.SharedInstance.ClientID = Build.GoogleClientId;
                log.Info("AppDelegate", string.Format("Error configuring the Google context: {0}", configureError));
            }
        }

        public static TogglWindow TogglWindow
        {
            get
            {
                return ((AppDelegate)UIApplication.SharedApplication.Delegate).window;
            }
        }

        #region IPlatformUtil implementation

        string IPlatformUtils.AppIdentifier
        {
            get { return Build.AppIdentifier; }
        }

        private string appVersion;

        public string AppVersion
        {
            get
            {
                if (appVersion == null)
                {
                    appVersion = NSBundle.MainBundle.InfoDictionary.ObjectForKey(
                                     new NSString("CFBundleShortVersionString")).ToString();
                }
                return appVersion;
            }
        }

        public bool IsWidgetAvailable
        {
            get
            {
                // iOS 8 is the version where Today Widgets are availables.
                return systemVersion > minVersionWidget;
            }
        }

        public ISQLitePlatform SQLiteInfo
        {
            get
            {
                return new SQLitePlatformIOS();
            }
        }

        public void DispatchOnUIThread(Action action)
        {
        }

        #endregion
    }
}

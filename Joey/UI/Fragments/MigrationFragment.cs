﻿using System;
using System.Threading.Tasks;
using Android.OS;
using Android.Views;
using Android.Widget;
using Toggl.Joey.UI.Activities;
using Toggl.Phoebe;
using Toggl.Phoebe.Data;
using Toggl.Phoebe.Reactive;
using XPlatUtils;
using Fragment = Android.Support.V4.App.Fragment;

namespace Toggl.Joey.UI.Fragments
{
    public class MigrationFragment : Fragment
    {
#if DEBUG // TODO: DELETE TEST CODE --------
        private static void setupV0database(IPlatformUtils xplat)
        {
            var path = DatabaseHelper.GetDatabasePath(DatabaseHelper.GetDatabaseDirectory(), 0);
            if (System.IO.File.Exists(path)) { System.IO.File.Delete(path); }

            using(var db = new SQLite.Net.SQLiteConnection(xplat.SQLiteInfo, path))
            {
                db.CreateTable<Phoebe.Data.Models.Old.DB_VERSION_0.ClientData>();
                db.CreateTable<Phoebe.Data.Models.Old.DB_VERSION_0.ProjectData>();
                db.CreateTable<Phoebe.Data.Models.Old.DB_VERSION_0.ProjectUserData>();
                db.CreateTable<Phoebe.Data.Models.Old.DB_VERSION_0.TagData>();
                db.CreateTable<Phoebe.Data.Models.Old.DB_VERSION_0.TaskData>();
                db.CreateTable<Phoebe.Data.Models.Old.DB_VERSION_0.TimeEntryData>();
                db.CreateTable<Phoebe.Data.Models.Old.DB_VERSION_0.TimeEntryTagData>();
                db.CreateTable<Phoebe.Data.Models.Old.DB_VERSION_0.UserData>();
                db.CreateTable<Phoebe.Data.Models.Old.DB_VERSION_0.WorkspaceData>();
                db.CreateTable<Phoebe.Data.Models.Old.DB_VERSION_0.WorkspaceUserData>();
            }
        }

        private static void insertIntoV0Database(IPlatformUtils xplat, params object[] objects)
        {
            var dbPath = DatabaseHelper.GetDatabasePath(DatabaseHelper.GetDatabaseDirectory(), 0);
            using(var db = new SQLite.Net.SQLiteConnection(xplat.SQLiteInfo, dbPath))
            {
                db.InsertAll(objects);
            }
        }

        public static void CreateOldDbForTesting()
        {
            var workspaceData = new Phoebe.Data.Models.Old.DB_VERSION_0.WorkspaceData
            {
                Id = Guid.NewGuid(),
                Name = "the matrix",
                BillableRatesVisibility = Phoebe.Data.Models.AccessLevel.Admin,
                DefaultCurrency = "currency",
                DefaultRate = null,
                IsPremium = true,
                LogoUrl = "http://toggl.com",
                ProjectCreationPrivileges = Phoebe.Data.Models.AccessLevel.Regular,
                RoundingMode = RoundingMode.Down,
                RoundingPercision = 1
            };

            var xplat = ServiceContainer.Resolve<IPlatformUtils>();
            setupV0database(xplat);
            insertIntoV0Database(xplat, workspaceData);
        }
#endif

        private int oldVersion;
        private int newVersion;
        private bool userTriedAgain;

        private TextView topLabel;
        private TextView descLabel;
        private TextView discardLabel;
        private TextView discardDesc;
        private TextView percente;
        private ProgressBar progressBar;
        private Button tryAgainBtn;
        private Button discardBtn;
        private ImageView toggler1;
        private ImageView toggler2;

        Action<bool> completionListener;

        public MigrationFragment()
        {
        }

        public MigrationFragment(IntPtr jref, Android.Runtime.JniHandleOwnership xfer) : base(jref, xfer)
        {
        }

        public static MigrationFragment NewInstance(int oldVersion, Action<bool> completionListener)
        {
            // TODO Block press back button from
            // this screen until migration is completed??

            var fragment = new MigrationFragment();
            fragment.oldVersion = oldVersion;
            fragment.completionListener = completionListener;
            return fragment;
        }

        public override View OnCreateView(LayoutInflater inflater, ViewGroup container, Bundle savedInstanceState)
        {
            var view = inflater.Inflate(Resource.Layout.MigrationFragment, container, false);
            var platformUtils = ServiceContainer.Resolve<IPlatformUtils>();

            Task.Run(() =>
            {
                var success = DatabaseHelper.Migrate(
                                  platformUtils.SQLiteInfo, DatabaseHelper.GetDatabaseDirectory(),
                                  oldVersion, SyncSqliteDataStore.DB_VERSION, setProgress);

#if DEBUG // TODO: DELETE TEST CODE --------
                System.Threading.Thread.Sleep(10000);
#endif

                platformUtils.DispatchOnUIThread(() => completionListener(success));

            }).ConfigureAwait(false);

            return view;
        }

        private void MigrateDatabase()
        {
            Task.Run(() =>
            {
                var migrationResult = DatabaseHelper.Migrate(
                                          ServiceContainer.Resolve<IPlatformUtils>().SQLiteInfo,
                                          DatabaseHelper.GetDatabaseDirectory(),
                                          oldVersion, newVersion,
                                          setProgress
                                      );
                if (migrationResult)
                {
                    BaseActivity.CurrentActivity.RunOnUiThread(() => DisplaySuccessState());
                    RxChain.Send(new DataMsg.InitStateAfterMigration());
                }
                else
                {
                    BaseActivity.CurrentActivity.RunOnUiThread(() =>
                    {
                        if (!userTriedAgain)
                            DisplayErrorState();
                        else
                            DisplayDiscardState();
                    });
                }
            });

            DisplayInitialState();
        }


        private void DisplayInitialState()
        {
            topLabel.Text = Resources.GetString(Resource.String.MigratingUpdateTitle);
            descLabel.Text = Resources.GetString(Resource.String.MigratingUpdateDesc);
            progressBar.Visibility = ViewStates.Visible;

            toggler2.Visibility = ViewStates.Gone;
            tryAgainBtn.Visibility = ViewStates.Gone;
            discardBtn.Visibility = ViewStates.Gone;
            discardDesc.Visibility = ViewStates.Gone;
        }

        private void DisplaySuccessState()
        {
            topLabel.Text = Resources.GetString(Resource.String.MigratingSuccessTitle);;
            descLabel.Text = Resources.GetString(Resource.String.MigratingSuccessDesc);
            toggler2.Visibility = ViewStates.Visible;

            toggler1.Visibility = ViewStates.Gone;
            progressBar.Visibility = ViewStates.Gone;
            tryAgainBtn.Visibility = ViewStates.Gone;
            percente.Visibility = ViewStates.Gone;
        }

        private void DisplayErrorState()
        {
            topLabel.Text = Resources.GetString(Resource.String.MigratingTryTitle);
            descLabel.Text = Resources.GetString(Resource.String.MigratingTryDesc);
            tryAgainBtn.Visibility = ViewStates.Visible;

            toggler1.Visibility = ViewStates.Gone;
            toggler2.Visibility = ViewStates.Gone;
            percente.Visibility = ViewStates.Gone;
            progressBar.Visibility = ViewStates.Gone;
            discardBtn.Visibility = ViewStates.Gone;
            discardDesc.Visibility = ViewStates.Gone;
        }

        private void DisplayDiscardState()
        {
            topLabel.Text = Resources.GetString(Resource.String.MigratingFeedbackTitle);
            descLabel.Text = Resources.GetString(Resource.String.MigratingFeedbackDesc);
            discardLabel.Visibility = ViewStates.Visible;
            discardDesc.Visibility = ViewStates.Visible;
            discardBtn.Visibility = ViewStates.Visible;

            toggler1.Visibility = ViewStates.Gone;
            toggler2.Visibility = ViewStates.Gone;
            percente.Visibility = ViewStates.Gone;
            progressBar.Visibility = ViewStates.Gone;
            tryAgainBtn.Visibility = ViewStates.Gone;
        }

        private void setProgress(float percentage)
        {
            var per = Math.Truncate(percentage * 100);
            progressBar.Progress = Convert.ToInt16(per);
            percente.Text = per + " %";
        }
    }
}

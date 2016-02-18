﻿using System.Collections.Generic;
using System.Threading;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using Toggl.Joey.UI.Fragments;

namespace Toggl.Joey.UI.Activities
{
    [Activity (
         Exported = false,
         WindowSoftInputMode = SoftInput.StateHidden,
         ScreenOrientation = ScreenOrientation.Portrait,
         Theme = "@style/Theme.Toggl.App")]
    public class EditTimeEntryActivity : BaseActivity
    {
        private static readonly string groupfragmentTag = "editgroup_fragment";
        private static readonly string fragmentTag = "edit_fragment";

        public static readonly string IsGrouped = "com.toggl.timer.grouped_edit";
        public static readonly string OpenProjects = "com.toggl.timer.open_projects";
        public static readonly string ExtraTimeEntryId = "com.toggl.timer.time_entry_id";
        public static readonly string ExtraGroupedTimeEntriesGuids = "com.toggl.timer.grouped_time_entry_id";

        // Explanation of native constructor
        // http://stackoverflow.com/questions/10593022/monodroid-error-when-calling-constructor-of-custom-view-twodscrollview/10603714#10603714
        public EditTimeEntryActivity (System.IntPtr a, Android.Runtime.JniHandleOwnership b) : base (a, b)
        {
        }

        public EditTimeEntryActivity ()
        {
        }

        protected override void OnCreateActivity (Bundle state)
        {
            base.OnCreateActivity (state);

            SetContentView (Resource.Layout.EditTimeEntryActivity);

            var isGrouped = Intent.Extras.GetBoolean (IsGrouped, false);
            var fragment = FragmentManager.FindFragmentByTag (fragmentTag);
            var groupedFragment = FragmentManager.FindFragmentByTag (groupfragmentTag);

            var guids = Intent.GetStringArrayListExtra (ExtraGroupedTimeEntriesGuids);
            if (guids == null) {
                Finish ();
            }

            if (isGrouped) {
                if (groupedFragment == null) {
                    groupedFragment = EditGroupedTimeEntryFragment.NewInstance (guids);
                    FragmentManager.BeginTransaction ()
                    .Add (Resource.Id.FrameLayout, groupedFragment, groupfragmentTag)
                    .Commit ();
                } else {
                    FragmentManager.BeginTransaction ()
                    .Attach (groupedFragment)
                    .Commit ();
                }
            } else {
                if (fragment == null) {
                    fragment = EditTimeEntryFragment.NewInstance (guids[0]);
                    FragmentManager.BeginTransaction ()
                    .Add (Resource.Id.FrameLayout, fragment, fragmentTag)
                    .Commit ();
                } else {
                    FragmentManager.BeginTransaction ()
                    .Attach (fragment)
                    .Commit ();
                }
            }
        }

        public void ShowSoftKeyboard (View input, bool selectText)
        {
            if (selectText) { ((EditText)input).SelectAll(); }
            ThreadPool.QueueUserWorkItem (s => {
                Thread.Sleep (100); // For some reason, a short delay is required here.
                RunOnUiThread (() => ((InputMethodManager)GetSystemService (InputMethodService)).ShowSoftInput (input, ShowFlags.Implicit));
            });
        }
    }
}

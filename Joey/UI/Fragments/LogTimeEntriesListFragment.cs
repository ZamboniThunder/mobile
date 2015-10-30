﻿using System;
using System.Collections.Generic;
using Android.Content;
using Android.OS;
using Android.Support.Design.Widget;
using Android.Support.V4.App;
using Android.Support.V7.Widget;
using Android.Support.V7.Widget.Helper;
using Android.Views;
using Android.Widget;
using Praeclarum.Bind;
using Toggl.Joey.UI.Activities;
using Toggl.Joey.UI.Adapters;
using Toggl.Joey.UI.Utils;
using Toggl.Joey.UI.Views;
using Toggl.Phoebe.Data.Utils;
using Toggl.Phoebe.Data.ViewModels;
using Toggl.Phoebe.Data.Views;

namespace Toggl.Joey.UI.Fragments
{
    public class LogTimeEntriesListFragment : Fragment, SwipeDismissCallback.IDismissListener, ItemTouchListener.IItemTouchListener
    {
        private RecyclerView recyclerView;
        private View emptyMessageView;
        private LogTimeEntriesAdapter logAdapter;
        private StartStopFab startStopBtn;
        private CoordinatorLayout coordinatorLayout;

        private TimeEntriesCollectionView collectionView;
        private LogTimeEntriesViewModel viewModel;
        private Binding binding;

        // Recycler setup
        private DividerItemDecoration dividerDecoration;
        private ShadowItemDecoration shadowDecoration;
        private ItemTouchListener itemTouchListener;

        #region Binded properties

        private TimeEntriesCollectionView CollectionView
        {
            get {
                return collectionView;
            } set {
                collectionView = value;
                logAdapter = new LogTimeEntriesAdapter (recyclerView, collectionView);
                recyclerView.SetAdapter (logAdapter);
            }
        }

        private bool IsRunning
        {
            get {
                return false;
            } set {
                startStopBtn.ButtonAction = value ? FABButtonState.Stop : FABButtonState.Start;
            }
        }

        private int ItemCount
        {
            get {
                return 0;
            } set {
                // if ItemCount > 0 and CollectionView.HasMore
                // show the recycler!
                ShowOnboardingInfo (value > 0 || viewModel.CollectionView.HasMore);
            }
        }

        private bool HasMore
        {
            get {
                return false;
            } set {
                ShowOnboardingInfo (viewModel.ItemCount > 0 || value);
            }
        }

        #endregion

        public override View OnCreateView (LayoutInflater inflater, ViewGroup container, Bundle savedInstanceState)
        {
            var view = inflater.Inflate (Resource.Layout.LogTimeEntriesListFragment, container, false);
            view.FindViewById<TextView> (Resource.Id.EmptyTitleTextView).SetFont (Font.Roboto);
            view.FindViewById<TextView> (Resource.Id.EmptyTextTextView).SetFont (Font.RobotoLight);

            emptyMessageView = view.FindViewById<View> (Resource.Id.EmptyMessageView);
            emptyMessageView.Visibility = ViewStates.Gone;
            recyclerView = view.FindViewById<RecyclerView> (Resource.Id.LogRecyclerView);
            recyclerView.SetLayoutManager (new LinearLayoutManager (Activity));
            coordinatorLayout = view.FindViewById<CoordinatorLayout> (Resource.Id.logCoordinatorLayout);
            startStopBtn = view.FindViewById<StartStopFab> (Resource.Id.StartStopBtn);

            SetupRecyclerView ();
            return view;
        }

        public async override void OnViewCreated (View view, Bundle savedInstanceState)
        {
            base.OnViewCreated (view, savedInstanceState);
            viewModel = new LogTimeEntriesViewModel ();
            await viewModel.Init ();

            binding = Binding.Create (() =>
                                      HasMore == viewModel.CollectionView.HasMore &&
                                      ItemCount == viewModel.ItemCount &&
                                      CollectionView == viewModel.CollectionView &&
                                      IsRunning == viewModel.IsTimeEntryRunning);

            startStopBtn.Click += async (sender, e) => await viewModel.StartStopTimeEntry ();
        }

        public override void OnDestroyView ()
        {
            // Protect against Java side being GCed
            if (Handle == IntPtr.Zero) {
                return;
            }

            binding.Unbind ();
            viewModel.Dispose ();
            ReleaseRecyclerView ();

            base.OnDestroyView ();
        }

        private void SetupRecyclerView ()
        {
            // Touch listeners.
            itemTouchListener = new ItemTouchListener (recyclerView, this);
            recyclerView.AddOnItemTouchListener (itemTouchListener);

            var touchCallback = new SwipeDismissCallback (ItemTouchHelper.Up | ItemTouchHelper.Down, ItemTouchHelper.Left, this);
            var touchHelper = new ItemTouchHelper (touchCallback);
            touchHelper.AttachToRecyclerView (recyclerView);

            // Decorations.
            dividerDecoration = new DividerItemDecoration (Activity, DividerItemDecoration.VerticalList);
            shadowDecoration = new ShadowItemDecoration (Activity);
            recyclerView.AddItemDecoration (dividerDecoration);
            recyclerView.AddItemDecoration (shadowDecoration);

            recyclerView.GetItemAnimator ().SupportsChangeAnimations = false;
        }

        private void ReleaseRecyclerView ()
        {
            recyclerView.RemoveItemDecoration (shadowDecoration);
            recyclerView.RemoveItemDecoration (dividerDecoration);
            recyclerView.RemoveOnItemTouchListener (itemTouchListener);

            recyclerView.GetAdapter ().Dispose ();
            recyclerView.Dispose ();
            logAdapter = null;

            itemTouchListener.Dispose ();
            dividerDecoration.Dispose ();
            shadowDecoration.Dispose ();
        }

        private void ShowOnboardingInfo (bool showIt)
        {
            // TODO: animate with alpha transitions
            recyclerView.Visibility = showIt ? ViewStates.Visible : ViewStates.Gone;
            emptyMessageView.Visibility = showIt ? ViewStates.Gone : ViewStates.Visible;
        }

        #region IDismissListener implementation

        public bool CanDismiss (RecyclerView recyclerView, RecyclerView.ViewHolder viewHolder)
        {
            var adapter = recyclerView.GetAdapter ();
            return adapter.GetItemViewType (viewHolder.LayoutPosition) == LogTimeEntriesAdapter.ViewTypeContent;
        }

        public async void OnDismiss (RecyclerView.ViewHolder viewHolder)
        {
            var duration = TimeEntriesCollectionView.UndoSecondsInterval * 1000;

            await collectionView.RemoveItemWithUndoAsync (viewHolder.AdapterPosition);
            var snackBar = Snackbar
                           .Make (coordinatorLayout, Resources.GetString (Resource.String.UndoBarDeletedText), duration)
                           .SetAction (Resources.GetString (Resource.String.UndoBarButtonText),
                                       async v => await collectionView.RestoreItemFromUndoAsync ());
            ChangeSnackBarColor (snackBar);
            snackBar.Show ();
        }

        #endregion

        #region IRecyclerViewOnItemClickListener implementation

        public void OnItemClick (RecyclerView parent, View clickedView, int position)
        {
            var intent = new Intent (Activity, typeof (EditTimeEntryActivity));

            IList<string> guids = ((TimeEntryHolder)logAdapter.GetEntry (position)).TimeEntryGuids;
            intent.PutStringArrayListExtra (EditTimeEntryActivity.ExtraGroupedTimeEntriesGuids, guids);
            intent.PutExtra (EditTimeEntryActivity.IsGrouped, guids.Count > 1);

            StartActivity (intent);
        }

        public void OnItemLongClick (RecyclerView parent, View clickedView, int position)
        {
            OnItemClick (parent, clickedView, position);
        }

        public bool CanClick (RecyclerView view, int position)
        {
            var adapter = recyclerView.GetAdapter ();
            return adapter.GetItemViewType (position) == LogTimeEntriesAdapter.ViewTypeContent;
        }

        #endregion

        // Temporal hack to change the
        // action color in snack bar
        private void ChangeSnackBarColor (Snackbar snack)
        {
            var group = (ViewGroup) snack.View;
            for (int i = 0; i < group.ChildCount; i++) {
                View v = group.GetChildAt (i);
                var textView = v as TextView;
                if (textView != null) {
                    TextView t = textView;
                    if (t.Text == Resources.GetString (Resource.String.UndoBarButtonText)) {
                        t.SetTextColor (Resources.GetColor (Resource.Color.material_green));
                    }

                }
            }
        }
    }
}
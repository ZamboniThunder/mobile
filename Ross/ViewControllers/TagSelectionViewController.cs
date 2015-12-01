using System;
using System.Collections.Generic;
using CoreGraphics;
using System.Linq;
using System.Threading.Tasks;
using Foundation;
using UIKit;
using Toggl.Phoebe.Analytics;
using Toggl.Phoebe.Data;
using Toggl.Phoebe.Data.DataObjects;
using Toggl.Phoebe.Data.Models;
using Toggl.Phoebe.Data.Views;
using XPlatUtils;
using Toggl.Ross.DataSources;
using Toggl.Ross.Theme;
using Toggl.Ross.Views;

namespace Toggl.Ross.ViewControllers
{
    public class TagSelectionViewController : UITableViewController
    {
        private const float CellSpacing = 4f;
        private readonly TimeEntryModel model;
        private List<TimeEntryTagData> modelTags;
        private Source source;
        private bool isSaving;

        public TagSelectionViewController (TimeEntryModel model) : base (UITableViewStyle.Plain)
        {
            this.model = model;

            Title = "TagTitle".Tr ();

            LoadTags ();
        }

        private async void LoadTags ()
        {
            var dataStore = ServiceContainer.Resolve<IDataStore> ();
            modelTags = await dataStore.Table<TimeEntryTagData> ()
                        .Where (r => r.TimeEntryId == model.Id && r.DeletedAt == null)
                        .ToListAsync ();
            SetupDataSource ();
        }

        private void SetupDataSource ()
        {
            var modelTagsReady = modelTags != null;

            if (source != null || !modelTagsReady || !IsViewLoaded) {
                return;
            }

            // Attach source
            source = new Source (this, modelTags.Select (data => data.TagId));
            source.Attach ();
        }

        public override void ViewDidLoad ()
        {
            base.ViewDidLoad ();

            View.Apply (Style.Screen);
            EdgesForExtendedLayout = UIRectEdge.None;
            SetupDataSource ();

            NavigationItem.RightBarButtonItem = new UIBarButtonItem (
                "TagSet".Tr (), UIBarButtonItemStyle.Plain, OnNavigationBarSetClicked)
            .Apply (Style.NavLabelButton);
        }

        public override void ViewDidAppear (bool animated)
        {
            base.ViewDidAppear (animated);

            ServiceContainer.Resolve<ITracker> ().CurrentScreen = "Select Tags";
        }

        private async void OnNavigationBarSetClicked (object s, EventArgs e)
        {
            if (isSaving) {
                return;
            }

            isSaving = true;
            try {
                var tags = source.SelectedTags.ToList ();

                // Delete unused tag relations:
                var deleteTasks = modelTags
                                  .Where (oldTag => !tags.Any (newTag => newTag.Id == oldTag.TagId))
                                  .Select (data => new TimeEntryTagModel (data).DeleteAsync ()).ToList();

                // Create new tag relations:
                var createTasks = tags
                                  .Where (newTag => !modelTags.Any (oldTag => oldTag.TagId == newTag.Id))
                .Select (data => new TimeEntryTagModel () { TimeEntry = model, Tag = new TagModel (data) } .SaveAsync ()).ToList();

                await Task.WhenAll (deleteTasks.Concat (createTasks));

                if (deleteTasks.Count > 0 || createTasks.Count > 0) {
                    model.Touch ();
                    await model.SaveAsync ();
                }

                NavigationController.PopViewController (true);
            } finally {
                isSaving = false;
            }
        }

        private class Source : PlainDataViewSource<TagData>
        {
            private readonly static NSString TagCellId = new NSString ("EntryCellId");
            private readonly TagSelectionViewController controller;
            private readonly HashSet<Guid> selectedTags;
            private UIButton newTagButton;

            public Source (TagSelectionViewController controller, IEnumerable<Guid> selectedTagIds)
            : base (controller.TableView, new WorkspaceTagsView (controller.model.Workspace.Id))
            {
                this.controller = controller;
                this.selectedTags = new HashSet<Guid> (selectedTagIds);
            }

            public override void Attach ()
            {
                base.Attach ();

                controller.TableView.RegisterClassForCellReuse (typeof (TagCell), TagCellId);
                controller.TableView.SeparatorStyle = UITableViewCellSeparatorStyle.None;
            }

            public override nfloat EstimatedHeight (UITableView tableView, NSIndexPath indexPath)
            {
                return 60f;
            }

            public override nfloat GetHeightForRow (UITableView tableView, NSIndexPath indexPath)
            {
                return EstimatedHeight (tableView, indexPath);
            }

            public override UITableViewCell GetCell (UITableView tableView, NSIndexPath indexPath)
            {
                var cell = (TagCell)tableView.DequeueReusableCell (TagCellId, indexPath);
                cell.Bind ((TagModel)GetRow (indexPath));
                cell.Checked = selectedTags.Contains (cell.TagId);
                return cell;
            }

            public override void RowSelected (UITableView tableView, NSIndexPath indexPath)
            {
                var cell = tableView.CellAt (indexPath) as TagCell;
                if (cell != null) {
                    if (selectedTags.Remove (cell.TagId)) {
                        cell.Checked = false;
                    } else if (selectedTags.Add (cell.TagId)) {
                        cell.Checked = true;
                    }
                }

                tableView.DeselectRow (indexPath, false);
            }

            public IEnumerable<TagData> SelectedTags
            {
                get {
                    return DataView.Data.Where (data => selectedTags.Contains (data.Id));
                }
            }

            private void RefreshSelected()
            {
                foreach (var cell in TableView.VisibleCells) {
                    var tagCell = cell as TagCell;
                    if (tagCell != null) {
                        tagCell.Checked = selectedTags.Contains (tagCell.TagId);
                    }
                }
            }

            protected override void UpdateFooter ()
            {
                base.UpdateFooter ();

                if (TableView.TableFooterView == null) {
                    if (newTagButton == null) {
                        newTagButton = new UIButton (new CGRect (0, 0, 200, 60)).Apply (Style.TagList.NewTagButton);
                        newTagButton.SetTitle ("TagNewTag".Tr(), UIControlState.Normal);
                        newTagButton.TouchUpInside += delegate {
                            var vc = new NewTagViewController (controller.model.Workspace);
                            vc.TagCreated = (tag) => {
                                selectedTags.Add (tag.Id);
                                RefreshSelected();

                                controller.NavigationController.PopToViewController (controller, true);
                            };
                            controller.NavigationController.PushViewController (vc, true);
                        };
                    }

                    TableView.TableFooterView = newTagButton;
                }
            }
        }

        private class TagCell : ModelTableViewCell<TagModel>
        {
            private readonly UILabel nameLabel;

            public TagCell (IntPtr handle) : base (handle)
            {
                this.Apply (Style.Screen);
                ContentView.Add (nameLabel = new UILabel ().Apply (Style.TagList.NameLabel));
                BackgroundView = new UIView ().Apply (Style.TagList.RowBackground);
            }

            public override void LayoutSubviews ()
            {
                base.LayoutSubviews ();

                var contentFrame = new CGRect (0, CellSpacing / 2, Frame.Width, Frame.Height - CellSpacing);
                SelectedBackgroundView.Frame = BackgroundView.Frame = ContentView.Frame = contentFrame;

                contentFrame.X = 15f;
                contentFrame.Y = 0;
                contentFrame.Width -= 15f;

                if (Checked) {
                    // Adjust for the checkbox accessory
                    contentFrame.Width -= 40f;
                }

                nameLabel.Frame = contentFrame;
            }

            protected override void ResetTrackedObservables ()
            {
                Tracker.MarkAllStale ();

                if (DataSource != null) {
                    Tracker.Add (DataSource, HandleTagPropertyChanged);
                }

                Tracker.ClearStale ();
            }

            private void HandleTagPropertyChanged (string prop)
            {
                if (prop == TagModel.PropertyName) {
                    Rebind ();
                }
            }

            protected override void Rebind ()
            {
                ResetTrackedObservables ();

                if (DataSource == null) {
                    return;
                }

                if (String.IsNullOrWhiteSpace (DataSource.Name)) {
                    nameLabel.Text = "TagNoNameTag".Tr ();
                } else {
                    nameLabel.Text = DataSource.Name;
                }
            }

            public bool Checked
            {
                get { return Accessory == UITableViewCellAccessory.Checkmark; }
                set {
                    Accessory = value ? UITableViewCellAccessory.Checkmark : UITableViewCellAccessory.None;
                    SetNeedsLayout ();
                }
            }

            public Guid TagId
            {
                get {
                    if (DataSource != null) {
                        return DataSource.Id;
                    }
                    return Guid.Empty;
                }
            }
        }
    }
}

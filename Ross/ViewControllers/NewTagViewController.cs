using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cirrious.FluentLayouts.Touch;
using Foundation;
using UIKit;
using Toggl.Phoebe.Analytics;
using Toggl.Phoebe.Data;
using Toggl.Phoebe.Data.DataObjects;
using Toggl.Phoebe.Data.Models;
using XPlatUtils;
using Toggl.Ross.Theme;
using Toggl.Ross.Views;

namespace Toggl.Ross.ViewControllers
{
    public class NewTagViewController : UIViewController
    {
        private readonly TagModel model;
        private TextField nameTextField;
        private bool shouldRebindOnAppear;
        private bool isSaving;

        public NewTagViewController (WorkspaceModel workspace)
        {
            this.model = new TagModel () {
                Workspace = workspace,
            };
            Title = "NewTagTitle".Tr ();
        }

        public Action<TagModel> TagCreated { get; set; }

        private void BindNameField (TextField v)
        {
            if (v.Text != model.Name) {
                v.Text = model.Name;
            }
        }

        private void Rebind ()
        {
            nameTextField.Apply (BindNameField);
        }

        public override void LoadView ()
        {
            var view = new UIView ().Apply (Style.Screen);

            view.Add (nameTextField = new TextField () {
                TranslatesAutoresizingMaskIntoConstraints = false,
                AttributedPlaceholder = new NSAttributedString (
                    "NewTagNameHint".Tr (),
                    foregroundColor: Color.Gray
                ),
                ShouldReturn = (tf) => tf.ResignFirstResponder (),
            } .Apply (Style.NewProject.NameField).Apply (BindNameField));
            nameTextField.EditingChanged += OnNameFieldEditingChanged;

            view.AddConstraints (VerticalLinearLayout (view));

            EdgesForExtendedLayout = UIRectEdge.None;
            View = view;

            NavigationItem.RightBarButtonItem = new UIBarButtonItem (
                "NewTagAdd".Tr (), UIBarButtonItemStyle.Plain, OnNavigationBarAddClicked)
            .Apply (Style.NavLabelButton);
        }

        private void OnNameFieldEditingChanged (object sender, EventArgs e)
        {
            model.Name = nameTextField.Text;
        }

        private async void OnNavigationBarAddClicked (object sender, EventArgs e)
        {
            if (String.IsNullOrWhiteSpace (model.Name)) {
                // TODO: Show error dialog?
                return;
            }

            if (isSaving) {
                return;
            }

            isSaving = true;
            try {
                // Create new tag:
                var tag = await CreateTag (model);

                // Invoke callback hook
                var cb = TagCreated;
                if (cb != null) {
                    cb (tag);
                } else {
                    NavigationController.PopViewController (true);
                }
            } finally {
                isSaving = false;
            }
        }

        private static async Task<TagModel> CreateTag (TagModel model)
        {
            var store = ServiceContainer.Resolve<IDataStore>();
            var existing = await store.Table<TagData>()
                           .Where (r => r.WorkspaceId == model.Workspace.Id && r.Name == model.Name)
                           .ToListAsync ()
                           .ConfigureAwait (false);

            TagModel tag;
            if (existing.Count > 0) {
                tag = new TagModel (existing [0]);
            } else {
                tag = model;
                await tag.SaveAsync ().ConfigureAwait (false);
            }

            return tag;
        }

        private IEnumerable<FluentLayout> VerticalLinearLayout (UIView container)
        {
            UIView prev = null;

            var subviews = container.Subviews.Where (v => !v.Hidden).ToList ();
            foreach (var v in subviews) {
                if (prev == null) {
                    yield return v.AtTopOf (container, 10f);
                } else {
                    yield return v.Below (prev, 5f);
                }
                yield return v.Height ().EqualTo (60f).SetPriority (UILayoutPriority.DefaultLow);
                yield return v.Height ().GreaterThanOrEqualTo (60f);
                yield return v.AtLeftOf (container);
                yield return v.AtRightOf (container);

                prev = v;
            }
        }

        public override void ViewWillAppear (bool animated)
        {
            base.ViewWillAppear (animated);

            if (shouldRebindOnAppear) {
                Rebind ();
            } else {
                shouldRebindOnAppear = true;
            }
        }

        public override void ViewDidAppear (bool animated)
        {
            base.ViewDidAppear (animated);
            nameTextField.BecomeFirstResponder ();

            ServiceContainer.Resolve<ITracker> ().CurrentScreen = "New Tag";
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Cirrious.FluentLayouts.Touch;
using CoreAnimation;
using CoreGraphics;
using Foundation;
using GalaSoft.MvvmLight.Helpers;
using Toggl.Phoebe;
using Toggl.Phoebe.Data;
using Toggl.Phoebe.Data.Models;
using Toggl.Phoebe.Helpers;
using Toggl.Phoebe.Reactive;
using Toggl.Phoebe.ViewModels;
using Toggl.Phoebe.ViewModels.Timer;
using Toggl.Ross.DataSources;
using Toggl.Ross.Theme;
using Toggl.Ross.Views;
using UIKit;
using XPlatUtils;

namespace Toggl.Ross.ViewControllers
{
    public class EditTimeEntryViewController : UIViewController,
        DurationChangeViewController.IChangeDuration,
        IOnTagSelectedHandler,
        IOnProjectSelectedHandler
    {
        private enum LayoutVariant
        {
            Default,
            Description
        }

        private const string DefaultDurationText = " 00:00:00 ";
        readonly static NSString EntryCellId = new NSString("autocompletionCell");

        private LayoutVariant layoutVariant = LayoutVariant.Default;
        private NSLayoutConstraint[] trackedWrapperConstraints;
        private UIView wrapper;
        private StartStopView startStopView { get; set; }
        private ProjectClientTaskButton projectButton { get; set; }
        private TextField descriptionTextField { get; set; }
        private LabelSwitchView billableSwitch { get; set; }
        private UIDatePicker datePicker;
        private UIButton tagsButton;
        private UIButton deleteButton;
        private UIButton durationButton;
        private UITableView autoCompletionTableView;
        private UIBarButtonItem autoCopmletionDoneBarButtonItem;

        private bool hideDatePicker = true;
        private readonly List<NSObject> notificationObjects = new List<NSObject> ();

        // to avoid weak references to be removed
        private Binding<string, string> durationBinding, projectBinding, clientBinding, descriptionBinding, taskBinding, projectColorBinding;
        private Binding<DateTime, DateTime> startTimeBinding, stopTimeBinding;
        private Binding<List<string>, List<string>> tagBinding;
        private Binding<bool, bool> isBillableBinding, isRunningBinding, isPremiumBinding;
        private Binding<List<TimeEntryData>, List<TimeEntryData>> collectionBinding;

        protected EditTimeEntryVM ViewModel { get; set; }

        public EditTimeEntryViewController(Guid dataId)
        {
            ViewModel = new EditTimeEntryVM(StoreManager.Singleton.AppState, dataId);
        }

        class TGTableView : UITableView
        {
            public override UIView TableFooterView
            {
                get
                {
                    return base.TableFooterView;
                }
                set
                {
                    base.TableFooterView = value ?? new UIView();
                }
            }
        }

        public override void LoadView()
        {
            durationButton = new UIButton().Apply(Style.NavTimer.DurationButton);
            durationButton.SetTitle(DefaultDurationText, UIControlState.Normal);  // Dummy content to use for sizing of the label
            durationButton.SizeToFit();
            durationButton.TouchUpInside += OnDurationButtonTouchUpInside;
            NavigationItem.TitleView = durationButton;

            var scrollView = new UIScrollView().Apply(Style.Screen);

            scrollView.Add(wrapper = new UIView()
            {
                TranslatesAutoresizingMaskIntoConstraints = false,
            });

            wrapper.Add(startStopView = new StartStopView
            {
                TranslatesAutoresizingMaskIntoConstraints = false,
            });
            startStopView.SelectedChanged += OnStartStopViewSelectedChanged;

            wrapper.Add(datePicker = new UIDatePicker
            {
                TranslatesAutoresizingMaskIntoConstraints = false,
                Hidden = DatePickerHidden,
                Alpha = 0,
            } .Apply(Style.EditTimeEntry.DatePicker));
            datePicker.ValueChanged += OnDatePickerValueChanged;

            wrapper.Add(projectButton = new ProjectClientTaskButton
            {
                TranslatesAutoresizingMaskIntoConstraints = false,
            });
            projectButton.TouchUpInside += OnProjectButtonTouchUpInside;

            wrapper.Add(descriptionTextField = new TextField
            {
                TranslatesAutoresizingMaskIntoConstraints = false,
                AttributedPlaceholder = new NSAttributedString(
                    "EditEntryDesciptionTimerHint".Tr(),
                    foregroundColor: Color.Gray
                ),
                ShouldReturn = tf => tf.ResignFirstResponder(),
            } .Apply(Style.EditTimeEntry.DescriptionField));

            descriptionTextField.ShouldBeginEditing += (s) =>
            {
                ForceDimissDatePicker();
                initialDescription = s.Text;
                return true;
            };
            descriptionTextField.ShouldEndEditing += s =>
            {
                DescriptionEditingMode = false;
                return true;
            };

            wrapper.Add(tagsButton = new UIButton()
            {
                TranslatesAutoresizingMaskIntoConstraints = false,
            } .Apply(Style.EditTimeEntry.TagsButton));
            tagsButton.TouchUpInside += OnTagsButtonTouchUpInside;

            wrapper.Add(billableSwitch = new LabelSwitchView()
            {
                TranslatesAutoresizingMaskIntoConstraints = false,
                Text = "EditEntryBillable".Tr(),
            } .Apply(Style.EditTimeEntry.BillableContainer));
            billableSwitch.Label.Apply(Style.EditTimeEntry.BillableLabel);

            wrapper.Add(deleteButton = new UIButton()
            {
                TranslatesAutoresizingMaskIntoConstraints = false,
            } .Apply(Style.EditTimeEntry.DeleteButton));
            deleteButton.SetTitle("EditEntryDelete".Tr(), UIControlState.Normal);
            deleteButton.TouchUpInside += OnDeleteButtonTouchUpInside;

            wrapper.Add(autoCompletionTableView = new TGTableView()
            {
                TranslatesAutoresizingMaskIntoConstraints = false,
                EstimatedRowHeight = 60.0f,
                BackgroundColor = UIColor.Clear
            });

            ResetWrapperConstraints();
            scrollView.AddConstraints(
                wrapper.AtTopOf(scrollView),
                wrapper.AtBottomOf(scrollView),
                wrapper.AtLeftOf(scrollView),
                wrapper.AtRightOf(scrollView),
                wrapper.WithSameWidth(scrollView),
                wrapper.Height().GreaterThanOrEqualTo().HeightOf(scrollView).Minus(64f),
                null
            );


            View = scrollView;

            DescriptionEditingMode = false;
        }

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();

            // Bindings
            durationBinding = this.SetBinding(() => ViewModel.Duration).WhenSourceChanges(() => durationButton.SetTitle(ViewModel.Duration, UIControlState.Normal));
            startTimeBinding = this.SetBinding(() => ViewModel.StartDate, () => startStopView.StartTime);
            stopTimeBinding = this.SetBinding(() => ViewModel.StopDate, () => startStopView.StopTime);
            projectBinding = this.SetBinding(() => ViewModel.ProjectName, () => projectButton.ProjectName);
            projectColorBinding = this.SetBinding(() => ViewModel.ProjectColorHex, () => projectButton.ProjectColorHex);
            taskBinding = this.SetBinding(() => ViewModel.TaskName, () => projectButton.TaskName);
            clientBinding = this.SetBinding(() => ViewModel.ClientName, () => projectButton.ClientName);
            tagBinding = this.SetBinding(() => ViewModel.Tags).WhenSourceChanges(() =>
            {
                FeedTags(ViewModel.Tags, tagsButton);
            });
            descriptionBinding = this.SetBinding(() => ViewModel.Description, () => descriptionTextField.Text);
            isPremiumBinding = this.SetBinding(() => ViewModel.IsPremium, () => billableSwitch.Hidden).ConvertSourceToTarget(isPremium => !isPremium);
            isRunningBinding = this.SetBinding(() => ViewModel.IsRunning).WhenSourceChanges(() =>
            {
                if (ViewModel.IsRunning)
                {
                    startStopView.StopTime = DateTime.MaxValue;
                }
                else
                {
                    startStopView.StopTime = ViewModel.StopDate;
                }
            });
            collectionBinding = this.SetBinding(() => ViewModel.SuggestionsCollection).WhenSourceChanges(() =>
            {
                if (DescriptionEditingMode)
                {
                    autoCompletionTableView.Source = new SuggestionSource(this);
                    autoCompletionTableView.ReloadData();
                }
            });
            isBillableBinding = this.SetBinding(() => ViewModel.IsBillable, () => billableSwitch.Switch.On);

            // Events to edit some fields
            descriptionTextField.EditingChanged += (sender, e) =>
            {
                ViewModel.ChangeDescription(descriptionTextField.Text);

                if (descriptionTextField.Text.Length == 0 && initialDescription.Length > 0)
                    initialDescription = descriptionTextField.Text;

                if (shoudShowAutoComplete)
                    DescriptionEditingMode = true;
            };
            billableSwitch.Switch.ValueChanged += (sender, e) => { ViewModel.ChangeBillable(billableSwitch.Switch.On); };

            NavigationItem.HidesBackButton = true;
            var newBackButton = new UIBarButtonItem("EditEntryBack".Tr(), UIBarButtonItemStyle.Bordered, (sender, e) =>
            {
                var savedProperly = ViewModel?.Save();
                if (savedProperly == false)
                {
                    XPlatUtils.ServiceContainer.Resolve<GalaSoft.MvvmLight.Views.IDialogService>()
                    .ShowMessage(EditTimeEntryVM.StartTimeError, EditTimeEntryVM.ErrorTitle);
                }
                else
                {
                    NavigationController?.PopViewController(true);
                }
            });
            NavigationItem.LeftBarButtonItem = newBackButton;
        }

        private string initialDescription;

        private bool shoudShowAutoComplete
        {
            get
            {
                if (DescriptionEditingMode)
                    return true;

                if (descriptionTextField.Text.Length >= EditTimeEntryVM.LoadSuggestionsCharLimit)
                    return initialDescription.Length == 0;
                return false;
            }
        }

        private void ResetWrapperConstraints()
        {
            if (trackedWrapperConstraints != null)
            {
                wrapper.RemoveConstraints(trackedWrapperConstraints);
                trackedWrapperConstraints = null;
            }

            switch (layoutVariant)
            {
                case LayoutVariant.Default:
                    trackedWrapperConstraints = VerticalLinearLayout(wrapper).ToLayoutConstraints();
                    break;
                case LayoutVariant.Description:
                    trackedWrapperConstraints = new []
                    {
                        descriptionTextField.AtTopOf(wrapper),
                        descriptionTextField.AtLeftOf(wrapper),
                        descriptionTextField.AtRightOf(wrapper),
                        descriptionTextField.Height().EqualTo(60.0f),
                        autoCompletionTableView.AtTopOf(wrapper, 65.0f),
                        autoCompletionTableView.AtLeftOf(wrapper),
                        autoCompletionTableView.AtRightOf(wrapper),
                        autoCompletionTableView.AtBottomOf(wrapper)
                    } .ToLayoutConstraints();
                    break;
            }

            wrapper.AddConstraints(trackedWrapperConstraints);
        }

        private bool changedModeOnce = false;
        private bool descriptionEditingMode__;
        private bool DescriptionEditingMode
        {
            get { return descriptionEditingMode__; }
            set
            {
                if (value == descriptionEditingMode__)
                    return;
                UIScrollView scrlView = (UIScrollView)View;
                scrlView.ScrollEnabled = !value;
                if (value)
                {
                    layoutVariant = LayoutVariant.Description;
                    NavigationItem.Apply(BindAutoCompletionDoneBarButtonItem);
                }
                else
                {
                    descriptionTextField.ResignFirstResponder();
                    layoutVariant = LayoutVariant.Default;
                    NavigationItem.Apply(UnBindAutoCompletionDoneBarButtonItem);
                }
                ResetWrapperConstraints();
                UIView.Animate(changedModeOnce ? 0.3f : 0.0f, delegate
                {
                    SetEditingModeViewsHidden(value);
                    wrapper.LayoutIfNeeded();
                });
                descriptionEditingMode__ = value;
                changedModeOnce = true;
            }
        }

        private void SetEditingModeViewsHidden(bool editingMode)
        {
            billableSwitch.Alpha = tagsButton.Alpha = startStopView.Alpha = projectButton.Alpha = deleteButton.Alpha = editingMode ? 0 : 1;
            autoCompletionTableView.Alpha = 1 - tagsButton.Alpha;
        }

        public override void ViewWillDisappear(bool animated)
        {
            NSNotificationCenter.DefaultCenter.RemoveObservers(notificationObjects);
            notificationObjects.Clear();

            // TODO: Release ViewModel only when the
            // ViewController is poped. It is a weird behaviour
            // considering the property name used.
            // But it works ok.
            if (IsMovingFromParentViewController)
            {
                startTimeBinding.Detach();
                stopTimeBinding.Detach();
                tagBinding.Detach();
                isPremiumBinding.Detach();
                isRunningBinding.Detach();
                ViewModel.Dispose();
            }
            base.ViewWillDisappear(animated);
        }

        private void FeedTags(List<string> tagNames, UIButton btn)
        {
            // Construct tags attributed strings:
            NSMutableAttributedString text = null;
            foreach (var tag in tagNames)
            {

                var chip = NSAttributedString.CreateFrom(new NSTextAttachment
                {
                    Image = ServiceContainer.Resolve<TagChipCache> ().Get(tag, btn),
                });

                if (text == null)
                {
                    text = new NSMutableAttributedString(chip);
                }
                else
                {
                    text.Append(new NSAttributedString(" ", Style.EditTimeEntry.WithTags));
                    text.Append(chip);
                }
            }

            if (text == null)
            {
                btn.SetAttributedTitle(new NSAttributedString("EditEntryTagsHint".Tr(), Style.EditTimeEntry.NoTags), UIControlState.Normal);
            }
            else
            {
                btn.SetAttributedTitle(text, UIControlState.Normal);
            }
        }

        private void OnDatePickerValueChanged(object sender, EventArgs e)
        {
            switch (startStopView.Selected)
            {
                case TimeKind.Start:
                    ViewModel.ChangeTimeEntryStart(datePicker.Date.ToDateTime());
                    break;
                case TimeKind.Stop:
                    ViewModel.ChangeTimeEntryStop(datePicker.Date.ToDateTime());
                    break;
            }
        }

        private void OnProjectButtonTouchUpInside(object sender, EventArgs e)
        {
            var controller = new ProjectSelectionViewController(ViewModel.WorkspaceId, this);
            NavigationController.PushViewController(controller, true);
        }

        private void OnDurationButtonTouchUpInside(object sender, EventArgs e)
        {
            // TODO: This condition is valid or not?
            if (ViewModel.IsRunning)
            {
                return;
            }

            var controller = new DurationChangeViewController(ViewModel.StopDate, ViewModel.StartDate, this);
            NavigationController.PushViewController(controller, true);
        }

        private void OnTagsButtonTouchUpInside(object sender, EventArgs e)
        {
            var controller = new TagSelectionViewController(ViewModel.WorkspaceId, ViewModel.Tags, this);
            NavigationController.PushViewController(controller, true);
        }

        private void OnDeleteButtonTouchUpInside(object sender, EventArgs e)
        {
            var alert = new UIAlertView(
                "EditEntryConfirmTitle".Tr(),
                "EditEntryConfirmMessage".Tr(),
                null,
                "EditEntryConfirmCancel".Tr(),
                "EditEntryConfirmDelete".Tr());
            alert.Clicked += (s, ev) =>
            {
                if (ev.ButtonIndex == 1)
                {
                    ViewModel.Delete();
                    NavigationController.PopToRootViewController(true);
                }
            };
            alert.Show();
        }

        private void OnKeyboardHeightChanged(int height)
        {
            var scrollView = (UIScrollView)View;

            var inset = scrollView.ContentInset;
            inset.Bottom = height;
            scrollView.ContentInset = inset;

            inset = scrollView.ScrollIndicatorInsets;
            inset.Bottom = height;
            scrollView.ScrollIndicatorInsets = inset;
        }

        private void OnStartStopViewSelectedChanged(object sender, EventArgs e)
        {
            var currentValue = datePicker.Date.ToDateTime().ToUtc();
            var animate = !datePicker.Hidden;
            DatePickerHidden = startStopView.Selected == TimeKind.None;
            switch (startStopView.Selected)
            {
                case TimeKind.Start:
                    datePicker.Mode = UIDatePickerMode.DateAndTime;
                    datePicker.MaximumDate = (NSDate)(ViewModel.IsRunning ? DateTime.Now : DateTime.Now + TimeSpan.FromDays(356));
                    if (currentValue != ViewModel.StartDate)
                    {
                        datePicker.SetDate(ViewModel.StartDate.ToNSDate(), animate);
                    }
                    break;
                case TimeKind.Stop:
                    datePicker.Mode = UIDatePickerMode.DateAndTime;
                    if (currentValue != ViewModel.StopDate)
                    {
                        datePicker.SetDate(ViewModel.StopDate.ToNSDate(), animate);
                    }
                    break;
            }
        }

        private SuggestionSource autocompletionTableViewSource;

        private void BindAutocompletionTableView(UITableView v)
        {
            autocompletionTableViewSource = new SuggestionSource(this);
        }

        private void BindAutoCompletionDoneBarButtonItem(UINavigationItem v)
        {
            autoCopmletionDoneBarButtonItem = new UIBarButtonItem(UIBarButtonSystemItem.Done);
            autoCopmletionDoneBarButtonItem.Clicked += (object sender, EventArgs e) =>
            {
                DescriptionEditingMode = false;
            };
            v.SetRightBarButtonItem(autoCopmletionDoneBarButtonItem, true);
        }

        private void UnBindAutoCompletionDoneBarButtonItem(UINavigationItem v)
        {
            if (v.RightBarButtonItem == autoCopmletionDoneBarButtonItem)
            {
                v.SetRightBarButtonItem(null, true);
                autoCopmletionDoneBarButtonItem = null;
            }
        }

        private void UpdateEntry(TimeEntryData data)
        {
            ViewModel.UpdateEntry(data);
            DescriptionEditingMode = false;
        }

        internal class SuggestionSource : UITableViewSource
        {
            private readonly EditTimeEntryViewController owner;
            private readonly UITableView table;
            private readonly List<TimeEntryData> collection;

            public SuggestionSource(EditTimeEntryViewController owner)
            {
                collection = owner.ViewModel.SuggestionsCollection;
                table = owner.autoCompletionTableView;
                this.owner = owner;
                table.RegisterClassForCellReuse(typeof(SuggestionTableViewCell), EntryCellId);
            }

            public override UITableViewCell GetCell(UITableView tableView, NSIndexPath indexPath)
            {
                UITableViewCell cell = tableView.DequeueReusableCell(EntryCellId);
                var data = collection.ElementAt(indexPath.Row);
                ((SuggestionTableViewCell)cell).Bind((TimeEntryData)data);

                return cell;
            }

            public override nint RowsInSection(UITableView tableview, nint section)
            {
                return collection.Count;
            }

            public override nfloat EstimatedHeight(UITableView tableView, NSIndexPath indexPath)
            {
                return 60f;
            }

            public override nfloat GetHeightForRow(UITableView tableView, NSIndexPath indexPath)
            {
                return EstimatedHeight(tableView, indexPath);
            }

            public override void RowSelected(UITableView tableView, NSIndexPath indexPath)
            {
                owner.UpdateEntry(collection[indexPath.Row]);
                tableView.DeselectRow(indexPath, false);
            }
        }
        private void ForceDimissDatePicker()
        {
            DatePickerHidden = true;
            startStopView.Selected = TimeKind.None;
        }

        #region IProjectSelected implementation
        public void OnProjectSelected(Guid projectId, Guid taskId)
        {
            ViewModel.ChangeProjectAndTask(projectId, taskId);
        }
        #endregion

        #region IChangeDuration implementation
        public void OnChangeDuration(TimeSpan newDuration)
        {
            ViewModel.ChangeTimeEntryDuration(newDuration);
        }
        #endregion

        #region IUpdateTagList implementation
        public void OnCreateNewTag(string newTag)
        {
            var newTagList = ViewModel.Tags.ToList();
            newTagList.Add(newTag);
            ViewModel.ChangeTagList(newTagList);
            NavigationController.PopToViewController(this, true);
        }

        public void OnModifyTagList(List<string> newTagList)
        {
            ViewModel.ChangeTagList(newTagList);
            NavigationController.PopToViewController(this, true);
        }
        #endregion

        #region UI layout helpers
        private bool DatePickerHidden
        {
            get { return hideDatePicker; }
            set
            {
                if (hideDatePicker == value)
                {
                    return;
                }
                hideDatePicker = value;

                if (hideDatePicker)
                {
                    UIView.AnimateKeyframes(
                        0.4, 0, 0,
                        delegate
                    {
                        UIView.AddKeyframeWithRelativeStartTime(0, 0.4, delegate
                        {
                            datePicker.Alpha = 0;
                        });
                        UIView.AddKeyframeWithRelativeStartTime(0.2, 0.8, delegate
                        {
                            ResetWrapperConstraints();
                            View.LayoutIfNeeded();
                        });
                    },
                    delegate
                    {
                        if (hideDatePicker)
                        {
                            datePicker.Hidden = true;
                        }
                    }
                    );
                }
                else
                {
                    descriptionTextField.ResignFirstResponder();
                    datePicker.Hidden = false;
                    UIView.AnimateKeyframes(
                        0.4, 0, 0,
                        delegate
                    {
                        UIView.AddKeyframeWithRelativeStartTime(0, 0.6, delegate
                        {
                            ResetWrapperConstraints();
                            View.LayoutIfNeeded();
                        });
                        UIView.AddKeyframeWithRelativeStartTime(0.4, 0.8, delegate
                        {
                            datePicker.Alpha = 1;
                        });
                    },
                    delegate
                    {
                    }
                    );
                }
            }
        }

        private IEnumerable<FluentLayout> VerticalLinearLayout(UIView container)
        {
            UIView prev = null;

            var subviews = container.Subviews.Where(v => !v.Hidden && !(v == datePicker && DatePickerHidden)).ToList();
            foreach (var v in subviews)
            {
                var isLast = subviews [subviews.Count - 1] == v;

                if (prev == null)
                {
                    yield return v.AtTopOf(container);
                }
                else if (isLast)
                {
                    yield return v.Top().GreaterThanOrEqualTo().BottomOf(prev).Plus(5f);
                }
                else
                {
                    yield return v.Below(prev, 5f);
                }
                yield return v.Height().EqualTo(60f).SetPriority(UILayoutPriority.DefaultLow);
                yield return v.Height().GreaterThanOrEqualTo(60f);
                yield return v.AtLeftOf(container);
                yield return v.AtRightOf(container);

                prev = v;
            }

            if (prev != null)
            {
                yield return prev.AtBottomOf(container);
            }
        }
        #endregion

        #region Custom UI components
        private class StartStopView : UIView
        {
            private readonly List<NSLayoutConstraint> trackedConstraints = new List<NSLayoutConstraint>();
            private readonly UILabel startDateLabel;
            private readonly UILabel startTimeLabel;
            private readonly UIButton startDateTimeButton;
            private readonly UILabel stopDateLabel;
            private readonly UILabel stopTimeLabel;
            private readonly UIButton stopDateTimeButton;
            private readonly UIImageView arrowImageView;
            private LayoutVariant viewLayout = LayoutVariant.StartOnly;
            private bool stopTimeHidden = true;
            private DateTime startTime;
            private DateTime stopTime;
            private TimeKind selectedTime;

            public StartStopView()
            {
                startDateTimeButton = new UIButton()
                {
                    TranslatesAutoresizingMaskIntoConstraints = false,
                };
                startDateTimeButton.TouchUpInside += OnStartDateTimeButtonTouchUpInside;
                arrowImageView = new UIImageView()
                {
                    TranslatesAutoresizingMaskIntoConstraints = false,
                    Image = Image.IconDurationArrow,
                };
                stopDateTimeButton = new UIButton()
                {
                    TranslatesAutoresizingMaskIntoConstraints = false,
                };
                stopDateTimeButton.TouchUpInside += OnStopDateTimeButtonTouchUpInside;

                ConstructDateTimeView(startDateTimeButton, ref startDateLabel, ref startTimeLabel);
                ConstructDateTimeView(stopDateTimeButton, ref stopDateLabel, ref stopTimeLabel);

                Add(startDateTimeButton);

                StartTime = DateTime.Now - TimeSpan.FromHours(4);
            }

            private void OnStartDateTimeButtonTouchUpInside(object sender, EventArgs e)
            {
                Selected = Selected == TimeKind.Start ? TimeKind.None : TimeKind.Start;
            }

            private void OnStopDateTimeButtonTouchUpInside(object sender, EventArgs e)
            {
                Selected = Selected == TimeKind.Stop ? TimeKind.None : TimeKind.Stop;
            }

            public DateTime StartTime
            {
                get { return startTime; }
                set
                {
                    if (startTime == value)
                    {
                        return;
                    }

                    startTime = value;
                    var time = startTime.ToLocalTime();
                    startDateLabel.Text = time.ToLocalizedDateString();
                    startTimeLabel.Text = time.ToLocalizedTimeString();
                }
            }

            public DateTime StopTime
            {
                get { return stopTime; }
                set
                {

                    if (stopTime == value)
                    {
                        return;
                    }
                    stopTime = value;

                    if (stopTime != DateTime.MaxValue)
                    {
                        var time = stopTime.ToLocalTime();
                        stopDateLabel.Text = time.ToLocalizedDateString();
                        stopTimeLabel.Text = time.ToLocalizedTimeString();
                    }
                    SetStopTimeHidden(stopTime == DateTime.MaxValue, false);

                    if (stopTime == DateTime.MaxValue && Selected == TimeKind.Stop)
                    {
                        Selected = TimeKind.None;
                    }
                }
            }

            public TimeKind Selected
            {
                get { return selectedTime; }
                set
                {
                    if (selectedTime == value)
                    {
                        return;
                    }

                    selectedTime = value;

                    if (selectedTime == TimeKind.Start)
                    {
                        startDateLabel.Apply(Style.EditTimeEntry.DateLabelActive);
                        startTimeLabel.Apply(Style.EditTimeEntry.TimeLabelActive);
                    }
                    else
                    {
                        startDateLabel.Apply(Style.EditTimeEntry.DateLabel);
                        startTimeLabel.Apply(Style.EditTimeEntry.TimeLabel);
                    }

                    if (selectedTime == TimeKind.Stop)
                    {
                        stopDateLabel.Apply(Style.EditTimeEntry.DateLabelActive);
                        stopTimeLabel.Apply(Style.EditTimeEntry.TimeLabelActive);
                    }
                    else
                    {
                        stopDateLabel.Apply(Style.EditTimeEntry.DateLabel);
                        stopTimeLabel.Apply(Style.EditTimeEntry.TimeLabel);
                    }

                    var handler = SelectedChanged;
                    if (handler != null)
                    {
                        handler(this, EventArgs.Empty);
                    }
                }
            }

            public event EventHandler SelectedChanged;

            private void SetStopTimeHidden(bool hidden, bool animate)
            {
                if (stopTimeHidden == hidden)
                {
                    return;
                }
                stopTimeHidden = hidden;

                if (!animate)
                {
                    ViewLayout = hidden ? LayoutVariant.StartOnly : LayoutVariant.BothCenterAll;
                }
                else if (hidden)
                {
                    ViewLayout = LayoutVariant.BothCenterAll;
                    stopDateTimeButton.Alpha = 1;
                    arrowImageView.Alpha = 1;
                    LayoutIfNeeded();

                    UIView.AnimateKeyframes(
                        0.4, 0,
                        UIViewKeyframeAnimationOptions.CalculationModeCubic,
                        delegate
                    {
                        UIView.AddKeyframeWithRelativeStartTime(0, 1, delegate
                        {
                            ViewLayout = LayoutVariant.BothCenterStart;
                            LayoutIfNeeded();
                        });
                        UIView.AddKeyframeWithRelativeStartTime(0, 0.8, delegate
                        {
                            stopDateTimeButton.Alpha = 0;
                            arrowImageView.Alpha = 0;
                        });
                    },
                    delegate
                    {
                        if (ViewLayout == LayoutVariant.BothCenterStart)
                        {
                            ViewLayout = LayoutVariant.StartOnly;
                            LayoutIfNeeded();
                        }
                    });
                }
                else
                {
                    ViewLayout = LayoutVariant.BothCenterStart;
                    stopDateTimeButton.Alpha = 0;
                    arrowImageView.Alpha = 0;
                    LayoutIfNeeded();

                    UIView.AnimateKeyframes(
                        0.4, 0,
                        UIViewKeyframeAnimationOptions.CalculationModeCubic,
                        delegate
                    {
                        UIView.AddKeyframeWithRelativeStartTime(0, 1, delegate
                        {
                            ViewLayout = LayoutVariant.BothCenterAll;
                            LayoutIfNeeded();
                        });
                        UIView.AddKeyframeWithRelativeStartTime(0.2, 1, delegate
                        {
                            stopDateTimeButton.Alpha = 1;
                            arrowImageView.Alpha = 1;
                        });
                    },
                    delegate
                    {
                    });
                }
            }

            private static void ConstructDateTimeView(UIView view, ref UILabel dateLabel, ref UILabel timeLabel)
            {
                view.Add(dateLabel = new UILabel().Apply(Style.EditTimeEntry.DateLabel));
                view.Add(timeLabel = new UILabel().Apply(Style.EditTimeEntry.TimeLabel));
                view.AddConstraints(
                    dateLabel.AtTopOf(view, 10f),
                    dateLabel.AtLeftOf(view, 10f),
                    dateLabel.AtRightOf(view, 10f),

                    timeLabel.Below(dateLabel, 2f),
                    timeLabel.AtBottomOf(view, 10f),
                    timeLabel.AtLeftOf(view, 10f),
                    timeLabel.AtRightOf(view, 10f)
                );
                view.SubviewsDoNotTranslateAutoresizingMaskIntoConstraints();
            }

            public override void UpdateConstraints()
            {
                if (trackedConstraints.Count > 0)
                {
                    RemoveConstraints(trackedConstraints.ToArray());
                    trackedConstraints.Clear();
                }

                switch (viewLayout)
                {
                    case LayoutVariant.StartOnly:
                        arrowImageView.RemoveFromSuperview();
                        stopDateTimeButton.RemoveFromSuperview();

                        trackedConstraints.AddRange(new []
                        {
                            startDateTimeButton.WithSameCenterX(this),
                            startDateTimeButton.WithSameCenterY(this),
                            startDateTimeButton.AtTopOf(this, 10f),
                            startDateTimeButton.AtBottomOf(this, 10f)
                        } .ToLayoutConstraints());
                        break;
                    case LayoutVariant.BothCenterStart:
                        AddSubview(arrowImageView);
                        AddSubview(stopDateTimeButton);

                        trackedConstraints.AddRange(new []
                        {
                            startDateTimeButton.WithSameCenterX(this),
                            startDateTimeButton.WithSameCenterY(this),
                            startDateTimeButton.AtTopOf(this, 10f),
                            startDateTimeButton.AtBottomOf(this, 10f),

                            arrowImageView.WithSameCenterY(startDateTimeButton),
                            arrowImageView.ToRightOf(startDateTimeButton, 10f),
                            arrowImageView.ToLeftOf(stopDateTimeButton, 10f),

                            stopDateTimeButton.AtTopOf(this, 10f),
                            stopDateTimeButton.AtBottomOf(this, 10f)
                        } .ToLayoutConstraints());
                        break;
                    default:
                        AddSubview(arrowImageView);
                        AddSubview(stopDateTimeButton);

                        trackedConstraints.AddRange(new []
                        {
                            startDateTimeButton.AtTopOf(this, 10f),
                            startDateTimeButton.AtBottomOf(this, 10f),

                            arrowImageView.WithSameCenterX(this),
                            arrowImageView.WithSameCenterY(this),
                            arrowImageView.ToRightOf(startDateTimeButton, 10f),
                            arrowImageView.ToLeftOf(stopDateTimeButton, 10f),

                            stopDateTimeButton.AtTopOf(this, 10f),
                            stopDateTimeButton.AtBottomOf(this, 10f)
                        } .ToLayoutConstraints());
                        break;
                }

                AddConstraints(trackedConstraints.ToArray());

                base.UpdateConstraints();
            }

            private LayoutVariant ViewLayout
            {
                get { return viewLayout; }
                set
                {
                    if (viewLayout == value)
                    {
                        return;
                    }
                    viewLayout = value;
                    SetNeedsUpdateConstraints();
                    SetNeedsLayout();
                }
            }

            private enum LayoutVariant
            {
                StartOnly,
                BothCenterStart,
                BothCenterAll
            }

            [Export("requiresConstraintBasedLayout")]
            public static new bool RequiresConstraintBasedLayout()
            {
                return true;
            }
        }

        private enum TimeKind
        {
            None,
            Start,
            Stop
        }

        private class ProjectClientTaskButton : UIButton
        {
            private readonly List<NSLayoutConstraint> trackedConstraints = new List<NSLayoutConstraint>();
            private readonly UIView container;
            private readonly UILabel projectLabel;
            private readonly UILabel clientLabel;
            private readonly UILabel taskLabel;

            public ProjectClientTaskButton()
            {
                Add(container = new UIView()
                {
                    TranslatesAutoresizingMaskIntoConstraints = false,
                    UserInteractionEnabled = false,
                });

                var maskLayer = new CAGradientLayer()
                {
                    AnchorPoint = CGPoint.Empty,
                    StartPoint = new CGPoint(0.0f, 0.0f),
                    EndPoint = new CGPoint(1.0f, 0.0f),
                    Colors = new []
                    {
                        UIColor.FromWhiteAlpha(1, 1).CGColor,
                        UIColor.FromWhiteAlpha(1, 1).CGColor,
                        UIColor.FromWhiteAlpha(1, 0).CGColor,
                    },
                    Locations = new []
                    {
                        NSNumber.FromFloat(0f),
                        NSNumber.FromFloat(0.9f),
                        NSNumber.FromFloat(1f),
                    },
                };
                container.Layer.Mask = maskLayer;

                container.Add(projectLabel = new UILabel()
                {
                    TranslatesAutoresizingMaskIntoConstraints = false,
                } .Apply(Style.EditTimeEntry.ProjectLabel));
                container.Add(clientLabel = new UILabel()
                {
                    TranslatesAutoresizingMaskIntoConstraints = false,
                } .Apply(Style.EditTimeEntry.ClientLabel));
                container.Add(taskLabel = new UILabel()
                {
                    TranslatesAutoresizingMaskIntoConstraints = false,
                } .Apply(Style.EditTimeEntry.TaskLabel));
            }

            public override void UpdateConstraints()
            {
                if (trackedConstraints.Count > 0)
                {
                    RemoveConstraints(trackedConstraints.ToArray());
                    trackedConstraints.Clear();
                }

                trackedConstraints.AddRange(new FluentLayout[]
                {
                    container.AtLeftOf(this, 15f),
                    container.WithSameCenterY(this),

                    projectLabel.AtTopOf(container),
                    projectLabel.AtLeftOf(container),
                    null
                } .ToLayoutConstraints());

                if (!clientLabel.Hidden)
                {
                    var baselineOffset = (float)Math.Floor(projectLabel.Font.Descender - clientLabel.Font.Descender);
                    trackedConstraints.AddRange(new FluentLayout[]
                    {
                        clientLabel.AtTopOf(container, -baselineOffset),
                        clientLabel.AtRightOf(container),
                        clientLabel.ToRightOf(projectLabel, 5f),
                        clientLabel.AtBottomOf(projectLabel, baselineOffset),
                        null
                    } .ToLayoutConstraints());
                }
                else
                {
                    trackedConstraints.AddRange(new FluentLayout[]
                    {
                        projectLabel.AtRightOf(container),
                        null
                    } .ToLayoutConstraints());
                }

                if (!taskLabel.Hidden)
                {
                    trackedConstraints.AddRange(new FluentLayout[]
                    {
                        taskLabel.Below(projectLabel, 3f),
                        taskLabel.AtLeftOf(container),
                        taskLabel.AtRightOf(container),
                        taskLabel.AtBottomOf(container),
                        null
                    } .ToLayoutConstraints());
                }
                else
                {
                    trackedConstraints.AddRange(new FluentLayout[]
                    {
                        projectLabel.AtBottomOf(container),
                        null
                    } .ToLayoutConstraints());
                }

                AddConstraints(trackedConstraints.ToArray());

                base.UpdateConstraints();
            }

            public override void LayoutSubviews()
            {
                base.LayoutSubviews();

                // Update fade mask position:
                var bounds = container.Frame;
                bounds.Width = Frame.Width - bounds.X;
                container.Layer.Mask.Bounds = bounds;
            }

            public string ProjectName
            {
                get { return projectLabel.Text; }
                set
                {
                    var projectName = string.IsNullOrEmpty(value) ? "EditEntryProjectHint".Tr() : value;
                    projectLabel.Text = projectName;
                }
            }

            public string ClientName
            {
                get { return clientLabel.Text; }
                set
                {
                    if (clientLabel.Text == value)
                    {
                        return;
                    }

                    var visibilityChanged = string.IsNullOrWhiteSpace(clientLabel.Text) != string.IsNullOrWhiteSpace(value);
                    clientLabel.Text = value;

                    if (visibilityChanged)
                    {
                        SetNeedsUpdateConstraints();
                        clientLabel.Hidden = string.IsNullOrWhiteSpace(value);
                    }
                }
            }

            public string TaskName
            {
                get { return taskLabel.Text; }
                set
                {
                    if (taskLabel.Text == value)
                    {
                        return;
                    }

                    var visibilityChanged = string.IsNullOrWhiteSpace(taskLabel.Text) != string.IsNullOrWhiteSpace(value);
                    taskLabel.Text = value;

                    if (visibilityChanged)
                    {
                        SetNeedsUpdateConstraints();
                        taskLabel.Hidden = string.IsNullOrWhiteSpace(value);
                    }
                }
            }

            public string ProjectColorHex
            {
                get { return "#ffffff"; }
                set
                {
                    // If string Hex color is default or null:
                    if (value == ProjectData.HexColors [ProjectData.DefaultColor] || string.IsNullOrEmpty(value))
                    {
                        projectLabel.Apply(Style.EditTimeEntry.ProjectHintLabel);
                        SetBackgroundImage(Color.White.ToImage(), UIControlState.Normal);
                        SetBackgroundImage(Color.LightestGray.ToImage(), UIControlState.Highlighted);
                    }
                    else
                    {
                        projectLabel.Apply(Style.EditTimeEntry.ProjectLabel);
                        SetBackgroundImage(UIColor.Clear.FromHex(value).ToImage(), UIControlState.Normal);
                        SetBackgroundImage(null, UIControlState.Highlighted);
                    }
                }
            }

            [Export("requiresConstraintBasedLayout")]
            public static new bool RequiresConstraintBasedLayout()
            {
                return true;
            }
        }

        public class SuggestionTableViewCell : UITableViewCell
        {
            private const float HorizPadding = 15.0f;
            private readonly UIView textContentView;
            private readonly UILabel projectLabel;
            private readonly UILabel clientLabel;
            private readonly UILabel descriptionLabel;

            public SuggestionTableViewCell(IntPtr handle) : base(handle)
            {
                textContentView = new UIView();

                projectLabel = new UILabel().Apply(Style.Recent.CellProjectLabel);
                clientLabel = new UILabel().Apply(Style.Recent.CellClientLabel);
                descriptionLabel = new UILabel().Apply(Style.Recent.CellDescriptionLabel);

                textContentView.AddSubviews(projectLabel, clientLabel, descriptionLabel);

                ContentView.AddSubview(textContentView);

                ContentView.AddConstraints(new FluentLayout[]
                {
                    textContentView.AtTopOf(ContentView),
                    textContentView.AtBottomOf(ContentView),
                    textContentView.AtLeftOf(ContentView),
                    textContentView.AtRightOf(ContentView)
                } .ToLayoutConstraints());

                textContentView.AddConstraints(new FluentLayout[]
                {
                    projectLabel.AtTopOf(textContentView, 8),
                    projectLabel.AtLeftOf(textContentView, HorizPadding),
                    clientLabel.WithSameCenterY(projectLabel).Plus(1),
                    clientLabel.ToRightOf(projectLabel, 6),
                    clientLabel.AtRightOf(textContentView, HorizPadding),
                    descriptionLabel.Below(projectLabel, 4),
                    descriptionLabel.AtLeftOf(textContentView, HorizPadding + 1),
                    descriptionLabel.AtRightOf(textContentView, HorizPadding)
                } .ToLayoutConstraints());

                textContentView.SubviewsDoNotTranslateAutoresizingMaskIntoConstraints();
                ContentView.SubviewsDoNotTranslateAutoresizingMaskIntoConstraints();
            }


            public void Bind(TimeEntryData data)
            {
                var projectName = "LogCellNoProject".Tr();
                var projectColor = Color.Gray;
                var clientName = String.Empty;

                var info = StoreManager.Singleton.AppState.LoadTimeEntryInfo(data);
                if (data.ProjectId != Guid.Empty)
                {

                    projectName = info.ProjectData.Name;

                    var hex = info.ProjectData.Id != Guid.Empty
                              ? ProjectData.HexColors[info.ProjectData.Color % ProjectData.HexColors.Length]
                              : ProjectData.HexColors[ProjectData.DefaultColor];
                    projectColor = UIColor.Clear.FromHex(hex);

                    if (info.ProjectData.Id != Guid.Empty)
                    {
                        clientName = info.ClientData.Name;
                    }
                }

                projectLabel.TextColor = projectColor;

                if (projectLabel.Text != projectName)
                {
                    projectLabel.Text = projectName;
                    projectLabel.InvalidateIntrinsicContentSize();
                    SetNeedsLayout();
                }

                if (clientLabel.Text != clientName)
                {
                    clientLabel.Text = clientName;
                    clientLabel.InvalidateIntrinsicContentSize();
                    SetNeedsLayout();
                }

                var description = data.Description;
                var descriptionHidden = String.IsNullOrWhiteSpace(description);

                if (descriptionHidden)
                {
                    description = "LogCellNoDescription".Tr();
                    descriptionHidden = false;
                }

                if (descriptionLabel.Text != description)
                {
                    descriptionLabel.Text = description;
                    descriptionLabel.InvalidateIntrinsicContentSize();
                    SetNeedsLayout();
                }

                LayoutIfNeeded();
            }
        }

        #endregion
    }
}

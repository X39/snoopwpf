// (c) Copyright Cory Plotts.
// This source is subject to the Microsoft Public License (Ms-PL).
// Please see http://go.microsoft.com/fwlink/?LinkID=131993 for details.
// All other rights reserved.

namespace Snoop.Views
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Linq;
    using System.Text;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Data;
    using System.Windows.Input;
    using System.Windows.Media;
    using JetBrains.Annotations;
    using Snoop.Converters;
    using Snoop.Infrastructure;
    using Snoop.Windows;

    public class TrackedEventContainer
    {
        public TrackedEvent Event { get; set; }

        public bool IsSelected { get; set; }

        public static TrackedEventContainer ToTrackedEventContainer(TrackedEvent trackedEvent)
        {
            return new TrackedEventContainer { Event = trackedEvent };
        }

        public static implicit operator TrackedEventContainer(TrackedEvent trackedEvent)
        {
            return ToTrackedEventContainer(trackedEvent);
        }
    }

    public partial class EventsView : INotifyPropertyChanged
    {
        public static readonly RoutedCommand ClearCommand = new RoutedCommand(nameof(ClearCommand), typeof(EventsView));

        public static readonly RoutedCommand CopySelectedEventsToClipboardCommand = new RoutedCommand(nameof(CopySelectedEventsToClipboardCommand), typeof(EventsView));

        public EventsView()
        {
            this.InitializeComponent();

            var sorter = new List<EventTracker>();

            foreach (var routedEvent in EventManager.GetRoutedEvents())
            {
                var tracker = new EventTracker(typeof(UIElement), routedEvent);
                tracker.EventHandled += this.HandleEventHandled;
                sorter.Add(tracker);

                if (defaultEvents.Contains(routedEvent))
                {
                    tracker.IsEnabled = true;
                }
            }

            sorter.Sort();
            foreach (var tracker in sorter)
            {
                this.trackers.Add(tracker);
            }

            this.CommandBindings.Add(new CommandBinding(ClearCommand, this.HandleClear));
            this.CommandBindings.Add(new CommandBinding(ClearCommand, this.HandleCopySelectedEventsToClipboard));
        }

        #region Collection: InterestingEvents (System.IEnumerable)
        public IEnumerable InterestingEvents
        {
            get { return this.interestingEvents; }
        }

        private readonly ObservableCollection<TrackedEventContainer> interestingEvents = new ObservableCollection<TrackedEventContainer>();
        #endregion

        #region Property: MaxEventsDisplayed (System.Int32)
        public int MaxEventsDisplayed
        {
            get { return this.maxEventsDisplayed; }

            set
            {
                if (value < 0)
                {
                    value = 0;
                }

                this.maxEventsDisplayed = value;
                this.OnPropertyChanged(nameof(this.MaxEventsDisplayed));

                if (this.maxEventsDisplayed == 0)
                {
                    this.interestingEvents.Clear();
                }
                else
                {
                    this.EnforceInterestingEventsLimit();
                }
            }
        }

        private int maxEventsDisplayed = 100;
        #endregion

        public object AvailableEvents
        {
            get
            {
                var pgd = new PropertyGroupDescription
                {
                    PropertyName = nameof(EventTracker.Category),
                    StringComparison = StringComparison.OrdinalIgnoreCase
                };

                var cvs = new CollectionViewSource();
                cvs.SortDescriptions.Add(new SortDescription(nameof(EventTracker.Category), ListSortDirection.Ascending));
                cvs.SortDescriptions.Add(new SortDescription(nameof(EventTracker.Name), ListSortDirection.Ascending));
                cvs.GroupDescriptions.Add(pgd);

                cvs.Source = this.trackers;

                cvs.View.Refresh();
                return cvs.View;
            }
        }

        private void EnforceInterestingEventsLimit()
        {
            while (this.interestingEvents.Count > this.maxEventsDisplayed)
            {
                this.interestingEvents.RemoveAt(0);
            }
        }

        private void HandleEventHandled(TrackedEvent trackedEvent)
        {
            var visual = trackedEvent.Originator.Handler as Visual;
            if (visual != null && !visual.IsPartOfSnoopVisualTree())
            {
                Action action =
                    () =>
                    {
                        this.interestingEvents.Add(trackedEvent);
                        this.EnforceInterestingEventsLimit();

                        var tvi = (TreeViewItem)this.EventTree.ItemContainerGenerator.ContainerFromItem(trackedEvent);
                        tvi?.BringIntoView();
                    };

                if (this.Dispatcher.CheckAccess())
                {
                    action.Invoke();
                }
                else
                {
                    this.RunInDispatcherAsync(action);
                }
            }
        }

        private void HandleCopySelectedEventsToClipboard(object sender, ExecutedRoutedEventArgs e)
        {
            var builder = new StringBuilder();

            foreach (var container in this.interestingEvents)
            {
                if (!container.IsSelected)
                {
                    continue;
                }

                var eventsArgType = container.Event.EventArgs.GetType();
                var handledByType = container.Event.HandledBy.GetType();
                int spaceIndex = 0;

                builder.AppendLine($"{new string(' ', spaceIndex * 4)}{eventsArgType.FullName} handled by {handledByType.FullName} ({container.Event.Handled}):");

                {
                    spaceIndex++;
                    builder.AppendLine($"{new string(' ', spaceIndex * 4)}Handled By:");

                    {
                        spaceIndex++;
                        builder.AppendLine(ObjectToStringConverter.Instance.Convert(container.Event.HandledBy).Replace("\n", $"\n{new string(' ', spaceIndex * 4)}"));
                        spaceIndex--;
                    }

                    builder.AppendLine($"{new string(' ', spaceIndex * 4)}Args:");
                    {
                        spaceIndex++;
                        builder.AppendLine(ObjectToStringConverter.Instance.Convert(container.Event.EventArgs).Replace("\n", $"\n{new string(' ', spaceIndex * 4)}"));
                        spaceIndex--;
                    }

                    spaceIndex--;
                }
            }
        }

        private void HandleClear(object sender, ExecutedRoutedEventArgs e)
        {
            this.interestingEvents.Clear();
        }

        private void EventTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue != null)
            {
                if (e.NewValue is EventEntry entry)
                {
                    SnoopUI.InspectCommand.Execute(entry.Handler, this);
                }
                else if (e.NewValue is TrackedEvent @event)
                {
                    SnoopUI.InspectCommand.Execute(@event.EventArgs, this);
                }
            }
        }

        private readonly ObservableCollection<EventTracker> trackers = new ObservableCollection<EventTracker>();

        private static readonly List<RoutedEvent> defaultEvents =
            new List<RoutedEvent>(
                new RoutedEvent[]
                {
                    Keyboard.KeyDownEvent,
                    Keyboard.KeyUpEvent,
                    TextCompositionManager.TextInputEvent,
                    Mouse.MouseDownEvent,
                    Mouse.PreviewMouseDownEvent,
                    Mouse.MouseUpEvent,
                    CommandManager.ExecutedEvent,
                });

        #region INotifyPropertyChanged Members
        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected void OnPropertyChanged(string propertyName)
        {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion
    }
}

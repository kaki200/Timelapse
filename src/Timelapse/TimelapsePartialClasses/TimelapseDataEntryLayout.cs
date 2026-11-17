using System;
using System.Windows;
using System.Windows.Controls;

namespace Timelapse
{
    public partial class TimelapseWindow
    {
        public bool IsDataEntryPanelDockedRight { get; private set; }

        public double DataEntryPanelWidth => DataEntryRightColumn.ActualWidth;

        // The two AvalonDock managers own different content. Position their hosts rather
        // than moving controls between managers (which loses their saved content IDs).
        public void SetDataEntryPanelDockedRight(bool dockRight, double width = 320)
        {
            IsDataEntryPanelDockedRight = dockRight;
            Grid.SetColumn(DockingManager1, dockRight ? 2 : 0);
            Grid.SetColumnSpan(DockingManager1, dockRight ? 1 : 3);
            Grid.SetRowSpan(DockingManager1, dockRight ? 3 : 1);
            Grid.SetColumnSpan(DockingManager2, dockRight ? 1 : 3);
            Grid.SetColumnSpan(GridFileNavigator, dockRight ? 1 : 3);
            DataEntryRightColumn.MinWidth = dockRight ? 240 : 0;
            DataEntryRightColumn.Width = new GridLength(dockRight ? Math.Clamp(double.IsFinite(width) ? width : 320, 240, 800) : 0);
            DataEntrySplitterColumn.Width = new GridLength(dockRight ? 5 : 0);
            DataEntryRightSplitter.Visibility = dockRight ? Visibility.Visible : Visibility.Collapsed;
            DataEntryScrollViewer.HorizontalAlignment = dockRight ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
            DataEntryScrollViewer.VerticalAlignment = dockRight ? VerticalAlignment.Stretch : VerticalAlignment.Top;
            DataEntryControls.SetVerticalLayout(dockRight);
            MenuItemWindowDataEntryOnRight.IsChecked = dockRight;
        }
    }
}

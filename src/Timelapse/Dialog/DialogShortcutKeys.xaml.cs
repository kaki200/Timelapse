using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Timelapse.DataTables;
using Timelapse.Util;

namespace Timelapse.Dialog
{
    public partial class DialogShortcutKeys : Window
    {
        private readonly TimelapseWindow parentWindow;

        public ObservableCollection<ShortcutItem> ShortcutItems { get; } = [];

        public class ShortcutItem : INotifyPropertyChanged
        {
            public string DataLabel { get; set; }
            public string Label { get; set; }

            private string shortcutKey;
            public string ShortcutKey
            {
                get => shortcutKey;
                set
                {
                    shortcutKey = value;
                    OnPropertyChanged(nameof(ShortcutKey));
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
            protected void OnPropertyChanged(string name)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }

        public DialogShortcutKeys(TimelapseWindow parent)
        {
            InitializeComponent();
            parentWindow = parent;
            Owner = parent;

            LoadFlags();
            ShortcutGrid.ItemsSource = ShortcutItems;
        }

        private void LoadFlags()
        {
            if (parentWindow.DataHandler?.FileDatabase?.Controls == null) return;

            var flagControls = parentWindow.DataHandler.FileDatabase.Controls
                .Where(c => c.Type == Timelapse.Constant.Control.Flag)
                .OrderBy(c => c.ControlOrder);

            var existingShortcuts = parentWindow.State.FlagShortcutKeys;

            foreach (ControlRow control in flagControls)
            {
                string assignedKey = string.Empty;
                if (existingShortcuts.TryGetValue(control.DataLabel, out string key))
                {
                    assignedKey = key;
                }

                ShortcutItems.Add(new ShortcutItem
                {
                    DataLabel = control.DataLabel,
                    Label = string.IsNullOrEmpty(control.Label) ? control.DataLabel : control.Label,
                    ShortcutKey = assignedKey
                });
            }
        }

        private void TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;

            if (e.Key == Key.Escape)
            {
                e.Handled = false;
                return;
            }

            if (sender is TextBox textBox)
            {
                if (e.Key == Key.Back || e.Key == Key.Delete)
                {
                    textBox.Text = string.Empty;
                }
                else
                {
                    if (!KeyboardShortcut.TryFormat(e, out string shortcut))
                    {
                        return;
                    }
                    textBox.Text = shortcut;
                }

                // Move focus to next row if possible, or just remove focus so the binding updates
                textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is ShortcutItem item)
            {
                item.ShortcutKey = string.Empty;
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            ShortcutItem[] assignedItems = ShortcutItems
                .Where(item => !string.IsNullOrWhiteSpace(item.ShortcutKey))
                .ToArray();
            IGrouping<string, ShortcutItem> duplicate = assignedItems
                .GroupBy(item => item.ShortcutKey, System.StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicate != null)
            {
                string labels = string.Join(", ", duplicate.Select(item => item.Label));
                MessageBox.Show(this,
                    $"The shortcut {duplicate.Key} is assigned more than once: {labels}",
                    "Duplicate shortcut",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            parentWindow.State.FlagShortcutKeys = assignedItems.ToDictionary(
                item => item.DataLabel,
                item => item.ShortcutKey);

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

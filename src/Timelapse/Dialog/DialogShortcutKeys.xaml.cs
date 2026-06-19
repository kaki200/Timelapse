using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Timelapse.Constant;
using Timelapse.DataTables;

namespace Timelapse.Dialog
{
    public partial class DialogShortcutKeys : Window
    {
        private TimelapseWindow parentWindow;

        public ObservableCollection<ShortcutItem> ShortcutItems { get; set; } = new ObservableCollection<ShortcutItem>();

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

            // Ignore modifier keys alone
            if (e.Key == Key.LeftShift || e.Key == Key.RightShift ||
                e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
                e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
                e.Key == Key.LWin || e.Key == Key.RWin)
            {
                return;
            }

            if (e.Key == Key.Escape)
            {
                // Let escape close the dialog or just ignore
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
                    Key actualKey = e.Key == Key.System ? e.SystemKey : e.Key;
                    string keyStr = "";
                    var modifiers = Keyboard.Modifiers;
                    if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control) keyStr += "Ctrl+";
                    if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift) keyStr += "Shift+";
                    if ((modifiers & ModifierKeys.Alt) == ModifierKeys.Alt) keyStr += "Alt+";
                    keyStr += actualKey.ToString();
                    textBox.Text = keyStr;
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
            // Save to registry settings
            var newShortcuts = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var item in ShortcutItems)
            {
                if (!string.IsNullOrWhiteSpace(item.ShortcutKey))
                {
                    newShortcuts[item.DataLabel] = item.ShortcutKey;
                }
            }

            parentWindow.State.FlagShortcutKeys = newShortcuts;
            
            // The settings will be saved to the registry when Timelapse closes,
            // but we can optionally force a write now if needed.
            // parentWindow.State.WriteSettingsToRegistry();

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

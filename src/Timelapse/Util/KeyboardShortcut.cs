using System.Collections.Generic;
using System.Windows.Input;

namespace Timelapse.Util
{
    internal static class KeyboardShortcut
    {
        public static bool TryFormat(KeyEventArgs keyEvent, out string shortcut)
        {
            Key key = keyEvent.Key == Key.System ? keyEvent.SystemKey : keyEvent.Key;
            if (IsModifierKey(key) || key == Key.None)
            {
                shortcut = string.Empty;
                return false;
            }

            shortcut = Format(key, Keyboard.Modifiers);
            return true;
        }

        private static string Format(Key key, ModifierKeys modifiers)
        {
            List<string> parts = [];
            if ((modifiers & ModifierKeys.Control) != 0)
            {
                parts.Add("Ctrl");
            }
            if ((modifiers & ModifierKeys.Shift) != 0)
            {
                parts.Add("Shift");
            }
            if ((modifiers & ModifierKeys.Alt) != 0)
            {
                parts.Add("Alt");
            }
            if ((modifiers & ModifierKeys.Windows) != 0)
            {
                parts.Add("Win");
            }
            parts.Add(key.ToString());
            return string.Join("+", parts);
        }

        private static bool IsModifierKey(Key key)
        {
            return key is Key.LeftShift or Key.RightShift or
                Key.LeftCtrl or Key.RightCtrl or
                Key.LeftAlt or Key.RightAlt or
                Key.LWin or Key.RWin;
        }
    }
}

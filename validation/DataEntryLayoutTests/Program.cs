using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Timelapse.ControlsDataEntry;
using Timelapse.DataTables;
using Field = Timelapse.Constant.Control;

internal static class Program
{
    private static int checks;

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        checks++;
        Console.WriteLine("PASS: " + description);
    }

    private static ControlRow Row(string dataLabel, string label, string type = "Flag", int width = 20)
    {
        DataTable table = new();
        foreach (string column in new[] { Field.DataLabel, Field.Label, Field.Tooltip, Field.Type, Field.DefaultValue, Field.List })
            table.Columns.Add(column, typeof(string));
        table.Columns.Add(Field.Copyable, typeof(bool));
        table.Columns.Add(Field.TextBoxWidth, typeof(int));
        DataRow row = table.NewRow();
        row[Field.DataLabel] = dataLabel;
        row[Field.Label] = label;
        row[Field.Tooltip] = label;
        row[Field.Type] = type;
        row[Field.DefaultValue] = "false";
        row[Field.List] = "{\"ChoiceListNonEmpty\":[\"A\",\"B\"],\"IncludeEmptyChoice\":false}";
        row[Field.Copyable] = true;
        row[Field.TextBoxWidth] = width;
        return new ControlRow(row);
    }

    [STAThread]
    private static int Main()
    {
        try
        {
            Application application = new();
            DataEntryControls panel = new();
            List<DataEntryFlag> flags = [];
            for (int i = 0; i < 20; i++)
            {
                string label = i == 2 ? "Long label with enough words to wrap within a narrow sidebar, retaining its shortcut (K)" : $"Flag {i} (K)";
                DataEntryFlag flag = new(Row("Flag" + i, label), panel);
                flag.SetContentAndTooltip(i % 2 == 0 ? "true" : "false");
                flags.Add(flag);
                panel.Controls.Add(flag);
            }
            DataEntryMultiChoice multi = new(Row("Multi", "Multi choice", "MultiChoice", 200), panel);
            multi.SetContentAndTooltip("A,B");
            panel.Controls.Add(multi);
            ScrollViewer scroll = new() { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Grid root = new() { Width = 320, Height = 300 };
            root.Children.Add(scroll);
            void Layout()
            {
                root.Measure(new Size(root.Width, root.Height));
                root.Arrange(new Rect(0, 0, root.Width, root.Height));
                root.UpdateLayout();
            }

            panel.SetVerticalLayout(true);
            Layout();
            Point[] locations = flags.Select(flag => flag.Container.TranslatePoint(new Point(), panel)).ToArray();
            Check(locations.Zip(locations.Skip(1)).All(pair => pair.First.Y < pair.Second.Y && Math.Abs(pair.First.X - pair.Second.X) < .1), "all flags occupy distinct vertically aligned rows");
            Check(scroll.ScrollableHeight > 0 && scroll.ExtentHeight > scroll.ViewportHeight, "long lists can scroll inside the available window height");
            Check(flags.All(flag => flag.Container.ActualWidth <= panel.ActualWidth), "flag rows fit the sidebar width");
            Check(flags[2].LabelControl.ActualHeight > flags[0].LabelControl.ActualHeight, "long labels and shortcut text wrap instead of clipping");
            Check(((SolidColorBrush)flags[0].Container.Background).Color != ((SolidColorBrush)flags[1].Container.Background).Color, "adjacent flag fields use distinct colors");
            Check(flags.All(flag => flag.Container.Children[0] == flag.ContentControl), "checkboxes align on the left of the vertical rows");
            for (int i = 0; i < 10; i++)
            {
                panel.SetVerticalLayout(false);
                Layout();
                Check(flags.All(flag => flag.Container.Children[0] == flag.LabelControl), "horizontal layout restores label-first controls");
                panel.SetVerticalLayout(true);
                Layout();
            }
            Check(flags.Select((flag, i) => flag.Content == (i % 2 == 0 ? "true" : "false")).All(value => value), "repeated layout switching preserves every checkbox value");
            Check(multi.Content == "A,B", "reparenting preserves multi-choice values without serialization errors");
            root.Width = 250;
            Layout();
            Check(flags.All(flag => flag.Container.ActualWidth <= panel.ActualWidth), "narrow sidebar resizing remains bounded");
            panel.Reset();
            panel.SetVerticalLayout(false);
            Check(panel.Controls.Count == 0, "closing/resetting an image set clears the vertical list safely");
            Console.WriteLine($"PASS: {checks} data-entry layout checks");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
}

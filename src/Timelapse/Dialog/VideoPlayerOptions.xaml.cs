using System.Windows;
using System.Windows.Controls;
using Timelapse.State;

namespace Timelapse.Dialog
{
    public partial class VideoPlayerOptions
    {
        private readonly TimelapseState state;

        public VideoPlayerOptions(TimelapseState state, Window owner)
        {
            InitializeComponent();
            this.state = state;
            this.Owner = owner;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            this.FastForwardSlider.Value = this.state.VideoFastForwardSpeed;
            this.RewindSlider.Value = this.state.VideoRewindSpeed;
            this.MotionBboxEnabledCheckBox.IsChecked = this.state.VideoMotionBboxEnabled;
            SelectByTag(this.MotionAlgorithmComboBox, this.state.VideoMotionBboxAlgorithm);
            SelectByTag(this.MotionSensitivityComboBox, this.state.VideoMotionBboxDifferenceThreshold.ToString());
            this.MotionPaddingSlider.Value = this.state.VideoMotionBboxPadding;
            SelectByTag(this.BottomBandModeComboBox, this.state.VideoMotionBboxBottomBandMode);
            this.ManualBottomBandSlider.Value = this.state.VideoMotionBboxManualBottomBandPixels;
            this.UpdateSpeedText();
            this.UpdateMotionText();
        }

        private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            this.UpdateSpeedText();
        }

        private void UpdateSpeedText()
        {
            if (this.FastForwardText == null || this.RewindText == null)
            {
                return;
            }

            this.FastForwardText.Text = $"{this.FastForwardSlider.Value:F0}x";
            this.RewindText.Text = $"{this.RewindSlider.Value:F0}x";
        }

        private void MotionPaddingSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            this.UpdateMotionText();
        }

        private void ManualBottomBandSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            this.UpdateMotionText();
        }

        private void BottomBandModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            this.UpdateMotionText();
        }

        private void UpdateMotionText()
        {
            if (this.MotionPaddingText == null || this.ManualBottomBandText == null || this.ManualBottomBandSlider == null)
            {
                return;
            }

            this.MotionPaddingText.Text = $"{this.MotionPaddingSlider.Value:F2}x";
            this.ManualBottomBandText.Text = $"{this.ManualBottomBandSlider.Value:F0}px";
            this.ManualBottomBandSlider.IsEnabled = GetSelectedTag(this.BottomBandModeComboBox) == "Manual";
        }

        private static void SelectByTag(ComboBox comboBox, string tag)
        {
            foreach (object item in comboBox.Items)
            {
                if (item is ComboBoxItem comboBoxItem && comboBoxItem.Tag?.ToString() == tag)
                {
                    comboBox.SelectedItem = comboBoxItem;
                    return;
                }
            }
            comboBox.SelectedIndex = 0;
        }

        private static string GetSelectedTag(ComboBox comboBox)
        {
            return (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            this.state.VideoFastForwardSpeed = this.FastForwardSlider.Value;
            this.state.VideoRewindSpeed = this.RewindSlider.Value;
            this.state.VideoMotionBboxEnabled = this.MotionBboxEnabledCheckBox.IsChecked == true;
            this.state.VideoMotionBboxAlgorithm = GetSelectedTag(this.MotionAlgorithmComboBox);
            this.state.VideoMotionBboxDifferenceThreshold = int.TryParse(GetSelectedTag(this.MotionSensitivityComboBox), out int threshold) ? threshold : 18;
            this.state.VideoMotionBboxPadding = this.MotionPaddingSlider.Value;
            this.state.VideoMotionBboxBottomBandMode = GetSelectedTag(this.BottomBandModeComboBox);
            this.state.VideoMotionBboxManualBottomBandPixels = (int)this.ManualBottomBandSlider.Value;
            this.DialogResult = true;
        }
    }
}

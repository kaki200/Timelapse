using System.Windows;
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
            this.UpdateSpeedText();
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

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            this.state.VideoFastForwardSpeed = this.FastForwardSlider.Value;
            this.state.VideoRewindSpeed = this.RewindSlider.Value;
            this.DialogResult = true;
        }
    }
}

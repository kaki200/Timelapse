using System.Windows;
using Timelapse.State;

namespace Timelapse.Dialog
{
    public partial class VideoPlayerOptions : Window
    {
        private readonly TimelapseState state;

        public VideoPlayerOptions(TimelapseState state, Window owner)
        {
            InitializeComponent();
            Owner = owner;
            this.state = state;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Timelapse.Util.FormattedDialogHelper.SetupStaticReferenceResolver(Message);
            this.Message.BuildContentFromProperties();
            Dialogs.TryPositionAndFitDialogIntoWindow(this);

            FastForwardSlider.Value = state.VideoFastForwardSpeed;
            RewindSlider.Value = state.VideoRewindSpeed;

            FastForwardSlider.ValueChanged += (s, ev) => { state.VideoFastForwardSpeed = FastForwardSlider.Value; UpdateText(); };
            RewindSlider.ValueChanged += (s, ev) => { state.VideoRewindSpeed = RewindSlider.Value; UpdateText(); };

            UpdateText();
        }

        private void UpdateText()
        {
            FastForwardText.Text = $"{state.VideoFastForwardSpeed:F1}x";
            RewindText.Text = $"{state.VideoRewindSpeed:F1}x";
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}

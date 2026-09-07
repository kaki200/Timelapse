using Microsoft.WindowsAPICodePack.Shell;
using Microsoft.WindowsAPICodePack.Shell.PropertySystem;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Timelapse.Constant;
using Timelapse.DataStructures;
using Timelapse.Images;
using Timelapse.Util;
using File = System.IO.File;
using RadioButton = System.Windows.Controls.RadioButton;

namespace Timelapse.Controls
{
    public partial class VideoPlayer
    {
        #region Public properties

        /// <summary>
        /// True if the video is unscaled (or null), false if it is zoomed in
        /// </summary>        
        public bool IsUnScaled => Math.Abs(this.videoZoom - 1.0) < 0.0001;

        /// <summary>The WPF element representing the displayed video.</summary>
        public FrameworkElement VideoSurface => this.FlyleafHostActive;

        /// <summary>True when a video source is assigned.</summary>
        public bool HasSource => this.sourceUri != null;

        /// <summary>
        /// True while this control represents the current file, including the short period
        /// while Flyleaf is still opening it.
        /// </summary>
        public bool IsCurrentVideo => this.Visibility == Visibility.Visible && this.HasSource;

        /// <summary>
        /// Raised for wheel input received by Flyleaf's separate overlay window.
        /// </summary>
        public event MouseWheelEventHandler VideoMouseWheel;

        /// <summary>Input events received by Flyleaf's separate video windows.</summary>
        public event MouseButtonEventHandler VideoPreviewMouseLeftButtonDown;
        public event MouseButtonEventHandler VideoMouseLeftButtonDown;
        public event MouseButtonEventHandler VideoMouseLeftButtonUp;
        public event MouseEventHandler VideoMouseMove;
        public event MouseEventHandler VideoMouseLeave;
        public event EventHandler VideoPanStarted;

        /// <summary>
        ///  We do VideoPosition as a property so we can manipulate and/or check things whenever the video position is changed.
        /// </summary>
        internal TimeSpan CurrentPosition
        {
            get => TimeSpan.FromTicks(this.player.CurTime);
            set => this.player.CurTime = value.Ticks;
        }

        #endregion

        #region Private variables
        // Pointers to the video itself
        private readonly FlyleafLib.MediaPlayer.Player player;
        private long FileIndex; // A pointer to the ImageRow
        private Uri sourceUri; // The currently loaded video
        private bool isSourceReady; // True only after Flyleaf has opened sourceUri
        private Window inputOverlay;
        private Window inputSurface;

        // Bounding boxes and variables, so we can track where we are and what we need to show
        public float? FrameRate { get; set; } = -1;
        public int FrameToShow { get; set; }
        private double VideoDurationSeconds = -1;
        private TimeSpan VideoDurationTimeSpan;
        private BoundingBoxes BoxesForFile; // The bounding boxes for the current video
        private bool isProgrammaticUpdate; // To control callback execution

        // Tracks whether the player was previously loaded
        private bool IsVideoPlayerLoaded;

        // Timers
        private readonly DispatcherTimer TimerUpdatePosition;
        private readonly DispatcherTimer TimerKeyboardSeek;

        // Holding Up/Down performs a smooth seek while preserving whether playback was
        // running before the key was pressed.
        private bool isKeyboardSeeking;
        private int keyboardSeekDirection;
        private double keyboardSeekSpeed;
        private long keyboardSeekTargetTicks;
        private bool resumeAfterKeyboardSeek;
        private DateTime lastKeyboardSeekUpdate;

        // There is a bug in the media player, where pausing and playing it doesn't respect the speed ratio
        // The workaround is to save the ratio here, then reset the ratio to 1 before playing, then reset
        // the ratio back to the saved one whenever the video position changes.
        double SavedSpeedRatio = 1;

        // Flyleaf renders zoom and pan on the GPU. These values mirror that state for
        // Timelapse's zoom limits and its WPF bounding-box overlay.
        private double videoZoom = 1.0;
        private double videoOffsetX;
        private double videoOffsetY;
        private readonly ScaleTransform videoOverlayScale = new();
        private readonly TranslateTransform videoOverlayTranslation = new();
        private bool isDirectVideoDragActive;
        private bool wasVideoPanned;
        private Point videoDragStartScreenPosition;
        private double videoDragStartPanX;
        private double videoDragStartPanY;
        private double videoDragStartOffsetX;
        private double videoDragStartOffsetY;
        #endregion

        #region Constructor, Loading, Unloading

        public VideoPlayer()
        {
            InitializeComponent();
            // Timelapse owns all input behavior. Flyleaf's defaults conflict with Timelapse's
            // wheel zoom, drag pan, click-to-toggle, and application keyboard shortcuts.
            this.FlyleafHostActive.MouseBindings = FlyleafLib.Controls.WPF.AvailableWindows.None;
            this.FlyleafHostActive.KeyBindings = FlyleafLib.Controls.WPF.AvailableWindows.None;
            this.FlyleafHostActive.PanZoomOnCtrlWheel = FlyleafLib.Controls.WPF.AvailableWindows.None;
            this.FlyleafHostActive.PanMoveOnCtrl = FlyleafLib.Controls.WPF.AvailableWindows.None;
            this.FlyleafHostActive.PanRotateOnShiftWheel = FlyleafLib.Controls.WPF.AvailableWindows.None;
            this.FlyleafHostActive.ToggleFullScreenOnDoubleClick = FlyleafLib.Controls.WPF.AvailableWindows.None;
            this.FlyleafHostActive.BringToFrontOnClick = false;
            this.FlyleafHostActive.OverlayCreated += FlyleafHost_OverlayCreated;
            this.FlyleafHostActive.SurfaceCreated += FlyleafHost_SurfaceCreated;
            TransformGroup overlayTransform = new();
            overlayTransform.Children.Add(this.videoOverlayScale);
            overlayTransform.Children.Add(this.videoOverlayTranslation);
            this.VideoOverlayCanvas.RenderTransform = overlayTransform;
            this.AttachFlyleafInputHandlers();

            this.player = new FlyleafLib.MediaPlayer.Player();
            this.FlyleafHostActive.Player = this.player;
            this.player.OpenCompleted += Player_OpenCompleted;
            this.player.PlaybackStopped += Player_PlaybackStopped;

            this.isProgrammaticUpdate = false;

            // Initialize the timer that updates the UI to reflect the current video position 
            this.TimerUpdatePosition = new() { Interval = TimeSpan.FromMilliseconds(125.0) };
            this.TimerKeyboardSeek = new() { Interval = TimeSpan.FromMilliseconds(33.0) };
            this.TimerKeyboardSeek.Tick += TimerKeyboardSeek_Tick;

            // Start with the video controls disabled
            this.IsEnabled = false;

        }

        private void VideoPlayer_Loaded(object sender, RoutedEventArgs e)
        {
            if (false == GlobalReferences.MainWindow.IsFileDatabaseAvailable() || this.IsVideoPlayerLoaded)
            {
                // - No need to load things until a file database is actually available.
                // - For an unknown reason, this may be re-invoked twice. IsVideoPlayerLoaded ensures we ignore the 2nd invocation.
                return;
            }
            this.IsVideoPlayerLoaded = true;
            this.AttachFlyleafInputHandlers();

            // Initialize Timer callbacks
            this.TimerUpdatePosition.Tick += TimerUpdatePosition_Tick;

            // Set various controls to their previously saved state.
            this.CBAutoPlay.IsChecked = GlobalReferences.TimelapseState.VideoAutoPlay;
            this.CBRepeat.IsChecked = GlobalReferences.TimelapseState.VideoRepeat;
            this.CBMute.IsChecked = GlobalReferences.TimelapseState.VideoMute;
            this.CBMute_CheckedChanged(null, null); // and set the mute state


            if (GlobalReferences.TimelapseState.VideoSpeed == 2)
            {
                this.RBNormal.IsChecked = true;
                this.player.Speed = 1;
            }
            else if (GlobalReferences.TimelapseState.VideoSpeed == 1)
            {
                this.RBSlow.IsChecked = true;
                this.player.Speed = 0.5;
            }
            else if (GlobalReferences.TimelapseState.VideoSpeed == 3)// if (GlobalReferences.TimelapseState.VideoSpeed == 3)
            {
                this.RBFast.IsChecked = true;
                this.player.Speed = 2;
            }
            else if (GlobalReferences.TimelapseState.VideoSpeed == 4)
            {
                this.RBVeryFast.IsChecked = true;
                this.player.Speed = 6;
            }
            else // set to normal speed
            {
                this.RBNormal.IsChecked = true;
                this.player.Speed = 1;
            }
            // Save the current speed
            this.SavedSpeedRatio = this.player.Speed;

            this.SliderScrubbing.ValueChanged += SliderScrubbing_ValueChanged;
            this.SliderScrubbing.PreviewMouseDown += SliderScrubbing_PreviewMouseDown;

            this.PlayOrPause.Click += PlayOrPause_Click;

            this.RBSlow.Checked += SetSpeed_CheckedChanged;
            this.RBNormal.Checked += SetSpeed_CheckedChanged;
            this.RBFast.Checked += SetSpeed_CheckedChanged;
            this.RBVeryFast.Checked += SetSpeed_CheckedChanged;

            this.CBAutoPlay.Checked += CBAutoPlay_CheckChanged;
            this.CBAutoPlay.Unchecked += CBAutoPlay_CheckChanged;

            this.OpenExternalPlayer.Click += OpenExternalPlayer_Click;
            this.CBMute.Checked += CBMute_CheckedChanged;
            this.CBMute.Unchecked += CBMute_CheckedChanged;

            this.CBRepeat.Checked += CBRepeat_CheckChanged;
            this.CBRepeat.Unchecked += CBRepeat_CheckChanged;
        }

        // Set the flag to indicate that the video player is no longer loaded.
        private void VideoPlayer_Unloaded(object sender, RoutedEventArgs e)
        {
            this.CancelKeyboardSeek();
            this.TimerUpdatePosition.Stop();
            this.player.Pause();
        }

        public void Shutdown()
        {
            this.CancelKeyboardSeek();
            this.TimerUpdatePosition.Stop();
            this.TimerKeyboardSeek.Tick -= TimerKeyboardSeek_Tick;
            this.FlyleafHostActive.OverlayCreated -= FlyleafHost_OverlayCreated;
            this.FlyleafHostActive.SurfaceCreated -= FlyleafHost_SurfaceCreated;
            this.SetFlyleafInputHandlers(ref this.inputOverlay, null);
            this.SetFlyleafInputHandlers(ref this.inputSurface, null);
            this.player.OpenCompleted -= Player_OpenCompleted;
            this.player.PlaybackStopped -= Player_PlaybackStopped;
            this.player.Dispose();
            this.FlyleafHostActive.Dispose();
        }

        private void FlyleafHost_OverlayCreated(object sender, EventArgs e)
        {
            this.SetFlyleafInputHandlers(ref this.inputOverlay, this.FlyleafHostActive.Overlay);
        }

        private void FlyleafHost_SurfaceCreated(object sender, EventArgs e)
        {
            this.SetFlyleafInputHandlers(ref this.inputSurface, this.FlyleafHostActive.Surface);
        }

        private void AttachFlyleafInputHandlers()
        {
            this.SetFlyleafInputHandlers(ref this.inputOverlay, this.FlyleafHostActive.Overlay);
            this.SetFlyleafInputHandlers(ref this.inputSurface, this.FlyleafHostActive.Surface);
        }

        private void SetFlyleafInputHandlers(ref Window hookedWindow, Window target)
        {
            if (ReferenceEquals(hookedWindow, target))
            {
                return;
            }

            if (hookedWindow != null)
            {
                hookedWindow.RemoveHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler(FlyleafWindow_PreviewMouseWheel));
                hookedWindow.RemoveHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(FlyleafWindow_PreviewMouseDown));
                hookedWindow.RemoveHandler(Mouse.PreviewMouseMoveEvent, new MouseEventHandler(FlyleafWindow_PreviewMouseMove));
                hookedWindow.RemoveHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(FlyleafWindow_PreviewMouseUp));
                hookedWindow.RemoveHandler(Mouse.MouseLeaveEvent, new MouseEventHandler(FlyleafWindow_MouseLeave));
            }

            hookedWindow = target;
            if (hookedWindow != null)
            {
                hookedWindow.AddHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler(FlyleafWindow_PreviewMouseWheel), true);
                hookedWindow.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(FlyleafWindow_PreviewMouseDown), true);
                hookedWindow.AddHandler(Mouse.PreviewMouseMoveEvent, new MouseEventHandler(FlyleafWindow_PreviewMouseMove), true);
                hookedWindow.AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(FlyleafWindow_PreviewMouseUp), true);
                hookedWindow.AddHandler(Mouse.MouseLeaveEvent, new MouseEventHandler(FlyleafWindow_MouseLeave), true);
            }
        }

        private void FlyleafWindow_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            this.VideoMouseWheel?.Invoke(this, e);
        }

        private void FlyleafWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            this.isDirectVideoDragActive = true;
            this.wasVideoPanned = false;
            this.videoDragStartScreenPosition = NativeMethods.GetCursorScreenPosition();
            this.videoDragStartPanX = this.player.Config.Video.PanXOffset;
            this.videoDragStartPanY = this.player.Config.Video.PanYOffset;
            this.videoDragStartOffsetX = this.videoOffsetX;
            this.videoDragStartOffsetY = this.videoOffsetY;
            this.VideoCanvas.Focus();
            this.VideoPreviewMouseLeftButtonDown?.Invoke(this, e);
            if (!e.Handled)
            {
                this.VideoMouseLeftButtonDown?.Invoke(this, e);
            }

            // Flyleaf's surface and overlay are separate windows. Capturing the window that
            // received the press keeps move/up events flowing while a drag crosses their edge.
            if (sender is IInputElement inputElement)
            {
                Mouse.Capture(inputElement, CaptureMode.SubTree);
            }
        }

        private void FlyleafWindow_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (this.isDirectVideoDragActive && e.LeftButton == MouseButtonState.Pressed && !this.IsUnScaled)
            {
                Point currentScreenPosition = NativeMethods.GetCursorScreenPosition();
                Vector movement = currentScreenPosition - this.videoDragStartScreenPosition;
                double dragThreshold = Timelapse.Constant.MarkableCanvas.MarkingVsPanningDistanceThreshold *
                                       Math.Max(this.FlyleafHostActive.DpiX, this.FlyleafHostActive.DpiY);
                if (!this.wasVideoPanned && movement.Length > dragThreshold)
                {
                    this.wasVideoPanned = true;
                    this.VideoPanStarted?.Invoke(this, EventArgs.Empty);
                }

                if (this.wasVideoPanned)
                {
                    this.ApplyDirectVideoPan(currentScreenPosition);
                    if (sender is Window window)
                    {
                        window.Cursor = Cursors.ScrollAll;
                    }
                    e.Handled = true;
                    return;
                }
            }

            this.VideoMouseMove?.Invoke(this, e);
        }

        private void FlyleafWindow_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            if (this.wasVideoPanned)
            {
                this.ApplyDirectVideoPan(NativeMethods.GetCursorScreenPosition());
            }
            this.VideoMouseLeftButtonUp?.Invoke(this, e);
            this.isDirectVideoDragActive = false;
            this.wasVideoPanned = false;
            if (sender is Window window)
            {
                window.Cursor = Cursors.Arrow;
            }
            if (ReferenceEquals(Mouse.Captured, sender))
            {
                Mouse.Capture(null);
            }
        }

        private void FlyleafWindow_MouseLeave(object sender, MouseEventArgs e)
        {
            this.VideoMouseLeave?.Invoke(this, e);
        }
        #endregion

        #region SetSource
        /// <summary>
        /// Set the Source Video provided in the URI.
        /// Doing so invokes the MediaOpened callback, which actually initializes everything
        /// </summary>

        public void SetSource(Uri source, long fileIndex)
        {
            this.CancelKeyboardSeek();
            this.TimerUpdatePosition.Stop();
            this.isSourceReady = false;

            // OpenAsync does not immediately clear CanPlay. Without explicitly closing the
            // previous input, a key pressed while the next video is opening can seek the old
            // decoder. This is especially visible when images and videos alternate.
            this.StopPlayerInput();

            this.isDirectVideoDragActive = false;
            this.wasVideoPanned = false;
            this.ClearBoundingBoxes();
            this.FileIndex = fileIndex;
            this.sourceUri = source;

            // Get bounding boxes for this video (if any), and enable the  best frame button as needed
            this.BoxesForFile = null == GlobalReferences.MainWindow?.DataHandler?.FileDatabase?.FileTable
                ? null
                : GlobalReferences.MainWindow?.GetBoundingBoxesForCurrentFile(GlobalReferences.MainWindow.DataHandler.FileDatabase.FileTable[(int)fileIndex].ID);
            this.ButtonEnableBestFrameIfNeeded();

            this.videoZoom = 1.0;
            this.videoOffsetX = 0;
            this.videoOffsetY = 0;
            this.UpdateVideoLayout();

            // Reset Flyleaf zoom/pan state before loading the new video
            if (this.player.Config?.Video != null)
            {
                this.player.Config.Video.Zoom = 100;
                this.player.Config.Video.PanXOffset = 0;
                this.player.Config.Video.PanYOffset = 0;
            }

            if (source != null)
            {
                this.player.OpenAsync(source.LocalPath);
            }
        }

        /// <summary>
        /// Release the video belonging to the previous file when a new still image is shown.
        /// Merely collapsing the Flyleaf host leaves its decoder and CanPlay state alive.
        /// </summary>
        public void ClearSource()
        {
            this.CancelKeyboardSeek();
            this.TimerUpdatePosition.Stop();
            this.sourceUri = null;
            this.isSourceReady = false;
            this.IsEnabled = false;
            this.PlayOrPause.IsChecked = false;
            this.StopPlayerInput();
        }

        private void StopPlayerInput()
        {
            try
            {
                this.player.Stop();
            }
            catch (Exception)
            {
                // Flyleaf can already be between inputs when rapid file navigation cancels an
                // OpenAsync. The next OpenAsync still starts with isSourceReady cleared.
            }
        }
        #endregion

        #region Flyleaf opened / ended
        private void Player_OpenCompleted(object sender, FlyleafLib.MediaPlayer.OpenCompletedArgs e)
        {
            if (e.Error != null)
            {
                this.isSourceReady = false;
                return;
            }
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() => Video_MediaOpened(sender, null));
        }

        // When the video is first opened, set everything to conform to this video and play it if Auto play is on
        private void Video_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (this.sourceUri == null)
            {
                this.isSourceReady = false;
                this.IsEnabled = false;
                return;
            }

            this.isSourceReady = true;
            this.IsEnabled = true;

            // Determine and set the FrameRate, FrameToShow, VideoDuraton and Slider Scrubbing Duration values
            this.DoSetFrameRateAndFrameToShowValues();

            this.DoSetDurationValues();
            this.DoSetSliderAttributesForDuration();

            // We start at the beginning of the video
            TimeSpan startTime = TimeSpan.Zero;
            this.FrameToShow = 0;

            // Set the video, slider and feedback text to the desired start time.
            this.isProgrammaticUpdate = true;
            this.SliderScrubbing.Value = startTime.TotalSeconds; // This will be set in ShowPosition so no need to do it here.
            this.CurrentPosition = startTime;
            this.isProgrammaticUpdate = false;

            // Update the slider as needed
            this.DoSetSliderText();

            // Update the bounding boxes (if any) to display at this time interval  (i.e., 0)
            this.UpdateBoundingBoxes();

            // This is needed to prime the video
            this.Play();

            // Pause the video depending on conditions
            if (Visibility != Visibility.Visible || CBAutoPlay.IsChecked == false)
            {
                this.Pause();
            }
        }

        // When the video finishes playing, pause it and automatically return it to the beginning
        // Repeat playing if Auto play is on
        private void Player_PlaybackStopped(object sender, FlyleafLib.MediaPlayer.PlaybackStoppedArgs e)
        {
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (this.player.Duration > 0)
                {
                    // Check if it reached the end (within 0.5 sec)
                    if (this.player.CurTime >= this.player.Duration - TimeSpan.FromMilliseconds(500).Ticks)
                    {
                        this.Pause();

                        SliderScrubbing.Value = SliderScrubbing.Minimum; // Go back to the beginning
                        if (CBRepeat.IsChecked == true)
                        {
                            this.Play();
                        }
                    }
                }
            });
        }

        #endregion

        #region Play, TryPlayOrPause, Pause - Public ones invoked by MarkableCanvas
        private void Play()
        {
            try
            {
                this.CancelKeyboardSeek();
                PlayOrPause.IsChecked = true;
                // start over from beginning if at end of video
                // Technote: The natural duration default value is Automatic if you query this property before MediaOpened. So we just reset the position if its a new video.
                if (this.CurrentPosition >= this.VideoDurationTimeSpan)
                {
                    this.CurrentPosition = TimeSpan.Zero;
                    ShowPosition();
                }

                this.player.Speed = this.SavedSpeedRatio;
                this.player.Play();
                TimerUpdatePosition.Start();
            }
            catch (Exception)
            {
                // A user reported a rare crash in the above
                PlayOrPause.IsChecked = false;
                TimerUpdatePosition.Stop();
            }
        }

        // Pause the video.
        // Public as the markable canvas pauses the video when switching to Image or ThumbnailGrid views
        public void Pause()
        {
            this.CancelKeyboardSeek();
            TimerUpdatePosition.Stop();
            this.player.Pause();
            this.PlayOrPause.IsChecked = false;
            if (this.isProgrammaticUpdate == false)
            {
                this.ShowPosition();
            }
        }

        // A programmatic equivalent to clicking the play/pause button
        public bool TryTogglePlayOrPause()
        {
            if (this.Visibility != Visibility.Visible)
            {
                // Don't bother if the video player isn't being displayed
                return false;
            }

            // WPF doesn't offer a way to fire a toggle button's click event programatically (ToggleButtonAutomationPeer -> IToggleProvider -> Toggle()
            // changes the state of the button but fails to trigger the click event) so do the equivalent in code
            PlayOrPause.IsChecked = !PlayOrPause.IsChecked;
            PlayOrPause_Click(this, null);
            return true;
        }


        // Try refreshing the source video
        // Unused, but hesitant to delete it in case I need it in the future
        // ReSharper disable once UnusedMember.Global
        public void TryRefreshSource()
        {
            if (this.Visibility == Visibility.Visible && this.HasSource)
            {
                this.SetSource(this.sourceUri, this.FileIndex);
            }
        }
        #endregion

        #region Keyboard fast-forward and rewind

        public bool StartFastForward()
        {
            return this.TryStartKeyboardSeek(1, GlobalReferences.TimelapseState.VideoFastForwardSpeed);
        }

        public bool StartRewind()
        {
            return this.TryStartKeyboardSeek(-1, GlobalReferences.TimelapseState.VideoRewindSpeed);
        }

        private bool TryStartKeyboardSeek(int direction, double speed)
        {
            if (!this.IsCurrentVideo || !this.isSourceReady || !this.player.CanPlay)
            {
                return false;
            }

            if (!this.isKeyboardSeeking)
            {
                this.resumeAfterKeyboardSeek = this.player.IsPlaying;
                this.player.Pause();
                this.TimerUpdatePosition.Stop();
                this.isKeyboardSeeking = true;
                this.keyboardSeekTargetTicks = this.player.CurTime;
                this.lastKeyboardSeekUpdate = DateTime.UtcNow;
            }
            else if (this.keyboardSeekDirection != Math.Sign(direction))
            {
                this.keyboardSeekTargetTicks = this.player.CurTime;
                this.lastKeyboardSeekUpdate = DateTime.UtcNow;
            }

            this.keyboardSeekDirection = Math.Sign(direction);
            this.keyboardSeekSpeed = Math.Max(1.0, Math.Abs(speed));
            this.TimerKeyboardSeek.Start();
            return true;
        }

        public void StopSpeedChange()
        {
            if (!this.isKeyboardSeeking)
            {
                return;
            }

            bool resumePlayback = this.resumeAfterKeyboardSeek;
            long finalSeekTargetTicks = this.keyboardSeekTargetTicks;
            this.CancelKeyboardSeek();

            // A Flyleaf seek completes asynchronously. Keep the final logical target instead
            // of allowing a late completion from an older seek to snap the position backwards.
            this.player.CurTime = finalSeekTargetTicks;

            if (resumePlayback && this.Visibility == Visibility.Visible && this.HasSource)
            {
                this.player.Speed = this.SavedSpeedRatio;
                this.player.Play();
                this.TimerUpdatePosition.Start();
            }
            else
            {
                this.player.Pause();
                this.PlayOrPause.IsChecked = false;
                this.ShowPosition();
            }
        }

        private void CancelKeyboardSeek()
        {
            this.TimerKeyboardSeek.Stop();
            this.isKeyboardSeeking = false;
            this.keyboardSeekDirection = 0;
            this.keyboardSeekSpeed = 0;
            this.keyboardSeekTargetTicks = 0;
            this.resumeAfterKeyboardSeek = false;
        }

        private void TimerKeyboardSeek_Tick(object sender, EventArgs e)
        {
            if (!this.isKeyboardSeeking || !this.isSourceReady || !this.HasSource || !this.player.CanPlay)
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            double elapsedSeconds = Math.Min(0.25, (now - this.lastKeyboardSeekUpdate).TotalSeconds);
            this.lastKeyboardSeekUpdate = now;
            if (elapsedSeconds <= 0)
            {
                return;
            }

            // Do not use player.CurTime as the base here. Flyleaf completes seeks
            // asynchronously, so CurTime can temporarily report an older request. Basing the
            // next target on that value makes fast-forward visibly jump backwards. Maintain a
            // monotonic logical target owned by Timelapse instead.
            long target = this.keyboardSeekTargetTicks +
                          TimeSpan.FromSeconds(elapsedSeconds * this.keyboardSeekSpeed * this.keyboardSeekDirection).Ticks;
            if (this.player.Duration > 0 && target >= this.player.Duration)
            {
                // Preserve the previous fast-forward behavior: continuing past the end
                // wraps to the start of the video.
                target %= this.player.Duration;
            }
            else if (target <= 0)
            {
                target = 0;
                this.keyboardSeekSpeed = 0;
            }

            this.keyboardSeekTargetTicks = target;
            this.player.CurTime = target;
            if (this.FrameRate is > 0)
            {
                this.FrameToShow = Convert.ToInt32(Math.Floor(TimeSpan.FromTicks(target).TotalSeconds * this.FrameRate.Value));
            }
            this.ShowPosition();
        }

        #endregion

        #region Frame by frame navigation
        private void BtnNavigateFrame_OnClick(object sender, RoutedEventArgs e)
        {
            if (sender is RepeatButton { Tag: "Next" })
            {
                // Navigate to the next frame
                this.NavigateFrame(true);
            }
            else if (sender is RepeatButton { Tag: "Previous" })
            {
                // Navigate to the previous frame
                this.NavigateFrame(false);
            }
        }

        // Navigate the next or previous frame in the video. Return true if the navigation did anything (i.e., not at beginning or end of video)
        public bool NavigateFrame(bool forward)
        {
            if (this.Visibility != Visibility.Visible || !this.HasSource || this.VideoDurationSeconds <= 0)
            {
                // No video to navigate
                return false;
            }

            this.isProgrammaticUpdate = true;
            this.Pause();
            this.isProgrammaticUpdate = false;

            // We should almost always have a valid frame rate.
            // But if we don't, we can't navigate by frames
            // So lets just set it for 15, which may be too fast or too slow for some videos
            float? frameRate = this.FrameRate == null || this.FrameRate == 0
                ? 15.0f
                : this.FrameRate;

            TimeSpan newPosition = forward
                ? TimeSpan.FromMilliseconds(this.CurrentPosition.TotalMilliseconds + 1000.0 / frameRate.Value)
                : TimeSpan.FromMilliseconds(this.CurrentPosition.TotalMilliseconds - 1000.0 / frameRate.Value);

            if (newPosition.TotalMilliseconds < 0)
            {
                // We are at the beginning of the video, so don't go back any further
                return false;
            }
            this.CurrentPosition = newPosition;
            TimerUpdatePosition.Start();
            return true;
        }
        #endregion

        #region ShowPosition : Update everything depending upon the current video position
        private void UpdatePosition(TimeSpan videoTime)
        {
            // Update the position in the video to the given time
            if (!this.HasSource || this.FrameRate == null || this.VideoDurationSeconds <= 0)
            {
                return;
            }

            this.CurrentPosition = videoTime;
        }

        // Show the current play position in the ScrollBar and TextBox, if possible.
        // TODO Performance (very minor, likley not worth doing). Sometimes, when scrubbing, the same video frame is updated several times.
        // To see this, just click on the slider without moving its position.
        private void ShowPosition()
        {
            if (!this.HasSource)
            {
                // We aren't displaying anything, so nothing to show
                return;
            }

            isProgrammaticUpdate = true;
            try
            {
                // Feedback: show current position in the video (text and slider)
                TimeFromStart.Text = this.CurrentPosition.ToString(Time.VideoPositionFormat);
                this.isProgrammaticUpdate = true;
                SliderScrubbing.Value = this.CurrentPosition.TotalSeconds;
                this.isProgrammaticUpdate = false;

                // Update the bounding boxes
                this.ClearBoundingBoxes();
                //this.FrameRate = this.BoxesForFile?.FrameRate; // Already set in DoSetFrameRateAndFrameToShowValues
                if (this.FrameToShow < 0 || null == this.FrameRate || this.FrameRate <= 0)
                {
                    // Can't process any bounding boxes
                    isProgrammaticUpdate = false;
                    return;
                }

                // Start searching from a frame a half second before the desired one
                Point vidSize = GetActualVideoSize();
                int frameWindow = this.FrameRate == null
                    ? 0
                    : (int)Math.Floor((decimal)(this.FrameRate / 2.0));

                if (null != this.BoxesForFile)
                {
                    this.BoxesForFile.DrawBoundingBoxesInCanvas(this.VideoOverlayCanvas, vidSize.X, vidSize.Y, 0, null, this.FrameToShow, frameWindow);
                    if (this.FrameRate != null)
                    {
                        this.FrameToShow = Convert.ToInt32(Math.Ceiling((double)(SliderScrubbing.Value * this.FrameRate)));
                    }
                    this.DoButtonBestFrameSetBackgroundColor();
                }
                isProgrammaticUpdate = false;
            }
            catch
            {
                isProgrammaticUpdate = false;
            }
        }

        #endregion

        #region Callback: IsVisibleChanged
        // When the visibility changes to visible, play it if Autoplay is checked, otherwise pause it.
        private void VideoPlayer_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (CBAutoPlay.IsChecked == true && (bool)e.NewValue)
            {
                this.Play();
            }
            else
            {
                this.Pause();
            }
        }
        #endregion

        #region Callbacks: Buttons and Checkbuttons

        // The round play/pause button was clicked
        private void PlayOrPause_Click(object sender, RoutedEventArgs e)
        {
            if (this.PlayOrPause.IsChecked == true)
            {
                this.Play();
            }
            else
            {
                this.Pause();
            }
        }

        // The Mute checkbutton
        private void CBMute_CheckedChanged(object sender, RoutedEventArgs e)
        {
            bool mute = CBMute.IsChecked == true;
            this.player.Audio.Mute = mute;
            GlobalReferences.TimelapseState.VideoMute = mute;
        }

        // The Speed Checkbutton.
        // Note that some videos don't do anything even if the speed ratio is changed.
        private void SetSpeed_CheckedChanged(object sender, RoutedEventArgs e)
        {
            RadioButton rb = sender as RadioButton;
            // The speed is hard-wired to the tag. Note: we use tryparse as there was a rare bug when the tag could not be converted to a double
            if (rb?.Tag != null && Double.TryParse((string)rb.Tag, out double newSpeed))
            {
                this.player.Speed = newSpeed;
                this.SavedSpeedRatio = newSpeed;

                if (rb.Name == "RBFast")
                {
                    GlobalReferences.TimelapseState.VideoSpeed = 3;
                }
                else if (rb.Name == "RBVeryFast")
                {
                    GlobalReferences.TimelapseState.VideoSpeed = 4;
                }
                else if (rb.Name == "RBSlow")
                {
                    GlobalReferences.TimelapseState.VideoSpeed = 1;
                }
                else // (rb.Name == "RBNormal")
                {
                    GlobalReferences.TimelapseState.VideoSpeed = 2;
                }
                this.Play();
            }
        }

        // The AutoPlay checkbutton
        private void CBAutoPlay_CheckChanged(object sender, RoutedEventArgs e)
        {
            if (CBAutoPlay.IsChecked == true)
            {
                this.Play();
            }
            else
            {
                this.Pause();
            }
            GlobalReferences.TimelapseState.VideoAutoPlay = CBAutoPlay.IsChecked == true;
        }

        private void CBRepeat_CheckChanged(object sender, RoutedEventArgs e)
        {
            GlobalReferences.TimelapseState.VideoRepeat = CBRepeat.IsChecked == true;
        }

        // The Open ExternalPlayer button
        private void OpenExternalPlayer_Click(object sender, RoutedEventArgs e)
        {
            if (this.sourceUri != null && File.Exists(Uri.UnescapeDataString(this.sourceUri.AbsolutePath)))
            {
                Uri uri = new(Uri.UnescapeDataString(this.sourceUri.AbsolutePath));
                ProcessExecution.TryProcessStart(uri);
            }
        }
        #endregion

        #region Callbacks: Timer Ticks

        private void TimerUpdatePosition_Tick(object sender, EventArgs e)
        {
            if (this.HasSource)
            {
                ShowPosition();
            }
        }
        #endregion

        #region Private Slider-related callbacks
        // The Slider's position has changed
        // But we only take action when the change is due to scrubbing actions by the user 
        private void SliderScrubbing_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (false == this.IsVisible || isProgrammaticUpdate || !this.player.CanPlay)
            {
                return;
            }

            TimeSpan videoPosition = TimeSpan.FromSeconds(SliderScrubbing.Value);
            this.CurrentPosition = videoPosition;

            // Update which frame we should be displaying
            if (this.FrameRate != null)
            {
                this.FrameToShow = Convert.ToInt32(System.Math.Floor((double)(SliderScrubbing.Value * this.FrameRate)));
            }
            this.Pause();
        }

        // When the user starts moving the slider, we want to pause the video so the two actions don't interfere with each other
        private void SliderScrubbing_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            this.Pause();
        }
        #endregion

        #region Private Bounding box helpers

        // Remove all children (i.e., should only be existing bounding boxes) except for the video player
        private void ClearBoundingBoxes()
        {
            if (this.IsVisible)
            {
                // not sure if we should only do this if isVisible
                GlobalReferences.MainWindow.MarkableCanvas.ClearBoundingBoxes();
            }
            this.VideoOverlayCanvas.Children.Clear();
        }

        #endregion

        #region Private Video helpers

        // The video running in the player may be smaller than the the player (i.e. when the player shows black borders)
        // This calculates the actual width/height of the video within that player. It does this by determining the actual video width/height resolution,
        // Since either the width or the height of the video will completely fit within the video player, we determine which is which, where we calculate
        // the other dimension based on the current width (or height) of the player.
        private Point GetActualVideoSize()
        {
            double videoWidth = this.player.Video.Width;
            double videoHeight = this.player.Video.Height;
            double playerWidth = this.FlyleafHostActive.ActualWidth;
            double playerHeight = this.FlyleafHostActive.ActualHeight;
            if (videoWidth <= 0 || videoHeight <= 0 || playerWidth <= 0 || playerHeight <= 0)
            {
                return new(Math.Max(0, playerWidth), Math.Max(0, playerHeight));
            }
            // Compare the aspect ratio of the video player to the video aspect ratio 
            // Depending on the results, we are either filling the video's width or its height.
            // We can then use the aspect ratio to determine the other dimension.
            return playerWidth / playerHeight <= videoWidth / videoHeight
                ? new(playerWidth, playerWidth * videoHeight / videoWidth)
                : new Point(videoWidth * playerHeight / videoHeight, playerHeight);
        }

        #endregion

        #region Static methods:Frame rate / number helpers

        // Given a frame number and frame rate, get the approximate matching time in the video 
        // If we can't, return a negative timespan
        private static TimeSpan GetTimeSpanFromFrameNumber(int frameNumber, float? frameRate, TimeSpan duration)
        {
            TimeSpan errorTime = TimeSpan.FromSeconds(-1);

            if (frameNumber == 0)
            {
                // Simplest case
                return TimeSpan.FromSeconds(0);
            }

            if (frameNumber < 0 || null == frameRate || frameRate <= 0)
            {
                // Error
                return errorTime;
            }

            TimeSpan startTime = TimeSpan.FromSeconds(frameNumber / (double)frameRate);
            return startTime < TimeSpan.Zero || startTime > duration
                ? TimeSpan.Zero
                : startTime;
        }

        private static float? GetFrameRate(BoundingBoxes boxesForFile, TimelapseWindow mainWindow)
        {
            float? frameRate = boxesForFile?.FrameRate;

            // We couln't get the frameRate from the bounding boxes, so try getting it from the file
            if (frameRate == null || frameRate <= 0)
            {
                if (null == mainWindow?.DataHandler?.ImageCache.Current)
                {
                    return null;
                }

                string currentPath = mainWindow.DataHandler.ImageCache.Current.GetFilePath(mainWindow.DataHandler.FileDatabase.RootPathToImages);
                frameRate = VideoPlayer.GetFrameRateFromFile(currentPath);
                if (frameRate <= 0)
                {
                    frameRate = null;
                }
            }

            return frameRate;
        }

        // Given a path to a file, return the frame rate property attached to that file
        // On failure, return -1.
        private static float? GetFrameRateFromFile(string path)
        {
            if (false == File.Exists(path))
            {
                return null;
            }

            try
            {
                // The frame rate will be 
                ShellObject obj = ShellObject.FromParsingName(path);
                ShellProperty<uint?> rateProp = obj?.Properties?.GetProperty<uint?>("System.Video.FrameRate");
                return rateProp?.Value == null
                    ? null
                    : (float?)(rateProp.Value / 1000.0); // converts it from milliseconds to seconds
            }
            catch
            {
                // Error. Prehapse because the file is not accessible or property isn't there?
                return null;
            }
        }

        #endregion

        #region Do actions
        // Update the slider to the current values
        private void DoSetFrameRateAndFrameToShowValues()
        {
            // Set the frame rate and frame to show (default is frame 0) for this video from the bounding box structure
            // Default is frame 0
            if (this.BoxesForFile == null)
            {
                this.FrameRate = null;
                this.FrameToShow = 0;
            }
            else
            {
                this.FrameRate = GetFrameRate(this.BoxesForFile, GlobalReferences.MainWindow);
                this.FrameToShow = null == FrameRate
                    ? 0
                    : this.BoxesForFile.InitialVideoFrame;
            }
        }

        // Detremine the video duration
        private void DoSetDurationValues()
        {
            // Set video player slider and text feedback to the video's length
            this.VideoDurationSeconds = this.player.Duration > 0
                ? TimeSpan.FromTicks(this.player.Duration).TotalSeconds
                : 0;
            this.VideoDurationTimeSpan = new(0, 0, 0, 0, Convert.ToInt32(this.VideoDurationSeconds * 1000));
        }

        private void DoSetSliderAttributesForDuration()
        {
            this.isProgrammaticUpdate = true;
            this.SliderScrubbing.Maximum = this.VideoDurationSeconds;
            this.TimeDuration.Text = this.VideoDurationSeconds.ToString(Time.VideoPositionFormat);
            this.isProgrammaticUpdate = false;
        }

        // Set the various text values in the slider
        private void DoSetSliderText()
        {
            if (!this.HasSource || this.FrameRate == null || this.VideoDurationSeconds <= 0)
            {
                DoSetSliderTextToUnknown();
                return;
            }
            // TracePrint.StackTrace("--->", 5);
            TimeFromStart.Text = this.CurrentPosition.ToString(Time.VideoPositionFormat);
            TimeDuration.Text = this.VideoDurationTimeSpan.ToString(Time.VideoPositionFormat);
        }
        private void DoSetSliderTextToUnknown()
        {
            TimeFromStart.Text = "--";
            TimeDuration.Text = "--";

            this.isProgrammaticUpdate = true;
            SliderScrubbing.Value = 0;
            SliderScrubbing.Maximum = 0;
            this.isProgrammaticUpdate = false;
        }

        private void UpdateVideoLayout()
        {
            double viewportWidth = this.VideoCanvas.ActualWidth;
            double viewportHeight = this.VideoCanvas.ActualHeight;
            if (viewportWidth <= 0 || viewportHeight <= 0)
            {
                return;
            }

            // Keep Flyleaf's native window fixed to the viewport. Resizing it for every
            // wheel/move event forces an expensive HWND/D3D layout and makes zoom stutter.
            this.FlyleafHostActive.Width = viewportWidth;
            this.FlyleafHostActive.Height = viewportHeight;
            Canvas.SetLeft(this.FlyleafHostActive, 0);
            Canvas.SetTop(this.FlyleafHostActive, 0);

            this.videoOffsetX = ClampOffset(this.videoOffsetX, viewportWidth, viewportWidth * this.videoZoom);
            this.videoOffsetY = ClampOffset(this.videoOffsetY, viewportHeight, viewportHeight * this.videoZoom);
            this.ApplyVideoOverlayTransform();
        }

        private void ApplyVideoOverlayTransform()
        {
            this.videoOverlayScale.ScaleX = this.videoZoom;
            this.videoOverlayScale.ScaleY = this.videoZoom;
            this.videoOverlayTranslation.X = this.videoOffsetX;
            this.videoOverlayTranslation.Y = this.videoOffsetY;
        }

        private static double ClampOffset(double offset, double viewportSize, double contentSize)
        {
            return Math.Min(0, Math.Max(viewportSize - contentSize, offset));
        }
        #endregion

        #region UpdateBoundingBoxes
        // This may be invoked several times unecessarily, but in those case the tests below (e.g., for null etc)
        // make those cases a nearly 'noop' operation
        private void UpdateBoundingBoxes()
        {
            try
            {
                this.ClearBoundingBoxes();

                if (!this.HasSource || this.FrameRate == null || this.FrameRate <= 0 || this.FrameToShow < 0 || this.VideoDurationSeconds <= 0)
                {
                    // Can't process any bounding boxes or can't display them
                    return;
                }

                isProgrammaticUpdate = true;
                // Start searching from a frame a second before the desired one
                Point vidSize = GetActualVideoSize();
                int frameWindow = this.FrameRate == null
                    ? 0
                    : (int)Math.Floor((decimal)(this.FrameRate / 2.0));

                if (null != this.BoxesForFile)
                {
                    //Debug.Print($"{this.FrameToShow}, {fromFrame}, {span}");
                    this.BoxesForFile.DrawBoundingBoxesInCanvas(this.VideoOverlayCanvas, vidSize.X, vidSize.Y, 0, null, this.FrameToShow, frameWindow);
                    if (this.FrameRate != null)
                    {
                        this.FrameToShow = Convert.ToInt32(Math.Ceiling((double)(SliderScrubbing.Value * this.FrameRate)));
                    }

                    this.DoButtonBestFrameSetBackgroundColor();
                }

                isProgrammaticUpdate = false;
            }
            catch
            {
                isProgrammaticUpdate = false;
            }
        }
        #endregion

        #region Go to best frame
        // Try going to the best video frame, if possible
        // Usually invoked with a shortcut key
        public void TryGoToBestFrame()
        {
            if (this.Visibility == Visibility.Visible && this.HasSource)
            {
                // This will go to the best bounding box, but only if there are bounding boxes available
                this.DoGoToBestFrame();
            }
        }

        private void DoGoToBestFrame()
        {
            if (null == this.BoxesForFile || this.BoxesForFile.Boxes.Count == 0)
            {
                return;
            }

            // Estimate the video's start time from the given frame and frame rate. If we can't, then just start at 0 
            TimeSpan startTime = GetTimeSpanFromFrameNumber(this.BoxesForFile.InitialVideoFrame, this.FrameRate, this.VideoDurationTimeSpan);

            // Because we got here should have some valid numbers, so set them all
            this.FrameToShow = this.BoxesForFile.InitialVideoFrame;

            // Set the video, slider and feedback text to the desired start time.
            this.isProgrammaticUpdate = true;
            this.SliderScrubbing.Value = startTime.TotalSeconds; // This will be set in ShowPosition so no need to do it here.
            this.isProgrammaticUpdate = false;

            // Update the slider as needed
            this.DoSetSliderText();

            // Set the video position to the initial position
            this.isProgrammaticUpdate = true;
            this.UpdatePosition(startTime);
            this.isProgrammaticUpdate = false;

            // Show the bounding box for the current frame
            this.UpdateBoundingBoxes();
        }
        #endregion

        #region ButtonBestFrame
        private void ButtonEnableBestFrameIfNeeded()
        {
            // Enable only if there are bounding boxes and the max confidence > the display threshold
            this.BorderButtonBestFrame.Visibility = null != this.BoxesForFile &&
                                                    this.BoxesForFile.Boxes.Count > 0 &&
                                                    this.BoxesForFile.MaxConfidence >= GlobalReferences.TimelapseState.BoundingBoxDisplayThreshold &&
                                                    this.BoxesForFile.FrameRate is > 0
                ? Visibility.Visible
                : Visibility.Hidden;
        }

        private void ButtonBestFrame_OnClick(object sender, RoutedEventArgs e)
        {
            DoGoToBestFrame();
        }

        private void DoButtonBestFrameSetBackgroundColor()
        {
            if (this.BoxesForFile.InitialVideoFrame >= this.FrameToShow - 1 && this.BoxesForFile.InitialVideoFrame <= this.FrameToShow + 1)
            {
                this.ButtonBestFrame.Background = Brushes.Coral;
            }
            else
            {
                this.ButtonBestFrame.Background = Brushes.Red;
            }
        }
        #endregion

        #region Public methods (ScaleVideo, TranslateVideo) invoked only by Markable Canvas

        /// <summary>
        /// Scale (Zoom) the video in or out around the provided location, which should be the cursor location in video coordinates
        /// </summary>
        public void ScaleVideo(Point currentMousePosition, bool zoomIn)
        {
            const double VideoZoomMaximum = 4.0;
            const double VideoZoomMinimum = 1.0;

            double oldZoom = this.videoZoom;
            if ((zoomIn && oldZoom >= VideoZoomMaximum) || (!zoomIn && oldZoom <= VideoZoomMinimum))
            {
                return;
            }

            double viewportWidth = this.VideoCanvas.ActualWidth;
            double viewportHeight = this.VideoCanvas.ActualHeight;
            currentMousePosition.X = Math.Max(0, Math.Min(viewportWidth, currentMousePosition.X));
            currentMousePosition.Y = Math.Max(0, Math.Min(viewportHeight, currentMousePosition.Y));

            // Preserve the point under the cursor while changing the host's layout size.
            double oldWidth = viewportWidth * oldZoom;
            double oldHeight = viewportHeight * oldZoom;
            double relativeX = oldWidth <= 0 ? 0.5 : (currentMousePosition.X - this.videoOffsetX) / oldWidth;
            double relativeY = oldHeight <= 0 ? 0.5 : (currentMousePosition.Y - this.videoOffsetY) / oldHeight;

            // Flyleaf performs this operation in the renderer, avoiding native-window resize
            // and layout work. It expects a point in physical pixels.
            Point flyleafPoint = new(
                currentMousePosition.X * this.FlyleafHostActive.DpiX,
                currentMousePosition.Y * this.FlyleafHostActive.DpiY);
            if (zoomIn)
            {
                this.player.Config.Video.ZoomIn(flyleafPoint);
            }
            else
            {
                this.player.Config.Video.ZoomOut(flyleafPoint);
            }

            double newZoom = Math.Clamp(Convert.ToDouble(this.player.Config.Video.Zoom) / 100.0, VideoZoomMinimum, VideoZoomMaximum);
            this.player.Config.Video.Zoom = Convert.ToInt32(newZoom * 100.0);
            this.videoZoom = newZoom;
            if (this.IsUnScaled)
            {
                this.videoOffsetX = 0;
                this.videoOffsetY = 0;
                this.player.Config.Video.PanXOffset = 0;
                this.player.Config.Video.PanYOffset = 0;
            }
            else
            {
                this.videoOffsetX = currentMousePosition.X - relativeX * viewportWidth * newZoom;
                this.videoOffsetY = currentMousePosition.Y - relativeY * viewportHeight * newZoom;
            }

            this.videoOffsetX = ClampOffset(this.videoOffsetX, viewportWidth, viewportWidth * this.videoZoom);
            this.videoOffsetY = ClampOffset(this.videoOffsetY, viewportHeight, viewportHeight * this.videoZoom);
            this.ApplyVideoOverlayTransform();
        }

        /// Translate the video from the previous mouse position the the current mouse position
        public void TranslateVideo(Point currentMousePosition, Point previousMousePosition)
        {
            // Flyleaf-window drags are handled directly in FlyleafWindow_PreviewMouseMove.
            // This method remains for the shared image/video interaction contract.
        }

        private void ApplyDirectVideoPan(Point currentScreenPosition)
        {
            double viewportWidth = this.VideoCanvas.ActualWidth;
            double viewportHeight = this.VideoCanvas.ActualHeight;
            double surfaceWidth = this.FlyleafHostActive.Surface?.ActualWidth ?? viewportWidth;
            double surfaceHeight = this.FlyleafHostActive.Surface?.ActualHeight ?? viewportHeight;
            if (viewportWidth <= 0 || viewportHeight <= 0 || surfaceWidth <= 0 || surfaceHeight <= 0 || this.IsUnScaled)
            {
                return;
            }

            Vector movement = currentScreenPosition - this.videoDragStartScreenPosition;

            // Match FlyleafHost's native pan implementation: offsets are calculated from
            // the original mouse-down state and the physical screen cursor position.
            this.player.Config.Video.PanXOffset = this.videoDragStartPanX + movement.X / surfaceWidth;
            this.player.Config.Video.PanYOffset = this.videoDragStartPanY + movement.Y / surfaceHeight;

            this.videoOffsetX = ClampOffset(
                this.videoDragStartOffsetX + movement.X / this.FlyleafHostActive.DpiX,
                viewportWidth,
                viewportWidth * this.videoZoom);
            this.videoOffsetY = ClampOffset(
                this.videoDragStartOffsetY + movement.Y / this.FlyleafHostActive.DpiY,
                viewportHeight,
                viewportHeight * this.videoZoom);

            // Avoid waking WPF's transparent overlay compositor when no annotations exist.
            if (this.VideoOverlayCanvas.Children.Count > 0)
            {
                this.ApplyVideoOverlayTransform();
            }
        }

        #endregion

        #region Private Size changed callbacks

        // Scale the video by one increment around the screen location
        // Minor problem: This is invoked twice when Timelapse is first started on a video,
        // Likley due to an OnIsVisibleChange event
        private void VideoPlayer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Minor inefficiency: SizeChanged is invoked twice when Timelapse is first started on a video
            this.UpdateVideoLayout();
            this.UpdateBoundingBoxes();
        }
        #endregion


    }
}

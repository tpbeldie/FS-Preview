using FSPreview.MediaFramework.MediaContext;
using FSPreview.MediaFramework.MediaInput;
using FSPreview.MediaFramework.MediaStream;
using FSPreview.Plugins;

namespace FSPreview.MediaPlayer
{
    public class Video : NotifyPropertyChanged
    {
        public List<VideoInput> Inputs => ((IProvideVideo)m_decoder?.OpenedPlugin)?.VideoInputs;

        public Dictionary<string, IProvideVideo> Plugins => m_decoder?.PluginsProvideVideo;

        public List<VideoStream> Streams => m_decoder?.VideoDemuxer.VideoStreams;

        /// <summary>
        /// Whether the input has video and it is configured
        /// </summary>
        public bool IsOpened {
            get => isOpened;
            internal set => Set(ref _IsOpened, value);
        }

        internal bool _IsOpened, isOpened;

        public string Codec {
            get => codec;
            internal set => Set(ref _Codec, value);
        }

        internal string _Codec, codec;

        ///// <summary>
        ///// Video bitrate (Kbps)
        ///// </summary>
        public double BitRate {
            get => bitRate;
            internal set => Set(ref _BitRate, value);
        }

        internal double _BitRate, bitRate;

        public AspectRatio AspectRatio {
            get => aspectRatio;
            internal set => Set(ref _AspectRatio, value);
        }

        internal AspectRatio _AspectRatio, aspectRatio;

        ///// <summary>
        ///// Total Dropped Frames
        ///// </summary>
        public int FramesDropped {
            get => framesDropped;
            internal set => Set(ref _FramesDropped, value);
        }

        internal int _FramesDropped, framesDropped;

        /// <summary>
        /// Total Frames
        /// </summary>
        public int FramesTotal {
            get => framesTotal;
            internal set => Set(ref _FramesTotal, value);
        }

        internal int _FramesTotal, framesTotal;

        public int FramesDisplayed {
            get => framesDisplayed;
            internal set => Set(ref _FramesDisplayed, value);
        }

        internal int _FramesDisplayed, framesDisplayed;

        public double FPS {
            get => fps;
            internal set => Set(ref _FPS, value);
        }

        internal double _FPS, fps;

        /// <summary>
        /// Actual Frames rendered per second (FPS)
        /// </summary>
        public double FPSCurrent {
            get => fpsCurrent;
            internal set => Set(ref _FPSCurrent, value);
        }

        internal double _FPSCurrent, fpsCurrent;

        public string PixelFormat { 
            get => pixelFormat;
            internal set => Set(ref _PixelFormat, value); }
      
        internal string _PixelFormat, pixelFormat;

        public int Width {
            get => width;
            internal set => Set(ref _Width, value);
        }
       
        internal int _Width, width;

        public int Height {
            get => height; 
            internal set => Set(ref _Height, value); 
        }
   
        internal int _Height, height;

        public bool VideoAcceleration { 
            get => videoAcceleration;
            internal set => Set(ref _VideoAcceleration, value);
        }
     
        internal bool _VideoAcceleration, videoAcceleration;

        public bool ZeroCopy { 
            get => zeroCopy;
            internal set => Set(ref _ZeroCopy, value);
        }
       
        internal bool _ZeroCopy, zeroCopy;

        Action m_uiAction;
    
        Player m_player;
    
        DecoderContext m_decoder => m_player.decoder;
    
        VideoStream m_disabledStream;
     
        Config Config => m_player.Config;

        public Video(Player player) {
            this.m_player = player;
            m_uiAction = () => {
                IsOpened = IsOpened;
                Codec = Codec;
                AspectRatio = AspectRatio;
                FramesTotal = FramesTotal;
                FPS = FPS;
                PixelFormat = PixelFormat;
                Width = Width;
                Height = Height;
                VideoAcceleration = VideoAcceleration;
                ZeroCopy = ZeroCopy;
                FramesDisplayed = FramesDisplayed;
                FramesDropped = FramesDropped;
            };
        }

        internal void Reset(bool andDisabledStream = true) {
            codec = null;
            AspectRatio = new AspectRatio(0, 0);
            bitRate = 0;
            fps = 0;
            pixelFormat = null;
            width = 0;
            height = 0;
            framesTotal = 0;
            videoAcceleration = false;
            zeroCopy = false;
            isOpened = false;
            m_player.renderer.DisableRendering = true;
            if (andDisabledStream) {
                m_disabledStream = null;
            }
            m_player.UIAdd(m_uiAction);
        }

        internal void Refresh() {
            if (m_decoder.VideoStream == null) { 
                Reset(); 
                return; 
            }
            codec = m_decoder.VideoStream.Codec;
            aspectRatio = m_decoder.VideoStream.AspectRatio;
            fps = m_decoder.VideoStream.FPS;
            pixelFormat = m_decoder.VideoStream.PixelFormatStr;
            width = m_decoder.VideoStream.Width;
            height = m_decoder.VideoStream.Height;
            framesTotal = m_decoder.VideoStream.TotalFrames;
            videoAcceleration = m_decoder.VideoDecoder.VideoAccelerated;
            zeroCopy = m_decoder.VideoDecoder.ZeroCopy;
            isOpened = !m_decoder.VideoDecoder.Disposed;
            framesDisplayed = 0;
            framesDropped = 0;
            m_player.renderer.DisableRendering = false;
            m_player.UIAdd(m_uiAction);
        }

        internal void Enable() {
            if (m_player.VideoDemuxer.Disposed || Config.Player.Usage == Usage.Audio) {
                return;
            }
            if (m_disabledStream == null) {
                m_disabledStream = m_decoder.SuggestVideo(m_decoder.VideoDemuxer.VideoStreams);
            }
            if (m_disabledStream == null) {
                return;
            }
            bool wasPlaying = m_player.IsPlaying;
            m_player.Pause();
            m_player.Open(m_disabledStream);
            Refresh();
            m_player.UIAll();
            if (wasPlaying || Config.Player.AutoPlay) {
                m_player.Play();
            }
        }

        internal void Disable() {
            if (!IsOpened || Config.Player.Usage == Usage.Audio) {
                return;
            }
            bool wasPlaying = m_player.IsPlaying;
            m_disabledStream = m_decoder.VideoStream;
            m_player.Pause();
            m_player.VideoDecoder.Dispose(true);
            m_player.Subtitles.subsText = "";
            m_player.UIAdd(() => m_player.Subtitles.SubsText = m_player.Subtitles.SubsText);
            if (!m_player.Audio.IsOpened) {
                m_player.canPlay = false;
                m_player.UIAdd(() => m_player.CanPlay = m_player.CanPlay);
            }
            Reset(false);
            m_player.UIAll();
            if (wasPlaying || Config.Player.AutoPlay) {
                m_player.Play();
            }
        }

        public void Toggle() {
            Config.Video.Enabled = !Config.Video.Enabled;
        }

        public void ToggleKeepRatio() {
            if (Config.Video.AspectRatio == AspectRatio.Keep) {
                Config.Video.AspectRatio = AspectRatio.Fill;
            }
            else if (Config.Video.AspectRatio == AspectRatio.Fill) {
                Config.Video.AspectRatio = AspectRatio.Keep;
            }
        }

        public void ToggleVideoAcceleration() {
            Config.Video.VideoAcceleration = !Config.Video.VideoAcceleration;
        }
    }
}

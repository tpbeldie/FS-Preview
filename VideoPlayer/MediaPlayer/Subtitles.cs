using FSPreview.MediaFramework.MediaContext;
using FSPreview.MediaFramework.MediaInput;
using FSPreview.MediaFramework.MediaStream;
using FSPreview.Plugins;

namespace FSPreview.MediaPlayer
{
    public class Subtitles : NotifyPropertyChanged
    {
        public List<SubtitlesInput> Inputs => ((IProvideSubtitles)decoder?.OpenedSubtitlesPlugin)?.SubtitlesInputs;
        public Dictionary<string, IProvideSubtitles> Plugins => decoder?.PluginsProvideSubtitles;
        public List<SubtitlesStream> Streams => decoder?.VideoDemuxer.SubtitlesStreams;
        public List<SubtitlesStream> ExternalStreams => decoder?.SubtitlesDemuxer.SubtitlesStreams;

        /// <summary>
        /// Whether the input has subtitles and it is configured
        /// </summary>
        public bool IsOpened { get => isOpened; internal set => Set(ref _IsOpened, value); }

        internal bool _IsOpened, isOpened;

        public string Codec { get => codec; internal set => Set(ref _Codec, value); }

        internal string _Codec, codec;

        /// <summary>
        /// Subtitles Text (updates dynamically while playing based on the duration that it should be displayed)
        /// </summary>
        public string SubsText { get => subsText; internal set => Set(ref _SubsText, value); }
        internal string _SubsText = "", subsText = "";

        Action m_uiAction;
        Player m_player;
        DecoderContext decoder => m_player?.decoder;
        Config Config => m_player.Config;
        SubtitlesStream disabledStream;

        public Subtitles(Player player) {
            this.m_player = player;
            m_uiAction = () => {
                IsOpened = IsOpened;
                Codec = Codec;
                SubsText = SubsText;
            };
        }

        internal void Reset() {
            codec = null;
            isOpened = false;
            subsText = "";
            disabledStream = null;
            m_player.UIAdd(m_uiAction);
        }

        internal void Refresh() {
            if (decoder.SubtitlesStream == null) {
                Reset();
                return;
            }
            codec = decoder.SubtitlesStream.Codec;
            isOpened = !decoder.SubtitlesDecoder.Disposed;
            // disabledStream = decoder.SubtitlesStream;
            m_player.UIAdd(m_uiAction);
        }

        internal void Enable() {
            if (!m_player.CanPlay || Config.Player.Usage != Usage.AVS) {
                return;
            }
            SubtitlesInput suggestedInput = null;
            if (disabledStream == null) {
                decoder.SuggestSubtitles(out disabledStream, out suggestedInput, m_player.VideoDemuxer.SubtitlesStreams);
            }
            if (disabledStream != null) {
                if (disabledStream.SubtitlesInput != null) {
                    m_player.Open(disabledStream.SubtitlesInput);
                }
                else {
                    m_player.Open(disabledStream);
                }
            }
            else if (suggestedInput != null) {
                m_player.Open(suggestedInput);
            }
            Refresh();
            m_player.UIAll();
        }

        internal void Disable() {
            if (!IsOpened || Config.Player.Usage != Usage.AVS) {
                return;
            }
            disabledStream = decoder.SubtitlesStream;
            m_player.SubtitlesDecoder.Dispose(true);
            m_player.sFrame = null;
            Reset();
            m_player.UIAll();
        }

        public void DelayRemove() {
            Config.Subtitles.Delay -= Config.Player.SubtitlesDelayOffset;
        }

        public void DelayAdd() {
            Config.Subtitles.Delay += Config.Player.SubtitlesDelayOffset;
        }

        public void DelayRemove2() {
            Config.Subtitles.Delay -= Config.Player.SubtitlesDelayOffset2;
        }

        public void DelayAdd2() {
            Config.Subtitles.Delay += Config.Player.SubtitlesDelayOffset2;
        }

        public void Toggle() {
            Config.Subtitles.Enabled = !Config.Subtitles.Enabled;
        }
    }
}

using System;

namespace FSPreview.MediaFramework.MediaInput
{
    public class VideoInput : InputBase
    {
        public double       FPS         { get; set; }
        public int          Height      { get; set; }
        public int          Width       { get; set; }

        public bool         HasAudio    { get; set; }
        public bool         SearchedForSubtitles 
                                        { get; set; }
    }
}

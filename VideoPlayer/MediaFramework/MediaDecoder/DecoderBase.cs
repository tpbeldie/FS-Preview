using FFmpeg.AutoGen;
using FSPreview.MediaFramework.MediaDemuxer;
using FSPreview.MediaFramework.MediaStream;
using static FFmpeg.AutoGen.ffmpeg;

namespace FSPreview.MediaFramework.MediaDecoder
{
    public abstract unsafe class DecoderBase : RunThreadBase
    {
        public MediaType Type { get; protected set; }

        public bool OnVideoDemuxer => m_demuxer?.Type == MediaType.Video;
      
        public Demuxer Demuxer => m_demuxer;
      
        public StreamBase Stream { get; protected set; }
       
        public AVCodecContext* CodecCtx => m_codecCtx;
       
        public Action<DecoderBase> CodecChanged { get; set; }
       
        public Config Config { get; protected set; }
     
        public int Speed { get; set; } = 1;

        protected int curSpeedFrame = 1;

        protected AVFrame* frame;

        protected AVCodecContext* m_codecCtx;

        internal object m_lockCodecCtx = new object();

        protected Demuxer m_demuxer;

        public DecoderBase(Config config, int uniqueId = -1) : base(uniqueId) {
            Config = config;
            if (this is VideoDecoder) {
                Type = MediaType.Video;
            }
            else if (this is AudioDecoder) {
                Type = MediaType.Audio;
            }
            else if (this is SubtitlesDecoder) {
                Type = MediaType.Subs;
            }
            threadName = $"Decoder: {Type.ToString().PadLeft(5, ' ')}";
        }

        public string Open(StreamBase stream) {
            lock (lockActions) {
                StreamBase prevStream = Stream;
                Dispose();
                int ret = -1;
                string error = null;
                try {
                    if (stream == null || stream.Demuxer.Interrupter.ForceInterrupt == 1 || stream.Demuxer.Disposed) {
                        return "Cancelled";
                    }
                    lock (stream.Demuxer.lockActions) {
                        if (stream == null || stream.Demuxer.Interrupter.ForceInterrupt == 1 || stream.Demuxer.Disposed) {
                            return "Cancelled";
                        }
                        Disposed = false;
                        Status = Status.Opening;
                        Stream = stream;
                        m_demuxer = stream.Demuxer;
                        AVCodec* codec = avcodec_find_decoder(stream.AVStream->codecpar->codec_id);
                        if (codec == null) {
                            return error = $"[{Type} avcodec_find_decoder] No suitable codec found";
                        }
                        m_codecCtx = avcodec_alloc_context3(null);
                        if (m_codecCtx == null) {
                            return error = $"[{Type} avcodec_alloc_context3] Failed to allocate context3";
                        }
                        ret = avcodec_parameters_to_context(m_codecCtx, stream.AVStream->codecpar);
                        if (ret < 0) {
                            return error = $"[{Type} avcodec_parameters_to_context] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})";
                        }
                        m_codecCtx->pkt_timebase = stream.AVStream->time_base;
                        m_codecCtx->codec_id = codec->id;
                        try { 
                            ret = Setup(codec); } 
                        catch (Exception e) { 
                            return error = $"[{Type} Setup] {e.Message}";
                        }
                        if (ret < 0) {
                            return error = $"[{Type} Setup] {ret}";
                        }
                        ret = avcodec_open2(m_codecCtx, codec, null);
                        if (ret < 0) {
                            return error = $"[{Type} avcodec_open2] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})";
                        }
                        frame = av_frame_alloc();
                        if (prevStream != null) {
                            if (prevStream.Demuxer.Type == stream.Demuxer.Type)
                                stream.Demuxer.SwitchStream(stream);
                            else if (!prevStream.Demuxer.Disposed) {
                                if (prevStream.Demuxer.Type == MediaType.Video) {
                                    prevStream.Demuxer.DisableStream(prevStream);
                                }
                                else if (prevStream.Demuxer.Type == MediaType.Audio || prevStream.Demuxer.Type == MediaType.Subs) {
                                    prevStream.Demuxer.Dispose();
                                }
                                stream.Demuxer.EnableStream(stream);
                            }
                        }
                        else {
                            stream.Demuxer.EnableStream(stream);
                        }
                        Status = Status.Stopped;
                        CodecChanged?.Invoke(this);
                        return null;
                    }
                } finally {
                    if (error != null) {
                        Dispose(true);
                    }
                }
            }
        }

        protected abstract int Setup(AVCodec* codec);

        public void Dispose(bool closeStream = false) {
            if (Disposed) {
                return;
            }
            lock (lockActions) {
                if (Disposed) {
                    return;
                }
                Stop();
                DisposeInternal();
                if (closeStream && Stream != null && !Stream.Demuxer.Disposed) {
                    if (Stream.Demuxer.Type == MediaType.Video) {
                        Stream.Demuxer.DisableStream(Stream);
                    }
                    else {
                        Stream.Demuxer.Dispose();
                    }
                }
                if (frame != null) {
                    fixed (AVFrame** ptr = &frame) {
                        av_frame_free(ptr);
                    }
                }
                if (m_codecCtx != null) {
                    // TBR possible not required, also in case of image codec it will through an access violation
                    // avcodec_flush_buffers(m_codecCtx);
                    avcodec_close(m_codecCtx);
                    fixed (AVCodecContext** ptr = &m_codecCtx) {
                        avcodec_free_context(ptr);
                    }
                }
                m_demuxer = null;
                Stream = null;
                Status = Status.Stopped;
                curSpeedFrame = Speed;
                Disposed = true;
                Log("Disposed");
            }
        }

        protected abstract void DisposeInternal();

    }
}
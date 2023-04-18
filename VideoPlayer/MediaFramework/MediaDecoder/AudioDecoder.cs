using FFmpeg.AutoGen;
using FSPreview.MediaFramework.MediaFrame;
using FSPreview.MediaFramework.MediaRemuxer;
using FSPreview.MediaFramework.MediaStream;
using System.Collections.Concurrent;
using System.Security;
using static FFmpeg.AutoGen.ffmpeg;

namespace FSPreview.MediaFramework.MediaDecoder
{
    public unsafe class AudioDecoder : DecoderBase
    {
        public AudioStream AudioStream => (AudioStream)Stream;

        public VideoDecoder VideoDecoder { get; internal set; } // For Resync

        public ConcurrentQueue<AudioFrame> Frames { get; protected set; } = new ConcurrentQueue<AudioFrame>();

        static AVSampleFormat AOutSampleFormat = AVSampleFormat.AV_SAMPLE_FMT_S16;
    
        static int AOutChannelLayout = AV_CH_LAYOUT_STEREO;
     
        static int AOutChannels = av_get_channel_layout_nb_channels((ulong)AOutChannelLayout);
     
        SwrContext* m_swrCtx;
     
        byte[] m_circularBuffer;
     
        AVFrame* m_circularFrame;
     
        int m_circularBufferPos;

        internal bool m_keyFrameRequired;

        public AudioDecoder(Config config, int uniqueId = -1, VideoDecoder syncDecoder = null) : base(config, uniqueId) { VideoDecoder = syncDecoder; }

        protected override unsafe int Setup(AVCodec* codec) {
            int ret;
            if (m_swrCtx == null) {
                m_swrCtx = swr_alloc();
            }
            m_circularBufferPos = 0;
            m_circularBuffer = new byte[2 * 1024 * 1024]; // TBR: Should be based on max audio frames, max samples buffer size & max buffers used by xaudio2
            m_circularFrame = av_frame_alloc();
            av_opt_set_int(m_swrCtx, "in_channel_layout", (int)m_codecCtx->channel_layout, 0);
            av_opt_set_int(m_swrCtx, "in_channel_count", m_codecCtx->channels, 0);
            av_opt_set_int(m_swrCtx, "in_sample_rate", m_codecCtx->sample_rate, 0);
            av_opt_set_sample_fmt(m_swrCtx, "in_sample_fmt", m_codecCtx->sample_fmt, 0);
            av_opt_set_int(m_swrCtx, "out_channel_layout", AOutChannelLayout, 0);
            av_opt_set_int(m_swrCtx, "out_channel_count", AOutChannels, 0);
            av_opt_set_int(m_swrCtx, "out_sample_rate", m_codecCtx->sample_rate, 0);
            av_opt_set_sample_fmt(m_swrCtx, "out_sample_fmt", AOutSampleFormat, 0);
            ret = swr_init(m_swrCtx);
            if (ret < 0) {
                Log($"[AudioSetup] [ERROR-1] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})");
            }
            m_keyFrameRequired = !VideoDecoder.Disposed;
            return ret;
        }

        protected override void DisposeInternal() {
            DisposeFrames();
            if (m_swrCtx != null) {
                swr_close(m_swrCtx);
                fixed (SwrContext** ptr = &m_swrCtx) {
                    swr_free(ptr);
                }
                m_swrCtx = null;
            }
            if (m_circularFrame != null) {
                fixed (AVFrame** ptr = &m_circularFrame) {
                    av_frame_free(ptr);
                }
                m_circularFrame = null;
            }
            m_circularBuffer = null;
        }

        public void DisposeFrames() {
            Frames = new ConcurrentQueue<AudioFrame>();
        }

        public void Flush() {
            lock (lockActions)
                lock (m_lockCodecCtx) {
                    if (Disposed) {
                        return;
                    }
                    if (Status == Status.Ended) {
                        Status = Status.Stopped;
                    }
                    DisposeFrames();
                    avcodec_flush_buffers(m_codecCtx);
                    m_keyFrameRequired = !VideoDecoder.Disposed;
                    curSpeedFrame = Speed;
                }
        }

        protected override void RunInternal() {
            int ret = 0;
            int allowedErrors = Config.Decoder.MaxErrors;
            AVPacket* packet;
            do {
                // Wait until Queue not Full or Stopped
                if (Frames.Count >= Config.Decoder.MaxAudioFrames) {
                    lock (lockStatus)
                        if (Status == Status.Running) {
                            Status = Status.QueueFull;
                        }
                    while (Frames.Count >= Config.Decoder.MaxAudioFrames && Status == Status.QueueFull) {
                        Thread.Sleep(20);
                    }
                    lock (lockStatus) {
                        if (Status != Status.QueueFull) break;
                        Status = Status.Running;
                    }
                }
                // While Packets Queue Empty (Ended | Quit if Demuxer stopped | Wait until we get packets)
                if (m_demuxer.AudioPackets.Count == 0) {
                    CriticalArea = true;
                    lock (lockStatus)
                        if (Status == Status.Running) {
                            Status = Status.QueueEmpty;
                        }
                    while (m_demuxer.AudioPackets.Count == 0 && Status == Status.QueueEmpty) {
                        if (m_demuxer.Status == Status.Ended) {
                            Status = Status.Ended;
                            break;
                        }
                        else if (!m_demuxer.IsRunning) {
                            Log($"Demuxer is not running [Demuxer Status: {m_demuxer.Status}]");
                            int retries = 5;
                            while (retries > 0) {
                                retries--;
                                Thread.Sleep(10);
                                if (m_demuxer.IsRunning) {
                                    break;
                                }
                            }
                            lock (m_demuxer.lockStatus)
                                lock (lockStatus) {
                                    if (m_demuxer.Status == Status.Pausing || m_demuxer.Status == Status.Paused) {
                                        Status = Status.Pausing;
                                    }
                                    else if (m_demuxer.Status != Status.Ended) {
                                        Status = Status.Stopping;
                                    }
                                    else {
                                        continue;
                                    }
                                }
                            break;
                        }
                        Thread.Sleep(20);
                    }
                    lock (lockStatus) {
                        CriticalArea = false;
                        if (Status != Status.QueueEmpty) {
                            break;
                        }
                        Status = Status.Running;
                    }
                }
                lock (m_lockCodecCtx) {
                    if (Status == Status.Stopped || m_demuxer.AudioPackets.Count == 0) continue;
                    m_demuxer.AudioPackets.TryDequeue(out IntPtr pktPtr);
                    packet = (AVPacket*)pktPtr;
                    if (m_isRecording) {
                        if (!m_recGotKeyframe && VideoDecoder.StartRecordTime != AV_NOPTS_VALUE && (long)(packet->pts * AudioStream.Timebase) - m_demuxer.StartTime > VideoDecoder.StartRecordTime) {
                            m_recGotKeyframe = true;
                        }
                        if (m_recGotKeyframe) {
                            m_curRecorder.Write(av_packet_clone(packet), !OnVideoDemuxer);
                        }
                    }
                    ret = avcodec_send_packet(m_codecCtx, packet);
                    av_packet_free(&packet);
                    if (ret != 0 && ret != AVERROR(EAGAIN)) {
                        if (ret == AVERROR_EOF) {
                            Status = Status.Ended;
                            break;
                        }
                        else {
                            allowedErrors--;
                            Log($"[ERROR-2] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})");
                            if (allowedErrors == 0) {
                                Log("[ERROR-0] Too many errors!");
                                Status = Status.Stopping;
                                break;
                            }
                            continue;
                        }
                    }
                    while (true) {
                        ret = avcodec_receive_frame(m_codecCtx, frame);
                        if (ret != 0) {
                            av_frame_unref(frame);
                            break;
                        }
                        frame->pts = frame->best_effort_timestamp == AV_NOPTS_VALUE ? frame->pts : frame->best_effort_timestamp;
                        if (frame->pts == AV_NOPTS_VALUE) {
                            av_frame_unref(frame);
                            continue;
                        }
                        AudioFrame mFrame = ProcessAudioFrame(frame);
                        if (mFrame != null) {
                            Frames.Enqueue(mFrame);
                        }
                        av_frame_unref(frame);
                    }
                }

            } while (Status == Status.Running);
            /* ::::::::::::::::::::::::::::::::::: */
            if (m_isRecording) {
                StopRecording();
                m_recCompleted(MediaType.Audio);
            }
        }

        [SecurityCritical]
        private AudioFrame ProcessAudioFrame(AVFrame* frame) {
            // TBR: AVStream doesn't refresh, we can get the updated info only from m_codecCtx (what about timebase, what about re-opening the codec?)
            if (AudioStream.SampleRate != m_codecCtx->sample_rate || AudioStream.CodecID != m_codecCtx->codec_id || AudioStream.Channels != m_codecCtx->channels) {
                if (Disposed) {
                    return null;
                }
                Log($"Codec changed {AudioStream.CodecID} {AudioStream.SampleRate} => {m_codecCtx->codec_id} {m_codecCtx->sample_rate}");
                DisposeInternal();
                AudioStream.SampleFormat = (AVSampleFormat)Enum.ToObject(typeof(AVSampleFormat), m_codecCtx->sample_fmt);
                AudioStream.SampleFormatStr = AudioStream.SampleFormat.ToString().Replace("AV_SAMPLE_FMT_", "").ToLower();
                AudioStream.SampleRate = m_codecCtx->sample_rate;
                AudioStream.ChannelLayout = m_codecCtx->channel_layout;
                AudioStream.Channels = m_codecCtx->channels;
                AudioStream.Bits = m_codecCtx->bits_per_coded_sample;
                Setup(m_codecCtx->codec);
                CodecChanged?.Invoke(this);
            }
            if (Speed != 1) {
                curSpeedFrame++;
                if (curSpeedFrame < Speed) {
                    return null;
                }
                curSpeedFrame = 0;
            }
            AudioFrame mFrame = new AudioFrame();
            mFrame.timestamp = ((long)(frame->pts * AudioStream.Timebase) - m_demuxer.StartTime) + Config.Audio.Delay;
            // TODO: based on VideoStream's StartTime and not Demuxer's
            // mFrame.timestamp = (long)(frame->pts * AudioStream.Timebase) - AudioStream.StartTime - (VideoDecoder.VideoStream.StartTime - AudioStream.StartTime) + Config.Audio.Delay;
            // Log($"Decoding {Utils.TicksToTime(mFrame.timestamp)} | {Utils.TicksToTime((long)(mFrame.pts * AudioStream.Timebase))}");
            // Resync with VideoDecoder if required (drop early timestamps)
            if (m_keyFrameRequired) {
                while (VideoDecoder.StartTime == AV_NOPTS_VALUE && VideoDecoder.IsRunning && m_keyFrameRequired) Thread.Sleep(10);
                if (mFrame.timestamp < VideoDecoder.StartTime) {
                    // TODO: in case of long distance will spin (CPU issue), possible reseek?
                    // Log($"Droping {Utils.TicksToTime(mFrame.timestamp)} < {Utils.TicksToTime(VideoDecoder.StartTime)}");
                    return null;
                }
                else {
                    m_keyFrameRequired = false;
                }
            }
            try {
                int ret;
                if (m_circularFrame->nb_samples != frame->nb_samples) {
                    m_circularFrame->nb_samples = (int)av_rescale_rnd(swr_get_delay(m_swrCtx, m_codecCtx->sample_rate) + frame->nb_samples, m_codecCtx->sample_rate, m_codecCtx->sample_rate, AVRounding.AV_ROUND_UP);
                    fixed (byte* ptr = &m_circularBuffer[m_circularBufferPos]) {
                        av_samples_fill_arrays((byte**)&m_circularFrame->data, (int*)&m_circularFrame->linesize, ptr, AOutChannels, m_circularFrame->nb_samples, AOutSampleFormat, 0);
                    }
                }
                fixed (byte* circularBufferPosPtr = &m_circularBuffer[m_circularBufferPos]) {
                    *(byte**)&m_circularFrame->data = circularBufferPosPtr;
                    ret = swr_convert(m_swrCtx, (byte**)&m_circularFrame->data, m_circularFrame->nb_samples, (byte**)&frame->data, frame->nb_samples);
                    if (ret < 0) {
                        return null;
                    }
                    mFrame.dataLen = av_samples_get_buffer_size((int*)&m_circularFrame->linesize, AOutChannels, ret, AOutSampleFormat, 1);
                    mFrame.dataPtr = (IntPtr)circularBufferPosPtr;
                }
                // TBR: Randomly gives the max samples size to half buffer
                m_circularBufferPos += mFrame.dataLen;
                if (m_circularBufferPos > m_circularBuffer.Length / 2) {
                    m_circularBufferPos = 0;
                }
            } catch (Exception e) {
                Log("[ProcessAudioFrame] [Error] " + e.Message + " - " + e.StackTrace);
                return null;
            }
            return mFrame;
        }

        internal Action<MediaType> m_recCompleted;

        Remuxer m_curRecorder;

        bool m_recGotKeyframe;

        internal bool m_isRecording;

        internal void StartRecording(Remuxer remuxer, long startAt = -1) {
            if (Disposed || m_isRecording) {
                return;
            }
            m_curRecorder = remuxer;
            m_isRecording = true;
            m_recGotKeyframe = VideoDecoder.Disposed || VideoDecoder.Stream == null;
        }

        internal void StopRecording() {
            m_isRecording = false;
        }
    }
}
﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;
using static FFmpeg.AutoGen.AVCodecID;

using Vortice;
using Vortice.DXGI;
using Vortice.Direct3D11;
using Vortice.Mathematics;

using ID3D11Texture2D = Vortice.Direct3D11.ID3D11Texture2D;

using FSPreview.MediaFramework.MediaStream;
using FSPreview.MediaFramework.MediaFrame;
using FSPreview.MediaFramework.MediaRenderer;
using FSPreview.MediaFramework.MediaRemuxer;

namespace FSPreview.MediaFramework.MediaDecoder
{
    public unsafe class VideoDecoder : DecoderBase
    {
        public ConcurrentQueue<VideoFrame>
                                Frames              { get; protected set; } = new ConcurrentQueue<VideoFrame>();
        public Renderer         Renderer            { get; private set; }
        public bool             VideoAccelerated    { get; internal set; }
        public bool             ZeroCopy            { get; internal set; }
        
        public VideoStream      VideoStream         => (VideoStream) Stream;

        public long             StartTime           { get; internal set; } = AV_NOPTS_VALUE;
        public long             StartRecordTime     { get; internal set; } = AV_NOPTS_VALUE;

        // Hardware & Software_Handled (Y_UV | Y_U_V)
        Texture2DDescription    textDesc, textDescUV;

        // Software_Sws (RGBA)
        const AVPixelFormat     VOutPixelFormat = AVPixelFormat.AV_PIX_FMT_RGBA;
        const int               SCALING_HQ = SWS_ACCURATE_RND | SWS_BITEXACT | SWS_LANCZOS | SWS_FULL_CHR_H_INT | SWS_FULL_CHR_H_INP;
        const int               SCALING_LQ = SWS_BICUBIC;

        SwsContext*             swsCtx;
        IntPtr                  outBufferPtr;
        int                     outBufferSize;
        byte_ptrArray4          outData;
        int_array4              outLineSize;

        internal bool           keyFrameRequired;
        bool HDRDataSent;

        // Reverse Playback
        ConcurrentStack<List<IntPtr>>   curReverseVideoStack    = new ConcurrentStack<List<IntPtr>>();
        List<IntPtr>                    curReverseVideoPackets  = new List<IntPtr>();
        List<VideoFrame>                curReverseVideoFrames   = new List<VideoFrame>();
        int                             curReversePacketPos     = 0;

        public VideoDecoder(Config config, Control control = null, int uniqueId = -1, bool initVA = true) : base(config, uniqueId)
        {
            getHWformat = new AVCodecContext_get_format(get_format);

            if (initVA)
            {
                Renderer = new Renderer(this, control, UniqueId);
                Renderer.Initialize();
                Disposed = false;
            }
        }

        #region Video Acceleration (Should be disposed seperately)
        const int               AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX = 0x01;
        const AVHWDeviceType    HW_DEVICE   = AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA; // To fully support Win7/8 should consider AV_HWDEVICE_TYPE_DXVA2
        const AVPixelFormat     HW_PIX_FMT  = AVPixelFormat.AV_PIX_FMT_D3D11;
        internal ID3D11Texture2D
                                textureFFmpeg;
        AVCodecContext_get_format 
                                getHWformat;
        bool                    disableGetFormat;
        AVBufferRef*            hwframes;
        AVBufferRef*            hw_device_ctx;

        internal static bool CheckCodecSupport(AVCodec* codec)
        {
            for (int i = 0; ; i++)
            {
                AVCodecHWConfig* config = avcodec_get_hw_config(codec, i);
                if (config == null) break;
                if ((config->methods & AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX) == 0 || config->pix_fmt == AVPixelFormat.AV_PIX_FMT_NONE) continue;

                if (config->device_type == HW_DEVICE && config->pix_fmt == HW_PIX_FMT) return true;
            }

            return false;
        }
        internal int InitVA()
        {
            int ret;
            AVHWDeviceContext*      device_ctx;
            AVD3D11VADeviceContext* d3d11va_device_ctx;

            if (Renderer.Device == null || hw_device_ctx != null) return -1;

            hw_device_ctx  = av_hwdevice_ctx_alloc(HW_DEVICE);

            device_ctx          = (AVHWDeviceContext*) hw_device_ctx->data;
            d3d11va_device_ctx  = (AVD3D11VADeviceContext*) device_ctx->hwctx;
            d3d11va_device_ctx->device
                                = (FFmpeg.AutoGen.ID3D11Device*) Renderer.Device.NativePointer;

            ret = av_hwdevice_ctx_init(hw_device_ctx);
            if (ret != 0)
            {
                Log($"[ERROR-1]{Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})");
                
                fixed(AVBufferRef** ptr = &hw_device_ctx)
                    av_buffer_unref(ptr);

                hw_device_ctx = null;
            }

            Renderer.Device.AddRef(); // Important to give another reference for FFmpeg so we can dispose without issues

            return ret;
        }
        
        private AVPixelFormat get_format(AVCodecContext* avctx, AVPixelFormat* pix_fmts)
        {
            if (disableGetFormat)
                return avcodec_default_get_format(avctx, pix_fmts);

            int  ret = 0;
            bool foundHWformat = false;
            
            while (*pix_fmts != AVPixelFormat.AV_PIX_FMT_NONE)
            {
                #if DEBUG
                Log($"{*pix_fmts}");
                #endif

                if (*pix_fmts == AVPixelFormat.AV_PIX_FMT_D3D11)
                {
                    foundHWformat = true;
                    break;
                }

                pix_fmts++;
            }

            ret = ShouldAllocateNew();

            if (foundHWformat && ret == 0)
            {
                #if DEBUG
                Log("[HW] Frames already allocated");
                #endif

                if (hwframes != null && m_codecCtx->hw_frames_ctx == null)
                    m_codecCtx->hw_frames_ctx = av_buffer_ref(hwframes);

                textDesc.Format = textureFFmpeg.Description.Format;

                return AVPixelFormat.AV_PIX_FMT_D3D11;
            }

            lock (m_lockCodecCtx)
            {
                if (!foundHWformat || !VideoAccelerated) // TODO check failed
                {
                    #if DEBUG
                    Log("[HW] Format not found. Fallback to sw format");
                    #endif

                    lock (Renderer.lockDevice)
                    {
                        VideoAccelerated = false;
                        ZeroCopy = false;

                        if (hw_device_ctx != null)
                        fixed(AVBufferRef** ptr = &hw_device_ctx)
                            av_buffer_unref(ptr);

                        if (m_codecCtx->hw_device_ctx != null)
                            av_buffer_unref(&m_codecCtx->hw_device_ctx);

                        hw_device_ctx = null;
                        m_codecCtx->hw_device_ctx = null;
                        textDesc.Format = VideoStream.PixelFormatDesc->comp.ToArray()[0].depth > 8 ? Format.R16_UNorm : Format.R8_UNorm;
                        CodecChanged?.Invoke(this);
                        Renderer?.FrameResized();

                        disableGetFormat = true;
                    }

                    return avcodec_default_get_format(avctx, pix_fmts);
                }

                // TBR: Catch codec changed on live streams (check codec/profiles and check even on sw frames)
                if (ret == 2)
                {
                    Log($"Codec changed {VideoStream.CodecID} {VideoStream.Width}x{VideoStream.Height} => {m_codecCtx->codec_id} {m_codecCtx->width}x{m_codecCtx->height}");

                    VideoStream.Width   = m_codecCtx->width;
                    VideoStream.Height  = m_codecCtx->height;
                }

                if (AllocateHWFrames(Config.Decoder.VAPoolSize + Config.Decoder.MaxVideoFrames + m_codecCtx->thread_count) == 0)
                    Log("[HW] Frame allocation completed");
                else
                {
                    Log("[HW] WARNING: Frame allocation failed");

                    return AVPixelFormat.AV_PIX_FMT_NONE;
                }

                return AVPixelFormat.AV_PIX_FMT_D3D11;
            }
        }
        private int ShouldAllocateNew() // 0: No, 1: Yes, 2: Yes+Codec Changed
        {
            if (hwframes == null)
                return 1;

            var t2 = (AVHWFramesContext*) hwframes->data;

            //if (m_codecCtx->codec_id != VideoStream.CodecID)
                //return 2;

            if (m_codecCtx->coded_width != t2->width)
                return 2;

            if (m_codecCtx->coded_height != t2->height)
                return 2;

            var fmt = m_codecCtx->sw_pix_fmt == AVPixelFormat.AV_PIX_FMT_YUV420P10LE ? AVPixelFormat.AV_PIX_FMT_P010LE : (m_codecCtx->sw_pix_fmt == AVPixelFormat.AV_PIX_FMT_P010BE ? AVPixelFormat.AV_PIX_FMT_P010BE : AVPixelFormat.AV_PIX_FMT_NV12);
            if (fmt != t2->sw_format)
                return 2;

            return 0;
        }
        private int AllocateHWFrames(int poolsize)
        {
            if (hwframes != null)
                fixed(AVBufferRef** ptr = &hwframes)
                    av_buffer_unref(ptr);
            
            hwframes = null;

            if (m_codecCtx->hw_frames_ctx != null)
                av_buffer_unref(&m_codecCtx->hw_frames_ctx);

            m_codecCtx->hw_frames_ctx = av_hwframe_ctx_alloc(m_codecCtx->hw_device_ctx);
            if (m_codecCtx->hw_frames_ctx == null)
                return -1;

            //m_codecCtx->hwaccel_context = null;
            //m_codecCtx->hwaccel_flags |= 2; //AV_CODEC_HW_CONFIG_METHOD_HW_FRAMES_CTX

            // (Surface alignment & Number of Surfaces) | https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/dxva2.c
            
            int surface_alignment, num_surfaces;

            /* decoding MPEG-2 requires additional alignment on some Intel GPUs,
            but it causes issues for H.264 on certain AMD GPUs..... */
            if (m_codecCtx->codec_id == AV_CODEC_ID_MPEG2VIDEO)
                surface_alignment = 32;

            /* the HEVC DXVA2 spec asks for 128 pixel aligned surfaces to ensure
            all coding features have enough room to work with */
            else if (m_codecCtx->codec_id == AV_CODEC_ID_HEVC || m_codecCtx->codec_id == AV_CODEC_ID_AV1)
                surface_alignment = 128;
            else
                surface_alignment = 16;

            /* 1 base work surface */
            num_surfaces = 1;

            /* add surfaces based on number of possible refs */
            if (m_codecCtx->codec_id == AV_CODEC_ID_H264 || m_codecCtx->codec_id == AV_CODEC_ID_HEVC)
                num_surfaces += 16;
            else if (m_codecCtx->codec_id == AV_CODEC_ID_VP9 || m_codecCtx->codec_id == AV_CODEC_ID_AV1)
                num_surfaces += 8;
            else
                num_surfaces += 2;

            // We guarantee 4 base work surfaces. The function above guarantees 1
            // (the absolute minimum), so add the missing count.
            num_surfaces += 3;

            var t2 = (AVHWFramesContext*)(m_codecCtx->hw_frames_ctx->data);
            t2->format      = AVPixelFormat.AV_PIX_FMT_D3D11;
            t2->sw_format   = m_codecCtx->sw_pix_fmt == AVPixelFormat.AV_PIX_FMT_YUV420P10LE ? AVPixelFormat.AV_PIX_FMT_P010LE : (m_codecCtx->sw_pix_fmt == AVPixelFormat.AV_PIX_FMT_P010BE ? AVPixelFormat.AV_PIX_FMT_P010BE : AVPixelFormat.AV_PIX_FMT_NV12);
            t2->width       = Utils.Align(m_codecCtx->coded_width,  surface_alignment);
            t2->height      = Utils.Align(m_codecCtx->coded_height, surface_alignment);
            t2->initial_pool_size = Math.Max(num_surfaces, poolsize);

            AVD3D11VAFramesContext *t3 = (AVD3D11VAFramesContext *)t2->hwctx;
            t3->BindFlags  |= (uint)BindFlags.Decoder | (uint)BindFlags.ShaderResource;
            
            hwframes = av_buffer_ref(m_codecCtx->hw_frames_ctx);

            int ret = av_hwframe_ctx_init(m_codecCtx->hw_frames_ctx);
            if (ret == 0)
            {
                lock (Renderer.lockDevice)
                {
                    textureFFmpeg   = new ID3D11Texture2D((IntPtr) t3->texture);
                    textDesc.Format = textureFFmpeg.Description.Format;
                    ZeroCopy = Config.Decoder.ZeroCopy == FSPreview.ZeroCopy.Enabled || (Config.Decoder.ZeroCopy == FSPreview.ZeroCopy.Auto && m_codecCtx->width == textureFFmpeg.Description.Width && m_codecCtx->height == textureFFmpeg.Description.Height);
                    Renderer?.FrameResized();
                    CodecChanged?.Invoke(this);
                }
            }

            return ret;
        }
        internal void RecalculateZeroCopy()
        {
            lock (Renderer.lockDevice)
            {
                bool save = ZeroCopy;
                ZeroCopy = Config.Decoder.ZeroCopy == FSPreview.ZeroCopy.Enabled || (Config.Decoder.ZeroCopy == FSPreview.ZeroCopy.Auto && m_codecCtx->width == textureFFmpeg.Description.Width && m_codecCtx->height == textureFFmpeg.Description.Height);
                if (save != ZeroCopy)
                {
                    Renderer?.FrameResized();
                    CodecChanged?.Invoke(this);
                }
            }
        }
        #endregion

        protected override int Setup(AVCodec* codec)
        {
            Renderer?.Initialize();
            VideoAccelerated = false;

            if (Config.Video.VideoAcceleration)
            {
                if (CheckCodecSupport(codec))
                {
                    if (InitVA() == 0)
                    {
                        m_codecCtx->hw_device_ctx = av_buffer_ref(hw_device_ctx);
                        VideoAccelerated = true;
                        Log("[VA] Success");
                    }
                    else
                        Log("[VA] Init failed");
                }
                else
                    Log("[VA] Codec not supported");
            }
            else
                Log("[VA] Disabled");

            int bits = 0;
            try
            {
                bits = VideoStream.PixelFormatDesc->comp.ToArray()[0].depth;
            } catch { }

            textDesc = new Texture2DDescription()
            {
                Usage               = ResourceUsage.Default,
                BindFlags           = BindFlags.ShaderResource | BindFlags.RenderTarget,

                Format              = bits > 8 ? Format.R16_UNorm : Format.R8_UNorm,
                Width               = m_codecCtx->width,
                Height              = m_codecCtx->height,

                SampleDescription   = new SampleDescription(1, 0),
                ArraySize           = 1,
                MipLevels           = 1
            };

            textDescUV = new Texture2DDescription()
            {
                Usage               = ResourceUsage.Default,
                BindFlags           = BindFlags.ShaderResource | BindFlags.RenderTarget,

                Format              = bits > 8 ? Format.R16_UNorm : Format.R8_UNorm,
                Width               = m_codecCtx->width  >> VideoStream.PixelFormatDesc->log2_chroma_w,
                Height              = m_codecCtx->height >> VideoStream.PixelFormatDesc->log2_chroma_h,

                SampleDescription   = new SampleDescription(1, 0),
                ArraySize           = 1,
                MipLevels           = 1
            };
            
            // Can't get data from here?
            //var t1 = av_stream_get_side_data(VideoStream.AVStream, AVPacketSideDataType.AV_PKT_DATA_MASTERING_DISPLAY_METADATA, null);
            //var t2 = av_stream_get_side_data(VideoStream.AVStream, AVPacketSideDataType.AV_PKT_DATA_CONTENT_LIGHT_LEVEL, null);

            HDRDataSent = false;
            keyFrameRequired = true;
            ZeroCopy = false;

            // TBR: Is this only for non-hwaccel? when trying FF_THREAD_FRAME causes ffmpeg to create new frame hw pool
            m_codecCtx->thread_count = Math.Min(Config.Decoder.VideoThreads, m_codecCtx->codec_id == AV_CODEC_ID_HEVC ? 32 : 16);
            m_codecCtx->thread_type  = 0;

            if (VideoAccelerated)
            {
                m_codecCtx->pix_fmt = AVPixelFormat.AV_PIX_FMT_D3D11;
                m_codecCtx->get_format = getHWformat;
                disableGetFormat = false;
            }
            else
            {
                Renderer?.FrameResized();
            }

            return 0;
        }
        internal void Flush()
        {
            lock (lockActions)
            lock (m_lockCodecCtx)
            {
                if (Disposed) return;

                if (Status == Status.Ended) Status = Status.Stopped;
                else if (Status == Status.Draining) Status = Status.Stopping;

                DisposeFrames();
                avcodec_flush_buffers(m_codecCtx);
                
                keyFrameRequired = true;
                StartTime = AV_NOPTS_VALUE;
                curSpeedFrame = Speed;
            }
        }

        protected override void RunInternal()
        {
            if (m_demuxer.IsReversePlayback)
            {
                RunInternalReverse();
                return;
            }

            int ret = 0;
            int allowedErrors = Config.Decoder.MaxErrors;
            AVPacket *packet;

            do
            {
                // Wait until Queue not Full or Stopped
                if (Frames.Count >= Config.Decoder.MaxVideoFrames)
                {
                    lock (lockStatus)
                        if (Status == Status.Running) Status = Status.QueueFull;

                    while (Frames.Count >= Config.Decoder.MaxVideoFrames && Status == Status.QueueFull) Thread.Sleep(20);

                    lock (lockStatus)
                    {
                        if (Status != Status.QueueFull) break;
                        Status = Status.Running;
                    }
                }

                // While Packets Queue Empty (Drain | Quit if Demuxer stopped | Wait until we get packets)
                if (m_demuxer.VideoPackets.Count == 0)
                {
                    CriticalArea = true;

                    lock (lockStatus)
                        if (Status == Status.Running) Status = Status.QueueEmpty;

                    while (m_demuxer.VideoPackets.Count == 0 && Status == Status.QueueEmpty)
                    {
                        if (m_demuxer.Status == Status.Ended)
                        {
                            lock (lockStatus)
                            {
                                // TODO: let the m_demuxer push the draining packet
                                Log("Draining...");
                                Status = Status.Draining;
                                AVPacket* drainPacket = av_packet_alloc();
                                drainPacket->data = null;
                                drainPacket->size = 0;
                                m_demuxer.VideoPackets.Enqueue((IntPtr)drainPacket);
                            }
                            
                            break;
                        }
                        else if (!m_demuxer.IsRunning)
                        {
                            Log($"Demuxer is not running [Demuxer Status: {m_demuxer.Status}]");

                            int retries = 5;

                            while (retries > 0)
                            {
                                retries--;
                                Thread.Sleep(10);
                                if (m_demuxer.IsRunning) break;
                            }

                            lock (m_demuxer.lockStatus)
                            lock (lockStatus)
                            {
                                if (m_demuxer.Status == Status.Pausing || m_demuxer.Status == Status.Paused)
                                    Status = Status.Pausing;
                                else if (m_demuxer.Status != Status.Ended)
                                    Status = Status.Stopping;
                                else
                                    continue;
                            }

                            break;
                        }
                        
                        Thread.Sleep(20);
                    }

                    lock (lockStatus)
                    {
                        CriticalArea = false;
                        if (Status != Status.QueueEmpty && Status != Status.Draining) break;
                        if (Status != Status.Draining) Status = Status.Running;
                    }
                }

                lock (m_lockCodecCtx)
                {
                    if (Status == Status.Stopped || m_demuxer.VideoPackets.Count == 0) continue;
                    m_demuxer.VideoPackets.TryDequeue(out IntPtr pktPtr);
                    packet = (AVPacket*) pktPtr;

                    if (isRecording)
                    {
                        if (!recGotKeyframe && (packet->flags & AV_PKT_FLAG_KEY) != 0)
                        {
                            recGotKeyframe = true;
                            StartRecordTime = (long)(packet->pts * VideoStream.Timebase) - m_demuxer.StartTime;
                        }

                        if (recGotKeyframe)
                            curRecorder.Write(av_packet_clone(packet));
                    }

                    // TBR: AVERROR(EAGAIN) means avcodec_receive_frame but after resend the same packet
                    ret = avcodec_send_packet(m_codecCtx, packet);
                    av_packet_free(&packet);

                    if (ret != 0 && ret != AVERROR(EAGAIN))
                    {
                        if (ret == AVERROR_EOF)
                        {
                            if (m_demuxer.VideoPackets.Count > 0) { avcodec_flush_buffers(m_codecCtx); continue; } // TBR: Happens on HLS while switching video streams
                            Status = Status.Ended;
                            break;
                        }
                        else
                        {
                            allowedErrors--;
                            Log($"[ERROR-2] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})");

                            if (allowedErrors == 0) { Log("[ERROR-0] Too many errors!"); Status = Status.Stopping; break; }

                            continue;
                        }
                    }
                    
                    while (true)
                    {
                        ret = avcodec_receive_frame(m_codecCtx, frame);
                        if (ret != 0) { av_frame_unref(frame); break; }

                        frame->pts = frame->best_effort_timestamp == AV_NOPTS_VALUE ? frame->pts : frame->best_effort_timestamp;
                        if (frame->pts == AV_NOPTS_VALUE) { av_frame_unref(frame); continue; }

                        if (keyFrameRequired)
                        {
                            if (frame->pict_type != AVPictureType.AV_PICTURE_TYPE_I)
                            {
                                Log($"Seek to keyframe failed [{frame->pict_type} | {frame->key_frame}]");
                                av_frame_unref(frame);
                                continue;
                            }
                            else
                            {
                                StartTime = (long)(frame->pts * VideoStream.Timebase) - m_demuxer.StartTime;
                                keyFrameRequired = false;
                            }
                        }

                        VideoFrame mFrame = ProcessVideoFrame(frame);
                        if (mFrame != null) Frames.Enqueue(mFrame);
                    }

                } // Lock CodecCtx

            } while (Status == Status.Running);

            if (isRecording) { StopRecording(); recCompleted(MediaType.Video); }

            if (Status == Status.Draining) Status = Status.Ended;
        }

        private void RunInternalReverse()
        {
            int ret = 0;
            int allowedErrors = Config.Decoder.MaxErrors;
            AVPacket *packet;

            do
            {
                // While Packets Queue Empty (Drain | Quit if Demuxer stopped | Wait until we get packets)
                if (m_demuxer.VideoPacketsReverse.Count == 0 && curReverseVideoStack.Count == 0 && curReverseVideoPackets.Count == 0)
                {
                    CriticalArea = true;

                    lock (lockStatus)
                        if (Status == Status.Running) Status = Status.QueueEmpty;
                    
                    while (m_demuxer.VideoPacketsReverse.Count == 0 && Status == Status.QueueEmpty)
                    {
                        if (m_demuxer.Status == Status.Ended) // TODO
                        {
                            lock (lockStatus) Status = Status.Ended;
                            
                            break;
                        }
                        else if (!m_demuxer.IsRunning)
                        {
                            Log($"Demuxer is not running [Demuxer Status: {m_demuxer.Status}]");

                            int retries = 5;

                            while (retries > 0)
                            {
                                retries--;
                                Thread.Sleep(10);
                                if (m_demuxer.IsRunning) break;
                            }

                            lock (m_demuxer.lockStatus)
                            lock (lockStatus)
                            {
                                if (m_demuxer.Status == Status.Pausing || m_demuxer.Status == Status.Paused)
                                    Status = Status.Pausing;
                                else if (m_demuxer.Status != Status.Ended)
                                    Status = Status.Stopping;
                                else
                                    continue;
                            }

                            break;
                        }
                        
                        Thread.Sleep(20);
                    }
                    
                    lock (lockStatus)
                    {
                        CriticalArea = false;
                        if (Status != Status.QueueEmpty) break;
                        Status = Status.Running;
                    }
                }

                if (curReverseVideoPackets.Count == 0)
                {
                    if (curReverseVideoStack.Count == 0)
                        m_demuxer.VideoPacketsReverse.TryDequeue(out curReverseVideoStack);

                    curReverseVideoStack.TryPop(out curReverseVideoPackets);
                    curReversePacketPos = 0;
                }

                keyFrameRequired = false;

                while (curReverseVideoPackets.Count > 0 && Status == Status.Running)
                {
                    // Wait until Queue not Full or Stopped
                    if (Frames.Count + curReverseVideoFrames.Count >= Config.Decoder.MaxVideoFrames)
                    {
                        lock (lockStatus)
                            if (Status == Status.Running) Status = Status.QueueFull;

                        while (Frames.Count + curReverseVideoFrames.Count >= Config.Decoder.MaxVideoFrames && Status == Status.QueueFull) Thread.Sleep(20);

                        lock (lockStatus)
                        {
                            if (Status != Status.QueueFull) break;
                            Status = Status.Running;
                        }
                    }

                    lock (m_lockCodecCtx)
                    {
                        if (keyFrameRequired == true)
                        {
                            curReversePacketPos = 0;
                            break;
                        }

                        packet = (AVPacket*)curReverseVideoPackets[curReversePacketPos++];
                        ret = avcodec_send_packet(m_codecCtx, packet);

                        if (ret != 0 && ret != AVERROR(EAGAIN))
                        {
                            if (ret == AVERROR_EOF) { Status = Status.Ended; break; }
                            
                            Log($"[ERROR-2] {Utils.FFmpeg.ErrorCodeToMsg(ret)} ({ret})");

                            allowedErrors--;
                            if (allowedErrors == 0) { Log("[ERROR-0] Too many errors!"); Status = Status.Stopping; break; }

                            for (int i=curReverseVideoPackets.Count-1; i>=curReversePacketPos-1; i--)
                            {
                                packet = (AVPacket*)curReverseVideoPackets[i];
                                av_packet_free(&packet);
                                curReverseVideoPackets[curReversePacketPos - 1] = IntPtr.Zero;
                                curReverseVideoPackets.RemoveAt(i);
                            }

                            avcodec_flush_buffers(m_codecCtx);
                            curReversePacketPos = 0;

                            for (int i=curReverseVideoFrames.Count -1; i>=0; i--)
                                Frames.Enqueue(curReverseVideoFrames[i]);

                            curReverseVideoFrames.Clear();

                            continue;
                        }

                        while (true)
                        {
                            ret = avcodec_receive_frame(m_codecCtx, frame);
                            if (ret != 0) { av_frame_unref(frame); break; }

                            frame->pts = frame->best_effort_timestamp == AV_NOPTS_VALUE ? frame->pts : frame->best_effort_timestamp;
                            if (frame->pts == AV_NOPTS_VALUE) { av_frame_unref(frame); continue; }

                            bool shouldProcess = curReverseVideoPackets.Count - curReversePacketPos < Config.Decoder.MaxVideoFrames;

                            if (shouldProcess)
                            {
                                av_packet_free(&packet);
                                curReverseVideoPackets[curReversePacketPos - 1] = IntPtr.Zero;
                                VideoFrame mFrame = ProcessVideoFrame(frame);
                                if (mFrame != null) curReverseVideoFrames.Add(mFrame);
                            }
                            else
                            av_frame_unref(frame);
                        }

                        if (curReversePacketPos == curReverseVideoPackets.Count)
                        {
                            curReverseVideoPackets.RemoveRange(Math.Max(0, curReverseVideoPackets.Count - Config.Decoder.MaxVideoFrames), Math.Min(curReverseVideoPackets.Count, Config.Decoder.MaxVideoFrames) );
                            avcodec_flush_buffers(m_codecCtx);
                            curReversePacketPos = 0;

                            for (int i = curReverseVideoFrames.Count -1; i>=0; i--)
                                Frames.Enqueue(curReverseVideoFrames[i]);

                            curReverseVideoFrames.Clear();
                            
                            break; // force recheck for max queues etc...
                        }

                    } // Lock CodecCtx

                    // Import Sleep required to prevent delay during Renderer.Present
                    // TBR: Might Monitor.TryEnter with priorities between decoding and rendering will work better
                    Thread.Sleep(10);
                    
                } // while curReverseVideoPackets.Count > 0

            } while (Status == Status.Running);

            if (Status != Status.Pausing && Status != Status.Paused)
                curReversePacketPos = 0;
        }
        
        internal VideoFrame ProcessVideoFrame(AVFrame* frame)
        {
            try
            {
                if (Speed != 1)
                {
                    curSpeedFrame++;
                    if (curSpeedFrame < Speed)
                        return null;

                    curSpeedFrame = 0;                    
                }
                
                VideoFrame mFrame = new VideoFrame();
                mFrame.timestamp = (long)(frame->pts * VideoStream.Timebase) - m_demuxer.StartTime;

                // TODO
                //mFrame.timestamp = (long)(frame->pts * VideoStream.Timebase) - VideoStream.StartTime;
                //Log($"Decoding {Utils.TicksToTime(mFrame.timestamp)}");

                if (!HDRDataSent && frame->side_data != null && *frame->side_data != null)
                {
                    HDRDataSent = true;
                    AVFrameSideData* sideData = *frame->side_data;
                    if (sideData->type == AVFrameSideDataType.AV_FRAME_DATA_MASTERING_DISPLAY_METADATA)
                        Renderer?.UpdateHDRtoSDR((AVMasteringDisplayMetadata*)sideData->data);
                }

                // Hardware Frame (NV12|P010)   | CopySubresourceRegion FFmpeg Texture Array -> Device Texture[1] (NV12|P010) / SRV (RX_RXGX) -> PixelShader (Y_UV)
                if (VideoAccelerated)
                {

                    // TBR: It is possible that FFmpeg will decide to re-create a new hw frames pool (if we provide wrong threads/initial pool size etc?)
                    //if ((IntPtr) frame->data.ToArray()[0] != (IntPtr) (((AVD3D11VAFramesContext *)((AVHWFramesContext*)hwframes->data)->hwctx)->texture))
                        //textureFFmpeg = new ID3D11Texture2D((IntPtr) frame->data.ToArray()[0]);

                    if (ZeroCopy)
                    {
                        mFrame.bufRef       = av_buffer_ref(frame->buf.ToArray()[0]);
                        mFrame.subresource  = (int) frame->data.ToArray()[1];
                    }
                    else
                    {
                        mFrame.textures     = new ID3D11Texture2D[1];
                        mFrame.textures[0]  = Renderer.Device.CreateTexture2D(textDesc);
                        Renderer.Device.ImmediateContext.CopySubresourceRegion(
                            mFrame.textures[0], 0, 0, 0, 0, // dst
                            textureFFmpeg, (int)frame->data.ToArray()[1],  // src
                            new Box(0, 0, 0, mFrame.textures[0].Description.Width, mFrame.textures[0].Description.Height, 1)); // crop decoder's padding
                    }
                }

                // Software Frame (8-bit YUV)   | YUV byte* -> Device Texture[3] (RX) / SRV (RX_RX_RX) -> PixelShader (Y_U_V)
                else if (VideoStream.PixelFormatType == PixelFormatType.Software_Handled)
                {
                    /* TODO
                     * Check which formats are suported from DXGI textures and the possibility to upload them directly so we can just blit them to rgba
                     * If not supported from DXGI just uploaded to one supported and process it on GPU with pixelshader
                     * Support > 8 bit
                     */

                    mFrame.textures = new ID3D11Texture2D[3];

                    // YUV Planar [Y0 ...] [U0 ...] [V0 ....]
                    if (VideoStream.IsPlanar)
                    {
                        SubresourceData db  = new SubresourceData();
                        db.DataPointer      = (IntPtr)frame->data.ToArray()[0];
                        db.RowPitch         = frame->linesize.ToArray()[0];
                        mFrame.textures[0]  = Renderer.Device.CreateTexture2D(textDesc, new SubresourceData[] { db });

                        db                  = new SubresourceData();
                        db.DataPointer      = (IntPtr)frame->data.ToArray()[1];
                        db.RowPitch         = frame->linesize.ToArray()[1];
                        mFrame.textures[1]  = Renderer.Device.CreateTexture2D(textDescUV, new SubresourceData[] { db });

                        db                  = new SubresourceData();
                        db.DataPointer      = (IntPtr)frame->data.ToArray()[2];
                        db.RowPitch         = frame->linesize.ToArray()[2];
                        mFrame.textures[2]  = Renderer.Device.CreateTexture2D(textDescUV, new SubresourceData[] { db });
                    }

                    // YUV Packed ([Y0U0Y1V0] ....)
                    else
                    {
                        DataStream dsY  = new DataStream(textDesc.  Width * textDesc.  Height, true, true);
                        DataStream dsU  = new DataStream(textDescUV.Width * textDescUV.Height, true, true);
                        DataStream dsV  = new DataStream(textDescUV.Width * textDescUV.Height, true, true);
                        SubresourceData    dbY  = new SubresourceData();
                        SubresourceData    dbU  = new SubresourceData();
                        SubresourceData    dbV  = new SubresourceData();

                        dbY.DataPointer = dsY.BasePointer;
                        dbU.DataPointer = dsU.BasePointer;
                        dbV.DataPointer = dsV.BasePointer;

                        dbY.RowPitch    = textDesc.  Width;
                        dbU.RowPitch    = textDescUV.Width;
                        dbV.RowPitch    = textDescUV.Width;

                        long totalSize = frame->linesize.ToArray()[0] * textDesc.Height;

                        byte* dataPtr = frame->data.ToArray()[0];
                        AVComponentDescriptor[] comps = VideoStream.PixelFormatDesc->comp.ToArray();

                        for (int i=0; i<totalSize; i+=VideoStream.Comp0Step)
                            dsY.WriteByte(*(dataPtr + i));

                        for (int i=1; i<totalSize; i+=VideoStream.Comp1Step)
                            dsU.WriteByte(*(dataPtr + i));

                        for (int i=3; i<totalSize; i+=VideoStream.Comp2Step)
                            dsV.WriteByte(*(dataPtr + i));

                        mFrame.textures[0] = Renderer.Device.CreateTexture2D(textDesc,   new SubresourceData[] { dbY });
                        mFrame.textures[1] = Renderer.Device.CreateTexture2D(textDescUV, new SubresourceData[] { dbU });
                        mFrame.textures[2] = Renderer.Device.CreateTexture2D(textDescUV, new SubresourceData[] { dbV });

                        dsY.Dispose(); dsU.Dispose(); dsV.Dispose();
                    }
                }

                // Software Frame (OTHER/sws_scale) | X byte* -> Sws_Scale RGBA -> Device Texture[1] (RGBA) / SRV (RGBA) -> PixelShader (RGBA)
                else
                {
                    if (swsCtx == null)
                    {
                        textDesc.Format = Format.R8G8B8A8_UNorm;
                        outData         = new byte_ptrArray4();
                        outLineSize     = new int_array4();
                        outBufferSize   = av_image_get_buffer_size(VOutPixelFormat, m_codecCtx->width, m_codecCtx->height, 1);
                        Marshal.FreeHGlobal(outBufferPtr);
                        outBufferPtr    = Marshal.AllocHGlobal(outBufferSize);
                        av_image_fill_arrays(ref outData, ref outLineSize, (byte*) outBufferPtr, VOutPixelFormat, m_codecCtx->width, m_codecCtx->height, 1);
                        
                        int vSwsOptFlags= Config.Video.SwsHighQuality ? SCALING_HQ : SCALING_LQ;
                        swsCtx          = sws_getContext(m_codecCtx->coded_width, m_codecCtx->coded_height, m_codecCtx->pix_fmt, m_codecCtx->width, m_codecCtx->height, VOutPixelFormat, vSwsOptFlags, null, null, null);
                        if (swsCtx == null) { Log($"[ProcessVideoFrame] [Error] Failed to allocate SwsContext"); return null; }
                    }

                    sws_scale(swsCtx, frame->data, frame->linesize, 0, frame->height, outData, outLineSize);

                    SubresourceData db  = new SubresourceData();
                    db.DataPointer      = (IntPtr)outData.ToArray()[0];
                    db.RowPitch         = outLineSize[0];
                    mFrame.textures     = new ID3D11Texture2D[1];
                    mFrame.textures[0]  = Renderer.Device.CreateTexture2D(textDesc, new SubresourceData[] { db });
                }

                av_frame_unref(frame);

                return mFrame;

            } catch (Exception e)
            {
                Log("[ProcessVideoFrame] [Error] " + e.Message + " - " + e.StackTrace);
                av_frame_unref(frame);

                return null; 
            }
        }

        public int GetFrameNumber(long timestamp)
        {
            // offset 2ms
            return (int) ((timestamp + 20000) / VideoStream.FrameDuration);
        }

        /// <summary>
        /// Performs accurate seeking to the requested video frame and returns it
        /// </summary>
        /// <param name="index">Zero based frame index</param>
        /// <returns>The requested VideoFrame or null on failure</returns>
        public VideoFrame GetFrame(int index)
        {
            int ret;

            // Calculation of FrameX timestamp (based on fps/avgFrameDuration) | offset 2ms
            long frameTimestamp = VideoStream.StartTime + (long) (index * VideoStream.FrameDuration) - 20000;
            //Log($"Searching for {Utils.TicksToTime(frameTimestamp)}");

            // Seeking at frameTimestamp or previous I/Key frame and flushing codec | Temp fix (max I/distance 3sec) for ffmpeg bug that fails to seek on keyframe with HEVC
            m_demuxer.Pause();
            Pause();
            m_demuxer.Interrupter.Request(MediaDemuxer.Requester.Seek);
            if (m_codecCtx->codec_id == AV_CODEC_ID_HEVC)
                ret = av_seek_frame(m_demuxer.FormatContext, -1, Math.Max(VideoStream.StartTime, frameTimestamp - (3 * (long)1000 * 10000)) / 10, AVSEEK_FLAG_ANY);
            else
                ret = av_seek_frame(m_demuxer.FormatContext, -1, frameTimestamp / 10, AVSEEK_FLAG_FRAME | AVSEEK_FLAG_BACKWARD);

            m_demuxer.DisposePackets();
            m_demuxer.UpdateCurTime();
            if (m_demuxer.Status == Status.Ended) m_demuxer.Status = Status.Stopped;
            if (ret < 0) return null; // handle seek error
            Flush();
            keyFrameRequired = false;
            StartTime = frameTimestamp - VideoStream.StartTime; // required for audio sync

            // Decoding until requested frame/timestamp
            bool checkExtraFrames = false;

            while (GetFrameNext(checkExtraFrames) == 0)
            {
                // Skip frames before our actual requested frame
                if ((long)(frame->best_effort_timestamp * VideoStream.Timebase) < frameTimestamp)
                {
                    //Log($"[Skip] [pts: {frame->best_effort_timestamp}] [time: {Utils.TicksToTime((long)(frame->best_effort_timestamp * VideoStream.Timebase))}]");
                    av_frame_unref(frame);
                    checkExtraFrames = true;
                    continue; 
                }

                //Log($"[Found] [pts: {frame->best_effort_timestamp}] [time: {Utils.TicksToTime((long)(frame->best_effort_timestamp * VideoStream.Timebase))}] | {Utils.TicksToTime(VideoStream.StartTime + (index * VideoStream.FrameDuration))}");
                return ProcessVideoFrame(frame);
            }

            return null;
        }

        /// <summary>
        /// Demuxes until the next valid video frame (will be stored in AVFrame* frame)
        /// </summary>
        /// <returns>0 on success</returns>
        /// 
        public VideoFrame GetFrameNext()
        {
            if (GetFrameNext(true) != 0) return null;

            return ProcessVideoFrame(frame);
        }

        /// <summary>
        /// Pushes the m_demuxer and the decoder to the next available video frame
        /// </summary>
        /// <param name="checkExtraFrames">Whether to check for extra frames within the decoder's cache. Set to true if not sure.</param>
        /// <returns></returns>
        public int GetFrameNext(bool checkExtraFrames)
        {
            // TODO: Should know if draining to be able to get more than one drained frames

            int ret;
            int allowedErrors = Config.Decoder.MaxErrors;

            if (checkExtraFrames)
            {
                ret = avcodec_receive_frame(m_codecCtx, frame);

                if (ret == 0)
                {
                    if (frame->best_effort_timestamp == AV_NOPTS_VALUE)
                        frame->best_effort_timestamp = frame->pts;

                    if (frame->best_effort_timestamp == AV_NOPTS_VALUE)
                    {
                        av_frame_unref(frame);
                        return GetFrameNext(true);
                    }

                    return 0;
                }

                if (ret != AVERROR(EAGAIN)) return ret;
            }

            while (true)
            {
                ret = m_demuxer.GetNextVideoPacket();
                if (ret != 0 && m_demuxer.Status != Status.Ended)
                    return ret;

                ret = avcodec_send_packet(m_codecCtx, m_demuxer.packet);
                av_packet_unref(m_demuxer.packet);

                if (ret != 0)
                {
                    if (allowedErrors < 1 || m_demuxer.Status == Status.Ended) return ret;
                    allowedErrors--;
                    continue;
                }

                ret = avcodec_receive_frame(m_codecCtx, frame);
                
                if (ret == AVERROR(EAGAIN))
                    continue;

                if (ret != 0)
                {
                    av_frame_unref(frame);
                    return ret;
                }

                if (frame->best_effort_timestamp == AV_NOPTS_VALUE)
                    frame->best_effort_timestamp = frame->pts;

                if (frame->best_effort_timestamp == AV_NOPTS_VALUE)
                {
                    av_frame_unref(frame);
                    return GetFrameNext(true);
                }

                return 0;
            }
        }

        public void DisposeFrames()
        {
            while (!Frames.IsEmpty)
            {
                Frames.TryDequeue(out VideoFrame frame);
                DisposeFrame(frame);
            }

            DisposeFramesReverse();
        }
        private void DisposeFramesReverse()
        {
            while (!curReverseVideoStack.IsEmpty)
            {
                curReverseVideoStack.TryPop(out var t2);
                for (int i = 0; i<t2.Count; i++)
                { 
                    if (t2[i] == IntPtr.Zero) continue;
                    AVPacket* packet = (AVPacket*)t2[i];
                    av_packet_free(&packet);
                }
            }

            for (int i = 0; i<curReverseVideoPackets.Count; i++)
            { 
                if (curReverseVideoPackets[i] == IntPtr.Zero) continue;
                AVPacket* packet = (AVPacket*)curReverseVideoPackets[i];
                av_packet_free(&packet);
            }

            curReverseVideoPackets.Clear();

            for (int i=0; i<curReverseVideoFrames.Count; i++)
                DisposeFrame(curReverseVideoFrames[i]);

            curReverseVideoFrames.Clear();
        }
        public static void DisposeFrame(VideoFrame frame)
        {
            if (frame == null)
                return;

            if (frame.textures != null)
                for (int i=0; i<frame.textures.Length; i++)
                    frame.textures[i].Dispose();

            if (frame.bufRef != null)
                fixed (AVBufferRef** ptr = &frame.bufRef)
                    av_buffer_unref(ptr);

            frame.textures  = null;
            frame.bufRef    = null;
        }
        protected override void DisposeInternal()
        {
            lock (m_lockCodecCtx)
            {
                DisposeFrames();
                Renderer.Dispose();

                if (m_codecCtx != null)
                {
                    avcodec_close(m_codecCtx);
                    fixed (AVCodecContext** ptr = &m_codecCtx) avcodec_free_context(ptr);

                    m_codecCtx = null;
                }

                if (hwframes != null)
                fixed(AVBufferRef** ptr = &hwframes)
                    av_buffer_unref(ptr);
                
                if (hw_device_ctx != null)
                    fixed(AVBufferRef** ptr = &hw_device_ctx)
                        av_buffer_unref(ptr);

                if (swsCtx != null)
                    sws_freeContext(swsCtx);
                
                hwframes    = null;
                swsCtx      = null;
                StartTime   = AV_NOPTS_VALUE;
                
                #if DEBUG
                Renderer.ReportLiveObjects();
                #endif
            }
        }

        internal Action<MediaType> recCompleted;
        Remuxer curRecorder;
        bool recGotKeyframe;
        internal bool isRecording;

        internal void StartRecording(Remuxer remuxer)
        {
            if (Disposed || isRecording) return;

            StartRecordTime     = AV_NOPTS_VALUE;
            curRecorder         = remuxer;
            recGotKeyframe      = false;
            isRecording         = true;
        }

        internal void StopRecording()
        {
            isRecording = false;
        }
    }
}
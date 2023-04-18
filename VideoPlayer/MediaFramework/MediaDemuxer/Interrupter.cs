﻿using FFmpeg.AutoGen;
using System.Runtime.InteropServices;

namespace FSPreview.MediaFramework.MediaDemuxer
{
    public unsafe class Interrupter
    {
        public int ForceInterrupt { get; set; }

        public Demuxer Demuxer { get; private set; }

        public Requester Requester { get; private set; }

        public long Requested { get; private set; }

        public int Interrupted { get; private set; }

        public AVIOInterruptCB_callback_func GetCallBackFunc() {
            return m_interruptClbk; 
        }

        AVIOInterruptCB_callback_func m_interruptClbk = new AVIOInterruptCB_callback_func();

        AVIOInterruptCB_callback InterruptClbk = (opaque) => {
            GCHandle demuxerHandle = (GCHandle)((IntPtr)opaque);
            Demuxer demuxer = (Demuxer)demuxerHandle.Target;
            return demuxer.Interrupter.ShouldInterrupt(demuxer);
        };

        public int ShouldInterrupt(Demuxer demuxer) {
            if (demuxer.Status == Status.Stopping) {
#if DEBUG
                demuxer.Log($"{demuxer.Interrupter.Requester} Interrupt (Stopping) !!!");
#endif
                return demuxer.Interrupter.Interrupted = 1;
            }
            if (demuxer.Config.AllowTimeouts) {
                long curTimeout = 0;
                switch (demuxer.Interrupter.Requester) {
                    case Requester.Close:
                    curTimeout = demuxer.Config.CloseTimeout;
                    break;
                    case Requester.Open:
                    curTimeout = demuxer.Config.OpenTimeout;
                    break;
                    case Requester.Read:
                    curTimeout = demuxer.Config.ReadTimeout;
                    break;
                    case Requester.Seek:
                    curTimeout = demuxer.Config.SeekTimeout;
                    break;
                }
                if (DateTime.UtcNow.Ticks - demuxer.Interrupter.Requested > curTimeout) {
#if DEBUG
                    demuxer.Log($"{demuxer.Interrupter.Requester} Timeout !!!! {(DateTime.UtcNow.Ticks - demuxer.Interrupter.Requested) / 10000} ms");
#endif
                    // Prevent Live Streams from Timeout (while m_demuxer is at the end)
                    if (demuxer.Interrupter.Requester == Requester.Read && (demuxer.Duration == 0 || (demuxer.HLSPlaylist != null && demuxer.HLSPlaylist->cur_seq_no > demuxer.HLSPlaylist->last_seq_no - 2))) {
#if DEBUG
                        demuxer.Log($"{demuxer.Interrupter.Requester} Timeout !!!! {(DateTime.UtcNow.Ticks - demuxer.Interrupter.Requested) / 10000} ms | Live HLS Excluded");
#endif
                        demuxer.Interrupter.Request(Requester.Read);
                        return demuxer.Interrupter.Interrupted = 0;
                    }
                    return demuxer.Interrupter.Interrupted = 1;
                }
            }
            if (demuxer.Interrupter.Requester == Requester.Close) {
                return 0;
            }
            if (demuxer.Interrupter.ForceInterrupt != 0 && demuxer.m_allowReadInterrupts) {
#if DEBUG
                demuxer.Log($"{demuxer.Interrupter.Requester} Interrupt !!!");
#endif
                return demuxer.Interrupter.Interrupted = 1;
            }
            return demuxer.Interrupter.Interrupted = 0;
        }

        public Interrupter(Demuxer demuxer) {
            Demuxer = demuxer;
            m_interruptClbk.Pointer = Marshal.GetFunctionPointerForDelegate(InterruptClbk);
        }

        public void Request(Requester requester) {
            if (!Demuxer.Config.AllowTimeouts) {
                return;
            }
            Requester = requester;
            Requested = DateTime.UtcNow.Ticks;
        }
    }

    public enum Requester
    {
        Close,
        Open,
        Read,
        Seek
    }
}

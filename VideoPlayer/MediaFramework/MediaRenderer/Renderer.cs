﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using SharpGen.Runtime;

using Vortice;
using Vortice.DXGI;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Mathematics;

using FSPreview.MediaFramework.MediaFrame;
using VideoDecoder = FSPreview.MediaFramework.MediaDecoder.VideoDecoder;

namespace FSPreview.MediaFramework.MediaRenderer
{
    public unsafe partial class Renderer : NotifyPropertyChanged, IDisposable
    {
        public Config           Config          => VideoDecoder?.Config;
        public Control          Control         { get; private set; }
        internal IntPtr ControlHandle; // When we re-created the swapchain so we don't access the control (which requires UI thread)

        public ID3D11Device     Device          { get; private set; }
        public bool             DisableRendering{ get; set; }
        public bool             Disposed        { get; private set; } = true;
        public RendererInfo     Info            { get; internal set; }
        public int              MaxOffScreenTextures
                                                { get; set; } = 20;
        public VideoDecoder     VideoDecoder    { get; internal set; }

        public Viewport         GetViewport     { get; private set; }

        public int              PanXOffset      { get => panXOffset; set { panXOffset = value; if (videoProcessor != VideoProcessors.Flyleaf && value != 0) FrameResized(); SetViewport(); Present(); } }
        int panXOffset;
        public int              PanYOffset      { get => panYOffset; set { panYOffset = value; if (videoProcessor != VideoProcessors.Flyleaf && value != 0) FrameResized(); SetViewport(); Present(); } }
        int panYOffset;
        public int              Zoom            { get => zoom;       set { zoom       = value; if (videoProcessor != VideoProcessors.Flyleaf && value != 0) FrameResized(); SetViewport(); Present(); } }
        int zoom;
        public int              UniqueId        { get; private set; }

        public Dictionary<VideoFilters, VideoFilter> 
                                Filters         { get; set; }
        public VideoFrame       LastFrame       { get; set; }


        ID3D11DeviceContext                     context;
        IDXGISwapChain1                         swapChain;

        ID3D11Texture2D                         backBuffer;
        ID3D11RenderTargetView                  backBufferRtv;
        
        // Used for off screen rendering
        ID3D11RenderTargetView[]                rtv2;
        ID3D11Texture2D[]                       backBuffer2;
        bool[]                                  backBuffer2busy;

        ID3D11SamplerState                      samplerLinear;
        //ID3D11SamplerState                      samplerPoint;

        //ID3D11BlendState                        blendStateAlpha;
        //ID3D11BlendState                        blendStateAlphaInv;

        Dictionary<string, ID3D11PixelShader>   PSShaders = new Dictionary<string, ID3D11PixelShader>();
        Dictionary<string, ID3D11VertexShader>  VSShaders = new Dictionary<string, ID3D11VertexShader>();

        ID3D11Buffer                            vertexBuffer;
        ID3D11InputLayout                       vertexLayout;


        ID3D11ShaderResourceView[]              curSRVs;
        ShaderResourceViewDescription           srvDescR, srvDescRG;

        VideoProcessorColorSpace                inputColorSpace;
        VideoProcessorColorSpace                outputColorSpace;

        internal object  lockDevice = new object();
        object  lockPresentTask     = new object();
        bool    isPresenting;
        long    lastPresentAt       = 0;
        long    lastPresentRequestAt= 0;
        float   curRatio            = 1.0f;

        public Renderer(VideoDecoder videoDecoder, Control control = null, int uniqueId = -1)
        {
            if (control != null)
            {
                Control = control;
                ControlHandle = control.Handle; // Requires UI Access
            }
            
            UniqueId = uniqueId == -1 ? Utils.GetUniqueId() : uniqueId;
            VideoDecoder = videoDecoder;
        }

        public void Initialize()
        {
            lock (lockDevice)
            {
                Log("Initializing");
                Disposed = false;

                IDXGIAdapter1 adapter = null;

                if (Config.Video.GPUAdapteLuid != -1)
                {
                    for (int adapterIndex = 0; Factory.EnumAdapters1(adapterIndex, out adapter).Success; adapterIndex++)
                    {
                        if (adapter.Description1.Luid == Config.Video.GPUAdapteLuid)
                            break;

                        adapter.Dispose();
                    }

                    if (adapter == null)
                        throw new Exception($"GPU Adapter with {Config.Video.GPUAdapteLuid} has not been found");
                }

                DeviceCreationFlags creationFlags = DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport;

                #if DEBUG
                if (D3D11.SdkLayersAvailable()) creationFlags |= DeviceCreationFlags.Debug;
                #endif

                // Creates the D3D11 Device based on selected adapter or default hardware (highest to lowest features and fall back to the WARP device. see http://go.microsoft.com/fwlink/?LinkId=286690)
                if (D3D11.D3D11CreateDevice(adapter, adapter == null ? DriverType.Hardware : DriverType.Unknown, creationFlags, featureLevelsAll, out ID3D11Device tempDevice).Failure)
                    if (D3D11.D3D11CreateDevice(adapter, adapter == null ? DriverType.Hardware : DriverType.Unknown, creationFlags, featureLevels, out tempDevice).Failure)
                        D3D11.D3D11CreateDevice(null, DriverType.Warp, creationFlags, featureLevels, out tempDevice).CheckError();

                Device = tempDevice. QueryInterface<ID3D11Device1>();
                context= Device.ImmediateContext;

                // Gets the default adapter from the D3D11 Device
                if (adapter == null)
                {
                    Device.Tag = (new Luid()).ToString();
                    using (var deviceTmp = Device.QueryInterface<IDXGIDevice1>())
                    using (var adapterTmp = deviceTmp.GetAdapter())
                        adapter = adapterTmp.QueryInterface<IDXGIAdapter1>();
                }
                else
                    Device.Tag = adapter.Description.Luid.ToString();

                RendererInfo.Fill(this, adapter);
                Log("\r\n" + Info.ToString());

                tempDevice.Dispose();
                adapter.Dispose();
            
                using (var mthread    = Device.QueryInterface<ID3D11Multithread>()) mthread.SetMultithreadProtected(true);
                using (var dxgidevice = Device.QueryInterface<IDXGIDevice1>())      dxgidevice.MaximumFrameLatency = 1;

                if (Control != null)
                    InitializeSwapChain();

                vertexBuffer  = Device.CreateBuffer(BindFlags.VertexBuffer, vertexBufferData);
                context.IASetVertexBuffers(0, new VertexBufferView[] { new VertexBufferView(vertexBuffer, sizeof(float) * 5, 0) });

                samplerLinear = Device.CreateSamplerState(new SamplerDescription()
                {
                    ComparisonFunction = ComparisonFunction.Never,
                    AddressU = TextureAddressMode.Clamp,
                    AddressV = TextureAddressMode.Clamp,
                    AddressW = TextureAddressMode.Clamp,
                    Filter   = Filter.MinMagMipLinear,
                    MinLOD   = 0,
                    MaxLOD   = float.MaxValue
                });

                //samplerPoint = Device.CreateSamplerState(new SamplerDescription()
                //{
                //    ComparisonFunction = ComparisonFunction.Never,
                //    AddressU = TextureAddressMode.Clamp,
                //    AddressV = TextureAddressMode.Clamp,
                //    AddressW = TextureAddressMode.Clamp,
                //    Filter   = Filter.MinMagMipPoint,
                //    MinLOD   = 0,
                //    MaxLOD   = float.MaxValue
                //});

                // Blend
                //var blendDesc = new BlendDescription();
                //blendDesc.RenderTarget[0].IsBlendEnabled = true;
                //blendDesc.RenderTarget[0].SourceBlend = Blend.One;
                //blendDesc.RenderTarget[0].DestinationBlend = Blend.SourceAlpha;
                //blendDesc.RenderTarget[0].BlendOperation = BlendOperation.Add;
                //blendDesc.RenderTarget[0].SourceBlendAlpha = Blend.One;
                //blendDesc.RenderTarget[0].DestinationBlendAlpha = Blend.Zero;
                //blendDesc.RenderTarget[0].BlendOperationAlpha = BlendOperation.Add;
                //blendDesc.RenderTarget[0].RenderTargetWriteMask = ColorWriteEnable.All;
                //blendStateAlpha = Device.CreateBlendState(blendDesc);

                //blendDesc.RenderTarget[0].DestinationBlend = Blend.InverseSourceAlpha;
                //blendStateAlphaInv = Device.CreateBlendState(blendDesc);

                // Vertex
                foreach(var shader in VSShaderBlobs)
                {
                    VSShaders.Add(shader.Key, Device.CreateVertexShader(shader.Value));
                    vertexLayout = Device.CreateInputLayout(inputElements, shader.Value);

                    context.IASetInputLayout(vertexLayout);
                    context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
                    context.VSSetShader(VSShaders["VSSimple"]);

                    break; // Using single vertex
                }

                // Pixel Shaders
                foreach(var shader in PSShaderBlobs) 
                    PSShaders.Add(shader.Key, Device.CreatePixelShader(shader.Value));

                // context.PSSetShader(PSShaders["PSSimple"]);
                context.PSSetSampler(0, samplerLinear);

                psBuffer = Device.CreateBuffer(new BufferDescription() 
                {
                    Usage           = ResourceUsage.Default,
                    BindFlags       = BindFlags.ConstantBuffer,
                    CpuAccessFlags  = CpuAccessFlags.None,
                    SizeInBytes     = sizeof(PSBufferType)
                });
                context.PSSetConstantBuffer(0, psBuffer);

                psBufferData.hdrmethod  = HDRtoSDRMethod.None;
                //psBufferData.brightness = Config.Video.Brightness / 100.0f;
                //psBufferData.contrast   = Config.Video.Contrast / 100.0f;

                context.UpdateSubresource(ref psBufferData, psBuffer);

                //if (Control != null)
                InitializeVideoProcessor();

                // TBR: Device Removal Event
                //ID3D11Device4 device4 = Device.QueryInterface<ID3D11Device4>(); device4.RegisterDeviceRemovedEvent(..);

                Log($"Initialized with Feature Level {(int)Device.FeatureLevel >> 12}.{(int)Device.FeatureLevel >> 8 & 0xf}");
            }
        }
        public void InitializeSwapChain()
        {
            Control.Resize += ResizeBuffers;

            SwapChainDescription1 swapChainDescription = new SwapChainDescription1()
            {
                Format      = Format.B8G8R8A8_UNorm,
                //Format      = Format.R10G10B10A2_UNorm,
                Width       = Control.Width,
                Height      = Control.Height,
                AlphaMode   = AlphaMode.Ignore,
                BufferUsage = Usage.RenderTargetOutput,
                Scaling     = Scaling.None, // We should re-draw the source texture for proper render
                SampleDescription = new SampleDescription(1, 0)
            };

            SwapChainFullscreenDescription fullscreenDescription = new SwapChainFullscreenDescription
            {
                Windowed = true
            };

            if (IsWin8OrGreater)
            {
                swapChainDescription.BufferCount= 2; // TBR: for hdr output or >=60fps maybe use 6
                swapChainDescription.SwapEffect = SwapEffect.FlipDiscard;
            }
            else
            {
                swapChainDescription.BufferCount= 1;
                swapChainDescription.SwapEffect = SwapEffect.Discard;
            }

            swapChain    = Factory.CreateSwapChainForHwnd(Device, ControlHandle, swapChainDescription, fullscreenDescription);
            backBuffer   = swapChain.GetBuffer<ID3D11Texture2D>(0);
            backBufferRtv= Device.CreateRenderTargetView(backBuffer);
        }
        public void DisposeSwapChain()
        {
            lock (lockDevice)
            {
                if (Control != null)
                    Control.Resize -= ResizeBuffers;

                vpov?.Dispose();
                backBufferRtv?.Dispose();
                backBuffer?.Dispose();
                swapChain?.Dispose();
                context?.Flush();
            }
        }
        public void Dispose()
        {
            lock (lockDevice)
            {
                if (Disposed)
                    return;

                Log("Disposing");

                DisposeVideoProcessor();

                foreach(var shader in PSShaders.Values)
                    shader.Dispose();
                PSShaders.Clear();

                foreach(var shader in VSShaders.Values)
                    shader.Dispose();
                VSShaders.Clear();

                //samplerPoint?.Dispose();
                //blendStateAlpha?.Dispose();
                //blendStateAlphaInv?.Dispose();

                VideoDecoder.DisposeFrame(LastFrame);
                psBuffer?.Dispose();
                samplerLinear?.Dispose();
                vertexLayout?.Dispose();
                vertexBuffer?.Dispose();
                DisposeSwapChain();

                if (rtv2 != null)
                {
                    for(int i=0; i<rtv2.Length-1; i++)
                        rtv2[i].Dispose();

                    rtv2 = null;
                }

                if (backBuffer2 != null)
                    for(int i=0; i<backBuffer2.Length-1; i++)
                        backBuffer2[i]?.Dispose();

                if (curSRVs != null)
                {
                    for (int i=0; i<curSRVs.Length; i++)
                        curSRVs[i]?.Dispose();

                    curSRVs = null;
                }

                if (Device != null)
                {
                    context.ClearState();
                    context.Flush();
                    context.Dispose();
                    Device.Dispose();
                    Device = null;
                }

                #if DEBUG
                ReportLiveObjects();
                #endif

                Disposed = true;
                curRatio = 1.0f;
                Log("Disposed");
            }
        }

        internal void FrameResized()
        {
            // TODO: Win7 doesn't support R8G8_UNorm so use SNorm will need also unormUV on pixel shader
            lock (lockDevice)
            {
                if (Disposed)
                    return;

                curRatio = VideoDecoder.VideoStream.AspectRatio.Value;
                IsHDR = VideoDecoder.VideoStream.ColorSpace == "BT2020";
                VideoProcessor = !VideoDecoder.VideoAccelerated || D3D11VPFailed || Config.Video.VideoProcessor == VideoProcessors.Flyleaf || zoom != 0 || PanXOffset != 0 || panYOffset != 0 || (isHDR && !Config.Video.Deinterlace) ? VideoProcessors.Flyleaf : VideoProcessors.D3D11;

                if (videoProcessor == VideoProcessors.Flyleaf)
                {
                    srvDescR = new ShaderResourceViewDescription();
                    srvDescR.Format = VideoDecoder.VideoStream.PixelBits > 8 ? Format.R16_UNorm : Format.R8_UNorm;

                    srvDescRG = new ShaderResourceViewDescription();
                    srvDescRG.Format = VideoDecoder.VideoStream.PixelBits > 8 ? Format.R16G16_UNorm : Format.R8G8_UNorm;

                    if (VideoDecoder.ZeroCopy)
                    {
                        srvDescR.ViewDimension = ShaderResourceViewDimension.Texture2DArray;
                        srvDescR.Texture2DArray = new Texture2DArrayShaderResourceView()
                        {
                            ArraySize = 1,
                            MipLevels = 1
                        };

                        srvDescRG.ViewDimension = ShaderResourceViewDimension.Texture2DArray;
                        srvDescRG.Texture2DArray = new Texture2DArrayShaderResourceView()
                        {
                            ArraySize = 1,
                            MipLevels = 1
                        };
                    }
                    else
                    {
                        srvDescR.ViewDimension = ShaderResourceViewDimension.Texture2D;
                        srvDescR.Texture2D = new Texture2DShaderResourceView()
                        {
                            MipLevels = 1,
                            MostDetailedMip = 0
                        };

                        srvDescRG.ViewDimension = ShaderResourceViewDimension.Texture2D;
                        srvDescRG.Texture2D = new Texture2DShaderResourceView()
                        {
                            MipLevels = 1,
                            MostDetailedMip = 0
                        };
                    }

                    psBufferData.format = VideoDecoder.VideoAccelerated ? PSFormat.Y_UV : ((VideoDecoder.VideoStream.PixelFormatType == PixelFormatType.Software_Handled ? PSFormat.Y_U_V : PSFormat.RGB));
                
                    lastLumin = 0;
                    psBufferData.hdrmethod = HDRtoSDRMethod.None;

                    if (isHDR)
                    {
                        psBufferData.coefsIndex = 0;
                        UpdateHDRtoSDR(null, false);
                    }
                    else if (VideoDecoder.VideoStream.ColorSpace == "BT709")
                        psBufferData.coefsIndex = 1;
                    else if (VideoDecoder.VideoStream.ColorSpace == "BT601")
                        psBufferData.coefsIndex = 2;
                    else
                        psBufferData.coefsIndex = 2;

                    context.UpdateSubresource(ref psBufferData, psBuffer);
                }
                else
                {
                    vpov?.Dispose();
                    vd1.CreateVideoProcessorOutputView(backBuffer, vpe, vpovd, out vpov);

                    inputColorSpace = new VideoProcessorColorSpace()
                    {
                        Usage           = 0,
                        RGB_Range       = VideoDecoder.VideoStream.AVStream->codecpar->color_range == FFmpeg.AutoGen.AVColorRange.AVCOL_RANGE_JPEG ? (uint) 0 : 1,
                        YCbCr_Matrix    = VideoDecoder.VideoStream.ColorSpace != "BT601" ? (uint) 1 : 0,
                        YCbCr_xvYCC     = 0,
                        Nominal_Range   = VideoDecoder.VideoStream.AVStream->codecpar->color_range == FFmpeg.AutoGen.AVColorRange.AVCOL_RANGE_JPEG ? (uint) 2 : 1
                    };

                    outputColorSpace = new VideoProcessorColorSpace()
                    {
                        Usage           = 0,
                        RGB_Range       = 0,
                        YCbCr_Matrix    = 1,
                        YCbCr_xvYCC     = 0,
                        Nominal_Range   = 2
                    };
                }

                if (Control != null)
                    SetViewport();
                else
                {
                    if (rtv2 != null)
                        for (int i = 0; i < rtv2.Length - 1; i++)
                            rtv2[i].Dispose();

                    if (backBuffer2 != null)
                        for (int i = 0; i < backBuffer2.Length - 1; i++)
                            backBuffer2[i].Dispose();

                    backBuffer2busy = new bool[MaxOffScreenTextures];
                    rtv2 = new ID3D11RenderTargetView[MaxOffScreenTextures];
                    backBuffer2 = new ID3D11Texture2D[MaxOffScreenTextures];

                    for (int i = 0; i < MaxOffScreenTextures; i++)
                    {
                        backBuffer2[i] = Device.CreateTexture2D(new Texture2DDescription()
                        {
                            Usage       = ResourceUsage.Default,
                            BindFlags   = BindFlags.RenderTarget,
                            Format      = Format.B8G8R8A8_UNorm,
                            Width       = VideoDecoder.VideoStream.Width,
                            Height      = VideoDecoder.VideoStream.Height,

                            ArraySize   = 1,
                            MipLevels   = 1,
                            SampleDescription = new SampleDescription(1, 0)
                        });

                        rtv2[i] = Device.CreateRenderTargetView(backBuffer2[i]);
                    }

                    context.RSSetViewport(0, 0, VideoDecoder.VideoStream.Width, VideoDecoder.VideoStream.Height);
                }
            }
        }
        internal void ResizeBuffers(object sender, EventArgs e)
        {   
            lock (lockDevice)
            {
                if (Disposed)
                    return;

                backBufferRtv.Dispose();
                vpov?.Dispose();
                backBuffer.Dispose();
                swapChain.ResizeBuffers(0, Control.Width, Control.Height, Format.Unknown, SwapChainFlags.None);
                backBuffer = swapChain.GetBuffer<ID3D11Texture2D>(0);
                backBufferRtv = Device.CreateRenderTargetView(backBuffer);
                if (videoProcessor == VideoProcessors.D3D11)
                    vd1.CreateVideoProcessorOutputView(backBuffer, vpe, vpovd, out vpov);

                SetViewport();
            }
        }
        public void SetViewport()
        {
            float ratio;

            if (Config.Video.AspectRatio == AspectRatio.Keep)
                ratio = curRatio;

            else if (Config.Video.AspectRatio == AspectRatio.Fill)
                ratio = Control.Width / (float)Control.Height;

            else if (Config.Video.AspectRatio == AspectRatio.Custom)
                ratio = Config.Video.CustomAspectRatio.Value;
            else
                ratio = Config.Video.AspectRatio.Value;

            if (ratio <= 0) ratio = 1;

            int Height = Control.Height + (zoom * 2);
            int Width  = Control.Width  + (zoom * 2);

            if (Width / ratio > Height)
                GetViewport = new Viewport(((Control.Width - (Height * ratio)) / 2) + PanXOffset, 0 - zoom + PanYOffset, Height * ratio, Height, 0.0f, 1.0f);
            else
                GetViewport = new Viewport(0 - zoom + PanXOffset, ((Control.Height - (Width / ratio)) / 2) + PanYOffset, Width, Width / ratio, 0.0f, 1.0f);

            // TODO: Handle Pan Move/Zoom and seperate from GetViewport
            if (videoProcessor == VideoProcessors.D3D11)
            {
                vc.VideoProcessorSetStreamDestRect(vp, 0, true, new RawRect((int)GetViewport.X, (int)GetViewport.Y, (int)GetViewport.Width + (int)GetViewport.X, (int)GetViewport.Height + (int)GetViewport.Y));
                vc.VideoProcessorSetOutputTargetRect(vp, true, new RawRect(0, 0, Control.Width, Control.Height));
            }
        }
        
        public bool Present(VideoFrame frame)
        {
            if (Monitor.TryEnter(lockDevice, 5))
            {
                try
                {
                    PresentInternal(frame);
                    VideoDecoder.DisposeFrame(LastFrame);
                    LastFrame = frame;

                    return true;

                } catch (Exception e)
                {
                    #if DEBUG
                    Log($"Error {e.Message} | {Device?.DeviceRemovedReason}");
                    #endif
                    VideoDecoder.DisposeFrame(frame);

                    return false;

                } finally
                {
                    Monitor.Exit(lockDevice);
                }
            }

            Log("Dropped Frame - Lock timeout " + (frame != null ? Utils.TicksToTime(frame.timestamp) : ""));
            VideoDecoder.DisposeFrame(frame);

            return false;
        }
        public void Present()
        {
            if (ControlHandle == IntPtr.Zero)
                return;

            // NOTE: We don't have TimeBeginPeriod, FpsForIdle will not be accurate
            lock (lockPresentTask)
            {
                if (VideoDecoder.IsRunning) return;

                if (isPresenting) { lastPresentRequestAt = DateTime.UtcNow.Ticks; return;}
                isPresenting = true;
            }

            Task.Run(() =>
            {
                do
                {
                    long sleepMs = DateTime.UtcNow.Ticks - lastPresentAt;
                    sleepMs = sleepMs < (long)( 1.0/Config.Player.IdleFps * 1000 * 10000) ? (long) (1.0 / Config.Player.IdleFps * 1000) : 0;
                    if (sleepMs > 2)
                        Thread.Sleep((int)sleepMs);

                    if (Monitor.TryEnter(lockDevice, 5))
                    {
                        try
                        {
                            if (Disposed)
                            {
                                if (Control != null)
                                    Control.BackColor = Utils.WPFToWinFormsColor(Config.Video.BackgroundColor);
                            }   
                            else if (LastFrame != null && (LastFrame.textures != null || LastFrame.bufRef != null))
                                PresentInternal(LastFrame);
                            else
                            {
                                context.OMSetRenderTargets(backBufferRtv);
                                context.ClearRenderTargetView(backBufferRtv, Config.Video._BackgroundColor);
                                context.RSSetViewport(new Viewport(Control.Bounds));
                                context.PSSetShader(PSShaders["PSSimple"]);
                                context.PSSetSampler(0, samplerLinear);
                                context.PSSetShaderResources(0, new ID3D11ShaderResourceView[0]);
                                context.Draw(6, 0);
                                swapChain.Present(Config.Video.VSync, PresentFlags.None);
                            }
                        } catch (Exception e)
                        {
                            Log($"[Present] Error {e.Message} | {Device.DeviceRemovedReason}");
                        } finally
                        {
                            Monitor.Exit(lockDevice);
                        }
                    }

                    lastPresentAt = DateTime.UtcNow.Ticks;

                } while (lastPresentRequestAt > lastPresentAt);

                isPresenting = false;
            });
        }
        internal void PresentInternal(VideoFrame frame)
        {
            if (VideoDecoder.VideoAccelerated)
            {
                if (videoProcessor == VideoProcessors.D3D11) // TODO: VideoProcessorBlt
                {
                    if (frame.bufRef != null)
                    {
                        vpivd.Texture2D.ArraySlice = frame.subresource;
                        vd1.CreateVideoProcessorInputView(VideoDecoder.textureFFmpeg, vpe, vpivd, out vpiv);
                    }
                    else
                    {
                        vpivd.Texture2D.ArraySlice = 0;
                        vd1.CreateVideoProcessorInputView(frame.textures[0], vpe, vpivd, out vpiv);
                    }
                    
                    vpsa[0].InputSurface = vpiv;
                    vc.VideoProcessorSetStreamColorSpace(vp, 0, inputColorSpace);
                    vc.VideoProcessorSetOutputColorSpace(vp, outputColorSpace);
                    vc.VideoProcessorBlt(vp, vpov, 0, 1, vpsa);
                    swapChain.Present(Config.Video.VSync, PresentFlags.None);

                    vpiv.Dispose();
                }
                else
                {
                    curSRVs = new ID3D11ShaderResourceView[2];

                    if (frame.bufRef != null)
                    {
                        srvDescR. Texture2DArray.FirstArraySlice = frame.subresource;
                        srvDescRG.Texture2DArray.FirstArraySlice = frame.subresource;
                        curSRVs[0] = Device.CreateShaderResourceView(VideoDecoder.textureFFmpeg, srvDescR);
                        curSRVs[1] = Device.CreateShaderResourceView(VideoDecoder.textureFFmpeg, srvDescRG);
                    }
                    else
                    {
                        curSRVs[0] = Device.CreateShaderResourceView(frame.textures[0], srvDescR);
                        curSRVs[1] = Device.CreateShaderResourceView(frame.textures[0], srvDescRG);
                    }

                    context.OMSetRenderTargets(backBufferRtv);
                    context.ClearRenderTargetView(backBufferRtv, Config.Video._BackgroundColor);
                    context.RSSetViewport(GetViewport);
                    context.PSSetShader(PSShaders["FlyleafPS"]);
                    context.PSSetSampler(0, samplerLinear);
                    context.PSSetShaderResources(0, curSRVs);
                    context.Draw(6, 0);
                    swapChain.Present(Config.Video.VSync, PresentFlags.None);

                    for (int i=0; i<curSRVs.Length; i++)
                        curSRVs[i].Dispose();
                }
            }
            else
            {
                curSRVs = new ID3D11ShaderResourceView[frame.textures.Length];
                for (int i=0; i<frame.textures.Length; i++)
                    curSRVs[i] = Device.CreateShaderResourceView(frame.textures[i]);

                context.OMSetRenderTargets(backBufferRtv);
                context.ClearRenderTargetView(backBufferRtv, Config.Video._BackgroundColor);
                context.RSSetViewport(GetViewport);
                context.PSSetShader(PSShaders["FlyleafPS"]);
                context.PSSetSampler(0, samplerLinear);
                context.PSSetShaderResources(0, curSRVs);
                context.Draw(6, 0);
                swapChain.Present(Config.Video.VSync, PresentFlags.None);

                for (int i=0; i<curSRVs.Length; i++)
                    curSRVs[i].Dispose();
            }
        }

        internal void PresentOffline(VideoFrame frame, ID3D11RenderTargetView rtv, Viewport viewport)
        {
            if (VideoDecoder.VideoAccelerated)
            {
                if (videoProcessor == VideoProcessors.D3D11) // TODO: VideoProcessorBlt
                {
                    vd1.CreateVideoProcessorOutputView(rtv.Resource, vpe, vpovd, out ID3D11VideoProcessorOutputView vpov);
                    vc.VideoProcessorSetStreamDestRect(vp, 0, true, new RawRect(0, 0, VideoDecoder.VideoStream.Width, VideoDecoder.VideoStream.Height));
                    vc.VideoProcessorSetOutputTargetRect(vp, true, new RawRect(0, 0, VideoDecoder.VideoStream.Width, VideoDecoder.VideoStream.Height));

                    if (frame.bufRef != null)
                    {
                        vpivd.Texture2D.ArraySlice = frame.subresource;
                        vd1.CreateVideoProcessorInputView(VideoDecoder.textureFFmpeg, vpe, vpivd, out vpiv);
                    }
                    else
                    {
                        vpivd.Texture2D.ArraySlice = 0;
                        vd1.CreateVideoProcessorInputView(frame.textures[0], vpe, vpivd, out vpiv);
                    }
                    
                    vpsa[0].InputSurface = vpiv;                    

                    vc.VideoProcessorSetStreamColorSpace(vp, 0, inputColorSpace);
                    vc.VideoProcessorSetOutputColorSpace(vp, outputColorSpace);
                    vc.VideoProcessorBlt(vp, vpov, 0, 1, vpsa);
                    vpiv.Dispose();
                    vpov.Dispose();

                    RawRect rect = new RawRect((int)viewport.X, (int)viewport.Y, (int)(viewport.Width - viewport.X), (int)(viewport.Height - viewport.Y));
                    vc.VideoProcessorSetStreamDestRect(vp, 0, true, rect);
                    vc.VideoProcessorSetOutputTargetRect(vp, true, rect);
                }
                else
                {
                    curSRVs = new ID3D11ShaderResourceView[2];

                    if (frame.bufRef != null)
                    {
                        srvDescR. Texture2DArray.FirstArraySlice = frame.subresource;
                        srvDescRG.Texture2DArray.FirstArraySlice = frame.subresource;
                        curSRVs[0] = Device.CreateShaderResourceView(VideoDecoder.textureFFmpeg, srvDescR);
                        curSRVs[1] = Device.CreateShaderResourceView(VideoDecoder.textureFFmpeg, srvDescRG);
                    }
                    else
                    {
                        curSRVs[0] = Device.CreateShaderResourceView(frame.textures[0], srvDescR);
                        curSRVs[1] = Device.CreateShaderResourceView(frame.textures[0], srvDescRG);
                    }

                    context.OMSetRenderTargets(rtv);
                    context.ClearRenderTargetView(rtv, Config.Video._BackgroundColor);
                    context.RSSetViewport(viewport);
                    context.PSSetShader(PSShaders["FlyleafPS"]);
                    context.PSSetSampler(0, samplerLinear);
                    context.PSSetShaderResources(0, curSRVs);
                    context.Draw(6, 0);

                    for (int i=0; i<curSRVs.Length; i++)
                        curSRVs[i].Dispose();
                }
            }
            else
            {
                curSRVs = new ID3D11ShaderResourceView[frame.textures.Length];
                for (int i=0; i<frame.textures.Length; i++)
                    curSRVs[i] = Device.CreateShaderResourceView(frame.textures[i]);

                context.OMSetRenderTargets(rtv);
                context.ClearRenderTargetView(rtv, Config.Video._BackgroundColor);
                context.RSSetViewport(viewport);
                context.PSSetShader(PSShaders["FlyleafPS"]);
                context.PSSetSampler(0, samplerLinear);
                context.PSSetShaderResources(0, curSRVs);
                context.Draw(6, 0);

                for (int i=0; i<curSRVs.Length; i++)
                    curSRVs[i].Dispose();
            }
        }

        /// <summary>
        /// Gets bitmap from a video frame
        /// (Currently cannot be used in parallel with the rendering)
        /// </summary>
        /// <param name="frame"></param>
        /// <returns></returns>
        public Bitmap GetBitmap(VideoFrame frame)
        {
            if (Device == null || frame == null) return null;

            int subresource = -1;

            var stageDesc = new Texture2DDescription(VideoDecoder.VideoStream.Width, VideoDecoder.VideoStream.Height, Format.B8G8R8A8_UNorm, 1, 1, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read);
            ID3D11Texture2D stage = Device.CreateTexture2D(stageDesc);

            lock (lockDevice)
            {
                while (true)
                {
                    for (int i=0; i<MaxOffScreenTextures; i++)
                        if (!backBuffer2busy[i]) { subresource = i; break;}

                    if (subresource != -1)
                        break;
                    else
                        Thread.Sleep(5);
                }

                backBuffer2busy[subresource] = true;

                //if (VideoDecoder.VideoAccelerated)
                //{
                //    curSRVs = new ID3D11ShaderResourceView[2];

                //    if (frame.bufRef != null) // Config.Decoder.ZeroCopy
                //    {
                //        srvDescR. Texture2DArray.FirstArraySlice = frame.subresource;
                //        srvDescRG.Texture2DArray.FirstArraySlice = frame.subresource;
                //        curSRVs[0]  = Device.CreateShaderResourceView(VideoDecoder.textureFFmpeg, srvDescR);
                //        curSRVs[1]  = Device.CreateShaderResourceView(VideoDecoder.textureFFmpeg, srvDescRG);
                //    }
                //    else
                //    {
                //        curSRVs[0]  = Device.CreateShaderResourceView(frame.textures[0], srvDescR);
                //        curSRVs[1]  = Device.CreateShaderResourceView(frame.textures[0], srvDescRG);
                //    }
                //}
                //else if (VideoDecoder.VideoStream.PixelFormatType == PixelFormatType.Software_Handled)
                //{
                //    curSRVs     = new ID3D11ShaderResourceView[3];
                //    curSRVs[0]  = Device.CreateShaderResourceView(frame.textures[0]);
                //    curSRVs[1]  = Device.CreateShaderResourceView(frame.textures[1]);
                //    curSRVs[2]  = Device.CreateShaderResourceView(frame.textures[2]);
                //}
                //else
                //{
                //    curSRVs     = new ID3D11ShaderResourceView[1];
                //    curSRVs[0]  = Device.CreateShaderResourceView(frame.textures[0]);
                //}

                PresentOffline(frame, rtv2[subresource], new Viewport(backBuffer2[subresource].Description.Width, backBuffer2[subresource].Description.Height));

                //context.PSSetShaderResources(0, curSRVs);
                //context.OMSetRenderTargets(rtv2[subresource]);
                ////context.ClearRenderTargetView(rtv2[subresource], Config.video._ClearColor);
                //context.Draw(6, 0);

                VideoDecoder.DisposeFrame(frame);

                if (curSRVs != null)
                {
                    for (int i=0; i<curSRVs.Length; i++)
                        curSRVs[i]?.Dispose();

                    curSRVs = null;
                }

                context.CopyResource(stage, backBuffer2[subresource]);
                backBuffer2busy[subresource] = false;
            }

            return GetBitmap(stage);
        }
        public Bitmap GetBitmap(ID3D11Texture2D stageTexture)
        {
            Bitmap bitmap   = new Bitmap(stageTexture.Description.Width, stageTexture.Description.Height);
            var db          = context.Map(stageTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            var bitmapData  = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            
            if (db.RowPitch == bitmapData.Stride)
                MemoryHelpers.CopyMemory(bitmapData.Scan0, db.DataPointer, bitmap.Width * bitmap.Height * 4);
            else
            {
                var sourcePtr   = db.DataPointer;
                var destPtr     = bitmapData.Scan0;

                for (int y = 0; y < bitmap.Height; y++)
                {
                    MemoryHelpers.CopyMemory(destPtr, sourcePtr, bitmap.Width * 4);

                    sourcePtr   = IntPtr.Add(sourcePtr, db.RowPitch);
                    destPtr     = IntPtr.Add(destPtr, bitmapData.Stride);
                }
            }

            bitmap.UnlockBits(bitmapData);
            context.Unmap(stageTexture, 0);
            stageTexture.Dispose();
            
            return bitmap;
        }

        public void TakeSnapshot(string fileName, ImageFormat imageFormat = null)
        {
            if (Disposed || VideoDecoder == null || VideoDecoder.VideoStream == null)
                return;

            Task.Run(() =>
            {
                try
                {
                    var stageDesc = new Texture2DDescription(VideoDecoder.VideoStream.Width, VideoDecoder.VideoStream.Height, Format.B8G8R8A8_UNorm, 1, 1, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read);
                    ID3D11Texture2D stage = Device.CreateTexture2D(stageDesc);

                    var gpuDesc = new Texture2DDescription(stageDesc.Width, stageDesc.Height, Format.B8G8R8A8_UNorm, 1, 1, BindFlags.RenderTarget | BindFlags.ShaderResource);
                    ID3D11Texture2D gpu = Device.CreateTexture2D(gpuDesc);

                    ID3D11RenderTargetView gpuRtv = Device.CreateRenderTargetView(gpu);
                    Viewport viewport = new Viewport(stageDesc.Width, stageDesc.Height);

                    lock (lockDevice)
                    {
                        PresentOffline(LastFrame, gpuRtv, viewport);

                        if (videoProcessor == VideoProcessors.D3D11)
                        {
                            vc.VideoProcessorSetStreamDestRect(vp, 0, true, new RawRect((int)GetViewport.X, (int)GetViewport.Y, (int)GetViewport.Width + (int)GetViewport.X, (int)GetViewport.Height + (int)GetViewport.Y));
                            vc.VideoProcessorSetOutputTargetRect(vp, true, new RawRect(0, 0, Control.Width, Control.Height));
                        }
                        //else // TBR: if we set it all time or not
                            //context.RSSetViewport(GetViewport);
                    }

                    context.CopyResource(stage, gpu);
                    gpuRtv.Dispose();
                    gpu.Dispose();

                    Bitmap snapshotBitmap = GetBitmap(stage);
                    try { snapshotBitmap.Save(fileName, imageFormat == null ? ImageFormat.Bmp : imageFormat); } catch (Exception) { }
                    snapshotBitmap.Dispose();
                } catch { }
            });
        }

        private void Log(string msg) { Debug.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [#{UniqueId}] [Renderer] {msg}"); }
    }
}
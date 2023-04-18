﻿using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace FSPreview
{
    public enum PixelFormatType
    {
        Hardware,
        Software_Handled,
        Software_Sws
    }

    public enum MediaType
    {
        Audio,
        Video,
        Subs
    }

    public enum HDRtoSDRMethod : int
    {
        None = 0,
        Aces = 1,
        Hable = 2,
        Reinhard = 3
    }

    public enum VideoProcessors
    {
        Auto,
        D3D11,
        Flyleaf,
    }

    public enum ZeroCopy : int
    {
        Auto = 0,
        Enabled = 1,
        Disabled = 2
    }

    public struct GPUAdapter
    {
        public string Description { get; internal set; }
        public long Luid { get; internal set; }
        public bool HasOutput { get; internal set; }
    }

    public enum VideoFilters
    {
        // Ensure we have the same values with Vortice.Direct3D11.VideoProcessorFilterCaps (d3d11.h) | we can extended if needed with other values
        Brightness = 0x01,
        Contrast = 0x02,
        Hue = 0x04,
        Saturation = 0x08,
        NoiseReduction = 0x10,
        EdgeEnhancement = 0x20,
        AnamorphicScaling = 0x40,
        StereoAdjustment = 0x80
    }

    public struct AspectRatio
    {
        public static readonly AspectRatio Keep = new AspectRatio(-1, 1);

        public static readonly AspectRatio Fill = new AspectRatio(-2, 1);

        public static readonly AspectRatio Custom = new AspectRatio(-3, 1);

        public static readonly AspectRatio Invalid = new AspectRatio(-999, 1);

        public static readonly List<AspectRatio> AspectRatios = new List<AspectRatio>()
        {
            Keep,
            Fill,
            Custom,
            new AspectRatio(1, 1),
            new AspectRatio(4, 3),
            new AspectRatio(16, 9),
            new AspectRatio(16, 10),
            new AspectRatio(2.35f, 1),
        };

        public static implicit operator AspectRatio(string value) { return (new AspectRatio(value)); }

        public float Num { get; set; }

        public float Den { get; set; }

        public float Value {
            get => Num / Den;
            set { Num = value; Den = 1; }
        }

        public string ValueStr {
            get => ToString();
            set => FromString(value);
        }

        public AspectRatio(float value) : this(value, 1) { }

        public AspectRatio(float num, float den) { Num = num; Den = den; }

        public AspectRatio(string value) { Num = Invalid.Num; Den = Invalid.Den; FromString(value); }

        public override bool Equals(object obj) {
            if ((obj == null) || !GetType().Equals(obj.GetType())) {
                return false;
            }
            else {
                return Num == ((AspectRatio)obj).Num && Den == ((AspectRatio)obj).Den;
            }
        }

        public static bool operator ==(AspectRatio a, AspectRatio b) => a.Equals(b);

        public static bool operator !=(AspectRatio a, AspectRatio b) => !(a == b);

        public override int GetHashCode() { return (int)(Value * 1000); }

        public void FromString(string value) {
            if (value == "Keep") { Num = Keep.Num; Den = Keep.Den; return; }
            else if (value == "Fill") { Num = Fill.Num; Den = Fill.Den; return; }
            else if (value == "Custom") { Num = Custom.Num; Den = Custom.Den; return; }
            else if (value == "Invalid") { Num = Invalid.Num; Den = Invalid.Den; return; }
            string newvalue = value.ToString().Replace(',', '.');
            if (Regex.IsMatch(newvalue.ToString(), @"^\s*[0-9\.]+\s*[:/]\s*[0-9\.]+\s*$")) {
                string[] values = newvalue.ToString().Split(':');
                if (values.Length < 2) {
                    values = newvalue.ToString().Split('/');
                }
                Num = float.Parse(values[0], NumberStyles.Any, CultureInfo.InvariantCulture);
                Den = float.Parse(values[1], NumberStyles.Any, CultureInfo.InvariantCulture);
            }
            else if (float.TryParse(newvalue.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out float result)) {
                Num = result;
                Den = 1;
            }
            else { 
                Num = Invalid.Num; 
                Den = Invalid.Den; 
            }
        }
        public override string ToString() {
            return this == Keep ? "Keep" : (this == Fill ? "Fill" : (this == Custom ? "Custom" : (this == Invalid ? "Invalid" : $"{Num}:{Den}")));
        }
    }

    class PlayerStats
    {
        public long TotalBytes { get; set; }
        public long VideoBytes { get; set; }
        public long AudioBytes { get; set; }
        public long FramesDisplayed { get; set; }
    }

    public class NotifyPropertyChanged : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        [System.Xml.Serialization.XmlIgnore]
        public bool DisableNotifications { get; set; }

        protected bool Set<T>(ref T field, T value, bool check = true, [CallerMemberName] string propertyName = "") {
            // System.Diagnostics.Debug.WriteLine($"[===| {propertyName} |===]");
            if (!check || (field == null && value != null) || (field != null && !field.Equals(value))) {
                // Utils.Log($"[===| {propertyName} ({(field != null ? field.ToString() : "null")} => {(value != null ? value.ToString() : "null")}) |===] | {(Master.UIControl != null ? Master.UIControl.InvokeRequired.ToString() : "")}");
                field = value;
                if (!DisableNotifications) {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
                }
                return true;
            }
            return false;
        }

        protected bool SetUI<T>(ref T field, T value, bool check = true, [CallerMemberName] string propertyName = "") {
            if (!check || (field == null && value != null) || (field != null && !field.Equals(value))) {
                field = value;
                if (!DisableNotifications) {
                    Utils.UI(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)));
                }
                return true;
            }
            return false;
        }

        protected void Raise([CallerMemberName] string propertyName = "") {
            if (!DisableNotifications) {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        protected void RaiseUI([CallerMemberName] string propertyName = "") {
            if (!DisableNotifications) {
                Utils.UI(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)));
            }
        }
    }
}
﻿using SharpGen.Runtime;
using SharpGen.Runtime.Win32;
using System.Collections.ObjectModel;
using Vortice.MediaFoundation;

namespace FSPreview
{
    public class AudioMaster : CallbackBase, IMMNotificationClient//, INotifyPropertyChanged
    {
        /* TODO
         * 
         * 1) Master/Session Volume/Mute probably not required
         *      Possible only to ensure that we have session's volume unmuted and to 1? (probably the defaults?)
         */

        #region Properties (Public)
        /// <summary>
        /// Default audio device name
        /// </summary>
        public string DefaultDeviceName { get; private set; } = "Default";

        /// <summary>
        /// Default audio device id
        /// </summary>
        public string DefaultDeviceId { get; private set; } = "0";

        /// <summary>
        /// Whether no audio devices were found or audio failed to initialize
        /// </summary>
        public bool Failed { get; private set; }

        public string CurrentDeviceName { get; private set; } = "Default";

        public string CurrentDeviceId { get; private set; } = "0";

        /// <summary>
        /// List with of the active audio devices
        /// </summary>
        public ObservableCollection<string> Devices { get; private set; } = new ObservableCollection<string>();

        public string GetDeviceId(string deviceName) {
            if (deviceName == DefaultDeviceName) {
                return DefaultDeviceId;
            }
            foreach (var device in m_deviceEnum.EnumAudioEndpoints(DataFlow.Render, DeviceStates.Active)) {
                if (device.FriendlyName.ToLower() != deviceName.ToLower()) {
                    continue;
                }
                return device.Id;
            }
            throw new Exception("The specified audio device doesn't exist");
        }

        public string GetDeviceName(string deviceId) {
            if (deviceId == DefaultDeviceId) {
                return DefaultDeviceName;
            }
            foreach (var device in m_deviceEnum.EnumAudioEndpoints(DataFlow.Render, DeviceStates.Active)) {
                if (device.Id.ToLower() != deviceId.ToLower()) {
                    continue;
                }
                return device.FriendlyName;
            }
            throw new Exception("The specified audio device doesn't exist");
        }

        #endregion

        IMMDeviceEnumerator m_deviceEnum;

        private object m_locker = new object();

        public AudioMaster() {
            try {
                m_deviceEnum = new IMMDeviceEnumerator();
                var defaultDevice = m_deviceEnum.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                if (defaultDevice == null) {
                    Failed = true;
                    return;
                }

                Devices.Clear();
                Devices.Add(DefaultDeviceName);
                foreach (var device in m_deviceEnum.EnumAudioEndpoints(DataFlow.Render, DeviceStates.Active)) {
                    Devices.Add(device.FriendlyName);
                }

                CurrentDeviceId = defaultDevice.Id;
                CurrentDeviceName = defaultDevice.FriendlyName;

#if DEBUG
                string dump = "Audio devices ...\r\n";
                foreach(var device in deviceEnum.EnumAudioEndpoints(DataFlow.Render, DeviceStates.Active))
                    dump += $"{device.Id} | {device.FriendlyName} {(defaultDevice.Id == device.Id ? "*" : "")}\r\n";
                Log(dump);
#endif

                m_deviceEnum.RegisterEndpointNotificationCallback(this);
            } catch { Failed = true; }
        }

        private void RefreshDevices() {
            // Refresh Devices and initialize audio players if requried
            lock (m_locker) {
                Utils.UI(() => {
                    Devices.Clear();
                    Devices.Add(DefaultDeviceName);
                    foreach (var device in m_deviceEnum.EnumAudioEndpoints(DataFlow.Render, DeviceStates.Active)) {
                        Devices.Add(device.FriendlyName);
                    }
                    foreach (var player in Master.Players) {
                        if (!Devices.Contains(player.Audio.Device)) {
                            player.Audio.Device = DefaultDeviceName;
                        }
                        else {
                            player.Audio.RaiseDevice();
                        }
                    }
                });
                var defaultDevice = m_deviceEnum.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                if (defaultDevice != null) {
                    CurrentDeviceId = defaultDevice.Id;
                    CurrentDeviceName = defaultDevice.FriendlyName;
                }
            }
        }

        public void OnDeviceStateChanged(string pwstrDeviceId, int newState) { RefreshDevices(); }

        public void OnDeviceAdded(string pwstrDeviceId) { RefreshDevices(); }

        public void OnDeviceRemoved(string pwstrDeviceId) { RefreshDevices(); }

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string pwstrDefaultDeviceId) { RefreshDevices(); }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

        private void Log(string msg) { Utils.Log($"[AudioMaster] {msg}"); }
    }
}
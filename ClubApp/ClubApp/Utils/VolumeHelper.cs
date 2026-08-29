using System;
using System.Runtime.InteropServices;

namespace AetherShell.Client.Utils
{
    /// <summary>Системная громкость через Core Audio (без внешних пакетов).</summary>
    public static class VolumeHelper
    {
        public static float GetLevel()
        {
            try
            {
                var vol = GetEndpointVolume();
                if (vol == null) return 0f;
                try
                {
                    vol.GetMasterVolumeLevelScalar(out float level);
                    return Clamp01(level);
                }
                finally { Marshal.ReleaseComObject(vol); }
            }
            catch { return 0f; }
        }

        public static void SetLevel(float level)
        {
            try
            {
                var vol = GetEndpointVolume();
                if (vol == null) return;
                try
                {
                    vol.SetMasterVolumeLevelScalar(Clamp01(level), Guid.Empty);
                    // При изменении громкости снимаем mute, как в Windows
                    vol.SetMute(false, Guid.Empty);
                }
                finally { Marshal.ReleaseComObject(vol); }
            }
            catch { }
        }

        public static bool IsMuted()
        {
            try
            {
                var vol = GetEndpointVolume();
                if (vol == null) return false;
                try
                {
                    vol.GetMute(out bool muted);
                    return muted;
                }
                finally { Marshal.ReleaseComObject(vol); }
            }
            catch { return false; }
        }

        public static void SetMute(bool mute)
        {
            try
            {
                var vol = GetEndpointVolume();
                if (vol == null) return;
                try { vol.SetMute(mute, Guid.Empty); }
                finally { Marshal.ReleaseComObject(vol); }
            }
            catch { }
        }

        public static void ToggleMute() => SetMute(!IsMuted());

        public static void Adjust(float delta)
        {
            SetLevel(GetLevel() + delta);
        }

        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        private static IAudioEndpointVolume GetEndpointVolume()
        {
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            object endpointObj = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out device);
                if (device == null) return null;
                Guid iid = typeof(IAudioEndpointVolume).GUID;
                device.Activate(ref iid, 0, IntPtr.Zero, out endpointObj);
                return endpointObj as IAudioEndpointVolume;
            }
            finally
            {
                if (device != null) Marshal.ReleaseComObject(device);
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }
        }

        private enum EDataFlow { eRender, eCapture, eAll }
        private enum ERole { eConsole, eMultimedia, eCommunications }

        [ComImport]
        [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject { }

        [ComImport]
        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int NotImpl1();
            [PreserveSig]
            int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppDevice);
        }

        [ComImport]
        [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig]
            int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        }

        [ComImport]
        [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            // vtable order must match the COM interface exactly
            int RegisterControlChangeNotify(IntPtr pNotify);
            int UnregisterControlChangeNotify(IntPtr pNotify);
            int GetChannelCount(out uint pnChannelCount);
            int SetMasterVolumeLevel(float fLevelDB, Guid pguidEventContext);
            [PreserveSig] int SetMasterVolumeLevelScalar(float fLevel, Guid pguidEventContext);
            int GetMasterVolumeLevel(out float pfLevelDB);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float pfLevel);
            int SetChannelVolumeLevel(uint nChannel, float fLevelDB, Guid pguidEventContext);
            int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, Guid pguidEventContext);
            int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
            int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, Guid pguidEventContext);
            [PreserveSig] int GetMute(out bool pbMute);
        }
    }
}

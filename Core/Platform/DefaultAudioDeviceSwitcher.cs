using System.Runtime.InteropServices;

namespace BtAudioMixer.Core.Platform
{
    public static class DefaultAudioDeviceSwitcher
    {
        public static void SetDefaultDevice(string deviceId)
        {
            var policyConfig = (IPolicyConfig)new PolicyConfigClient();
            try
            {
                policyConfig.SetDefaultEndpoint(deviceId, ERole.eConsole);
                policyConfig.SetDefaultEndpoint(deviceId, ERole.eMultimedia);
                policyConfig.SetDefaultEndpoint(deviceId, ERole.eCommunications);
            }
            finally
            {
                Marshal.ReleaseComObject(policyConfig);
            }
        }
    }

    [ComImport]
    [Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    internal class PolicyConfigClient
    {
    }

    internal enum ERole
    {
        eConsole = 0,
        eMultimedia = 1,
        eCommunications = 2
    }

    [ComImport]
    [Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(string pszDeviceId, out IntPtr ppFormat);
        [PreserveSig] int GetDeviceFormat(string pszDeviceId, bool bDefault, out IntPtr ppFormat);
        [PreserveSig] int ResetDeviceFormat(string pszDeviceId);
        [PreserveSig] int SetDeviceFormat(string pszDeviceId, IntPtr pEndpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod(string pszDeviceId, bool bDefault, out long pmftDefaultPeriod, out long pmftMinimumPeriod);
        [PreserveSig] int SetProcessingPeriod(string pszDeviceId, IntPtr pmftPeriod);
        [PreserveSig] int GetShareMode(string pszDeviceId, IntPtr pMode);
        [PreserveSig] int SetShareMode(string pszDeviceId, IntPtr mode);
        [PreserveSig] int GetPropertyValue(string pszDeviceId, IntPtr key, IntPtr pv);
        [PreserveSig] int SetPropertyValue(string pszDeviceId, IntPtr key, IntPtr pv);
        [PreserveSig] int SetDefaultEndpoint(string pszDeviceId, ERole role);
        [PreserveSig] int SetEndpointVisibility(string pszDeviceId, bool bVisible);
    }
}

using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace GamepadEnhancer
{
    /// <summary>
    /// How the client reaches the driver: open its private control interface, then issue METHOD_BUFFERED IOCTLs. Every method returns a
    /// Win32 error code (0 = success) and never throws for an ordinary failure, so the client and the tests can treat the driver as a
    /// plain request/response device. <see cref="Win32VhfTransport"/> is the real one; the tests use a fake.
    /// </summary>
    internal interface IVhfTransport : IDisposable
    {
        bool IsOpen { get; }
        /// <summary>Opens the first present control interface. ERROR_NOT_FOUND means the driver is not installed or not running.</summary>
        int Open();
        int Ioctl(uint code, byte[] input, byte[] output, out int bytesReturned);
        void Close();
    }

    /// <summary>
    /// The real transport: finds the driver's device interface (<see cref="VhfProtocol.InterfaceGuid"/>) with SetupAPI, opens it exactly as
    /// upstream's client does (read/write, no sharing, SQOS impersonation) and sends IOCTLs with DeviceIoControl.
    /// </summary>
    internal sealed class Win32VhfTransport : IVhfTransport
    {
        private SafeFileHandle handle;

        public bool IsOpen => handle != null && !handle.IsInvalid && !handle.IsClosed;

        public int Open()
        {
            Close();
            Guid guid = VhfProtocol.InterfaceGuid;
            IntPtr devices = NativeMethods.SetupDiGetClassDevsW(ref guid, null, IntPtr.Zero, NativeMethods.DigcfPresent | NativeMethods.DigcfDeviceInterface);
            if (devices == NativeMethods.InvalidHandleValue) return Marshal.GetLastWin32Error();
            int lastError = VhfWin32.ErrorNotFound;
            try
            {
                for (uint index = 0; ; index++)
                {
                    var data = new NativeMethods.SpDeviceInterfaceData { cbSize = Marshal.SizeOf<NativeMethods.SpDeviceInterfaceData>() };
                    if (!NativeMethods.SetupDiEnumDeviceInterfaces(devices, IntPtr.Zero, ref guid, index, ref data))
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error != VhfWin32.ErrorNoMoreItems) lastError = error;
                        break;
                    }

                    string path = DevicePath(devices, ref data, out int detailError);
                    if (path == null) { lastError = detailError; continue; }

                    SafeFileHandle opened = NativeMethods.CreateFileW(path, NativeMethods.GenericRead | NativeMethods.GenericWrite, 0, IntPtr.Zero,
                        NativeMethods.OpenExisting, NativeMethods.FileAttributeNormal | NativeMethods.SecuritySqosPresent
                        | NativeMethods.SecurityImpersonation | NativeMethods.SecurityContextTracking, IntPtr.Zero);
                    if (opened.IsInvalid) { lastError = Marshal.GetLastWin32Error(); opened.Dispose(); continue; }

                    handle = opened;
                    return VhfWin32.ErrorSuccess;
                }
            }
            finally { NativeMethods.SetupDiDestroyDeviceInfoList(devices); }
            return lastError;
        }

        private static string DevicePath(IntPtr devices, ref NativeMethods.SpDeviceInterfaceData data, out int error)
        {
            error = VhfWin32.ErrorSuccess;
            NativeMethods.SetupDiGetDeviceInterfaceDetailW(devices, ref data, IntPtr.Zero, 0, out int needed, IntPtr.Zero);
            int first = Marshal.GetLastWin32Error();
            if (first != 122 /* ERROR_INSUFFICIENT_BUFFER */ || needed < 6) { error = first == 0 ? VhfWin32.ErrorInvalidData : first; return null; }

            IntPtr buffer = Marshal.AllocHGlobal(needed);
            try
            {
                // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize is the size of the fixed part: 8 on 64-bit, 6 on 32-bit.
                Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                if (!NativeMethods.SetupDiGetDeviceInterfaceDetailW(devices, ref data, buffer, needed, out _, IntPtr.Zero))
                { error = Marshal.GetLastWin32Error(); return null; }
                return Marshal.PtrToStringUni(IntPtr.Add(buffer, 4));
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        public int Ioctl(uint code, byte[] input, byte[] output, out int bytesReturned)
        {
            bytesReturned = 0;
            if (!IsOpen) return VhfWin32.ErrorInvalidHandle;
            bool ok = NativeMethods.DeviceIoControl(handle, code, input, input?.Length ?? 0, output, output?.Length ?? 0, out bytesReturned, IntPtr.Zero);
            return ok ? VhfWin32.ErrorSuccess : Marshal.GetLastWin32Error();
        }

        public void Close()
        {
            SafeFileHandle h = handle;
            handle = null;
            h?.Dispose();
        }

        public void Dispose() => Close();

        private static class NativeMethods
        {
            public static readonly IntPtr InvalidHandleValue = new(-1);
            public const uint DigcfPresent = 0x2, DigcfDeviceInterface = 0x10;
            public const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, OpenExisting = 3;
            public const uint FileAttributeNormal = 0x80, SecuritySqosPresent = 0x00100000, SecurityImpersonation = 0x00020000, SecurityContextTracking = 0x00040000;

            [StructLayout(LayoutKind.Sequential)]
            public struct SpDeviceInterfaceData
            {
                public int cbSize;
                public Guid InterfaceClassGuid;
                public int Flags;
                public IntPtr Reserved;
            }

            [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, string enumerator, IntPtr hwndParent, uint flags);

            [DllImport("setupapi.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SpDeviceInterfaceData deviceInterfaceData);

            [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr deviceInfoSet, ref SpDeviceInterfaceData deviceInterfaceData, IntPtr deviceInterfaceDetailData, int deviceInterfaceDetailDataSize, out int requiredSize, IntPtr deviceInfoData);

            [DllImport("setupapi.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode, byte[] inBuffer, int inBufferSize, byte[] outBuffer, int outBufferSize, out int bytesReturned, IntPtr overlapped);
        }
    }
}

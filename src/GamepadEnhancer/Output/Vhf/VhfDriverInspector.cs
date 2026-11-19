using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace GamepadEnhancer
{
    internal enum VhfDriverState
    {
        /// <summary>No root device of the driver exists on this PC.</summary>
        NotInstalled,
        /// <summary>The device exists but its control interface is not available (stopped, disabled or failed to start).</summary>
        InstalledNotRunning,
        /// <summary>The driver answered the handshake and speaks this app's protocol.</summary>
        Ready,
        /// <summary>The control interface is held by another program (the driver allows one owner at a time).</summary>
        InUse,
        /// <summary>The installed driver speaks a different protocol version.</summary>
        ProtocolMismatch,
        Error
    }

    /// <summary>What Windows knows about the driver's source device.</summary>
    internal sealed class VhfInstalledDevice
    {
        public string DriverVersion { get; init; }
        public string DriverProvider { get; init; }
        public string DriverDate { get; init; }
        /// <summary>The driver's catalog inside the driver store, when it could be located.</summary>
        public string CatalogPath { get; init; }
    }

    internal interface IVhfDeviceInfoSource
    {
        /// <summary>The installed source device, or null when there is none. Throws <see cref="Win32Exception"/> if Windows cannot be asked.</summary>
        VhfInstalledDevice Find();
    }

    /// <summary>Everything the status panel shows about the driver, produced by <see cref="VhfDriverInspector"/>.</summary>
    internal sealed class VhfDriverStatus
    {
        public VhfDriverState State { get; init; }
        public string Headline { get; init; }
        public string DriverVersion { get; init; }
        public string DriverProvider { get; init; }
        public string DriverDate { get; init; }
        public string Profiles { get; init; }
        public VhfSignature Signature { get; init; }
        public int Win32Error { get; init; }

        public bool IsReady => State == VhfDriverState.Ready || State == VhfDriverState.InUse;

        public string VersionLine => string.IsNullOrEmpty(DriverVersion) ? "Driver version: unknown" :
            "Driver version: " + DriverVersion + (string.IsNullOrEmpty(DriverDate) ? "" : " (" + DriverDate + ")") + (string.IsNullOrEmpty(DriverProvider) ? "" : ", " + DriverProvider);

        public string SignatureLine => Signature == null
            ? (State == VhfDriverState.NotInstalled ? "Signature: not installed" : "Signature: could not be read from the driver store")
            : "Signature: " + Signature.Describe();
    }

    /// <summary>
    /// Works out whether the VHF driver is installed, running and compatible, without changing anything. It opens the control interface
    /// only for the handshake (the driver allows one owner at a time), so while this app's own VHF pad is connected the caller passes
    /// <c>ownedByThisApp</c> and the handshake is skipped.
    /// </summary>
    internal sealed class VhfDriverInspector
    {
        private const int ErrorSharingViolation = 32;
        private readonly IVhfDeviceInfoSource devices;
        private readonly Func<IVhfTransport> transportFactory;
        private readonly IVhfSignatureReader signatures;

        public VhfDriverInspector(IVhfDeviceInfoSource devices, Func<IVhfTransport> transportFactory, IVhfSignatureReader signatures)
        {
            this.devices = devices ?? throw new ArgumentNullException(nameof(devices));
            this.transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
            this.signatures = signatures ?? throw new ArgumentNullException(nameof(signatures));
        }

        public static VhfDriverInspector CreateDefault() =>
            new(new SetupApiDeviceInfoSource(), () => new Win32VhfTransport(), new WinTrustSignatureReader());

        public VhfDriverStatus Inspect(bool ownedByThisApp = false)
        {
            VhfInstalledDevice device;
            try { device = devices.Find(); }
            catch (Win32Exception ex)
            {
                return new VhfDriverStatus { State = VhfDriverState.Error, Win32Error = ex.NativeErrorCode, Headline = "Windows could not be asked about the driver: " + ex.Message };
            }

            VhfSignature signature = null;
            if (device?.CatalogPath != null)
            {
                try { signature = signatures.Read(device.CatalogPath); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                { signature = new VhfSignature(VhfSignatureKind.Unsigned, "", "", "", "The installed catalog could not be read: " + ex.Message); }
            }

            string profiles = null;
            int error = VhfWin32.ErrorSuccess;
            VhfInfo info = default;
            if (ownedByThisApp && device != null)
            {
                return Build(VhfDriverState.Ready, device, signature, null, 0, "Ready. This app's virtual controller is using it.");
            }

            using (IVhfTransport transport = transportFactory())
            {
                var client = new VhfClient(transport);
                error = client.Connect();
                if (error == VhfWin32.ErrorSuccess) { info = client.Info; profiles = ProfilesText(info); }
            }

            if (error == VhfWin32.ErrorSuccess)
                return Build(VhfDriverState.Ready, device, signature, profiles, 0, "Ready. Profiles: " + profiles + ".");
            if (error == VhfWin32.ErrorRevisionMismatch)
                return Build(VhfDriverState.ProtocolMismatch, device, signature, null, error, VhfErrors.Describe(error));
            if (error == ErrorSharingViolation)
                return Build(VhfDriverState.InUse, device, signature, null, error, "The driver is installed and in use by another program.");
            if (error == VhfWin32.ErrorNotFound || error == VhfWin32.ErrorFileNotFound)
            {
                return device == null
                    ? Build(VhfDriverState.NotInstalled, null, null, null, error, "Not installed. ViGEm remains the default output.")
                    : Build(VhfDriverState.InstalledNotRunning, device, signature, null, error, "Installed, but its control interface is not available. Restart Windows or reinstall the driver.");
            }
            return Build(VhfDriverState.Error, device, signature, null, error, VhfErrors.Describe(error));
        }

        private static VhfDriverStatus Build(VhfDriverState state, VhfInstalledDevice device, VhfSignature signature, string profiles, int error, string headline) => new()
        {
            State = state,
            Headline = headline,
            DriverVersion = device?.DriverVersion,
            DriverProvider = device?.DriverProvider,
            DriverDate = device?.DriverDate,
            Profiles = profiles,
            Signature = signature,
            Win32Error = error
        };

        public static string ProfilesText(VhfInfo info)
        {
            var names = new List<string>();
            foreach (VhfProfileId p in new[] { VhfProfileId.XboxSeries, VhfProfileId.XboxOne, VhfProfileId.DualShock4, VhfProfileId.DualSense })
                if (info.HasProfile(p)) names.Add(VhfProfiles.NameOf(p));
            return names.Count == 0 ? "none this app can use" : string.Join(", ", names);
        }
    }

    /// <summary>Asks SetupAPI about the driver's root device: its driver version, provider and date, and where its catalog is.</summary>
    internal sealed class SetupApiDeviceInfoSource : IVhfDeviceInfoSource
    {
        public VhfInstalledDevice Find()
        {
            IntPtr set = Native.SetupDiGetClassDevsW(IntPtr.Zero, "ROOT", IntPtr.Zero, Native.DigcfPresent | Native.DigcfAllClasses);
            if (set == Native.InvalidHandleValue) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                for (uint index = 0; ; index++)
                {
                    var info = new Native.SpDevinfoData { cbSize = (uint)Marshal.SizeOf<Native.SpDevinfoData>() };
                    if (!Native.SetupDiEnumDeviceInfo(set, index, ref info))
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error == VhfWin32.ErrorNoMoreItems) return null;
                        throw new Win32Exception(error);
                    }
                    if (!HasHardwareId(set, ref info)) continue;

                    string inf = ReadString(set, ref info, Native.KeyDriverInfPath);
                    return new VhfInstalledDevice
                    {
                        DriverVersion = ReadString(set, ref info, Native.KeyDriverVersion),
                        DriverProvider = ReadString(set, ref info, Native.KeyDriverProvider),
                        DriverDate = ReadDate(set, ref info),
                        CatalogPath = inf == null ? null : FindCatalog(inf)
                    };
                }
            }
            finally { Native.SetupDiDestroyDeviceInfoList(set); }
        }

        private static bool HasHardwareId(IntPtr set, ref Native.SpDevinfoData info)
        {
            var buffer = new byte[1024];
            if (!Native.SetupDiGetDeviceRegistryPropertyW(set, ref info, 1 /* SPDRP_HARDWAREID */, out _, buffer, (uint)buffer.Length, out _)) return false;
            string all = Encoding.Unicode.GetString(buffer);
            return all.Split('\0').Any(id => string.Equals(id, VhfProtocol.RootHardwareId, StringComparison.OrdinalIgnoreCase));
        }

        private static string ReadString(IntPtr set, ref Native.SpDevinfoData info, Native.DevPropKey key)
        {
            var buffer = new byte[512];
            if (!Native.SetupDiGetDevicePropertyW(set, ref info, ref key, out uint type, buffer, (uint)buffer.Length, out uint required, 0) || type != 0x12 /* DEVPROP_TYPE_STRING */) return null;
            int chars = (int)Math.Min(required, (uint)buffer.Length) / 2;
            return Encoding.Unicode.GetString(buffer, 0, chars * 2).TrimEnd('\0');
        }

        private static string ReadDate(IntPtr set, ref Native.SpDevinfoData info)
        {
            var buffer = new byte[8];
            Native.DevPropKey key = Native.KeyDriverDate;
            if (!Native.SetupDiGetDevicePropertyW(set, ref info, ref key, out uint type, buffer, (uint)buffer.Length, out _, 0) || type != 0x10 /* DEVPROP_TYPE_FILETIME */) return null;
            long ticks = BitConverter.ToInt64(buffer, 0);
            return ticks > 0 ? DateTime.FromFileTimeUtc(ticks).ToString("yyyy-MM-dd") : null;
        }

        /// <summary>The catalog next to the driver's INF in the driver store (the INF is looked up by its installed name, oemNN.inf).</summary>
        private static string FindCatalog(string installedInfName)
        {
            var path = new StringBuilder(520);
            if (!Native.SetupGetInfDriverStoreLocationW(installedInfName, IntPtr.Zero, null, path, path.Capacity, out _)) return null;
            string folder = Path.GetDirectoryName(path.ToString());
            return folder != null && Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.cat").FirstOrDefault() : null;
        }

        private static class Native
        {
            public static readonly IntPtr InvalidHandleValue = new(-1);
            public const uint DigcfPresent = 0x2, DigcfAllClasses = 0x4;

            [StructLayout(LayoutKind.Sequential)]
            public struct SpDevinfoData { public uint cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }

            [StructLayout(LayoutKind.Sequential)]
            public struct DevPropKey { public Guid fmtid; public uint pid; }

            private static readonly Guid DriverKeys = new("a8b865dd-2e3d-4094-ad97-e593a70c75d6");
            public static readonly DevPropKey KeyDriverDate = new() { fmtid = DriverKeys, pid = 2 };
            public static readonly DevPropKey KeyDriverVersion = new() { fmtid = DriverKeys, pid = 3 };
            public static readonly DevPropKey KeyDriverInfPath = new() { fmtid = DriverKeys, pid = 5 };
            public static readonly DevPropKey KeyDriverProvider = new() { fmtid = DriverKeys, pid = 9 };

            [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string enumerator, IntPtr hwndParent, uint flags);

            [DllImport("setupapi.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SpDevinfoData deviceInfoData);

            [DllImport("setupapi.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

            [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfoData, uint property, out uint propertyRegDataType, byte[] propertyBuffer, uint propertyBufferSize, out uint requiredSize);

            [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetupDiGetDevicePropertyW(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfoData, ref DevPropKey propertyKey, out uint propertyType, byte[] propertyBuffer, uint propertyBufferSize, out uint requiredSize, uint flags);

            [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetupGetInfDriverStoreLocationW(string fileName, IntPtr alternatePlatformInfo, string localeName, StringBuilder returnBuffer, int returnBufferSize, out int requiredSize);
        }
    }

    /// <summary>
    /// Installs and removes the driver through the package's own setup tool (<c>install --inf &lt;path&gt;</c>, <c>remove</c>). Always elevated
    /// through the normal Windows UAC prompt and visible to the user; the app never installs silently, never changes Secure Boot or
    /// test-signing, and only runs a tool from a package that has just passed <see cref="VhfPackageVerifier"/>.
    /// </summary>
    internal static class VhfInstaller
    {
        public static ProcessStartInfo InstallStartInfo(VhfPackageReport package)
        {
            if (package == null || !package.Ok || package.SetupToolPath == null || package.InfPath == null)
                throw new InvalidOperationException("Only a package that passed the checks can be installed.");
            return Elevated(package.SetupToolPath, "install --inf \"" + package.InfPath + "\"");
        }

        public static ProcessStartInfo RemoveStartInfo(VhfPackageReport package)
        {
            if (package == null || !package.Ok || package.SetupToolPath == null)
                throw new InvalidOperationException("The setup tool of a package that passed the checks is needed to remove the driver.");
            return Elevated(package.SetupToolPath, "remove");
        }

        private static ProcessStartInfo Elevated(string tool, string arguments) => new()
        {
            FileName = tool,
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(tool),
            UseShellExecute = true,    // required for the UAC elevation prompt
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };

        /// <summary>Runs the tool and waits. Returns its exit code; throws <see cref="OperationCanceledException"/> if the UAC prompt was declined.</summary>
        public static async Task<int> RunAsync(ProcessStartInfo startInfo)
        {
            try
            {
                using Process process = Process.Start(startInfo);
                if (process == null) throw new InvalidOperationException("The setup tool could not be started.");
                await process.WaitForExitAsync();
                return process.ExitCode;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223 /* ERROR_CANCELLED */)
            {
                throw new OperationCanceledException("The Windows administrator prompt was cancelled, so nothing was changed.", ex);
            }
        }
    }
}

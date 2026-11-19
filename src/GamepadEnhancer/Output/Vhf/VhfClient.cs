using System;

namespace GamepadEnhancer
{
    /// <summary>A VHF failure with the Win32 code that caused it and a message a person can act on.</summary>
    internal sealed class VhfException : Exception
    {
        public int Win32Error { get; }
        public VhfException(int win32Error, string message) : base(message) { Win32Error = win32Error; }
    }

    internal static class VhfErrors
    {
        /// <summary>What a Win32 code from the driver means for a person using the app (never a raw number alone).</summary>
        public static string Describe(int code) => code switch
        {
            VhfWin32.ErrorSuccess => "OK",
            VhfWin32.ErrorNotFound or VhfWin32.ErrorFileNotFound =>
                "The virtual gamepad driver is not installed or not running. Install it from the Network and input page, or switch the output back to ViGEm.",
            VhfWin32.ErrorAccessDenied => "Windows refused access to the driver, or the controller slot belongs to another program.",
            VhfWin32.ErrorRevisionMismatch =>
                "The installed driver speaks a different protocol version than this app (it expects " + VhfProtocol.Version + "). Install the matching driver package.",
            VhfWin32.ErrorNotSupported => "The installed driver does not provide the selected controller profile.",
            VhfWin32.ErrorBusy => "The driver is still removing the previous virtual controller. Try again in a moment.",
            VhfWin32.ErrorDeviceRemoved => "The virtual controller was removed by Windows (sleep, resume or a device change).",
            VhfWin32.ErrorNotReady => "The driver is starting or stopping.",
            VhfWin32.ErrorInvalidData => "The driver sent an answer this app does not understand.",
            VhfWin32.ErrorInvalidParameter => "The driver rejected a request from this app.",
            VhfWin32.ErrorInvalidHandle => "The connection to the driver is closed.",
            _ => "The driver returned Windows error " + code + "."
        };
    }

    /// <summary>
    /// Typed client for the driver's control interface: one handle owns the controller slots it creates, so closing it (or the app
    /// crashing) releases them. Like upstream's client it is not thread safe: one thread at a time. The engine's single output thread is
    /// the only caller while connected.
    /// </summary>
    internal sealed class VhfClient : IDisposable
    {
        private readonly IVhfTransport transport;
        private VhfInfo info;

        public VhfClient(IVhfTransport transport) { this.transport = transport ?? throw new ArgumentNullException(nameof(transport)); }

        public bool Connected { get; private set; }
        public VhfInfo Info => info;

        /// <summary>Opens the control interface and does the version handshake. Returns 0 or a Win32 error.</summary>
        public int Connect()
        {
            Close();
            int error = transport.Open();
            if (error != VhfWin32.ErrorSuccess) return error;

            var response = new byte[VhfProtocol.QueryInfoResponseSize];
            error = Issue(VhfProtocol.IoctlQueryInfo, VhfProtocol.BuildQueryInfo(), response, out int bytes);
            if (error == VhfWin32.ErrorSuccess) error = VhfProtocol.ParseInfo(response, bytes, out info);
            if (error != VhfWin32.ErrorSuccess) { transport.Close(); info = default; return error; }
            Connected = true;
            return VhfWin32.ErrorSuccess;
        }

        public int CreateController(uint controllerId, VhfProfileId profile)
        {
            if (!Connected) return VhfWin32.ErrorInvalidHandle;
            if (controllerId >= info.MaximumControllers) return VhfWin32.ErrorInvalidParameter;
            if (!info.HasProfile(profile)) return VhfWin32.ErrorNotSupported;
            return Issue(VhfProtocol.IoctlCreateController, VhfProtocol.BuildCreate(controllerId, profile), null, out _);
        }

        public int DestroyController(uint controllerId)
        {
            if (!Connected) return VhfWin32.ErrorInvalidHandle;
            if (controllerId >= info.MaximumControllers) return VhfWin32.ErrorInvalidParameter;
            return Issue(VhfProtocol.IoctlDestroyController, VhfProtocol.BuildControllerId(controllerId), null, out _);
        }

        public int SubmitInput(in VhfInput input)
        {
            if (!Connected) return VhfWin32.ErrorInvalidHandle;
            return Issue(VhfProtocol.IoctlSubmitInputState, VhfProtocol.BuildInput(input), null, out _);
        }

        /// <summary>Returns 0 with a decoded event, ERROR_NO_MORE_ITEMS when nothing is pending, or another Win32 error.</summary>
        public int PollFeedback(uint controllerId, out VhfFeedback feedback)
        {
            feedback = null;
            if (!Connected) return VhfWin32.ErrorInvalidHandle;
            var buffer = new byte[VhfProtocol.FeedbackEventSize];
            int error = Issue(VhfProtocol.IoctlPollFeedback, VhfProtocol.BuildControllerId(controllerId), buffer, out int bytes);
            if (error != VhfWin32.ErrorSuccess) return error;
            return VhfProtocol.ParseFeedback(buffer, bytes, controllerId, out feedback);
        }

        public void Close()
        {
            Connected = false;
            info = default;
            transport.Close();
        }

        public void Dispose() { Close(); transport.Dispose(); }

        /// <summary>Upstream maps the driver's "invalid user buffer" to a protocol mismatch on every IOCTL: we built every buffer ourselves.</summary>
        private int Issue(uint code, byte[] input, byte[] output, out int bytes)
        {
            int error = transport.Ioctl(code, input, output, out bytes);
            return error == VhfWin32.ErrorInvalidUserBuffer ? VhfWin32.ErrorRevisionMismatch : error;
        }
    }
}

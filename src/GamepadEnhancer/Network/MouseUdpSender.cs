using System;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace GamepadEnhancer
{
    /// <summary>
    /// Streams this PC's physical mouse button state over UDP (12-byte packets, see docs/NETWORK_API.md)
    /// so another PC can use a button as a remote trigger. Sends on every change plus a 100 ms heartbeat.
    /// </summary>
    internal sealed class MouseUdpSender : IDisposable
    {
        internal const int PacketSize = 12;
        private const int PollMs = 4;
        private const int HeartbeatMs = 100;

        private readonly object sync = new object();
        private Thread thread;
        private volatile bool running;
        private UdpClient client;

        public bool IsRunning => running;
        public event Action<string> StatusChanged;

        /// <summary>Button mask: bit0 LMB, bit1 RMB, bit2 MMB, bit3 X1, bit4 X2.</summary>
        internal static byte ReadMask()
        {
            bool swapped = MouseButtonState.IsSwapped();
            byte mask = 0;
            if (MouseButtonState.IsDown(MouseButtonState.ResolveVk(MouseButtonState.VkLeft, swapped))) mask |= 0x01;
            if (MouseButtonState.IsDown(MouseButtonState.ResolveVk(MouseButtonState.VkRight, swapped))) mask |= 0x02;
            if (MouseButtonState.IsDown(MouseButtonState.VkMiddle)) mask |= 0x04;
            if (MouseButtonState.IsDown(MouseButtonState.VkX1)) mask |= 0x08;
            if (MouseButtonState.IsDown(MouseButtonState.VkX2)) mask |= 0x10;
            return mask;
        }

        /// <summary>First 4 bytes of SHA-256(token UTF-8); all zero when no token is set.</summary>
        internal static byte[] TokenTag(string token)
        {
            var tag = new byte[4];
            if (string.IsNullOrEmpty(token)) return tag;
            Array.Copy(SHA256.HashData(Encoding.UTF8.GetBytes(token)), tag, 4);
            return tag;
        }

        internal static byte[] BuildPacket(byte mask, ushort seq, byte[] tokenTag)
        {
            var p = new byte[PacketSize];
            p[0] = (byte)'A'; p[1] = (byte)'X'; p[2] = (byte)'M'; p[3] = (byte)'B';
            p[4] = 1;
            p[5] = mask;
            p[6] = (byte)(seq & 0xFF);
            p[7] = (byte)(seq >> 8);
            Array.Copy(tokenTag, 0, p, 8, 4);
            return p;
        }

        public void Start(string host, int port, string token)
        {
            Stop();
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Address is empty");
            var udp = new UdpClient();
            udp.Connect(host.Trim(), port);
            var tag = TokenTag(token);
            lock (sync)
            {
                client = udp;
                running = true;
                thread = new Thread(() => Loop(udp, tag)) { IsBackground = true, Name = "Mouse UDP sender" };
                thread.Start();
            }
            StatusChanged?.Invoke($"Sending mouse buttons to {host.Trim()}:{port}");
        }

        public void Stop()
        {
            Thread t;
            UdpClient c;
            lock (sync)
            {
                running = false;
                t = thread; c = client;
                thread = null; client = null;
            }
            try { c?.Dispose(); } catch { }
            if (t != null && t != Thread.CurrentThread) t.Join(500);
        }

        private void Loop(UdpClient udp, byte[] tag)
        {
            ushort seq = 0;
            byte last = 0xFF;
            long lastSendMs = 0;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            bool errorReported = false;
            while (running)
            {
                try
                {
                    byte mask = ReadMask();
                    long now = clock.ElapsedMilliseconds;
                    if (mask != last || now - lastSendMs >= HeartbeatMs)
                    {
                        var packet = BuildPacket(mask, seq++, tag);
                        udp.Send(packet, packet.Length);
                        last = mask;
                        lastSendMs = now;
                        errorReported = false;
                    }
                }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    // Send failures (e.g. unreachable host) are transient for UDP; report once and keep trying.
                    if (!errorReported) { errorReported = true; StatusChanged?.Invoke("Mouse UDP send failed: " + ex.Message); }
                }
                Thread.Sleep(PollMs);
            }
        }

        public void Dispose() => Stop();
    }
}

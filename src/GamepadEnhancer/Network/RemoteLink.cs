using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace GamepadEnhancer
{
    /// <summary>
    /// Client side of the Network API: connects to another PC, sends "auth &lt;token&gt;", then lines of MAKCU-style commands.
    /// Sending never blocks the engine for long (short send timeout); a broken link is re-established in the background and
    /// <see cref="Reconnected"/> tells the owner to resend its full state.
    /// </summary>
    internal sealed class RemoteLink : IDisposable
    {
        private readonly RemoteTarget target;
        private readonly object sync = new();
        private TcpClient client;
        private volatile bool connected;
        private volatile bool disposed;
        private Thread reconnectThread;

        public RemoteLink(RemoteTarget target) { this.target = target ?? throw new ArgumentNullException(nameof(target)); }

        public bool IsConnected => connected;
        public event Action<string> StatusChanged;
        /// <summary>Raised (from the reconnect thread) after the link came back; the sender must resend every value.</summary>
        public event Action Reconnected;

        /// <summary>Connects and authenticates once; throws with a readable message on failure.</summary>
        public void Open()
        {
            TcpClient c = Dial(target, 4000);
            lock (sync) { client = c; connected = true; }
            Raise($"Remote PC connected ({target.Host}:{target.Port})");
        }

        private static TcpClient Dial(RemoteTarget t, int timeoutMs)
        {
            var c = new TcpClient { NoDelay = true, SendTimeout = 150, ReceiveTimeout = timeoutMs };
            try
            {
                if (!c.ConnectAsync(t.Host, t.Port).Wait(timeoutMs)) throw new TimeoutException($"Timed out connecting to {t.Host}:{t.Port}");
                NetworkStream s = c.GetStream();
                byte[] auth = Encoding.UTF8.GetBytes("auth " + t.Token + "\n");
                s.Write(auth, 0, auth.Length);
                var buf = new byte[64];
                int n = s.Read(buf, 0, buf.Length);
                string reply = n > 0 ? Encoding.UTF8.GetString(buf, 0, n) : "";
                if (!reply.StartsWith("OK", StringComparison.Ordinal)) throw new InvalidOperationException("The other PC rejected the token or is not ready");
                c.ReceiveTimeout = 0;
                return c;
            }
            catch (AggregateException ex) { c.Dispose(); throw new InvalidOperationException(ex.InnerException?.Message ?? ex.Message); }
            catch { c.Dispose(); throw; }
        }

        /// <summary>Sends text (already newline-terminated lines). Returns false when the link is down.</summary>
        public bool TrySend(string text)
        {
            if (!connected) return false;
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(text);
                TcpClient c;
                lock (sync) c = client;
                c?.GetStream().Write(data, 0, data.Length);
                return c != null;
            }
            catch
            {
                MarkBroken();
                return false;
            }
        }

        private void MarkBroken()
        {
            lock (sync)
            {
                if (!connected) return;
                connected = false;
                try { client?.Close(); } catch { }
                client = null;
                if (!disposed && (reconnectThread == null || !reconnectThread.IsAlive))
                {
                    reconnectThread = new Thread(ReconnectLoop) { IsBackground = true, Name = "Remote link reconnect" };
                    reconnectThread.Start();
                }
            }
            Raise("Remote PC connection lost, reconnecting...");
        }

        private void ReconnectLoop()
        {
            int delay = 500;
            while (!disposed)
            {
                Thread.Sleep(delay);
                if (disposed) return;
                try
                {
                    TcpClient c = Dial(target, 2000);
                    lock (sync) { if (disposed) { c.Close(); return; } client = c; connected = true; }
                    Raise($"Remote PC reconnected ({target.Host}:{target.Port})");
                    try { Reconnected?.Invoke(); } catch { }
                    return;
                }
                catch { delay = Math.Min(delay * 2, 4000); }
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            try { TrySend("km.controller_clear()\n"); } catch { }   // never leave a stick or button held on the other PC
            disposed = true;
            lock (sync) { connected = false; try { client?.Close(); } catch { } client = null; }
        }

        private void Raise(string text) { try { StatusChanged?.Invoke(text); } catch { } }
    }
}

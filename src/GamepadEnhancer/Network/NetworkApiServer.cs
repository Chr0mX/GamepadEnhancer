using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GamepadEnhancer
{
    /// <summary>
    /// Line-based TCP server that accepts MAKCU-style commands from another computer and applies them to a
    /// <see cref="RemoteControlState"/>. The first line of every connection must be "auth &lt;token&gt;". One client at a time:
    /// a new authenticated connection replaces the old one. When the client goes away everything it set is released.
    /// </summary>
    internal sealed class NetworkApiServer : IDisposable
    {
        private const int IdleTimeoutMs = 30_000;

        private readonly RemoteControlState state;
        private readonly Func<string> token;
        private readonly Func<bool> padConnected;
        private readonly Func<byte> mouseSource;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly object sync = new();

        private TcpListener listener;
        private CancellationTokenSource cts;
        private TcpClient current;

        /// <param name="mouseSource">This PC's physical mouse button mask, pushed to clients that send km.mouse_stream(1). Null = none.</param>
        public NetworkApiServer(RemoteControlState state, Func<string> token, Func<bool> padConnected, Func<byte> mouseSource = null)
        {
            this.mouseSource = mouseSource ?? (() => 0);
            this.state = state ?? throw new ArgumentNullException(nameof(state));
            this.token = token ?? throw new ArgumentNullException(nameof(token));
            this.padConnected = padConnected ?? (() => true);
        }

        public bool IsRunning => listener != null;
        public int Port { get; private set; }

        /// <summary>Raised on a thread-pool thread whenever the listening/client status changes.</summary>
        public event Action<string> StatusChanged;

        public void Start(int port, bool bindAll)
        {
            Stop();
            var l = new TcpListener(bindAll ? IPAddress.Any : IPAddress.Loopback, port);
            l.Start();
            listener = l;
            Port = ((IPEndPoint)l.LocalEndpoint).Port;
            cts = new CancellationTokenSource();
            CancellationToken ct = cts.Token;
            Raise(bindAll ? $"Network API listening on port {Port} (all interfaces)" : $"Network API listening on port {Port} (this PC only)");
            _ = Task.Run(() => AcceptLoop(l, ct));
        }

        public void Stop()
        {
            CancellationTokenSource c = cts; cts = null;
            TcpListener l = listener; listener = null;
            try { c?.Cancel(); } catch { }
            try { l?.Stop(); } catch { }
            DropCurrent();
            state.Clear();
            if (l != null) Raise("Network API stopped");
        }

        public void Dispose() => Stop();

        private async Task AcceptLoop(TcpListener l, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await l.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
                catch { break; }
                _ = Task.Run(() => HandleClient(client, ct));
            }
        }

        private void DropCurrent()
        {
            TcpClient old;
            lock (sync) { old = current; current = null; }
            try { old?.Close(); } catch { }
        }

        private async Task HandleClient(TcpClient client, CancellationToken ct)
        {
            bool authed = false;
            CancellationTokenSource pushCts = null;   // runs the mouse pusher while this client is subscribed
            try
            {
                client.NoDelay = true;
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                NetworkStream stream = client.GetStream();
                var reader = new LineReader(stream, MakcuCommandParser.MaxLineLength);
                var session = new MakcuSession();

                // Replies (this loop) and pushed mouse lines (the pusher task) share one socket: one whole line at a time.
                var gate = new SemaphoreSlim(1, 1);
                async Task SendLine(string text)
                {
                    await gate.WaitAsync().ConfigureAwait(false);
                    try { await Send(stream, text).ConfigureAwait(false); }
                    finally { gate.Release(); }
                }

                while (!ct.IsCancellationRequested)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(authed ? IdleTimeoutMs : 5_000);
                    string line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
                    if (line == null) break;                       // client closed
                    if (line.Length > MakcuCommandParser.MaxLineLength) { await SendLine("ERR"); break; }

                    if (!authed)
                    {
                        if (!CheckAuth(line)) { await SendLine("ERR auth"); break; }
                        authed = true;
                        TcpClient previous;
                        lock (sync) { previous = current; current = client; }
                        try { previous?.Close(); } catch { }       // newest client wins
                        Raise("Network API: client connected");
                        await SendLine("OK" + MakcuCommandParser.Prompt);
                        continue;
                    }

                    // Mouse commands do not need the virtual pad, so a client can subscribe before the pad is connected.
                    bool mouseCommand = line.StartsWith("km.mouse", StringComparison.OrdinalIgnoreCase);
                    if (!mouseCommand && !padConnected()) { await SendLine("ERR not connected"); continue; }
                    string reply = MakcuCommandParser.Handle(line, state, session, clock.ElapsedMilliseconds, () => mouseSource());
                    if (reply != null) await SendLine(reply);

                    if (session.MouseStream && pushCts == null)
                    {
                        pushCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        CancellationToken pushToken = pushCts.Token;   // captured now: pushCts is cleared on unsubscribe
                        _ = Task.Run(() => PushMouse(SendLine, pushToken));
                    }
                    else if (!session.MouseStream && pushCts != null)
                    {
                        try { pushCts.Cancel(); } catch { }
                        pushCts = null;
                    }
                }
            }
            catch { /* disconnect, timeout or cancel */ }
            finally
            {
                try { pushCts?.Cancel(); } catch { }
                bool wasCurrent;
                lock (sync) { wasCurrent = ReferenceEquals(current, client); if (wasCurrent) current = null; }
                try { client.Close(); } catch { }
                if (wasCurrent)
                {
                    state.Clear();                                  // a dropped client must never leave a stick or button held
                    Raise("Network API: client disconnected");
                }
            }
        }

        /// <summary>Pushes km.mouse(mask) to one subscribed client: immediately, on every change, and as a keep-alive.</summary>
        private async Task PushMouse(Func<string, Task> send, CancellationToken ct)
        {
            int last = -1;
            long lastSend = 0;
            var clk = Stopwatch.StartNew();
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    int mask = mouseSource() & MousePush.MaskAll;
                    long now = clk.ElapsedMilliseconds;
                    if (MousePush.ShouldSend(true, mask, last, now, lastSend))
                    {
                        await send("km.mouse(" + mask.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")").ConfigureAwait(false);
                        last = mask;
                        lastSend = now;
                    }
                    await Task.Delay(MousePush.PollMs, ct).ConfigureAwait(false);
                }
            }
            catch { /* client gone or cancelled */ }
        }

        private bool CheckAuth(string line)
        {
            string expected = token();
            if (string.IsNullOrEmpty(expected) || !line.StartsWith("auth ", StringComparison.OrdinalIgnoreCase)) return false;
            byte[] a = Encoding.UTF8.GetBytes(line.Substring(5).Trim());
            byte[] b = Encoding.UTF8.GetBytes(expected);
            return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
        }

        private static async Task Send(NetworkStream stream, string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text.EndsWith("> ", StringComparison.Ordinal) ? text : text + "\r\n");
            await stream.WriteAsync(data).ConfigureAwait(false);
        }

        private void Raise(string text) { try { StatusChanged?.Invoke(text); } catch { } }

        /// <summary>Reads "\n"-terminated lines with a hard length cap (so a client cannot make us buffer without limit).</summary>
        private sealed class LineReader
        {
            private readonly NetworkStream stream;
            private readonly int max;
            private readonly byte[] buf = new byte[512];
            private readonly StringBuilder sb = new();
            private int pos, len;

            public LineReader(NetworkStream stream, int max) { this.stream = stream; this.max = max; }

            public async Task<string> ReadLineAsync(CancellationToken ct)
            {
                while (true)
                {
                    while (pos < len)
                    {
                        byte c = buf[pos++];
                        if (c == (byte)'\n') { string s = sb.ToString().TrimEnd('\r'); sb.Clear(); return s; }
                        if (sb.Length <= max) sb.Append((char)c);
                    }
                    if (sb.Length > max) { string s = sb.ToString(); sb.Clear(); return s; }
                    len = await stream.ReadAsync(buf.AsMemory(0, buf.Length), ct).ConfigureAwait(false);
                    pos = 0;
                    if (len == 0) return null;
                }
            }
        }
    }
}

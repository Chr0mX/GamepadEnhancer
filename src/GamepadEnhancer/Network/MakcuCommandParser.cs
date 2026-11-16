using System;
using System.Globalization;

namespace GamepadEnhancer
{
    /// <summary>Per-connection protocol state.</summary>
    internal sealed class MakcuSession
    {
        public bool Echo;
    }

    /// <summary>
    /// Parses MAKCU/MAKXD-style text commands (one per line) and applies them to a <see cref="RemoteControlState"/>.
    /// Supported: km.controller(id,value[,hold_ms]), km.controller_clear(), km.controller(0|1) (accepted, no-op),
    /// km.version(), km.api_version(), km.echo([0|1]). Queries answer "result\r\n>>> "; mutations are silent (echo off) and
    /// answer nothing; anything else answers "ERR".
    /// </summary>
    internal static class MakcuCommandParser
    {
        public const string Prompt = "\r\n>>> ";
        public const int ApiVersion = 1;
        public const string VersionText = "ViGEm bridge (MAKCU API) 1.0";
        public const int MaxLineLength = 256;

        /// <returns>The text to send back, or null when nothing should be sent.</returns>
        public static string Handle(string line, RemoteControlState state, MakcuSession session, long nowMs)
        {
            if (line == null) return null;
            line = line.Trim();
            if (line.Length == 0) return null;
            if (line.Length > MaxLineLength) return "ERR";

            int open = line.IndexOf('(');
            if (open < 0 || !line.EndsWith(")", StringComparison.Ordinal)) return Error(line, session);
            string name = line.Substring(0, open).Trim().ToLowerInvariant();
            string args = line.Substring(open + 1, line.Length - open - 2).Trim();
            string[] parts = args.Length == 0 ? Array.Empty<string>() : args.Split(',');

            string reply;
            switch (name)
            {
                case "km.version":
                    reply = Query(VersionText);
                    break;
                case "km.api_version":
                    reply = Query(ApiVersion.ToString(CultureInfo.InvariantCulture));
                    break;
                case "km.echo":
                    if (parts.Length == 0) { reply = Query(session.Echo ? "1" : "0"); break; }
                    if (parts.Length != 1 || !TryInt(parts[0], out int e) || (e != 0 && e != 1)) return Error(line, session);
                    session.Echo = e == 1;
                    reply = null;
                    break;
                case "km.controller_clear":
                    if (parts.Length != 0) return Error(line, session);
                    state.Clear();
                    reply = null;
                    break;
                case "km.controller":
                    if (parts.Length == 1)
                    {
                        // km.controller(0|1): input-change streaming switch. Nothing to stream from a virtual pad; accepted.
                        if (!TryInt(parts[0], out int en) || (en != 0 && en != 1)) return Error(line, session);
                        reply = null;
                        break;
                    }
                    if (parts.Length == 0) { reply = Query("0"); break; }
                    if (parts.Length < 2 || parts.Length > 3) return Error(line, session);
                    if (!TryInt(parts[0], out int id) || !TryInt(parts[1], out int val)) return Error(line, session);
                    int hold = 0;
                    if (parts.Length == 3 && (!TryInt(parts[2], out hold) || hold < 0)) return Error(line, session);
                    if (!state.Set(id, val, hold, nowMs)) return Error(line, session);
                    reply = null;
                    break;
                default:
                    return Error(line, session);
            }

            if (session.Echo) return line + "\r\n" + (reply ?? Prompt.TrimStart('\r', '\n'));
            return reply;
        }

        private static string Query(string result) => result + Prompt;
        private static string Error(string line, MakcuSession session) => "ERR";

        private static bool TryInt(string s, out int v) =>
            int.TryParse(s.Trim().Trim('\'', '"'), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out v);
    }
}

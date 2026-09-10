// jrdp — RdpFile.cs
//
// Parses Microsoft .rdp connection files, the format mstsc.exe writes from
// "Save As" and that Windows hands a handler as a file path. Lines look like:
//
//   full address:s:srv01.corp.example:3389
//   username:s:CORP\me
//   audiomode:i:0
//   redirectclipboard:i:1
//
// i.e. `<setting>:<type>:<value>` where type is `s` (string), `i` (int) or
// `b` (binary blob — we ignore those; they hold things like the encrypted
// password and per-monitor layout that we can't and shouldn't consume).
//
// Only settings that map onto RdpConnectionSettings are read; everything else
// is ignored rather than rejected, because a real .rdp file written by mstsc
// contains dozens of keys we have no equivalent for and failing on them would
// make the file association useless.
//
// Not supported on purpose: `password 51:b:` — that's the DPAPI-encrypted
// password, decryptable only by the user who saved it. We leave it alone and
// let the server prompt.

using System;
using System.Globalization;
using System.IO;

namespace Jrdp
{
    internal static class RdpFile
    {
        // True if the argument looks like a .rdp file we should parse rather
        // than an option. Extension-based: Windows passes a full path, and we
        // don't want to sniff the contents of arbitrary files.
        public static bool LooksLikeRdpFile(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return false;
            return arg.EndsWith(".rdp", StringComparison.OrdinalIgnoreCase);
        }

        // Applies the file's settings onto `s`. Returns an error message, or
        // null on success. Values already set on `s` (e.g. from an earlier
        // --host) are overwritten by the file, and options passed AFTER the
        // file on the command line win, because Parse applies them later.
        public static string Apply(string path, RdpConnectionSettings s)
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
            }
            catch (Exception ex)
            {
                return "cannot read \"" + path + "\": " + ex.Message;
            }

            foreach (var raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;

                // Split into exactly three parts: setting, type, value. The
                // value itself may contain ':' (e.g. "host:3389", IPv6), so we
                // only split on the first two separators.
                int c1 = line.IndexOf(':');
                if (c1 <= 0) continue;
                int c2 = line.IndexOf(':', c1 + 1);
                if (c2 < 0) continue;

                string name  = line.Substring(0, c1).Trim().ToLowerInvariant();
                string type  = line.Substring(c1 + 1, c2 - c1 - 1).Trim().ToLowerInvariant();
                string value = line.Substring(c2 + 1);

                if (type == "b") continue;  // binary blob — not consumable

                switch (name)
                {
                    case "full address":
                        // "host", "host:3389", "[::1]:3389". Port, if present,
                        // overrides the separate `server port` setting.
                        ApplyAddress(value.Trim(), s);
                        break;
                    case "server port":
                        s.Port = ParseInt(value, s.Port);
                        break;
                    case "username":
                        s.Username = value.Trim();
                        break;
                    case "domain":
                        s.Domain = value.Trim();
                        break;

                    case "desktopwidth":
                        s.Width = ParseInt(value, s.Width);
                        break;
                    case "desktopheight":
                        s.Height = ParseInt(value, s.Height);
                        break;
                    // "smart sizing" in the file; 1 = scale to window.
                    case "smart sizing":
                        s.SmartSizing = ParseBool(value, s.SmartSizing);
                        break;

                    case "redirectclipboard":
                        s.RedirectClipboard = ParseBool(value, s.RedirectClipboard);
                        break;
                    // mstsc writes `drivestoredirect:s:*` (all drives) rather
                    // than a boolean; any non-empty value means "redirect".
                    case "drivestoredirect":
                        s.RedirectDrives = value.Trim().Length > 0;
                        break;
                    case "redirectprinters":
                        s.RedirectPrinters = ParseBool(value, s.RedirectPrinters);
                        break;
                    case "redirectsmartcards":
                        s.RedirectSmartCards = ParseBool(value, s.RedirectSmartCards);
                        break;
                    case "redirectcomports":
                        s.RedirectPorts = ParseBool(value, s.RedirectPorts);
                        break;
                    case "redirectwebauthn":
                        s.RedirectWebauthn = ParseBool(value, s.RedirectWebauthn);
                        break;

                    case "audiomode":
                        s.AudioMode = ParseInt(value, s.AudioMode);
                        break;
                    case "audiocapturemode":
                        s.AudioCapture = ParseBool(value, s.AudioCapture);
                        break;

                    case "enablecredsspsupport":
                        s.EnableNla = ParseBool(value, s.EnableNla);
                        break;
                    // 0 = connect and don't warn, 1 = warn, 2 = don't connect,
                    // 3 = warn. Only the explicit "ignore" value maps cleanly.
                    case "authentication level":
                        s.IgnoreCertErrors = ParseInt(value, 2) == 0;
                        break;

                    default:
                        break;  // unknown setting — ignore, don't fail
                }
            }

            if (string.IsNullOrEmpty(s.Host))
            {
                return "\"" + path + "\" has no \"full address\" setting";
            }
            if (s.AudioMode < 0 || s.AudioMode > 2) s.AudioMode = 0;
            if (s.Port < 1 || s.Port > 65535) s.Port = 3389;
            return null;
        }

        // Splits "host", "host:port" and "[v6::addr]:port" into Host + Port.
        private static void ApplyAddress(string address, RdpConnectionSettings s)
        {
            if (address.Length == 0) return;

            if (address.StartsWith("["))
            {
                // Bracketed IPv6 literal: [::1] or [::1]:3389
                int close = address.IndexOf(']');
                if (close > 0)
                {
                    s.Host = address.Substring(1, close - 1);
                    string rest = address.Substring(close + 1);
                    if (rest.StartsWith(":")) s.Port = ParseInt(rest.Substring(1), s.Port);
                    return;
                }
            }

            int colon = address.LastIndexOf(':');
            // A single colon means host:port; several means a bare IPv6 literal
            // (which has no port to split off).
            if (colon > 0 && address.IndexOf(':') == colon)
            {
                s.Host = address.Substring(0, colon);
                s.Port = ParseInt(address.Substring(colon + 1), s.Port);
                return;
            }
            s.Host = address;
        }

        private static int ParseInt(string v, int def)
        {
            int parsed;
            return int.TryParse(v.Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out parsed) ? parsed : def;
        }

        private static bool ParseBool(string v, bool def)
        {
            string t = v.Trim();
            if (t.Length == 0) return def;
            if (t == "1" || t.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (t == "0" || t.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            return def;
        }
    }
}

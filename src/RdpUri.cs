// jrdp — RdpUri.cs
//
// Parses rdp:// URIs, so a link in a browser, chat client or intranet page can
// launch a session. Two shapes are accepted:
//
//   1. Authority form (the intuitive one):
//        rdp://srv01
//        rdp://srv01:3390
//        rdp://CORP%5Cme@srv01:3389
//        rdp://srv01?redirectclipboard=0&audiomode=2
//
//   2. Microsoft's setting form, as used by the Remote Desktop clients:
//        rdp://full%20address=s:srv01:3389&username=s:CORP\me
//      Everything after "rdp://" is `&`-separated `setting=type:value` pairs
//      using the same names as a .rdp file.
//
// Both are percent-decoded. In the authority form the query string accepts the
// same setting names as a .rdp file (without the `:type:` part) so a link can
// carry redirection options.
//
// SECURITY: a password in a URI is intentionally NOT honoured. URIs get logged
// by browsers, shells and shell-history; accepting one would encourage putting
// credentials somewhere they leak. Use the dialog, or let the server prompt.

using System;

namespace Jrdp
{
    internal static class RdpUri
    {
        public const string Scheme = "rdp://";

        public static bool LooksLikeRdpUri(string arg)
        {
            return !string.IsNullOrEmpty(arg)
                && arg.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase);
        }

        // Applies the URI onto `s`. Returns an error message, or null on
        // success.
        public static string Apply(string uri, RdpConnectionSettings s)
        {
            string body = uri.Substring(Scheme.Length);
            if (body.Length == 0) return "empty rdp:// URI";

            // Microsoft's setting form is distinguished by an '=' appearing
            // before any '/' — "full address=s:host" vs "host?query=x".
            int eq = body.IndexOf('=');
            int slash = body.IndexOf('/');
            int question = body.IndexOf('?');
            bool settingForm = eq >= 0
                               && (slash < 0 || eq < slash)
                               && (question < 0 || eq < question);

            string error = settingForm ? ApplySettingForm(body, s)
                                       : ApplyAuthorityForm(body, s);
            if (error != null) return error;

            if (string.IsNullOrEmpty(s.Host)) return "rdp:// URI has no host";
            if (s.Port < 1 || s.Port > 65535) s.Port = 3389;
            if (s.AudioMode < 0 || s.AudioMode > 2) s.AudioMode = 0;
            return null;
        }

        // rdp://full%20address=s:srv01:3389&username=s:me
        private static string ApplySettingForm(string body, RdpConnectionSettings s)
        {
            foreach (var pair in body.Split('&'))
            {
                if (pair.Length == 0) continue;
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;

                string name = Decode(pair.Substring(0, eq)).Trim().ToLowerInvariant();
                string rest = pair.Substring(eq + 1);

                // Strip the "s:" / "i:" type prefix if present.
                if (rest.Length > 1 && rest[1] == ':'
                    && (rest[0] == 's' || rest[0] == 'i' || rest[0] == 'b'))
                {
                    if (rest[0] == 'b') continue;   // binary blob
                    rest = rest.Substring(2);
                }
                ApplySetting(name, Decode(rest), s);
            }
            return null;
        }

        // rdp://[user@]host[:port][?setting=value&...]
        private static string ApplyAuthorityForm(string body, RdpConnectionSettings s)
        {
            string authority = body;
            string query = null;

            int q = body.IndexOf('?');
            if (q >= 0)
            {
                authority = body.Substring(0, q);
                query = body.Substring(q + 1);
            }
            // Tolerate a trailing slash: rdp://srv01/
            authority = authority.TrimEnd('/');
            if (authority.Length == 0) return "rdp:// URI has no host";

            // Optional userinfo. Only the user name is taken; a password here
            // is deliberately ignored (see the note at the top of this file).
            int at = authority.LastIndexOf('@');
            if (at >= 0)
            {
                string userinfo = Decode(authority.Substring(0, at));
                authority = authority.Substring(at + 1);

                int colon = userinfo.IndexOf(':');
                if (colon >= 0) userinfo = userinfo.Substring(0, colon);

                // "DOMAIN\user" splits into Domain + Username.
                int slash = userinfo.IndexOf('\\');
                if (slash > 0)
                {
                    s.Domain   = userinfo.Substring(0, slash);
                    s.Username = userinfo.Substring(slash + 1);
                }
                else
                {
                    s.Username = userinfo;
                }
            }

            ApplyHostPort(Decode(authority), s);

            if (query != null)
            {
                foreach (var pair in query.Split('&'))
                {
                    if (pair.Length == 0) continue;
                    int eq = pair.IndexOf('=');
                    if (eq <= 0) continue;
                    ApplySetting(
                        Decode(pair.Substring(0, eq)).Trim().ToLowerInvariant(),
                        Decode(pair.Substring(eq + 1)),
                        s);
                }
            }
            return null;
        }

        private static void ApplyHostPort(string authority, RdpConnectionSettings s)
        {
            if (authority.StartsWith("["))
            {
                int close = authority.IndexOf(']');
                if (close > 0)
                {
                    s.Host = authority.Substring(1, close - 1);
                    string rest = authority.Substring(close + 1);
                    if (rest.StartsWith(":")) s.Port = ToInt(rest.Substring(1), s.Port);
                    return;
                }
            }
            int colon = authority.LastIndexOf(':');
            if (colon > 0 && authority.IndexOf(':') == colon)
            {
                s.Host = authority.Substring(0, colon);
                s.Port = ToInt(authority.Substring(colon + 1), s.Port);
                return;
            }
            s.Host = authority;
        }

        // Shared between both URI forms; setting names match .rdp files.
        private static void ApplySetting(string name, string value, RdpConnectionSettings s)
        {
            switch (name)
            {
                case "full address":
                case "host":
                case "server":
                    ApplyHostPort(value.Trim(), s);
                    break;
                case "server port":
                case "port":
                    s.Port = ToInt(value, s.Port);
                    break;
                case "username":
                case "user":
                    s.Username = value.Trim();
                    break;
                case "domain":
                    s.Domain = value.Trim();
                    break;

                case "desktopwidth":
                case "width":
                    s.Width = ToInt(value, s.Width);
                    break;
                case "desktopheight":
                case "height":
                    s.Height = ToInt(value, s.Height);
                    break;
                case "smart sizing":
                case "smartsizing":
                    s.SmartSizing = ToBool(value, s.SmartSizing);
                    break;

                case "redirectclipboard":
                case "clipboard":
                    s.RedirectClipboard = ToBool(value, s.RedirectClipboard);
                    break;
                case "drivestoredirect":
                case "drives":
                    s.RedirectDrives = ToBool(value, s.RedirectDrives);
                    break;
                case "redirectprinters":
                case "printers":
                    s.RedirectPrinters = ToBool(value, s.RedirectPrinters);
                    break;
                case "redirectsmartcards":
                    s.RedirectSmartCards = ToBool(value, s.RedirectSmartCards);
                    break;
                case "redirectcomports":
                    s.RedirectPorts = ToBool(value, s.RedirectPorts);
                    break;
                case "redirectwebauthn":
                    s.RedirectWebauthn = ToBool(value, s.RedirectWebauthn);
                    break;

                case "audiomode":
                    s.AudioMode = ToInt(value, s.AudioMode);
                    break;
                case "audiocapturemode":
                    s.AudioCapture = ToBool(value, s.AudioCapture);
                    break;

                case "enablecredsspsupport":
                case "nla":
                    s.EnableNla = ToBool(value, s.EnableNla);
                    break;
                case "authentication level":
                    s.IgnoreCertErrors = ToInt(value, 2) == 0;
                    break;

                // A password in a URI is ignored on purpose.
                case "password":
                case "password 51":
                    break;

                default:
                    break;  // unknown setting — ignore
            }
        }

        private static string Decode(string v)
        {
            if (string.IsNullOrEmpty(v)) return v ?? "";
            try { return Uri.UnescapeDataString(v.Replace("+", "%2B")); }
            catch { return v; }
        }

        private static int ToInt(string v, int def)
        {
            int parsed;
            return int.TryParse((v ?? "").Trim(), out parsed) ? parsed : def;
        }

        private static bool ToBool(string v, bool def)
        {
            string t = (v ?? "").Trim();
            if (t.Length == 0) return def;
            if (t == "1" || t.Equals("true", StringComparison.OrdinalIgnoreCase)
                         || t == "*") return true;
            if (t == "0" || t.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            return def;
        }
    }
}

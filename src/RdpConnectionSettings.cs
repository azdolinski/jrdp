// jrdp — RdpConnectionSettings.cs
//
// Everything needed to open an RDP session. Both entry points build one of
// these: stdio mode parses it out of the `connect` JSON command, GUI mode
// fills it from the connect dialog / command-line args. RdpSessionHost then
// applies it to the OCX in exactly one place.

using System.Text.Json.Nodes;

namespace Jrdp
{
    public class RdpConnectionSettings
    {
        public string Host = "";
        public int    Port = 3389;
        public string Username = "";
        public string Domain = "";
        public string Password = "";

        public int Width  = 1280;
        public int Height = 720;

        public bool SmartSizing = true;

        public bool RedirectDrives     = true;
        public bool RedirectPrinters   = true;
        public bool RedirectClipboard  = true;
        public bool RedirectSmartCards = false;
        public bool RedirectPorts      = false;
        public bool RedirectWebauthn   = false;

        // 0 = play locally, 1 = play on remote machine, 2 = disabled
        public int  AudioMode    = 0;
        public bool AudioCapture = false;

        public bool EnableNla        = true;
        public bool IgnoreCertErrors = false;

        // Parses the wire format used by the stdio `connect` command. Field
        // names and defaults are kept identical to the original helper so
        // existing hosts (yterminal) keep working byte-for-byte.
        public static RdpConnectionSettings FromJson(JsonObject cmd)
        {
            return new RdpConnectionSettings
            {
                Host     = cmd.GetStr("host"),
                Port     = cmd.GetInt("port", 3389),
                Username = cmd.GetStr("username"),
                Domain   = cmd.GetStr("domain"),
                Password = cmd.GetStr("password"),

                Width  = cmd.GetInt("width",  1280),
                Height = cmd.GetInt("height", 720),

                SmartSizing = cmd.GetBool("smartSizing", true),

                RedirectDrives     = cmd.GetBool("redirectDrives",     true),
                RedirectPrinters   = cmd.GetBool("redirectPrinters",   true),
                RedirectClipboard  = cmd.GetBool("redirectClipboard",  true),
                RedirectSmartCards = cmd.GetBool("redirectSmartCards", false),
                RedirectPorts      = cmd.GetBool("redirectPorts",      false),
                RedirectWebauthn   = cmd.GetBool("redirectWebauthn",   false),

                AudioMode    = cmd.GetInt("audioMode", 0),
                AudioCapture = cmd.GetBool("audioCapture", false),

                EnableNla        = cmd.GetBool("enableNla", true),
                IgnoreCertErrors = cmd.GetBool("ignoreCertErrors", false),
            };
        }
    }
}

// jrdp — CommandLineOptions.cs
//
// Minimal argument parser. Accepts `--key value` and `--key=value`; boolean
// flags take an optional explicit value so both `--no-clipboard` style
// negation and `--clipboard=false` work.
//
// Unknown arguments are an error rather than being ignored, so a typo like
// `--hosst srv01` doesn't silently open an empty connect dialog.

using System;
using System.Collections.Generic;

namespace Jrdp
{
    internal class CommandLineOptions
    {
        public bool Stdio;
        public bool ShowHelp;
        public bool ShowVersion;
        public bool PrintConfig;
        public string Error;
        public RdpConnectionSettings Settings = new RdpConnectionSettings();

        // The bare argument consumed as an input source (a .rdp path or an
        // rdp:// URI), so the option loop can skip it instead of reporting it
        // as unknown.
        public string ConsumedPositional;

        public static CommandLineOptions Parse(string[] args)
        {
            var o = new CommandLineOptions();
            var s = o.Settings;

            // A bare (non-option) argument is what Windows passes a registered
            // handler: a .rdp file path from the file association, or an
            // rdp:// URI from the protocol handler. Apply those FIRST so any
            // explicit flags after them win — `jrdp x.rdp --port 3390` uses
            // 3390 regardless of what the file says.
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg.Length == 0 || arg.StartsWith("-")) continue;

                // Only treat it as an input source if it's recognisably one;
                // otherwise leave it to the option loop, which reports it as
                // an unknown argument.
                if (RdpUri.LooksLikeRdpUri(arg))
                {
                    o.Error = RdpUri.Apply(arg, s);
                    if (o.Error != null) return o;
                    o.ConsumedPositional = arg;
                    break;
                }
                if (RdpFile.LooksLikeRdpFile(arg))
                {
                    o.Error = RdpFile.Apply(arg, s);
                    if (o.Error != null) return o;
                    o.ConsumedPositional = arg;
                    break;
                }
            }

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg.Length == 0) continue;

                // Already handled above as the input source.
                if (arg == o.ConsumedPositional) continue;

                // Split --key=value into key + inline value.
                string key = arg, inline = null;
                int eq = arg.IndexOf('=');
                if (eq > 0)
                {
                    key = arg.Substring(0, eq);
                    inline = arg.Substring(eq + 1);
                }
                string name = key.TrimStart('-').ToLowerInvariant();

                // Pull the next positional token as this option's value.
                Func<string> nextValue = () =>
                {
                    if (inline != null) return inline;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) return args[++i];
                    o.Error = "missing value for --" + name;
                    return null;
                };
                // Boolean: --flag, --flag=false, --flag true
                Func<bool, bool> boolValue = def =>
                {
                    string v = inline;
                    if (v == null && i + 1 < args.Length
                        && (args[i + 1] == "true" || args[i + 1] == "false"))
                    {
                        v = args[++i];
                    }
                    if (v == null) return def;
                    return !(v == "false" || v == "0" || v == "no");
                };
                Func<int, int> intValue = def =>
                {
                    string v = nextValue();
                    int parsed;
                    if (v == null) return def;
                    if (!int.TryParse(v, out parsed))
                    {
                        o.Error = "--" + name + " expects a number, got \"" + v + "\"";
                        return def;
                    }
                    return parsed;
                };

                switch (name)
                {
                    case "stdio":   o.Stdio = true; break;
                    case "help":
                    case "h":
                    case "?":       o.ShowHelp = true; break;
                    case "version":
                    case "v":       o.ShowVersion = true; break;
                    case "print-config": o.PrintConfig = true; break;

                    case "host":
                    case "server":  s.Host = nextValue() ?? s.Host; break;
                    case "port":    s.Port = intValue(s.Port); break;
                    case "user":
                    case "username": s.Username = nextValue() ?? s.Username; break;
                    case "domain":  s.Domain = nextValue() ?? s.Domain; break;
                    case "password": s.Password = nextValue() ?? s.Password; break;

                    case "width":   s.Width  = intValue(s.Width); break;
                    case "height":  s.Height = intValue(s.Height); break;

                    case "smart-sizing":    s.SmartSizing = boolValue(true); break;
                    case "no-smart-sizing": s.SmartSizing = false; break;

                    case "clipboard":       s.RedirectClipboard = boolValue(true); break;
                    case "no-clipboard":    s.RedirectClipboard = false; break;
                    case "drives":          s.RedirectDrives = boolValue(true); break;
                    case "no-drives":       s.RedirectDrives = false; break;
                    case "printers":        s.RedirectPrinters = boolValue(true); break;
                    case "no-printers":     s.RedirectPrinters = false; break;
                    case "smart-cards":     s.RedirectSmartCards = boolValue(true); break;
                    case "ports":           s.RedirectPorts = boolValue(true); break;
                    case "webauthn":        s.RedirectWebauthn = boolValue(true); break;

                    case "audio-mode":      s.AudioMode = intValue(s.AudioMode); break;
                    case "audio-capture":   s.AudioCapture = boolValue(true); break;

                    case "nla":             s.EnableNla = boolValue(true); break;
                    case "no-nla":          s.EnableNla = false; break;
                    case "ignore-cert-errors": s.IgnoreCertErrors = boolValue(true); break;

                    default:
                        o.Error = "unknown argument \"" + arg + "\"";
                        break;
                }

                if (o.Error != null) break;
            }

            if (o.Settings.AudioMode < 0 || o.Settings.AudioMode > 2)
            {
                o.Error = "--audio-mode must be 0 (local), 1 (remote) or 2 (disabled)";
            }
            return o;
        }

        // ASCII only: this goes to a console whose codepage mangles non-ASCII
        // (an em-dash renders as "-" at best, mojibake at worst).
        public const string HelpText = @"jrdp - embeddable Remote Desktop client for Windows

USAGE
  jrdp                          Open the connect dialog
  jrdp --host <name> [options]  Connect immediately
  jrdp <file.rdp>               Open a Microsoft .rdp connection file
  jrdp <rdp://host[:port]>      Open an rdp:// URI
  jrdp --stdio                  Helper mode: JSON protocol on stdin/stdout

A .rdp file or rdp:// URI may be combined with options; the options win:
  jrdp srv01.rdp --port 3390 --no-drives

Register jrdp as the handler for .rdp files and rdp:// links:
  see docs/windows-integration.md

CONNECTION
  --host, --server <name>   Computer name or IP address
  --port <n>                TCP port (default 3389)
  --user, --username <u>    User name
  --domain <d>              Domain
  --password <p>            Password (visible in the process list - prefer the dialog)
  --width <n>               Requested desktop width (default 1280)
  --height <n>              Requested desktop height (default 720)

REDIRECTION            (prefix with no- to disable, e.g. --no-clipboard)
  --clipboard               Clipboard        (default on)
  --drives                  Local drives     (default on)
  --printers                Printers         (default on)
  --smart-cards             Smart cards      (default off)
  --ports                   COM/LPT ports    (default off)
  --webauthn                WebAuthn/FIDO    (default off)
  --audio-mode <0|1|2>      0 play locally, 1 play remotely, 2 disabled (default 0)
  --audio-capture           Redirect the microphone (default off)

DISPLAY & SECURITY
  --smart-sizing            Scale the session to the window (default on)
  --nla                     Network Level Authentication (default on)
  --ignore-cert-errors      Accept any server certificate (INSECURE)

OTHER
  --stdio                   Helper mode for embedding hosts
  --print-config            Print the resolved settings and exit (no connect)
  --version                 Print the version and exit
  --help                    Print this help and exit
";
    }
}

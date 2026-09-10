// jrdp — StdioMode.cs
//
// Helper mode (`jrdp --stdio`). One JSON object per line in each direction.
// This is the ORIGINAL yterminal RdpActivexProxyClient contract, preserved
// verbatim — command names, event names and field names all unchanged — so an
// existing host can swap in jrdp.exe without touching its own code.
//
// Commands (stdin):
//   {"type":"connect","host":"h","port":3389,"username":"u","domain":"d",
//    "password":"p","width":1280,"height":720,"smartSizing":true,
//    "redirectDrives":true,"redirectPrinters":true,"redirectClipboard":true,
//    "redirectSmartCards":false,"redirectPorts":false,"redirectWebauthn":false,
//    "audioMode":0,"audioCapture":false,"enableNla":true,
//    "ignoreCertErrors":false}
//   {"type":"disconnect"}
//   {"type":"setBounds","x":0,"y":0,"width":800,"height":600}
//   {"type":"setVisible","visible":true}
//   {"type":"setOwner","ownerHwnd":"123456"}     // decimal int64 STRING
//   {"type":"screenshot","format":"jpeg","quality":75}
//   {"type":"quit"}
//
// Events (stdout):
//   {"type":"hwnd","hwnd":123456}
//   {"type":"ready","version":"1.0.0"}
//   {"type":"connecting"} {"type":"connected"} {"type":"disconnected"}
//   {"type":"key-esc"}
//   {"type":"screenshot","format":"jpeg","data":"<base64>"}  (or "error")
//   {"type":"warning","message":"..."}
//   {"type":"error","message":"...","stack":"..."}
//   {"type":"exiting"}

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Windows.Forms;

namespace Jrdp
{
    internal static class StdioMode
    {
        private static RdpSessionHost form;

        public static int Run()
        {
            // stdout: auto-flushing line mode so events ship immediately, and
            // "\n" (not "\r\n") because hosts split on newline.
            var sw = new StreamWriter(Console.OpenStandardOutput())
            {
                AutoFlush = true,
                NewLine = "\n",
            };
            Console.SetOut(sw);

            // Catch-all exception loggers — anything unhandled is emitted as a
            // JSON `error` event so the host can see why we died.
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try
                {
                    var ex = e.ExceptionObject as Exception;
                    EmitEvent("error", new JsonObject
                    {
                        ["message"] = "unhandled: " + (ex?.GetType().FullName ?? "?")
                                       + ": " + (ex?.Message ?? "?"),
                        ["stack"] = ex?.StackTrace ?? "",
                    });
                }
                catch { }
            };
            Application.ThreadException += (s, e) =>
            {
                try
                {
                    EmitEvent("error", new JsonObject
                    {
                        ["message"] = "thread: " + e.Exception.GetType().FullName
                                       + ": " + e.Exception.Message,
                        ["stack"] = e.Exception.StackTrace ?? "",
                    });
                }
                catch { }
            };

            try
            {
                form = new RdpSessionHost(embedded: true);
            }
            catch (Exception ex)
            {
                EmitEvent("error", new JsonObject
                {
                    ["message"] = "RdpSessionHost ctor: " + ex.GetType().FullName + ": " + ex.Message,
                    ["stack"] = ex.StackTrace ?? "",
                });
                return 1;
            }

            form.Connecting   += () => EmitEvent("connecting", null);
            form.Connected    += () => EmitEvent("connected", null);
            form.Disconnected += () => EmitEvent("disconnected", null);
            form.Warning      += m => EmitEvent("warning", new JsonObject { ["message"] = m });
            form.SessionError += m => EmitEvent("error",   new JsonObject { ["message"] = m });

            // If the constructor already forced handle creation, emit the HWND
            // immediately; otherwise wait for HandleCreated.
            if (form.IsHandleCreated)
            {
                EmitEvent("hwnd", new JsonObject { ["hwnd"] = form.Handle.ToInt64() });
            }
            else
            {
                form.HandleCreated += (s, e) =>
                    EmitEvent("hwnd", new JsonObject { ["hwnd"] = form.Handle.ToInt64() });
            }
            form.FormClosed += (s, e) => Application.Exit();

            var stdinThread = new Thread(StdinLoop) { IsBackground = true };
            stdinThread.Start();

            Action onEsc = () => EmitEvent("key-esc", null);
            Application.AddMessageFilter(new EscKeyFilter(onEsc));                       // belt
            LowLevelKeyboardHook.Install(onEsc,                                          // and braces
                m => EmitEvent("warning", new JsonObject { ["message"] = m }));

            EmitEvent("ready", new JsonObject { ["version"] = Program.VERSION });
            Application.Run(form);
            LowLevelKeyboardHook.Uninstall();
            EmitEvent("exiting", null);
            return 0;
        }

        private static void StdinLoop()
        {
            try
            {
                string line;
                while ((line = Console.In.ReadLine()) != null)
                {
                    JsonObject cmd;
                    try { cmd = JsonNode.Parse(line) as JsonObject; }
                    catch (Exception ex)
                    {
                        EmitEvent("error", new JsonObject { ["message"] = "parse: " + ex.Message });
                        continue;
                    }
                    if (cmd == null) continue;

                    if (form.IsHandleCreated)
                    {
                        try { form.BeginInvoke((Action)(() => Dispatch(cmd))); }
                        catch (Exception ex)
                        {
                            EmitEvent("error", new JsonObject { ["message"] = "invoke: " + ex.Message });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                EmitEvent("error", new JsonObject { ["message"] = "stdin: " + ex.Message });
            }
            // stdin closed — the host is gone, so shut down.
            try { form.BeginInvoke((Action)Application.Exit); } catch { /* form gone */ }
        }

        private static void Dispatch(JsonObject cmd)
        {
            string type = cmd.GetStr("type");
            try
            {
                switch (type)
                {
                    case "connect":
                        form.ConnectRdp(RdpConnectionSettings.FromJson(cmd));
                        break;
                    case "disconnect":
                        form.DisconnectRdp();
                        break;
                    case "setBounds":
                        form.SetSessionBounds(
                            cmd.GetInt("x", form.Location.X),
                            cmd.GetInt("y", form.Location.Y),
                            cmd.GetInt("width",  form.Size.Width),
                            cmd.GetInt("height", form.Size.Height));
                        break;
                    case "setVisible":
                        form.Visible = cmd.GetBool("visible", true);
                        break;
                    case "setOwner":
                        form.SetOwner(cmd.GetStr("ownerHwnd"));
                        break;
                    case "screenshot":
                        EmitScreenshot(cmd);
                        break;
                    case "quit":
                        Application.Exit();
                        break;
                    default:
                        EmitEvent("error", new JsonObject { ["message"] = "unknown command: " + type });
                        break;
                }
            }
            catch (Exception ex)
            {
                EmitEvent("error", new JsonObject { ["message"] = type + ": " + ex.Message });
            }
        }

        private static void EmitScreenshot(JsonObject cmd)
        {
            string format = cmd.GetStr("format", "jpeg");
            string error;
            string data = form.CaptureScreenshot(ref format, cmd.GetInt("quality", 75), out error);
            if (data == null)
            {
                EmitEvent("screenshot", new JsonObject { ["error"] = error });
                return;
            }
            EmitEvent("screenshot", new JsonObject
            {
                ["format"] = format,
                ["data"]   = data,
            });
        }

        internal static void EmitEvent(string type, JsonObject extra)
        {
            var obj = new JsonObject { ["type"] = type };
            if (extra != null)
            {
                foreach (var p in extra) obj[p.Key] = p.Value?.DeepClone();
            }
            Console.WriteLine(obj.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
        }
    }
}

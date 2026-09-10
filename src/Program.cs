// jrdp — Program.cs
//
// Entry point and mode selector:
//
//   jrdp                 -> GUI mode, connect dialog
//   jrdp --host srv01    -> GUI mode, connect immediately
//   jrdp --stdio         -> helper mode, JSON protocol on stdin/stdout
//
// The assembly is a WinExe (no console window on double-click). For --help /
// --version we attach to the parent console so text appears in the shell that
// launched us; when there is no parent console we fall back to a message box.

using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Jrdp
{
    internal static class Program
    {
        // Read from the assembly rather than hardcoded, so the release tag is
        // the single source of truth: CI stamps it with -p:Version=<tag>, and
        // a local build falls back to the csproj's <Version>. Reported in the
        // stdio `ready` event so a host can confirm which build it is talking
        // to, and by --version.
        internal static readonly string VERSION = ResolveVersion();

        private static string ResolveVersion()
        {
            // InformationalVersion carries the full string including any
            // pre-release suffix ("0.5.0", "0.0.0-dev"); AssemblyVersion would
            // flatten it to 0.5.0.0. SourceRevisionId (a "+<sha>" suffix the
            // SDK may append) is trimmed off.
            var attr = System.Reflection.Assembly.GetExecutingAssembly()
                .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>();
            string v = attr?.InformationalVersion;
            if (string.IsNullOrEmpty(v)) return "0.0.0";
            int plus = v.IndexOf('+');
            return plus > 0 ? v.Substring(0, plus) : v;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);
        private const int ATTACH_PARENT_PROCESS = -1;

        [STAThread]
        private static int Main(string[] args)
        {
            var opts = CommandLineOptions.Parse(args);

            if (opts.Error != null)
            {
                WriteConsoleOrBox("jrdp: " + opts.Error + "\n\nRun jrdp --help for usage.", true);
                return 2;
            }
            if (opts.ShowHelp)
            {
                WriteConsoleOrBox(CommandLineOptions.HelpText, false);
                return 0;
            }
            if (opts.ShowVersion)
            {
                WriteConsoleOrBox("jrdp " + VERSION, false);
                return 0;
            }
            // Resolve the command line and print what we WOULD connect with,
            // without connecting. The way to check that a .rdp file, an
            // rdp:// URI, or a registry handler passes what you expect.
            if (opts.PrintConfig)
            {
                WriteConsoleOrBox(opts.Settings.Describe(), false);
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            return opts.Stdio ? StdioMode.Run() : GuiMode.Run(opts);
        }

        // Print to the launching console when there is one (so `jrdp --help` in
        // a terminal behaves like a console app), otherwise show a dialog so a
        // double-clicked exe isn't silent.
        private static void WriteConsoleOrBox(string text, bool isError)
        {
            if (AttachConsole(ATTACH_PARENT_PROCESS))
            {
                if (isError) Console.Error.WriteLine(text);
                else Console.WriteLine(text);
                return;
            }
            MessageBox.Show(text, "jrdp", MessageBoxButtons.OK,
                isError ? MessageBoxIcon.Error : MessageBoxIcon.Information);
        }
    }
}

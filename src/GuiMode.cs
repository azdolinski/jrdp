// jrdp — GuiMode.cs
//
// Standalone-client mode (the default). Shows the connect dialog unless the
// command line already supplied a host, then opens a normal resizable window
// running the session. Disconnect closes the window; if we never connected we
// tell the user why instead of exiting silently.

using System;
using System.Windows.Forms;

namespace Jrdp
{
    internal static class GuiMode
    {
        public static int Run(CommandLineOptions opts)
        {
            var settings = opts.Settings;

            // --host given: connect straight away. Otherwise ask.
            if (string.IsNullOrEmpty(settings.Host))
            {
                using (var dialog = new ConnectDialog(settings))
                {
                    if (dialog.ShowDialog() != DialogResult.OK) return 0;
                    settings = dialog.Result;
                }
            }

            RdpSessionHost form;
            try
            {
                form = new RdpSessionHost(embedded: false);
            }
            catch (Exception ex)
            {
                // Almost always "no MsTscAx registered" — worth showing rather
                // than dying with a stack trace nobody sees.
                MessageBox.Show(
                    "Could not initialise the Remote Desktop ActiveX control.\n\n" + ex.Message,
                    "jrdp", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }

            string baseTitle = "jrdp — " + settings.Host;
            form.Text = baseTitle + " (connecting…)";

            bool everConnected = false;
            form.Connecting   += () => form.Text = baseTitle + " (connecting…)";
            form.Connected    += () => { everConnected = true; form.Text = baseTitle; };
            form.Disconnected += () =>
            {
                // Session ended (remote logoff, kick, network drop) — close the
                // window; the exit message below explains an early failure.
                form.Close();
            };
            form.SessionError += m => MessageBox.Show(form, m, "jrdp",
                MessageBoxButtons.OK, MessageBoxIcon.Error);

            // The Esc hooks exist for embedding hosts; in GUI mode the user has
            // a real title bar, so we don't install them.

            form.Shown += (s, e) => form.ConnectRdp(settings);
            Application.Run(form);

            if (!everConnected)
            {
                MessageBox.Show(
                    "Could not connect to " + settings.Host + ".\n\n" +
                    "Check the host name, port, credentials, and that Remote " +
                    "Desktop is enabled on the target machine.",
                    "jrdp", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }
            return 0;
        }
    }
}

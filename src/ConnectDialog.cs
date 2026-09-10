// jrdp — ConnectDialog.cs
//
// GUI-mode entry point: asks for a host, credentials and the redirection
// options that matter most. Laid out in code rather than with a .Designer.cs
// so the whole dialog is reviewable in one file and there's no designer state
// to keep in sync.
//
// Anything not exposed here keeps its RdpConnectionSettings default; the
// command line covers the rest for scripted use.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace Jrdp
{
    public class ConnectDialog : Form
    {
        private TextBox hostBox;
        private TextBox portBox;
        private TextBox userBox;
        private TextBox domainBox;
        private TextBox passBox;
        private CheckBox clipboardBox;
        private CheckBox drivesBox;
        private CheckBox printersBox;
        private CheckBox smartSizingBox;
        private CheckBox nlaBox;
        private CheckBox ignoreCertBox;

        // Populated when the user accepts the dialog.
        public RdpConnectionSettings Result { get; private set; }

        public ConnectDialog(RdpConnectionSettings initial)
        {
            initial = initial ?? new RdpConnectionSettings();

            Text = "jrdp — Connect to Remote Desktop";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(430, 400);
            Font = SystemFonts.MessageBoxFont;

            int labelX = 16, fieldX = 130, y = 18;
            const int rowH = 30, fieldW = 280;

            Func<string, string, TextBox> addRow = (labelText, value) =>
            {
                var label = new Label
                {
                    Text = labelText,
                    Location = new Point(labelX, y + 3),
                    Size = new Size(fieldX - labelX - 8, 20),
                    TextAlign = ContentAlignment.MiddleLeft,
                };
                var box = new TextBox
                {
                    Text = value,
                    Location = new Point(fieldX, y),
                    Size = new Size(fieldW, 24),
                };
                Controls.Add(label);
                Controls.Add(box);
                y += rowH;
                return box;
            };

            hostBox   = addRow("Computer:", initial.Host);
            portBox   = addRow("Port:",     initial.Port.ToString());
            userBox   = addRow("User name:", initial.Username);
            domainBox = addRow("Domain:",   initial.Domain);
            passBox   = addRow("Password:", initial.Password);
            passBox.UseSystemPasswordChar = true;

            y += 6;
            Func<string, bool, CheckBox> addCheck = (labelText, value) =>
            {
                var cb = new CheckBox
                {
                    Text = labelText,
                    Checked = value,
                    Location = new Point(fieldX, y),
                    Size = new Size(fieldW, 22),
                };
                Controls.Add(cb);
                y += 24;
                return cb;
            };

            var optionsLabel = new Label
            {
                Text = "Options:",
                Location = new Point(labelX, y + 3),
                Size = new Size(fieldX - labelX - 8, 20),
            };
            Controls.Add(optionsLabel);

            clipboardBox   = addCheck("Redirect clipboard",       initial.RedirectClipboard);
            drivesBox      = addCheck("Redirect drives",          initial.RedirectDrives);
            printersBox    = addCheck("Redirect printers",        initial.RedirectPrinters);
            smartSizingBox = addCheck("Scale session to window",  initial.SmartSizing);
            nlaBox         = addCheck("Network Level Authentication (NLA)", initial.EnableNla);
            ignoreCertBox  = addCheck("Ignore certificate errors", initial.IgnoreCertErrors);

            var connectBtn = new Button
            {
                Text = "Connect",
                DialogResult = DialogResult.OK,
                Location = new Point(ClientSize.Width - 210, ClientSize.Height - 40),
                Size = new Size(90, 28),
            };
            var cancelBtn = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(ClientSize.Width - 110, ClientSize.Height - 40),
                Size = new Size(90, 28),
            };
            Controls.Add(connectBtn);
            Controls.Add(cancelBtn);
            AcceptButton = connectBtn;
            CancelButton = cancelBtn;

            // Validate on OK; keep the dialog open if the host is empty or the
            // port isn't a usable TCP port.
            connectBtn.Click += (s, e) =>
            {
                string host = hostBox.Text.Trim();
                if (host.Length == 0)
                {
                    MessageBox.Show(this, "Enter a computer name or IP address.",
                        "jrdp", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    DialogResult = DialogResult.None;
                    hostBox.Focus();
                    return;
                }
                int port;
                if (!int.TryParse(portBox.Text.Trim(), out port) || port < 1 || port > 65535)
                {
                    MessageBox.Show(this, "Port must be a number between 1 and 65535.",
                        "jrdp", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    DialogResult = DialogResult.None;
                    portBox.Focus();
                    return;
                }

                // Carry over fields the dialog doesn't expose (audio, ports,
                // smart cards, webauthn, desktop size) from what we were given.
                var s2 = initial;
                s2.Host     = host;
                s2.Port     = port;
                s2.Username = userBox.Text.Trim();
                s2.Domain   = domainBox.Text.Trim();
                s2.Password = passBox.Text;

                s2.RedirectClipboard = clipboardBox.Checked;
                s2.RedirectDrives    = drivesBox.Checked;
                s2.RedirectPrinters  = printersBox.Checked;
                s2.SmartSizing       = smartSizingBox.Checked;
                s2.EnableNla         = nlaBox.Checked;
                s2.IgnoreCertErrors  = ignoreCertBox.Checked;

                Result = s2;
            };

            hostBox.Select();
        }
    }
}

// jrdp — RdpSessionHost.cs
//
// The Form that owns the RDP OCX. Shared by both modes; the `embedded` flag
// decides window presentation only:
//
//   embedded = true  (stdio mode) — borderless, WS_EX_TOOLWINDOW |
//     WS_EX_NOACTIVATE, no taskbar entry, parked off-screen until the host
//     process reparents us via `setOwner` and positions us via `setBounds`.
//   embedded = false (GUI mode) — an ordinary sizable window with a title bar
//     and taskbar entry that the user resizes directly.
//
// Connection state is surfaced by polling the OCX's `Connected` property at
// 200 ms intervals; we raise connected / disconnected / connecting on
// transitions. That's enough to drive a UI; precise disconnect reason codes
// aren't modelled.

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Jrdp
{
    public class RdpSessionHost : Form
    {
        private readonly bool embedded;
        private RdpAxControl axHost;
        private System.Windows.Forms.Timer stateTimer;
        private int lastConnectedState = -1;

        // Debounce timer for expensive resize follow-ups (SmartSizing toggle
        // and UpdateSessionDisplaySettings — the latter is a NETWORK call to
        // the RDP server). Without this, a fullscreen toggle or a window drag
        // floods the server with hundreds of session-resize requests and the
        // remote desktop stutters.
        private System.Windows.Forms.Timer resizeSettleTimer;
        private int pendingResizeW;
        private int pendingResizeH;

        // Raised on OCX connection-state transitions. Program wires these to
        // stdout JSON events (stdio mode) or to window chrome (GUI mode).
        public event Action Connecting;
        public event Action Connected;
        public event Action Disconnected;

        // Non-fatal diagnostics (a property missing on this OCX version, a
        // failed Win32 call). Fatal problems throw or raise SessionError.
        public event Action<string> Warning;
        public event Action<string> SessionError;

        public RdpSessionHost(bool embedded)
        {
            this.embedded = embedded;

            if (embedded)
            {
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                // Off-screen but NORMAL-sized — some AxHost EndInit paths
                // refuse to instantiate the OCX into a 1x1 client area.
                Location = new Point(-32000, -32000);
                Size = new Size(800, 600);
                MinimumSize = new Size(1, 1);
                ShowInTaskbar = false;
                Text = "jrdp";
            }
            else
            {
                FormBorderStyle = FormBorderStyle.Sizable;
                StartPosition = FormStartPosition.CenterScreen;
                Size = new Size(1280, 720);
                MinimumSize = new Size(320, 240);
                ShowInTaskbar = true;
                Text = "jrdp";
            }

            BackColor = Color.Black;

            axHost = new RdpAxControl
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black,   // hide the white default during resize
            };
            ((System.ComponentModel.ISupportInitialize)axHost).BeginInit();
            Controls.Add(axHost);
            ((System.ComponentModel.ISupportInitialize)axHost).EndInit();

            this.KeyPreview = true;

            // Reduce white-flash during resize: tell WinForms to paint the
            // background in WM_PAINT (not WM_ERASEBKGND) and double-buffer the
            // result. Combined with BackColor=Black, the area outside the OCX
            // is filled black instead of the default white system brush during
            // the gap between SetWindowPos and the OCX repaint.
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            UpdateStyles();

            // Force handle creation NOW so stdio mode can immediately read and
            // emit the HWND (otherwise HandleCreated fires inside this
            // constructor and the caller's listener attaches too late).
            var _ = this.Handle;

            // Connection-state poller. Cheap — property read via IDispatch.
            stateTimer = new System.Windows.Forms.Timer { Interval = 200 };
            stateTimer.Tick += (s, e) => PollConnectionState();

            resizeSettleTimer = new System.Windows.Forms.Timer { Interval = 200 };
            resizeSettleTimer.Tick += (s, e) =>
            {
                resizeSettleTimer.Stop();
                ApplyResizeNegotiation(pendingResizeW, pendingResizeH);
            };
        }

        public string ResolvedProgId => RdpAxControl.ResolvedProgId;

        private void Warn(string message)
        {
            var h = Warning;
            if (h != null) h(message);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                if (embedded)
                {
                    cp.ExStyle |= 0x80;        // WS_EX_TOOLWINDOW
                    cp.ExStyle |= 0x08000000;  // WS_EX_NOACTIVATE
                }
                cp.ClassStyle |= 0x0008;       // CS_DBLCLKS — harmless
                return cp;
            }
        }

        // Win32 fills the background between SetWindowPos and the first
        // WM_PAINT with the window class's background brush. WinForms uses a
        // brush derived from BackColor; on some setups Windows still resorts to
        // COLOR_WINDOW (white) for the newly-exposed area when a child control
        // is being resized. Paint it ourselves with a black brush instead.
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (var brush = new SolidBrush(this.BackColor))
            {
                e.Graphics.FillRectangle(brush, e.ClipRectangle);
            }
        }

        // GUI mode resizes by dragging the window frame rather than by
        // `setBounds` commands, so hook OnResize into the same debounced
        // renegotiation path the stdio side uses.
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (embedded) return;
            if (ClientSize.Width < MIN_REAL_DIM || ClientSize.Height < MIN_REAL_DIM) return;
            pendingResizeW = ClientSize.Width;
            pendingResizeH = ClientSize.Height;
            resizeSettleTimer.Stop();
            resizeSettleTimer.Start();
        }

        private void ApplyResizeNegotiation(int w, int h)
        {
            // SmartSizing toggle — the OCX re-evaluates its scale factor on the
            // rising edge of the property change. Cheap, no network involved.
            try
            {
                dynamic adv = axHost.Rdp.AdvancedSettings9;
                adv.SmartSizing = false;
                adv.SmartSizing = true;
            }
            catch { /* not connected yet */ }
            // UpdateSessionDisplaySettings sends a Display Configuration PDU to
            // the server. Expensive — only fire AFTER the user stopped resizing
            // for `resizeSettleTimer.Interval` ms.
            try
            {
                axHost.Rdp.UpdateSessionDisplaySettings(
                    (uint)w, (uint)h, (uint)w, (uint)h, 0u, 100u, 100u);
            }
            catch { /* old server or before connect */ }
        }

        private void PollConnectionState()
        {
            int state;
            try { state = (int)axHost.Rdp.Connected; }
            catch (Exception ex)
            {
                Warn("Connected getter: " + ex.Message);
                return;
            }
            if (state == lastConnectedState) return;
            int prev = lastConnectedState;
            lastConnectedState = state;
            // Raise `disconnected` ONLY when we transition from an active state
            // (connecting / connected) back to 0. Initial 0 readings before
            // Connect() actually negotiates aren't disconnects.
            switch (state)
            {
                case 0:
                    if (prev == 1 || prev == 2)
                    {
                        var d = Disconnected;
                        if (d != null) d();
                    }
                    break;
                case 1:
                    var c = Connected;
                    if (c != null) c();
                    break;
                case 2:
                    var g = Connecting;
                    if (g != null) g();
                    break;
            }
        }

        // Set a COM property via late-bound dynamic dispatch. COM objects don't
        // expose properties through .NET reflection (GetType().GetProperty
        // returns null on __ComObject), so we use dynamic, which routes through
        // IDispatch::Invoke. Failures (property missing on this OCX version) are
        // caught and reported as warnings.
        private void Set(string name, Action setter)
        {
            try { setter(); }
            catch (Exception ex)
            {
                Warn("set " + name + ": " + ex.Message);
            }
        }

        public void ConnectRdp(RdpConnectionSettings s)
        {
            dynamic rdp = axHost.Rdp;
            dynamic adv = rdp.AdvancedSettings9;

            rdp.Server   = s.Host;
            rdp.UserName = s.Username;
            rdp.Domain   = s.Domain;
            if (!string.IsNullOrEmpty(s.Password))
                Set("ClearTextPassword", () => adv.ClearTextPassword = s.Password);
            Set("RDPPort", () => adv.RDPPort = s.Port);

            rdp.DesktopWidth  = s.Width;
            rdp.DesktopHeight = s.Height;
            rdp.ColorDepth    = 32;

            Set("SmartSizing", () => adv.SmartSizing = s.SmartSizing);

            Set("RedirectDrives",       () => adv.RedirectDrives      = s.RedirectDrives);
            Set("RedirectPrinters",     () => adv.RedirectPrinters    = s.RedirectPrinters);
            Set("RedirectClipboard",    () => adv.RedirectClipboard   = s.RedirectClipboard);
            Set("RedirectSmartCards",   () => adv.RedirectSmartCards  = s.RedirectSmartCards);
            Set("RedirectPorts",        () => adv.RedirectPorts       = s.RedirectPorts);
            Set("RedirectWebauthn",     () => adv.RedirectWebauthn    = s.RedirectWebauthn);
            Set("AudioRedirectionMode",        () => adv.AudioRedirectionMode        = (uint)s.AudioMode);
            Set("AudioCaptureRedirectionMode", () => adv.AudioCaptureRedirectionMode = s.AudioCapture);

            Set("EnableCredSspSupport", () => adv.EnableCredSspSupport = s.EnableNla);
            uint authLevel = s.IgnoreCertErrors ? 0u : 2u;
            Set("AuthenticationLevel",  () => adv.AuthenticationLevel = authLevel);
            // PromptForCredentials lives on a different interface in some OCX
            // versions; silent fallback if it's not exposed.
            try { adv.PromptForCredentials = false; }
            catch { /* property not on this OCX version */ }

            stateTimer.Start();
            try
            {
                rdp.Connect();
            }
            catch (Exception ex)
            {
                var h = SessionError;
                if (h != null)
                    h("Connect(): " + ex.GetType().FullName + ": " + ex.Message);
            }
        }

        public void DisconnectRdp()
        {
            try { axHost.Rdp.Disconnect(); }
            catch { /* already disconnected */ }
        }

        // Win32 surface used only for runtime owner re-binding
        // (GWLP_HWNDPARENT). WS_EX_TOOLWINDOW is applied via CreateParams and
        // never changes at runtime, so we don't need GetWindowLongPtrW here.
        private static class OwnerWin32
        {
            public const int GWLP_HWNDPARENT = -8;

            // SWP_FRAMECHANGED forces the WM to re-evaluate the window's
            // non-client area after a style change. We only change the owner
            // here, but the kick is cheap insurance.
            public const uint SWP_NOSIZE       = 0x0001;
            public const uint SWP_NOMOVE       = 0x0002;
            public const uint SWP_NOZORDER     = 0x0004;
            public const uint SWP_NOACTIVATE   = 0x0010;
            public const uint SWP_FRAMECHANGED = 0x0020;

            [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            public static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

            [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetWindowPos(
                IntPtr hWnd, IntPtr hWndInsertAfter,
                int X, int Y, int cx, int cy, uint uFlags);
        }

        // ownerHwnd arrives as a decimal int64 STRING (an HWND on Win64 can
        // exceed Number.MAX_SAFE_INTEGER, so hosts never send it as a JSON
        // number). "0" / missing detaches the owner.
        public void SetOwner(string rawOwnerHwnd)
        {
            long ownerInt;
            if (string.IsNullOrEmpty(rawOwnerHwnd) || !long.TryParse(rawOwnerHwnd, out ownerInt))
            {
                ownerInt = 0;
            }
            IntPtr owner = new IntPtr(ownerInt);

            IntPtr prev = OwnerWin32.SetWindowLongPtrW(
                this.Handle, OwnerWin32.GWLP_HWNDPARENT, owner);
            int err = Marshal.GetLastWin32Error();
            // SetWindowLongPtrW returns 0 on the first set even on success;
            // only treat it as failure if LastError is non-zero.
            if (prev == IntPtr.Zero && err != 0)
            {
                Warn("SetWindowLongPtrW(GWLP_HWNDPARENT) failed (LastError=" + err + ")");
            }

            uint flags = OwnerWin32.SWP_NOSIZE | OwnerWin32.SWP_NOMOVE
                       | OwnerWin32.SWP_NOZORDER | OwnerWin32.SWP_NOACTIVATE
                       | OwnerWin32.SWP_FRAMECHANGED;
            OwnerWin32.SetWindowPos(this.Handle, IntPtr.Zero, 0, 0, 0, 0, flags);
        }

        // Smallest rect we treat as "real" — anything below indicates the host
        // hasn't laid out yet. Ignoring such calls keeps the Form at its ctor
        // size and avoids a bogus UpdateSessionDisplaySettings(1,1,...)
        // renegotiating the RDP desktop at near-zero size server-side.
        private const int MIN_REAL_DIM = 10;

        public void SetSessionBounds(int x, int y, int rawW, int rawH)
        {
            if (rawW < MIN_REAL_DIM || rawH < MIN_REAL_DIM)
            {
                Warn("setBounds ignored: dims below threshold (" + rawW + "x" + rawH + ")");
                return;
            }

            // Cheap, immediate visual: move the Form and force a repaint of the
            // newly-exposed area with our black background. No network call.
            SuspendLayout();
            SetBounds(x, y, rawW, rawH);
            axHost.Size = ClientSize;
            ResumeLayout(false);
            Invalidate();
            Update();
            axHost.Invalidate();
            axHost.Update();

            // Defer expensive session renegotiation until the host stops
            // firing setBounds (drag / animation settled).
            pendingResizeW = rawW;
            pendingResizeH = rawH;
            resizeSettleTimer.Stop();
            resizeSettleTimer.Start();
        }

        // PrintWindow captures a window's pixels into a device context.
        // PW_RENDERFULLCONTENT (0x2) is required for DWM-composited /
        // hardware-accelerated windows (Win 8.1+) like the RDP OCX, which
        // renders via DirectX. Without it, the bitmap comes out black.
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
        private const uint PW_RENDERFULLCONTENT = 0x00000002;

        // Snapshot the Form's client area (the OCX fills it via
        // DockStyle.Fill, so its rect == ours). Returns base64-encoded image
        // bytes, or null on failure with `error` describing why. `format` is
        // normalised in place to what was actually produced.
        public string CaptureScreenshot(ref string format, int quality, out string error)
        {
            error = null;
            format = (format ?? "jpeg").ToLowerInvariant();
            if (quality < 1)   quality = 1;
            if (quality > 100) quality = 100;

            var rect = ClientRectangle;
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                error = "no client area";
                return null;
            }
            try
            {
                using (var bmp = new Bitmap(rect.Width, rect.Height))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        var hdc = g.GetHdc();
                        try
                        {
                            if (!PrintWindow(Handle, hdc, PW_RENDERFULLCONTENT))
                            {
                                error = "PrintWindow failed err=" + Marshal.GetLastWin32Error();
                                return null;
                            }
                        }
                        finally { g.ReleaseHdc(hdc); }
                    }
                    using (var ms = new MemoryStream())
                    {
                        if (format == "png")
                        {
                            bmp.Save(ms, ImageFormat.Png);
                        }
                        else
                        {
                            // JPEG with explicit quality via EncoderParameters.
                            var jpgEncoder = ImageCodecInfo.GetImageEncoders()
                                .First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                            using (var ep = new EncoderParameters(1))
                            {
                                ep.Param[0] = new EncoderParameter(Encoder.Quality, (long)quality);
                                bmp.Save(ms, jpgEncoder, ep);
                            }
                            format = "jpeg"; // normalise echo
                        }
                        return Convert.ToBase64String(ms.ToArray());
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return null;
            }
        }
    }
}

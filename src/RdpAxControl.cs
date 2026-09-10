// jrdp — RdpAxControl.cs
//
// Hosts Microsoft's RDP ActiveX control by deriving from AxHost with one of
// the known MsTscAx CLSIDs. We pick the newest version registered on the
// machine at construction time. Methods and properties on the OCX are invoked
// via `dynamic` (IDispatch), which lets us skip aximp / typed interop
// generation entirely — no Windows SDK, no VS Build Tools.

using System;
using System.Windows.Forms;

namespace Jrdp
{
    [System.ComponentModel.DesignerCategory("")]
    public class RdpAxControl : AxHost
    {
        // `MsTscAx.MsTscAx.NN` = "Microsoft RDP Client Control" — the *true*
        // ActiveX control hostable by AxHost. (The sibling `MsRDP.MsRDP.NN`
        // keys are redistributable proxy classes and don't register
        // CATID_Control, so AxHost.CreateInstance throws
        // CLASS_E_CLASSNOTAVAILABLE.) Newest version first; the static
        // initializer picks the first one that's registered.
        private static readonly (string ProgId, string Clsid)[] Versions = new[]
        {
            ("MsTscAx.MsTscAx.11", "A0C63C30-F08D-4AB4-907C-34905D770C7D"),
            ("MsTscAx.MsTscAx.10", "8B918B82-7985-4C24-89DF-C33AD2BBFBCD"),
            ("MsTscAx.MsTscAx.9",  "A3BC03A0-041D-42E3-AD22-882B7865C9C5"),
        };

        private static string SelectedClsid;
        public static string ResolvedProgId { get; private set; }

        static RdpAxControl()
        {
            foreach (var v in Versions)
            {
                try
                {
                    var t = Type.GetTypeFromProgID(v.ProgId);
                    if (t != null)
                    {
                        SelectedClsid = v.Clsid;
                        ResolvedProgId = v.ProgId;
                        return;
                    }
                }
                catch { /* try next */ }
            }
            throw new InvalidOperationException(
                "No MsTscAx ActiveX control found in registry. Expected " +
                "MsTscAx.MsTscAx.9/10/11 under HKCR. Try re-registering the " +
                "control: regsvr32 C:\\Windows\\System32\\mstscax.dll");
        }

        public RdpAxControl() : base(SelectedClsid) { }

        // Public IDispatch surface — call any method/property on the OCX.
        public dynamic Rdp => GetOcx();
    }
}

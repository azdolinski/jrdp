// jrdp — JsonNodeExt.cs
//
// Defensive accessors for the stdin command JSON. Every getter falls back to a
// default rather than throwing: a host sending a string where we want an int
// should degrade to the default, not kill the session.

using System.Text.Json.Nodes;

namespace Jrdp
{
    internal static class JsonNodeExt
    {
        public static string GetStr(this JsonObject o, string k, string def = "")
        {
            if (o == null || !o.ContainsKey(k) || o[k] == null) return def;
            try { return o[k].GetValue<string>(); } catch { return def; }
        }

        public static int GetInt(this JsonObject o, string k, int def = 0)
        {
            if (o == null || !o.ContainsKey(k) || o[k] == null) return def;
            try { return o[k].GetValue<int>(); }
            catch
            {
                try { return (int)o[k].GetValue<long>(); } catch { return def; }
            }
        }

        public static bool GetBool(this JsonObject o, string k, bool def = false)
        {
            if (o == null || !o.ContainsKey(k) || o[k] == null) return def;
            try { return o[k].GetValue<bool>(); } catch { return def; }
        }
    }
}

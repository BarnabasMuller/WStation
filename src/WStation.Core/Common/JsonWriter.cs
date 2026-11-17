using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Idojaras
{
    // Egyszerű, gyors JSON-író (a nagy számtömbökhöz a JavaScriptSerializer lassú lenne)
    class JW
    {
        readonly StringBuilder sb = new StringBuilder(1 << 16);
        readonly Stack<bool> first = new Stack<bool>();
        bool afterKey;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        void Sep()
        {
            if (afterKey) { afterKey = false; return; }
            if (first.Count == 0) return;
            if (first.Peek()) { first.Pop(); first.Push(false); }
            else sb.Append(',');
        }

        public JW Obj() { Sep(); sb.Append('{'); first.Push(true); return this; }
        public JW End() { sb.Append('}'); first.Pop(); return this; }
        public JW Arr() { Sep(); sb.Append('['); first.Push(true); return this; }
        public JW EndArr() { sb.Append(']'); first.Pop(); return this; }

        public JW Key(string k) { Sep(); Esc(k); sb.Append(':'); afterKey = true; return this; }

        public JW Str(string s) { Sep(); if (s == null) sb.Append("null"); else Esc(s); return this; }
        public JW Num(double v, int dec = 2)
        {
            Sep();
            if (double.IsNaN(v) || double.IsInfinity(v)) sb.Append("null");
            else sb.Append(Math.Round(v, dec).ToString("R", Inv));
            return this;
        }
        public JW Long(long v) { Sep(); sb.Append(v.ToString(Inv)); return this; }
        public JW Bool(bool v) { Sep(); sb.Append(v ? "true" : "false"); return this; }
        public JW Null() { Sep(); sb.Append("null"); return this; }
        public JW Raw(string json) { Sep(); sb.Append(string.IsNullOrEmpty(json) ? "null" : json); return this; }

        public JW P(string k, string v) { return Key(k).Str(v); }
        public JW P(string k, double v, int dec = 2) { return Key(k).Num(v, dec); }
        public JW P(string k, long v) { return Key(k).Long(v); }
        public JW P(string k, int v) { return Key(k).Long(v); }
        public JW P(string k, bool v) { return Key(k).Bool(v); }
        // epoch másodperc -> JS ezredmásodperc (0 = null)
        public JW Ms(string k, long epochSec) { Key(k); if (epochSec == 0) return Null(); return Long(epochSec * 1000); }

        void Esc(string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        public override string ToString() { return sb.ToString(); }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace WStation.Core.Common
{
    /// <summary>
    /// A 2.x által használt <c>System.Web.Script.Serialization.JavaScriptSerializer</c> pótlása (PLAN-3.0 7.2).
    /// A beállítás- és állapotfájlokat (szarazsag.json, felulet.json, kalibracio.json, naplo.json, lekerdezes_allapot.json …)
    /// a régi írta, és a kód a régi alakokra épít, ezért ez a réteg ugyanazt adja: ugyanazokat a típusokat olvasáskor,
    /// és – ahol lehet – bájtra ugyanazt a szöveget íráskor.
    /// Az olvasás a <c>System.Text.Json</c> olvasójára épül (szigorú JSON); az írás saját, mert a régi escape-szabályai
    /// és számalakjai a <c>System.Text.Json</c> írójával nem állíthatók elő. A régi viselkedés a valódi
    /// <c>JavaScriptSerializer</c>-rel (.NET Framework 4.8) lett kimérve; a mért esetek a tesztekben vannak.
    /// </summary>
    public static class Json
    {
        /// <summary>A szöveg legnagyobb hossza karakterben (64 MB) – a régi <c>MaxJsonLength</c> megfelelője.</summary>
        public const int DefaultMaxLength = 64 * 1024 * 1024;

        /// <summary>A legnagyobb egymásba ágyazási mélység – a régi <c>RecursionLimit</c> megfelelője (ott 100 volt az alap).</summary>
        public const int DefaultMaxDepth = 200;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ===================================================================== írás

        /// <summary>
        /// Objektum → JSON. Osztálynál a nyilvános mezők, utánuk a nyilvános tulajdonságok, a deklaráció sorrendjében;
        /// a <c>null</c> érték is kiíródik. Szótárnál a kulcsok a bejárás sorrendjében.
        /// </summary>
        public static string Serialize(object? o)
        {
            return Serialize(o, DefaultMaxLength, DefaultMaxDepth);
        }

        /// <summary>Mint a <see cref="Serialize(object)"/>, megadott korlátokkal.</summary>
        /// <exception cref="InvalidOperationException">A kimenet hosszabb a korlátnál; körkörös hivatkozás; dátum típus.</exception>
        /// <exception cref="ArgumentException">A mélység túllépte a korlátot; a szótár kulcsa nem szöveg.</exception>
        public static string Serialize(object? o, int maxLength, int maxDepth)
        {
            CheckLimits(maxLength, maxDepth);
            var w = new Writer(maxLength, maxDepth);
            w.Value(o, 0);
            return w.ToString();
        }

        sealed class Writer
        {
            readonly StringBuilder sb = new StringBuilder();
            readonly int maxLength, maxDepth;
            // az éppen kiírás alatt álló objektumok (körkörös hivatkozás észleléséhez)
            readonly HashSet<object> inUse = new HashSet<object>(ReferenceEqualityComparer.Instance);

            public Writer(int maxLength, int maxDepth) { this.maxLength = maxLength; this.maxDepth = maxDepth; }

            public override string ToString() { CheckLength(); return sb.ToString(); }

            void CheckLength()
            {
                if (sb.Length > maxLength)
                    throw new InvalidOperationException("A JSON szöveg hosszabb a megengedettnél (" + maxLength.ToString(Inv) + " karakter).");
            }

            public void Value(object? o, int depth)
            {
                // a régi minden értéknél (az egyszerűeknél is) számolta a mélységet
                if (++depth > maxDepth)
                    throw new ArgumentException("A JSON túl mélyen egymásba ágyazott (legfeljebb " + maxDepth.ToString(Inv) + " szint).");
                if (o == null || o is DBNull) { sb.Append("null"); return; }

                if (o is string s) { Str(s); return; }
                if (o is bool b) { sb.Append(b ? "true" : "false"); return; }
                if (o is char c) { if (c == '\0') sb.Append("null"); else Str(c.ToString()); return; }
                if (o is double d) { sb.Append(FormatDouble(d) ?? "null"); return; }
                if (o is float f) { sb.Append(FormatSingle(f) ?? "null"); return; }
                if (o is decimal m) { sb.Append(FormatDecimal(m)); return; }
                // dátum nincs használatban: ne keletkezzen "\/Date(...)\/" alak (7.2)
                if (o is DateTime || o is DateTimeOffset)
                    throw new InvalidOperationException("Dátum típus (" + o.GetType().Name + ") nem írható JSON-ba – szövegként kell átadni.");
                if (o is Guid g) { sb.Append('"').Append(g.ToString("D")).Append('"'); return; }
                if (o is Uri u) { Str(u.IsAbsoluteUri ? u.GetComponents(UriComponents.SerializationInfoString, UriFormat.UriEscaped) : u.OriginalString); return; }
                if (o is Enum e) { sb.Append(e.ToString("D")); return; }
                if (o.GetType().IsPrimitive && o is IFormattable n) { sb.Append(n.ToString(null, Inv)); return; }

                if (o is IDictionary dict) Dict(dict, depth);
                else if (o is IEnumerable en) Arr(en, depth);
                else Obj(o, depth);
                CheckLength();
            }

            void Dict(IDictionary dict, int depth)
            {
                // a régi csak szöveg kulcsot fogadott el (mérve; az üres szótár bármilyen kulcstípussal "{}")
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry kv in dict)
                {
                    if (!(kv.Key is string key)) throw BadKey(dict);
                    if (!first) sb.Append(',');
                    first = false;
                    Str(key);
                    sb.Append(':');
                    Value(kv.Value, depth);
                }
                sb.Append('}');
            }

            static ArgumentException BadKey(IDictionary dict)
            {
                return new ArgumentException("A(z) " + dict.GetType() + " szótár nem írható JSON-ba: a kulcsnak szövegnek kell lennie.");
            }

            void Arr(IEnumerable en, int depth)
            {
                sb.Append('[');
                bool first = true;
                foreach (object? item in en)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Value(item, depth);
                }
                sb.Append(']');
            }

            void Obj(object o, int depth)
            {
                Type t = o.GetType();
                bool tracked = !t.IsValueType;
                if (tracked && !inUse.Add(o))
                    throw new InvalidOperationException("Körkörös hivatkozás a(z) " + t + " típusú objektum kiírása közben.");
                try
                {
                    sb.Append('{');
                    bool first = true;
                    // előbb a mezők, utánuk a tulajdonságok – ahogy a régi (mérve)
                    FieldInfo[] fields = t.GetFields(BindingFlags.Public | BindingFlags.Instance);
                    PropertyInfo[] props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                    SortByDeclaration(fields, t);
                    SortByDeclaration(props, t);
                    foreach (FieldInfo fi in fields)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        Str(fi.Name);
                        sb.Append(':');
                        Value(fi.GetValue(o), depth);
                    }
                    foreach (PropertyInfo pi in props)
                    {
                        MethodInfo? get = pi.GetGetMethod();
                        if (get == null || get.GetParameters().Length > 0) continue;
                        if (!first) sb.Append(',');
                        first = false;
                        Str(pi.Name);
                        sb.Append(':');
                        Value(get.Invoke(o, null), depth);
                    }
                    sb.Append('}');
                }
                finally
                {
                    if (tracked) inUse.Remove(o);
                }
            }

            // A deklaráció sorrendje: előbb a saját osztály tagjai, aztán az ősökéi (mérve), azon belül a forrás sorrendje.
            // A futtatókörnyezet általában eleve így adja vissza a tagokat, de ezt nem ígéri – a fájl tartalma ne múljon rajta.
            static void SortByDeclaration(MemberInfo[] members, Type t)
            {
                Array.Sort(members, (a, b) =>
                {
                    int c = Distance(t, a.DeclaringType).CompareTo(Distance(t, b.DeclaringType));
                    return c != 0 ? c : a.MetadataToken.CompareTo(b.MetadataToken);
                });
            }

            static int Distance(Type t, Type? declaring)
            {
                int n = 0;
                for (Type? x = t; x != null && x != declaring; x = x.BaseType) n++;
                return n;
            }

            // A régi escape-szabályai (mérve): a vezérlőkarakterek, a " és a \ mellett a < > & ' is \u00XX alakban (kisbetűs
            // hexa), továbbá a \u0085 (NEL), valamint az U+2028 és U+2029 (sor- és bekezdéselválasztó); minden más – az ékezetes betűk is – változatlanul. A / nincs escape-elve.
            void Str(string s)
            {
                sb.Append('"');
                foreach (char c in s)
                {
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\b': sb.Append("\\b"); break;
                        case '\t': sb.Append("\\t"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\f': sb.Append("\\f"); break;
                        case '\r': sb.Append("\\r"); break;
                        default:
                            if (c < 0x20 || c == '<' || c == '>' || c == '&' || c == '\'' || c == '\u0085' || c == 0x2028 || c == 0x2029)
                                sb.Append("\\u").Append(((int)c).ToString("x4", Inv));
                            else sb.Append(c);
                            break;
                    }
                }
                sb.Append('"');
            }
        }

        // A .NET Framework "R" alakja: 15 értékes jegy, ha abból a szám pontosan visszaolvasható, különben 17
        // (pl. 0.1 → "0.1", 1/3 → "0.33333333333333331", 1e-5 → "1E-05"). A modern .NET "R" alakja a legrövidebb
        // visszaolvasható szöveg ("0.3333333333333333"), ezért itt a régi szabály szerepel – így a fájlok bájtra egyeznek.
        // NaN és végtelen: null (a hívó "null"-t ír – 7.2).
        static string? FormatDouble(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return null;
            if (d == 0) return "0";   // a régi a negatív nullát is így írta
            string s = d.ToString("G15", Inv);
            if (double.Parse(s, NumberStyles.Float, Inv) != d) s = d.ToString("G17", Inv);
            return s;
        }

        static string? FormatSingle(float f)
        {
            if (float.IsNaN(f) || float.IsInfinity(f)) return null;
            if (f == 0) return "0";
            string s = f.ToString("G7", Inv);
            if (float.Parse(s, NumberStyles.Float, Inv) != f) s = f.ToString("G9", Inv);
            return s;
        }

        // a tizedesjegyek száma megmarad (1.10 → "1.10"); a negatív nulla előjel nélkül, ahogy a régi írta
        static string FormatDecimal(decimal m)
        {
            string s = m.ToString(Inv);
            return m == 0 && s.Length > 0 && s[0] == '-' ? s.Substring(1) : s;
        }

        // ===================================================================== olvasás

        /// <summary>
        /// JSON → általános alak: <c>Dictionary&lt;string, object&gt;</c> (a kulcsok a szöveg sorrendjében; ismétlődő kulcsnál
        /// az utolsó érték), <c>object[]</c>, <c>string</c>, <c>bool</c>, <c>int</c> / <c>long</c> / <c>decimal</c> / <c>double</c>, <c>null</c>.
        /// Szám: egész → <c>int</c>, ha belefér, különben <c>long</c>; ami abba sem fér, és a tizedes tört → <c>decimal</c>;
        /// a kitevős alak (<c>1e5</c>) és ami a <c>decimal</c>-ba sem fér → <c>double</c>. Üres szöveg → <c>null</c>.
        /// </summary>
        public static object? DeserializeObject(string s)
        {
            return DeserializeObject(s, DefaultMaxLength, DefaultMaxDepth);
        }

        /// <summary>Mint a <see cref="DeserializeObject(string)"/>, megadott korlátokkal.</summary>
        /// <exception cref="ArgumentNullException">A bemenet <c>null</c>.</exception>
        /// <exception cref="ArgumentException">Érvénytelen JSON; a szöveg hosszabb a korlátnál; a mélység túllépte a korlátot.</exception>
        public static object? DeserializeObject(string s, int maxLength, int maxDepth)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            CheckLimits(maxLength, maxDepth);
            if (s.Length > maxLength)
                throw new ArgumentException("A JSON szöveg hosszabb a megengedettnél (" + maxLength.ToString(Inv) + " karakter).", nameof(s));
            if (string.IsNullOrWhiteSpace(s)) return null;

            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(s), new JsonReaderOptions
            {
                MaxDepth = maxDepth,
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false
            });
            try
            {
                if (!reader.Read()) throw new ArgumentException("Érvénytelen JSON: nincs benne érték.", nameof(s));
                object? v = ReadValue(ref reader, 1, maxDepth);
                if (reader.Read()) throw new ArgumentException("Érvénytelen JSON: az érték után további tartalom áll.", nameof(s));
                return v;
            }
            catch (JsonException ex)
            {
                throw new ArgumentException("Érvénytelen JSON: " + ex.Message, nameof(s), ex);
            }
            catch (InvalidOperationException ex)
            {
                // pl. párosítatlan \ud83d escape egy szövegben
                throw new ArgumentException("Érvénytelen JSON: " + ex.Message, nameof(s), ex);
            }
        }

        static object? ReadValue(ref Utf8JsonReader reader, int depth, int maxDepth)
        {
            // a régi minden értéknél (az egyszerűeknél is) számolta a mélységet
            if (depth > maxDepth)
                throw new ArgumentException("A JSON túl mélyen egymásba ágyazott (legfeljebb " + maxDepth.ToString(Inv) + " szint).");
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    var d = new Dictionary<string, object?>();
                    while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
                    {
                        string key = reader.GetString() ?? "";
                        reader.Read();
                        d[key] = ReadValue(ref reader, depth + 1, maxDepth);   // ismétlődő kulcs: az utolsó érték, az első helyén
                    }
                    return d;
                case JsonTokenType.StartArray:
                    var list = new List<object?>();
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                        list.Add(ReadValue(ref reader, depth + 1, maxDepth));
                    return list.ToArray();
                case JsonTokenType.String: return reader.GetString();
                case JsonTokenType.Number: return ReadNumber(Encoding.ASCII.GetString(reader.ValueSpan));
                case JsonTokenType.True: return true;
                case JsonTokenType.False: return false;
                case JsonTokenType.Null: return null;
                default: throw new ArgumentException("Érvénytelen JSON: váratlan elem (" + reader.TokenType + ").");
            }
        }

        // a régi sorrendje: int → long → decimal → double (mérve)
        static object ReadNumber(string t)
        {
            if (int.TryParse(t, NumberStyles.Integer, Inv, out int i)) return i;
            if (long.TryParse(t, NumberStyles.Integer, Inv, out long l)) return l;
            if (decimal.TryParse(t, NumberStyles.Number, Inv, out decimal m))
                return m == 0 && decimal.IsNegative(m) ? decimal.Negate(m) : m;   // "-0.0": a régi előjel nélkül adta tovább
            if (double.TryParse(t, NumberStyles.Float, Inv, out double d) && !double.IsInfinity(d)) return d;
            throw new ArgumentException("Érvénytelen JSON: a szám nem ábrázolható (" + t + ").");
        }

        /// <summary>
        /// JSON → megadott típus. A nyilvános mezőket és (írható) tulajdonságokat tölti ki; az ismeretlen kulcs kimarad, a
        /// kis- és nagybetű nem számít; ami a szövegben nem szerepel, az alapértéken marad. Értéktípusú mezőnél a
        /// <c>null</c> szintén az alapértéket hagyja (a NaN <c>null</c>-ként íródik ki – 7.2); hivatkozás típusnál <c>null</c> lesz.
        /// A <c>null</c> és az üres szöveg eredménye <c>null</c>.
        /// </summary>
        public static T? Deserialize<T>(string s)
        {
            return Deserialize<T>(s, DefaultMaxLength, DefaultMaxDepth);
        }

        /// <summary>Mint a <see cref="Deserialize{T}(string)"/>, megadott korlátokkal.</summary>
        /// <exception cref="ArgumentException">Érvénytelen JSON, vagy túllépett korlát.</exception>
        /// <exception cref="InvalidOperationException">Az érték alakja nem illik a típushoz (pl. tömb egy osztály helyén).</exception>
        /// <exception cref="FormatException">Egy egyszerű érték nem alakítható a mező típusára (pl. <c>1.5</c> egész mezőbe).</exception>
        public static T? Deserialize<T>(string s, int maxLength, int maxDepth)
        {
            object? v = ConvertTo(DeserializeObject(s, maxLength, maxDepth), typeof(T));
            return v == null ? default : (T)v;
        }

        static object? ConvertTo(object? v, Type target)
        {
            Type t = Nullable.GetUnderlyingType(target) ?? target;
            if (v == null)
            {
                if (t.IsValueType && t == target) throw new InvalidOperationException("A null érték nem alakítható értéktípusra (" + t.Name + ").");
                return null;
            }
            if (t == typeof(object)) return v;
            if (v is Dictionary<string, object?> d) return FromDictionary(d, t);
            if (v is object?[] a) return FromArray(a, t);
            return FromSimple(v, t);
        }

        static object? FromDictionary(Dictionary<string, object?> d, Type t)
        {
            if (t.IsInstanceOfType(d)) return d;

            if (typeof(IDictionary).IsAssignableFrom(t) || GenericDictionary(t) != null)
            {
                Type? gen = GenericDictionary(t);
                Type valueType = typeof(object);
                Type concrete = t;
                if (gen != null)
                {
                    Type[] ga = gen.GetGenericArguments();
                    if (ga[0] != typeof(string) && ga[0] != typeof(object))
                        throw new InvalidOperationException("A(z) " + t + " szótár nem olvasható be JSON-ból: a kulcsnak szövegnek kell lennie.");
                    valueType = ga[1];
                    if (t.IsInterface || t.IsAbstract) concrete = typeof(Dictionary<,>).MakeGenericType(ga);
                }
                if (!(Activator.CreateInstance(concrete) is IDictionary target))
                    throw new InvalidOperationException("A(z) " + t + " szótár nem olvasható be JSON-ból.");
                foreach (var kv in d) target[kv.Key] = ConvertTo(kv.Value, valueType);
                return target;
            }

            object? inst = Activator.CreateInstance(t);   // paraméter nélküli konstruktor kell (ahogy a réginél)
            if (inst == null) return null;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;
            foreach (var kv in d)
            {
                if (kv.Key.Length == 0) continue;
                // előbb mező, aztán tulajdonság – ahogy a régi; a kulcsok a szöveg sorrendjében (az utolsó nyer)
                FieldInfo? fi = t.GetField(kv.Key, flags);
                if (fi != null)
                {
                    if (kv.Value == null && KeepsDefaultOnNull(fi.FieldType)) continue;
                    fi.SetValue(inst, ConvertTo(kv.Value, fi.FieldType));
                    continue;
                }
                PropertyInfo? pi = t.GetProperty(kv.Key, flags);
                MethodInfo? set = pi?.GetSetMethod();
                if (pi == null || set == null || set.GetParameters().Length != 1) continue;
                if (kv.Value == null && KeepsDefaultOnNull(pi.PropertyType)) continue;
                set.Invoke(inst, new[] { ConvertTo(kv.Value, pi.PropertyType) });
            }
            return inst;
        }

        // Értéktípusú mezőbe a null nem írható: az alapérték marad (7.2). A régi itt kivételt dobott, de az új réteg a
        // NaN-t null-ként írja ki, így egy ilyen fájlnak vissza kell tudnia töltődni.
        static bool KeepsDefaultOnNull(Type t)
        {
            return t.IsValueType && Nullable.GetUnderlyingType(t) == null;
        }

        static Type? GenericDictionary(Type t)
        {
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IDictionary<,>)) return t;
            foreach (Type i in t.GetInterfaces())
                if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>)) return i;
            return null;
        }

        static object FromArray(object?[] a, Type t)
        {
            if (t.IsArray && t.GetArrayRank() == 1)
            {
                Type et = t.GetElementType()!;
                Array arr = Array.CreateInstance(et, a.Length);
                for (int i = 0; i < a.Length; i++) arr.SetValue(ConvertTo(a[i], et), i);
                return arr;
            }
            if (t.IsGenericType && t.GetGenericArguments().Length == 1)
            {
                Type et = t.GetGenericArguments()[0];
                Type listType = typeof(List<>).MakeGenericType(et);
                if (t.IsAssignableFrom(listType))
                {
                    var list = (IList)Activator.CreateInstance(listType)!;
                    foreach (object? item in a) list.Add(ConvertTo(item, et));
                    return list;
                }
            }
            if (t.IsInstanceOfType(a)) return a;
            throw new InvalidOperationException("JSON-tömb nem olvasható be ilyen típusba: " + t + ".");
        }

        // Egyszerű érték (szöveg, logikai, szám) → a mező típusa. A régi a .NET típusátalakítóit használta: az értéket
        // (kultúrafüggetlen) szöveggé alakította, és abból olvasta be a céltípust – ezért pl. 1.0 nem megy egész mezőbe,
        // "7" igen, 1 nem megy logikai mezőbe, "true" igen (mind mérve).
        static object FromSimple(object v, Type t)
        {
            if (t.IsInstanceOfType(v)) return v;
            string text = v is bool b ? (b ? "True" : "False")
                : v is double d ? (FormatDouble(d) ?? d.ToString(Inv))
                : v is IFormattable f ? f.ToString(null, Inv)
                : v.ToString() ?? "";
            if (t == typeof(string)) return text;
            try
            {
                if (t.IsEnum)
                {
                    if (v is string) return Enum.Parse(t, text, true);
                    if (v is int || v is long) return Enum.ToObject(t, v);
                    throw new FormatException();
                }
                switch (Type.GetTypeCode(t))
                {
                    case TypeCode.Boolean: return bool.Parse(text);
                    case TypeCode.Char: if (text.Length == 1) return text[0]; throw new FormatException();
                    case TypeCode.SByte: return sbyte.Parse(text, NumberStyles.Integer, Inv);
                    case TypeCode.Byte: return byte.Parse(text, NumberStyles.Integer, Inv);
                    case TypeCode.Int16: return short.Parse(text, NumberStyles.Integer, Inv);
                    case TypeCode.UInt16: return ushort.Parse(text, NumberStyles.Integer, Inv);
                    case TypeCode.Int32: return int.Parse(text, NumberStyles.Integer, Inv);
                    case TypeCode.UInt32: return uint.Parse(text, NumberStyles.Integer, Inv);
                    case TypeCode.Int64: return long.Parse(text, NumberStyles.Integer, Inv);
                    case TypeCode.UInt64: return ulong.Parse(text, NumberStyles.Integer, Inv);
                    case TypeCode.Decimal: return decimal.Parse(text, NumberStyles.Float, Inv);
                    case TypeCode.Double:
                        double dv = double.Parse(text, NumberStyles.Float, Inv);
                        // a modern .NET a túl nagy számot végtelenként adja vissza; a régi hibát jelzett
                        if (double.IsInfinity(dv) && HasDigit(text)) throw new OverflowException();
                        return dv;
                    case TypeCode.Single:
                        float fv = float.Parse(text, NumberStyles.Float, Inv);
                        if (float.IsInfinity(fv) && HasDigit(text)) throw new OverflowException();
                        return fv;
                }
                if (t == typeof(Guid)) return Guid.Parse(text);
            }
            catch (Exception ex) when (ex is FormatException || ex is OverflowException || ex is ArgumentException)
            {
                throw new FormatException("A(z) „" + text + "” érték érvénytelen ehhez: " + t.Name + ".", ex);
            }
            throw new InvalidOperationException("„" + v.GetType() + "” típusú érték nem alakítható „" + t + "” típusúra.");
        }

        static bool HasDigit(string s)
        {
            foreach (char c in s) if (c >= '0' && c <= '9') return true;
            return false;
        }

        static void CheckLimits(int maxLength, int maxDepth)
        {
            if (maxLength < 0) throw new ArgumentOutOfRangeException(nameof(maxLength));
            if (maxDepth < 1) throw new ArgumentOutOfRangeException(nameof(maxDepth));
        }
    }
}

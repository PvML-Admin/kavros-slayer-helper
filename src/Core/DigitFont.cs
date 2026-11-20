using System.Collections.Generic;

namespace KavrosSlayerHelper.Core
{
    /// <summary>Digit shapes of the RS3 slayer counter, learned from real screenshots in tests/fixtures/real.
    /// Big: the kills-left number (249, 196, 247, 173 and the minimised 177; 0, 5 and 8 borrowed from the small
    /// set until a screenshot shows them big). Small: the Slayer points and Task streak values (6,257 291,
    /// 6,301 293, 6,411 298: every digit). 10x14 cells, 'a' = 0 .. 'u' = 1 coverage. Regenerate with
    /// <c>dotnet run -c Release --project tests/UnitTests -- --emit-digit-font</c>.</summary>
    public static class DigitFont
    {
        static readonly Dictionary<char, string> Big = new Dictionary<char, string>
        {
            { '0', "aauuuuuuaaaauuuuuuaaaaaaaaaaaaaaaaaaaaaauuaaaaaauuuuaaaaaauuuuaaaaaauuuuaaaaaauuuuaaaaaauuuuaaaaaauuuuaaaaaauuuuaaaaaauuuuaaaaaauuaauuuuuuaa" },
            { '1', "aaaauuaaaaaaaauuaaaaaauuuuaaaauuaauuaaaauuaauuaaaaaaaauuaaaaaaaauuaaaaaaaauuaaaaaaaauuaaaaaaaauuaaaaaaaauuaaaaaaaauuaaaaaaaauuaaaauuuuuuuuuu" },
            { '2', "aauuuuuuaaaauuuuuuaauuaaaaaauuaaaaaaaauuaaaaaaaauuaaaaaaaauuaaaaaauuuuaaaauuuuaaaaaauuuuaaaauuuuaaaauuuuaaaaaauuuuaaaaaauuaaaaaaaauuuuuuuuuu" },
            { '3', "aauuuuuuuuaauuuuuuuuaaaaaaauuaaaaaauuuuaaaaaauuuuaaaaaauuaaaaaaauuuaaaaaaaaaauuuaaaaaaauuuaaaaaaaaauaaaaaaaaauaaaaaaaaauuuuuaaauuuaaaauuuuua" },
            { '4', "aaaaaauuuaaaaaaauuuaaaaaaauuuaaaaaauaauaaaaaauaauaaaauuaaauaaauaaaaauauuaaaaaauauuaaaaaauauuuuuuuuuuaaaaaaaauaaaaaaaaauaaaaaaaaauaaaaaaaaaua" },
            { '5', "uuuuuuuuaauuuuuuuuaauuuaaaaaaauuuaaaaaaauuuaaaaaaauuuaaaaaaauuuuuuuuaaaaaaaaaauuaaaaaaaauuaaaaaaaauuaaaaaaaauuaaaaaaaauuaaaaaaaauuuuuuuuuuaa" },
            { '6', "aaaaauuuuaaaaaauuuuaaaauuaaaaaaauaaaaaaaaauaaaaaaauuuaaaaaaauuuuuuuuuauuuaaaaauauuuaaaaauauuaaaaaaauuuaaaaaaauuuaaaaaaauaauaaaaauaaaauuuuuaa" },
            { '7', "uuuuuuuuuuuuuuuuuuuuaaaaaaauuaaaaaaaauuaaaaaaaauuaaaaaaaauuaaaaaauuaaaaaaaauuaaaaaaaauuaaaaaaauaaaaaaaaauaaaaaaaaauaaaaaaauuaaaaaaaauuaaaaaa" },
            { '8', "aauuuuuuaaaauuuuuuaauuaaaaaaaauuaaaaaaaauuaaaaaaaauuaaaaaaaaaauuuuuuaaaauuaauuaaaauuaauuaauuaaaaaauuuuaaaaaauuuuaaaaaauuuuaaaaaauuaauuuuuuaa" },
            { '9', "aaauuuuuaaaaauuuuuaaaauaaaaauauuaaaaaauauuaaaaaauauuaaaaaaauuuaaaaaaauaauaaaaauuaauaaaaauuaaauuuuuuuaaaaaaaauaaaaaaaaauaaaaaaauuuaaauuuuaaaa" },
        };

        static readonly Dictionary<char, string> Small = new Dictionary<char, string>
        {
            { '0', "aauuuuuuaaaauuuuuuaaaaaaaaaaaaaaaaaaaaaauuaaaaaauuuuaaaaaauuuuaaaaaauuuuaaaaaauuuuaaaaaauuuuaaaaaauuuuaaaaaauuuuaaaaaauuuuaaaaaauuaauuuuuuaa" },
            { '1', "aaaauuuaaaaaaauuuaaaaaaauuuaaaaaaauuuaaaaaaauuuaaaaaaauuuaaaaaaauuuaaaaaaauuuaaaaaaauuuaaaaaaauuuaaaaaaauuuaaaaaaauuuaaaaaaauuuaaauuuuuuuuuu" },
            { '2', "aaauuuuuuuaaauuuuuuuaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaauuaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaauuuuuuuuuu" },
            { '3', "aaauuuuuaaaaauuuuuaaaaaaaaaauuaaaaaaaauuaaaaaaaauuaaaaaaaauuaaaaauuuaaaaaaaaaauuaaaaaaaauuaaaaaaaaaaaaaaaaaaaaaaaaaaaauuaaaaaaaauuuuuuuuuuaa" },
            { '4', "aaaaaauuuuaaaaaauuuuaaaaaaaauuaaaaaaaauuaaaauuaauuaaaauuaauuaauuaaaauuaaaaaaaauuaaaaaaaauuuuuuuuuuuuuuuuuuuuuuaaaaaaaauuaaaaaaaauuaaaaaaaauu" },
            { '5', "uuuuuuuuaauuuuuuuuaauuuaaaaaaauuuaaaaaaauuuaaaaaaauuuaaaaaaauuuuuuuuaaaaaaaaaauuaaaaaaaauuaaaaaaaauuaaaaaaaauuaaaaaaaauuaaaaaaaauuuuuuuuuuaa" },
            { '6', "aaaauuuuaaaaaauuuuaaaauuaaaaaaaauuaaaaaauuaaaaaaaauuaaaaaaaauuaauuuuaauuaaaaaauuuuaaaaaauuuuaaaaaauuuuaaaaaauuaaaaaaaauuaaaaaaaauuaauuuuuuaa" },
            { '7', "uuuuuuuuuuuuuuuuuuuuaaaaaaaauuaaaaaaaauuaaaaaaaaaaaaaaaaaaaaaaaaaauuaaaaaaaaaaaaaaaaaaaaaaaaaauuaaaaaaaauuaaaaaaaaaaaaaaaaaaaaaaaaaauuaaaaaa" },
            { '8', "aauuuuuuaaaauuuuuuaauuaaaaaaaauuaaaaaaaauuaaaaaaaauuaaaaaaaaaauuuuuuaaaauuaauuaaaauuaauuaauuaaaaaauuuuaaaaaauuuuaaaaaauuuuaaaaaauuaauuuuuuaa" },
            { '9', "aauuuuuuaaaauuuuuuaauuaaaaaaaauuaaaaaaaauuaaaaaauuuuaaaaaauuuuaaaaaauuaauuuuaauuaauuuuaauuaaaaaaaauuaaaaaaaauuaaaaaauuaaaaaaaauuaaaauuuuaaaa" },
        };

        /// <summary>Seeds the kills-left (big) shapes.</summary>
        public static void SeedInto(DigitReader reader) { Seed(reader, Big); }

        /// <summary>Seeds the points/streak (small) shapes.</summary>
        public static void SeedSmallInto(DigitReader reader) { Seed(reader, Small); }

        static void Seed(DigitReader reader, Dictionary<char, string> shapes)
        {
            foreach (var kv in shapes)
            {
                var v = new float[kv.Value.Length];
                for (int i = 0; i < v.Length; i++) v[i] = (kv.Value[i] - 'a') / 20f;
                reader.Seed(kv.Key, v);
            }
        }
    }
}

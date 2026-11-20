using System.Collections.Generic;

namespace KavrosSlayerHelper.Core
{
    /// <summary>Digit shapes of the RS3 slayer counter, learned from real screenshots (tests/fixtures/real:
    /// 249, 196, 247 and the minimised 177 for the big number; the points and streak values for 0, 3 and 5).
    /// 10x14 cells, 'a' = 0 .. 'u' = 1 coverage. 8 isn't in any screenshot yet: it is read by OCR and then
    /// learned. Regenerate with
    /// <c>dotnet run -c Release --project tests/UnitTests -- --emit-digit-font</c>.</summary>
    public static class DigitFont
    {
        static readonly Dictionary<char, string> Shapes = new Dictionary<char, string>
        {
            { '0', "aauuuuuuaaaauuuuuuaaaaaaaaaaaaaaaaaaaaaauuaaaaaauuuuaaaaaauuuuaaaaaauuuuaaaaaauuuuaaaaaauuuuaaaaaauuuuaaaaaauuuuaaaaaauuuuaaaaaauuaauuuuuuaa" },
            { '1', "aaaauuaaaaaaaauuaaaaaauuuuaaaauuaauuaaaauuaauuaaaaaaaauuaaaaaaaauuaaaaaaaauuaaaaaaaauuaaaaaaaauuaaaaaaaauuaaaaaaaauuaaaaaaaauuaaaauuuuuuuuuu" },
            { '2', "aauuuuuuaaaauuuuuuaauuaaaaaauuaaaaaaaauuaaaaaaaauuaaaaaaaauuaaaaaauuuuaaaauuuuaaaaaauuuuaaaauuuuaaaauuuuaaaaaauuuuaaaaaauuaaaaaaaauuuuuuuuuu" },
            { '3', "aaauuuuuaaaaauuuuuaaaaaaaaaauuaaaaaaaauuaaaaaaaauuaaaaaaaauuaaaaauuuaaaaaaaaaauuaaaaaaaauuaaaaaaaaaaaaaaaaaaaaaaaaaaaauuaaaaaaaauuuuuuuuuuaa" },
            { '4', "aaaaaauuuaaaaaaauuuaaaaaaauuuaaaaaauaauaaaaaauaauaaaauuaaauaaauaaaaauauuaaaaaauauuaaaaaauauuuuuuuuuuaaaaaaaauaaaaaaaaauaaaaaaaaauaaaaaaaaaua" },
            { '5', "uuuuuuuuaauuuuuuuuaauuuaaaaaaauuuaaaaaaauuuaaaaaaauuuaaaaaaauuuuuuuuaaaaaaaaaauuaaaaaaaauuaaaaaaaauuaaaaaaaauuaaaaaaaauuaaaaaaaauuuuuuuuuuaa" },
            { '6', "aaaaauuuuaaaaaauuuuaaaauuaaaaaaauaaaaaaaaauaaaaaaauuuaaaaaaauuuuuuuuuauuuaaaaauauuuaaaaauauuaaaaaaauuuaaaaaaauuuaaaaaaauaauaaaaauaaaauuuuuaa" },
            { '7', "uuuuuuuuuuuuuuuuuuuuaaaaaaauuaaaaaaaauuaaaaaaaauuaaaaaaaauuaaaaaauuaaaaaaaauuaaaaaaaauuaaaaaaauaaaaaaaaauaaaaaaaaauaaaaaaauuaaaaaaaauuaaaaaa" },
            { '9', "aaauuuuuaaaaauuuuuaaaauaaaaauauuaaaaaauauuaaaaaauauuaaaaaaauuuaaaaaaauaauaaaaauuaauaaaaauuaaauuuuuuuaaaaaaaauaaaaaaaaauaaaaaaauuuaaauuuuaaaa" },
        };

        public static void SeedInto(DigitReader reader)
        {
            foreach (var kv in Shapes)
            {
                var v = new float[kv.Value.Length];
                for (int i = 0; i < v.Length; i++) v[i] = (kv.Value[i] - 'a') / 20f;
                reader.Seed(kv.Key, v);
            }
        }
    }
}

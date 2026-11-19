using System;
using System.Collections.Generic;

namespace KavrosSlayerHelper.Core
{
    /// <summary>Works out which chat lines are new between two reads of the chatbox. New messages
    /// arrive at the bottom and push older ones up, so the old read's tail should match the start of
    /// the new read; whatever follows that overlap is new. Equality is fuzzy to absorb OCR jitter.</summary>
    public static class LineDiff
    {
        public static int NewFrom(IList<string> prev, IList<string> cur)
        {
            if (prev == null || prev.Count == 0) return 0;
            for (int m = 0; m < prev.Count; m++)
            {
                int overlap = prev.Count - m;
                if (overlap > cur.Count) continue;
                bool ok = true;
                for (int k = 0; k < overlap && ok; k++) ok = Same(prev[m + k], cur[k]);
                if (ok) return overlap;
            }
            return 0;
        }

        /// <summary>The lines of <paramref name="cur"/> that were not in <paramref name="prev"/>.</summary>
        public static List<string> NewLines(IList<string> prev, IList<string> cur)
        {
            int from = NewFrom(prev, cur);
            // No overlap at all between two full reads means the view jumped (chat tab switched, scrolled,
            // the box moved or OCR failed): nothing can be trusted as new, so treat it as a fresh baseline.
            if (from == 0 && prev != null && prev.Count >= 3 && cur.Count >= 3) return new List<string>();
            var r = new List<string>();
            for (int i = from; i < cur.Count; i++) r.Add(cur[i]);
            return r;
        }

        public static bool Same(string a, string b)
        {
            string x = SlayerData.Norm(a), y = SlayerData.Norm(b);
            if (x == y) return true;
            int len = Math.Max(x.Length, y.Length);
            if (len < 4) return false;
            if (Math.Abs(x.Length - y.Length) > Math.Max(2, len / 8)) return false;
            return SlayerData.Levenshtein(x, y) <= Math.Max(1, len / 12);
        }
    }
}

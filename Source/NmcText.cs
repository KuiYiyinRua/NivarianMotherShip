using System;
using System.Globalization;
using Verse;

namespace NivarianCommander
{
    // Only presentation text uses the current language. Save keys and simulation identities are invariant.
    internal static class NmcText
    {
        public static string Get(string key, params object[] args)
        {
            string text = key.Translate().ToString();
            return args.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, args);
        }
    }
}

using System;
using System.Collections.Generic;
using SoulsFormats;

namespace SoulsIds
{
    // Lazily read FMGs
    // This could also be an IReadOnlyDictionary but it's probably fine to use the type name anyway
    public class FMGDictionary
    {
        public Dictionary<string, FMG> FMGs = new Dictionary<string, FMG>();
        public Dictionary<string, byte[]> Inner { get; set; }

        // TODO: Return non-FMG with same API
        public FMGX this[string key]
        {
            get => GetX(key);
        }

        public FMG Get(string key)
        {
            if (!Inner.TryGetValue(key, out byte[] data)) throw new Exception($"Internal error: FMG {key} not found");
            if (!FMGs.TryGetValue(key, out FMG fmg))
            {
                FMGs[key] = fmg = FMG.Read(data);
            }
            return fmg;
        }

        public FMGX GetX(string key)
        {
            return FMGX.DLC(this, key);
        }

        public bool ContainsKey(string key) => Inner.ContainsKey(key);
        public IEnumerable<string> Keys => Inner.Keys;
    }
}

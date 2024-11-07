using SoulsFormats;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SoulsIds
{
    public class FMGX
    {
        // Main overrides Other
        public FMG Main { get; set; }
        public FMG Other { get; set; }

        public FMGX(FMG main, FMG other = null)
        {
            Main = main;
            Other = other;
        }

        public static FMGX DLC(Dictionary<string, FMG> fmgs, string key)
        {
            string mainKey = $"{key}_dlc01";
            if (fmgs.ContainsKey(mainKey))
            {
                return new FMGX(fmgs[mainKey], fmgs[key]);
            }
            else
            {
                return new FMGX(fmgs[key]);
            }
        }

        public static FMGX DLC(FMGDictionary fmgs, string key)
        {
            string mainKey = $"{key}_dlc01";
            if (fmgs.ContainsKey(mainKey))
            {
                return new FMGX(fmgs.Get(mainKey), fmgs.Get(key));
            }
            else
            {
                return new FMGX(fmgs.Get(key));
            }
        }

        // TODO: Make more efficient for frequent get situations
        public string this[int id]
        {
            // TODO: Do null/empty entries get patched?
            get => Main[id] ?? Other?[id];

            set
            {
                Main[id] = value;
            }
        }

        // Duplicate ids may cause issues
        public IEnumerable<FMG.Entry> Entries => Other == null ? Main.Entries : Main.Entries.Concat(Other.Entries);
    }
}

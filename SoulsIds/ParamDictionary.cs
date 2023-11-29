using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SoulsFormats;

namespace SoulsIds
{
    // Lazily applies paramdefs
    public class ParamDictionary
    {
        public Dictionary<string, PARAM> Inner = new Dictionary<string, PARAM>();
        public Dictionary<string, PARAM.Layout> Layouts { get; set; }
        public Dictionary<string, PARAMDEF> Defs { get; set; }

        public PARAM this[string key]
        {
            get
            {
                if (!Inner.TryGetValue(key, out PARAM param)) throw new Exception($"Internal error: Param {key} not found");
                if (param.AppliedParamdef == null)
                {
                    // TODO: Get overrideType from tentative type mapping when needed
                    if (Defs != null && ApplyParamdefAggressively(key, param, Defs.Values))
                    {
                        // It worked
                    }
                    else if (Layouts != null && Layouts.TryGetValue(param.ParamType, out PARAM.Layout layout))
                    {
                        param.ApplyParamdef(layout.ToParamdef(param.ParamType, out _));
                    }
                    else throw new Exception($"Internal error: Param {key} has no def file");
                }
                return param;
            }
        }
        public bool ContainsKey(string key) => Inner.ContainsKey(key);
        public IEnumerable<string> Keys => Inner.Keys;

        public static bool ApplyParamdefAggressively(string paramName, PARAM param, IEnumerable<PARAMDEF> paramdefs, string overrideType = null)
        {
            foreach (PARAMDEF paramdef in paramdefs)
            {
                if (ApplyParamdefAggressively(param, paramdef, overrideType))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool ApplyParamdefCarefully(PARAM param, IEnumerable<PARAMDEF> paramdefs, string overrideType = null)
        {
            foreach (PARAMDEF paramdef in paramdefs)
            {
                if (ApplyParamdefCarefully(param, paramdef, overrideType))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ApplyParamdefAggressively(PARAM param, PARAMDEF paramdef, string overrideType = null)
        {
            // ApplyParamdefCarefully does not include enough info to diagnose failed cases.
            // For now, require that paramdef ParamType instances are unique, as there is no
            // naming convention for supporting multiple versions.
            if (param.ParamType == paramdef.ParamType || overrideType == paramdef.ParamType)
            {
                if (param.ParamdefDataVersion == paramdef.DataVersion
                    && (param.DetectedSize == -1 || param.DetectedSize == paramdef.GetRowSize()))
                {
                    param.ApplyParamdef(paramdef);
                    return true;
                }
                else
                {
                    throw new Exception($"Error: {param.ParamType} cannot be applied (paramdef data version {paramdef.DataVersion} vs {param.ParamdefDataVersion}, paramdef size {paramdef.GetRowSize()} vs {param.DetectedSize})");
                }
            }
            return false;
        }

        public static bool ApplyParamdefCarefully(PARAM param, PARAMDEF paramdef, string overrideType = null)
        {
            if ((param.ParamType == paramdef.ParamType || overrideType == paramdef.ParamType)
                && param.ParamdefDataVersion == paramdef.DataVersion
                && (param.DetectedSize == -1 || param.DetectedSize == paramdef.GetRowSize()))
            {
                param.ApplyParamdef(paramdef);
                return true;
            }
            return false;
        }
    }
}

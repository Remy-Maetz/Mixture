using UnityEngine;
using UnityEngine.Rendering;

namespace Mixture
{
    public static class MaterialExtension
    {
        public static void SetKeywordEnabled(this Material material, string keyword, bool enabled)
        {
            if (enabled)
                material.EnableKeyword(keyword);
            else
                material.DisableKeyword(keyword);
        }
    }
}
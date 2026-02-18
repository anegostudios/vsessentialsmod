using Newtonsoft.Json;
using Vintagestory.API.Common;

#nullable disable

namespace Vintagestory.ServerMods.NoObf
{
    [JsonObject(MemberSerialization.OptIn)]
    public class LandformsWorldProperty : WorldProperty<LandformVariant>
    {
        [JsonIgnore]
        public LandformVariant[] LandFormsByIndex;
        
        /// <summary>
        /// Returns the index of the landform with the given code, or -1 if not found
        /// </summary>
        public int GetIndexByCode(string code)
        {
            if (LandFormsByIndex == null) return -1;
            for (int i = 0; i < LandFormsByIndex.Length; i++)
            {
                if (LandFormsByIndex[i]?.Code == code) return i;
            }
            return -1;
        }
    }
}

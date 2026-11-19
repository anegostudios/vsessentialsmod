using System.Collections.Generic;
using System.IO;

using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    /// <summary>
    /// Holds all loaded genome types. The server's copy is sent to the client along with the other server assets.
    /// </summary>
    public class GenomeTypeRegistry : CustomRegistryBase
    {
        public readonly Dictionary<AssetLocation, GenomeType> Types = new Dictionary<AssetLocation, GenomeType>();

        public override void ToBytes(IWorldAccessor resolver, out byte[] data, out int quantity)
        {
            using FastMemoryStream ms = new FastMemoryStream();
            BinaryWriter writer = new BinaryWriter(ms);

            foreach ((AssetLocation key, GenomeType type) in Types)
            {
                writer.Write(key.ToString());
                byte[] bytes = SerializerUtil.Serialize(type);
                writer.Write(bytes.Length);
                writer.Write(bytes);
            }

            quantity = Types.Count;
            data = ms.ToArray();
        }

        public override void FromBytes(IWorldAccessor resolver, int quantity, byte[] data)
        {
            BinarySpanReader reader = new BinarySpanReader(data);

            for (int i = 0; i < quantity; i++)
            {
                AssetLocation key = new AssetLocation(reader.ReadString());
                int length = reader.ReadInt32();
                Types[key] = SerializerUtil.Deserialize<GenomeType>(reader.ReadBytes(length));
            }
        }
    }
}

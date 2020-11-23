using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Vintagestory.GameContent
{

    public interface IWorldMapManager
    {
        bool IsShuttingDown { get; }
        bool IsOpened { get; }
        void TranslateWorldPosToViewPos(Vec3d worldPos, ref Vec2f viewPos);

        void SendMapDataToClient(MapLayer forMapLayer, IServerPlayer forPlayer, byte[] data);

        void SendMapDataToServer(MapLayer forMapLayer, byte[] data);
    }
}

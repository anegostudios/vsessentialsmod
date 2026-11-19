using Newtonsoft.Json;
using Vintagestory.API.Common.Entities;

#nullable disable

namespace Vintagestory.GameContent;

// Added this so in the future if we want to load/save more properties the data format remains backwards compatible
public class CameraPath
{
    public CameraPoint[] Points;
}

[JsonObject(MemberSerialization.OptIn)]
public class CameraPoint
{
    [JsonProperty]
    public double x;
    [JsonProperty]
    public double y;
    [JsonProperty]
    public double z;
    [JsonProperty]
    public float pitch;
    [JsonProperty]
    public float yaw;
    [JsonProperty]
    public float roll;
    [JsonProperty]
    public float fov;

    public double distance;

    public static CameraPoint FromEntityPos(EntityPos pos, float fov)
    {
        CameraPoint point = new CameraPoint();
        point.x = pos.X;
        point.y = pos.Y;
        point.z = pos.Z;
        point.pitch = pos.Pitch;
        point.yaw = pos.Yaw;
        point.roll = pos.Roll;
        point.fov = fov;

        return point;
    }

    public CameraPoint Clone()
    {
        return new CameraPoint()
        {
            x = x,
            y = y,
            z = z,
            pitch = pitch,
            yaw = yaw,
            roll = roll,
            fov = fov
        };
    }

    public CameraPoint ExtrapolateFrom(CameraPoint p, int direction)
    {
        double dx = p.x - x;
        double dy = p.y - y;
        double dz = p.z - z;
        float dpitch = p.pitch - pitch;
        float dyaw = p.yaw - yaw;
        float droll = p.roll - roll;
        float dfov = p.fov - fov;

        return new CameraPoint()
        {
            x = x - dx * direction,
            y = y - dy * direction,
            z = z - dz * direction,
            pitch = pitch - dpitch * direction,
            yaw = yaw - dyaw * direction,
            roll = roll - droll * direction,
            fov = fov - dfov * direction
        };
    }

    public bool PositionEquals(CameraPoint point)
    {
        return point.x == x && point.y == y && point.z == z;
    }

}


public struct CameraKeyframe
{
    public double X;
    public double Y;
    public double Z;

    public double Yaw;
    public double Pitch;
    public double Roll;
    public double FovRad;
}

public enum EnumAutoCamAngleMode
{
    Direction,
    Point,
}

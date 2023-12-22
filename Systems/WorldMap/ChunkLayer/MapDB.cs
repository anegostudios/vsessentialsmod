using ProtoBuf;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class MapBlockIdMappingDB
{
    public Dictionary<AssetLocation, int> BlockIndicesByBlockCode;
}


[ProtoContract]
public class MapPieceDB
{
    [ProtoMember(1)]
    public int[] Pixels;
}

public class MapDB : SQLiteDBConnection
{
    public override string DBTypeCode => "worldmap database";

    public MapDB(ILogger logger) : base(logger)
    {
    }

    SqliteCommand setMapPieceCmd;
    SqliteCommand getMapPieceCmd;


    public override void OnOpened()
    {
        base.OnOpened();

        setMapPieceCmd = sqliteConn.CreateCommand();
        setMapPieceCmd.CommandText = "INSERT OR REPLACE INTO mappiece (position, data) VALUES (@pos, @data)";
        setMapPieceCmd.Parameters.Add("@pos", SqliteType.Integer, 1);
        setMapPieceCmd.Parameters.Add("@data", SqliteType.Blob);
        setMapPieceCmd.Prepare();

        getMapPieceCmd = sqliteConn.CreateCommand();
        getMapPieceCmd.CommandText = "SELECT data FROM mappiece WHERE position=@pos";
        getMapPieceCmd.Parameters.Add("@pos", SqliteType.Integer, 1);
        getMapPieceCmd.Prepare();
    }

    protected override void CreateTablesIfNotExists(SqliteConnection sqliteConn)
    {
        using (var sqlite_cmd = sqliteConn.CreateCommand())
        {
            sqlite_cmd.CommandText = "CREATE TABLE IF NOT EXISTS mappiece (position integer PRIMARY KEY, data BLOB);";
            sqlite_cmd.ExecuteNonQuery();
        }

        using (var sqlite_cmd = sqliteConn.CreateCommand())
        {
            sqlite_cmd.CommandText = "CREATE TABLE IF NOT EXISTS blockidmapping (id integer PRIMARY KEY, data BLOB);";
            sqlite_cmd.ExecuteNonQuery();
        }
    }

    public void Purge()
    {
        using var cmd = sqliteConn.CreateCommand();
        cmd.CommandText = "delete FROM mappiece";
        cmd.ExecuteNonQuery();
    }

    public MapPieceDB[] GetMapPieces(List<Vec2i> chunkCoords)
    {
        var pieces = new MapPieceDB[chunkCoords.Count];
        for (var i = 0; i < chunkCoords.Count; i++)
        {
            getMapPieceCmd.Parameters["@pos"].Value = chunkCoords[i].ToChunkIndex();
            using var sqlite_datareader = getMapPieceCmd.ExecuteReader();
            while (sqlite_datareader.Read())
            {
                var data = sqlite_datareader["data"];
                if (data == null) return null;

                pieces[i] = SerializerUtil.Deserialize<MapPieceDB>(data as byte[]);
            }
        }

        return pieces;
    }

    public MapPieceDB GetMapPiece(Vec2i chunkCoord)
    {
        getMapPieceCmd.Parameters["@pos"].Value = chunkCoord.ToChunkIndex();

        using var sqlite_datareader = getMapPieceCmd.ExecuteReader();
        while (sqlite_datareader.Read())
        {
            object data = sqlite_datareader["data"];
            if (data == null) return null;

            return SerializerUtil.Deserialize<MapPieceDB>(data as byte[]);
        }

        return null;
    }

    public void SetMapPieces(Dictionary<Vec2i, MapPieceDB> pieces)
    {
        using var transaction = sqliteConn.BeginTransaction();
        setMapPieceCmd.Transaction = transaction;
        foreach (var val in pieces)
        {
            setMapPieceCmd.Parameters["@pos"].Value = val.Key.ToChunkIndex();
            setMapPieceCmd.Parameters["@data"].Value = SerializerUtil.Serialize(val.Value);
            setMapPieceCmd.ExecuteNonQuery();
        }

        transaction.Commit();
    }


    public MapBlockIdMappingDB GetMapBlockIdMappingDB()
    {
        using var cmd = sqliteConn.CreateCommand();
        cmd.CommandText = "SELECT data FROM blockidmapping WHERE id=1";

        using var sqlite_datareader = cmd.ExecuteReader();
        while (sqlite_datareader.Read())
        {
            var data = sqlite_datareader["data"];
            return data == null ? null : SerializerUtil.Deserialize<MapBlockIdMappingDB>(data as byte[]);
        }

        return null;
    }

    public void SetMapBlockIdMappingDB(MapBlockIdMappingDB mapping)
    {
        using var transaction = sqliteConn.BeginTransaction();
        using (DbCommand cmd = sqliteConn.CreateCommand())
        {
            cmd.Transaction = transaction;
            var data = SerializerUtil.Serialize(mapping);

            cmd.CommandText = "INSERT OR REPLACE INTO mappiece (position, data) VALUES (@position,@data)";
            cmd.Parameters.Add(CreateParameter("position", DbType.UInt64, 1, cmd));
            cmd.Parameters.Add(CreateParameter("data", DbType.Object, data, cmd));
            var affected = cmd.ExecuteNonQuery();
        }

        transaction.Commit();
    }



    public override void Close()
    {
        setMapPieceCmd?.Dispose();
        getMapPieceCmd?.Dispose();

        base.Close();
    }


    public override void Dispose()
    {
        setMapPieceCmd?.Dispose();
        getMapPieceCmd?.Dispose();

        base.Dispose();
    }
}
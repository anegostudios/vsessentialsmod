using ProtoBuf;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.SQLite;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class MapBlockIdMappingDB
    {
        public Dictionary<AssetLocation, int> BlockIndicesByBlockCode;
    }


    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class MapPieceDB
    {
        //public int[] BlockIndices;
        public int[] Pixels;
    }

    public class MapDB : SQLiteDB
    {
        public override string DBTypeCode => "worldmap database";

        public MapDB(ILogger logger) : base(logger)
        {
        }

        protected override void CreateTablesIfNotExists(SQLiteConnection sqliteConn)
        {
            using (SQLiteCommand sqlite_cmd = sqliteConn.CreateCommand())
            {
                sqlite_cmd.CommandText = "CREATE TABLE IF NOT EXISTS mappiece (position integer PRIMARY KEY, data BLOB);";
                sqlite_cmd.ExecuteNonQuery();
            }

            using (SQLiteCommand sqlite_cmd = sqliteConn.CreateCommand())
            {
                sqlite_cmd.CommandText = "CREATE TABLE IF NOT EXISTS blockidmapping (id integer PRIMARY KEY, data BLOB);";
                sqlite_cmd.ExecuteNonQuery();
            }
        }

        public MapPieceDB GetMapPiece(Vec2i chunkCoord)
        {
            using (SQLiteCommand cmd = sqliteConn.CreateCommand())
            {
                cmd.CommandText = "SELECT data FROM mappiece WHERE position=?";
                cmd.Parameters.Add(CreateParameter("position", DbType.UInt64, chunkCoord.ToChunkIndex(), cmd));

                using (SQLiteDataReader sqlite_datareader = cmd.ExecuteReader())
                {
                    while (sqlite_datareader.Read())
                    {
                        object data = sqlite_datareader["data"];
                        if (data == null) return null;

                        return SerializerUtil.Deserialize<MapPieceDB>(data as byte[]);
                    }
                }
            }

            return null;
        }

        public void SetMapPiece(Vec2i chunkCoord, MapPieceDB piece)
        {
            using (SQLiteTransaction transaction = sqliteConn.BeginTransaction())
            {
                using (DbCommand cmd = sqliteConn.CreateCommand())
                {
                    byte[] data = SerializerUtil.Serialize(piece);

                    cmd.CommandText = "INSERT OR REPLACE INTO mappiece (position, data) VALUES (?,?)";
                    cmd.Parameters.Add(CreateParameter("position", DbType.UInt64, chunkCoord.ToChunkIndex(), cmd));
                    cmd.Parameters.Add(CreateParameter("data", DbType.Object, data, cmd));
                    int affected = cmd.ExecuteNonQuery();
                }

                transaction.Commit();
            }
        }


        public MapBlockIdMappingDB GetMapBlockIdMappingDB()
        {
            using (SQLiteCommand cmd = sqliteConn.CreateCommand())
            {
                cmd.CommandText = "SELECT data FROM blockidmapping WHERE id=1";

                using (SQLiteDataReader sqlite_datareader = cmd.ExecuteReader())
                {
                    while (sqlite_datareader.Read())
                    {
                        object data = sqlite_datareader["data"];
                        if (data == null) return null;

                        return SerializerUtil.Deserialize<MapBlockIdMappingDB>(data as byte[]);
                    }
                }
            }

            return null;
        }

        public void SetMapBlockIdMappingDB(MapBlockIdMappingDB mapping)
        {
            using (SQLiteTransaction transaction = sqliteConn.BeginTransaction())
            {
                using (DbCommand cmd = sqliteConn.CreateCommand())
                {
                    byte[] data = SerializerUtil.Serialize(mapping);

                    cmd.CommandText = "INSERT OR REPLACE INTO mappiece (position, data) VALUES (?,?)";
                    cmd.Parameters.Add(CreateParameter("position", DbType.UInt64, 1, cmd));
                    cmd.Parameters.Add(CreateParameter("data", DbType.Object, data, cmd));
                    int affected = cmd.ExecuteNonQuery();
                }

                transaction.Commit();
            }
        }


    }
}

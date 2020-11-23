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

    public class MapDB : SQLiteDBConnection
    {
        public override string DBTypeCode => "worldmap database";

        public MapDB(ILogger logger) : base(logger)
        {
        }

        SQLiteCommand setMapPieceCmd;
        SQLiteCommand getMapPieceCmd;


        public override void OnOpened()
        {
            base.OnOpened();

            setMapPieceCmd = sqliteConn.CreateCommand();
            setMapPieceCmd.CommandText = "INSERT OR REPLACE INTO mappiece (position, data) VALUES (@pos, @data)";
            setMapPieceCmd.Parameters.Add("@pos", DbType.UInt64, 1);
            setMapPieceCmd.Parameters.Add("@data", DbType.Binary);
            setMapPieceCmd.Prepare();

            getMapPieceCmd = sqliteConn.CreateCommand();
            getMapPieceCmd.CommandText = "SELECT data FROM mappiece WHERE position=@pos";
            getMapPieceCmd.Parameters.Add("@pos", DbType.UInt64, 1);
            getMapPieceCmd.Prepare();
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

        public void Purge()
        {
            using (SQLiteCommand cmd = sqliteConn.CreateCommand())
            {
                cmd.CommandText = "delete FROM mappiece";
                cmd.ExecuteNonQuery();
            }
        }

        public MapPieceDB[] GetMapPieces(List<Vec2i> chunkCoords)
        {
            MapPieceDB[] pieces = new MapPieceDB[chunkCoords.Count];
            for (int i = 0; i < chunkCoords.Count; i++)
            {
                getMapPieceCmd.Parameters["@pos"].Value = chunkCoords[i].ToChunkIndex();
                using (SQLiteDataReader sqlite_datareader = getMapPieceCmd.ExecuteReader())
                {
                    while (sqlite_datareader.Read())
                    {
                        object data = sqlite_datareader["data"];
                        if (data == null) return null;

                        pieces[i] = SerializerUtil.Deserialize<MapPieceDB>(data as byte[]);
                    }
                }
            }

            return pieces;
        }

        public MapPieceDB GetMapPiece(Vec2i chunkCoord)
        {
            getMapPieceCmd.Parameters["@pos"].Value = chunkCoord.ToChunkIndex();

            using (SQLiteDataReader sqlite_datareader = getMapPieceCmd.ExecuteReader())
            {
                while (sqlite_datareader.Read())
                {
                    object data = sqlite_datareader["data"];
                    if (data == null) return null;

                    return SerializerUtil.Deserialize<MapPieceDB>(data as byte[]);
                }
            }

            return null;
        }

        public void SetMapPieces(Dictionary<Vec2i, MapPieceDB> pieces)
        {
            using (SQLiteTransaction transaction = sqliteConn.BeginTransaction())
            {
                foreach (var val in pieces)
                {
                    setMapPieceCmd.Parameters["@pos"].Value = val.Key.ToChunkIndex();
                    setMapPieceCmd.Parameters["@data"].Value = SerializerUtil.Serialize(val.Value);
                    setMapPieceCmd.ExecuteNonQuery();
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
}

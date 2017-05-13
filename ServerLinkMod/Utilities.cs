using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Timers;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace ServerLinkMod
{
    public class Utilities
    {
        public enum VerifyResult
        {
            Error,
            Ok,
            Timeout,
            ContentModified,
            WrongIP,
        }

        public const string ZERO_IP = "0.0.0.0:27016";

        private static readonly List<IMyPlayer> _playerCache = new List<IMyPlayer>();

        private static readonly Random Random = new Random();

        /// <summary>
        ///     IMyMultiplayer.JoinServer has to be called this way to prevent crashes.
        /// </summary>
        /// <param name="ip"></param>
        public static void JoinServer(string ip)
        {
            MyAPIGateway.Utilities.InvokeOnGameThread(() => MyAPIGateway.Multiplayer.JoinServer(ip));
        }

        public static byte[] SerializeAndSign(IMyCubeGrid grid, IMyPlayer player, Vector3I block)
        {
            var c = grid.GetCubeBlock(block)?.FatBlock as IMyCockpit;
            IMyCharacter pilot = c?.Pilot;
            c?.RemovePilot();

            var ob = (MyObjectBuilder_CubeGrid)grid.GetObjectBuilder();

            if (pilot != null)
                c.AttachPilot(pilot);

            IMyFaction fac = MyAPIGateway.Session.Factions.TryGetPlayerFaction(player.IdentityId);
            var data = new ClientData(ob, fac, block, Settings.Instance.HubIP);

            string obStr = MyAPIGateway.Utilities.SerializeToXML(data);
            string totalStr = DateTime.UtcNow.Ticks + obStr;
            string evalStr = totalStr + Settings.Instance.Password;

            var m = new MD5();
            m.Value = evalStr;
            totalStr += m.FingerPrint;

            return Encoding.UTF8.GetBytes(totalStr);
        }

        public static VerifyResult DeserializeAndVerify(byte[] data, out ClientData clientData, bool ignoreTimestamp = false)
        {
            string input = Encoding.UTF8.GetString(data);
            try
            {
                string timeAndOb = input.Substring(0, input.Length - 32);
                string hash = input.Substring(input.Length - 32);

                var m = new MD5();
                m.Value = timeAndOb + Settings.Instance.Password;
                string sign = m.FingerPrint;

                long ticks = long.Parse(timeAndOb.Substring(0, 18));
                string obString = timeAndOb.Substring(18);

                clientData = MyAPIGateway.Utilities.SerializeFromXML<ClientData>(obString);

                var time = new DateTime(ticks);

                if(clientData.HubIP != Settings.Instance.HubIP)
                    return VerifyResult.WrongIP;

                if (sign != hash)
                    return VerifyResult.ContentModified;

                if (!ignoreTimestamp && DateTime.UtcNow - time > TimeSpan.FromMinutes(10))
                    return VerifyResult.Timeout;

                return VerifyResult.Ok;
            }
            catch (Exception ex)
            {
                Logging.Instance.WriteLine("Error deserializing grid!");
                Logging.Instance.WriteLine(ex.ToString());
                Logging.Instance.WriteLine(input);
                clientData = null;
                return VerifyResult.Error;
            }
        }

        /// <summary>
        ///     MUST CALL RECREATEFACTION FIRST!
        /// </summary>
        /// <param name="ob"></param>
        /// <param name="playerId"></param>
        public static IMyCubeGrid FindPositionAndSpawn(MyObjectBuilder_CubeGrid ob, long playerId, Vector3I controlledBlock)
        {
            MyAPIGateway.Entities.RemapObjectBuilder(ob);
            ob.IsStatic = false;
            IMyEntity ent = MyAPIGateway.Entities.CreateFromObjectBuilder(ob);
            IMyFaction fac = MyAPIGateway.Session.Factions.TryGetPlayerFaction(playerId);

            foreach (MyObjectBuilder_CubeBlock block in ob.CubeBlocks)
            {
                block.Owner = playerId;
                var c = block as MyObjectBuilder_Cockpit;
                if (c == null)
                    continue;
                c.Pilot = null;
            }

            Vector3D pos = RandomPositionFromPoint(Vector3D.Zero, Random.NextDouble() * Settings.Instance.SpawnRadius);
            ent.SetPosition(MyAPIGateway.Entities.FindFreePlace(pos, (float)ent.WorldVolume.Radius) ?? pos);

            IMyPlayer player = GetPlayerById(playerId);
            if (fac != null)
            {
                var entities = new HashSet<IMyEntity>();
                MyAPIGateway.Entities.GetEntities(entities);
                foreach (IMyEntity entity in entities)
                {
                    var g = entity as IMyCubeGrid;
                    if (g == null)
                        continue;

                    if (g.BigOwners.Count == 0)
                        continue;

                    long owner = g.BigOwners[0];
                    IMyFaction f = MyAPIGateway.Session.Factions.TryGetPlayerFaction(owner);
                    if (f != fac)
                        continue;

                    Vector3D p = RandomPositionFromPoint(g.GetPosition(), 100);
                    ent.SetPosition(MyAPIGateway.Entities.FindFreePlace(p, (float)ent.WorldVolume.Radius) ?? p);

                    break;
                }
            }
            MyAPIGateway.Entities.AddEntity(ent);
            ((IMyCubeGrid)ent).ChangeGridOwnership(playerId, MyOwnershipShareModeEnum.Faction);

            IMySlimBlock slim = ((IMyCubeGrid)ent).GetCubeBlock(controlledBlock);
            if (slim?.FatBlock is IMyCockpit && player?.Character != null)
            {
                var c = (IMyCockpit)slim.FatBlock;
                player.Character.SetPosition(c.GetPosition());
                c.AttachPilot(player.Character);
            }
            else
            {
                Vector3D cPos = RandomPositionFromPoint(ent.WorldVolume.Center, ent.WorldVolume.Radius + 10);
                player?.Character?.SetPosition(cPos);
            }

            return (IMyCubeGrid)ent;
        }

        /// <summary>
        ///     DANGER WILL ROBINSON! DANGER!
        /// </summary>
        public static void ScrubServer()
        {
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);
            foreach (IMyPlayer p in players)
            {
                var block = p?.Controller?.ControlledEntity?.Entity as IMyCubeBlock;
                IMyCubeGrid grid = block?.CubeGrid;

                if (grid == null)
                    LinkModCore.Instance.PlayerGrids.TryGetValue(p.SteamUserId, out grid);

                if (grid != null)
                {
                    byte[] payload = Utilities.SerializeAndSign(grid, p, block?.Position ?? Vector3I.Zero);
                    Communication.SegmentAndSend(Communication.MessageType.ClientGridPart, payload, MyAPIGateway.Multiplayer.ServerId, p.SteamUserId);
                }
                Communication.RedirectClient(p.SteamUserId, Settings.Instance.HubIP);
            }

            var timer = new Timer(10000);
            timer.AutoReset = false;
            timer.Elapsed += (a, b) =>
                             {
                                 MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                                                                           {
                                                                               var entities = new HashSet<IMyEntity>();
                                                                               MyAPIGateway.Entities.GetEntities(entities);

                                                                               foreach (IMyEntity ent in entities)
                                                                               {
                                                                                   if (ent is IMyCharacter)
                                                                                       continue;

                                                                                   ent.Close();
                                                                               }

                                                                               foreach (KeyValuePair<long, IMyFaction> fac in MyAPIGateway.Session.Factions.Factions)
                                                                                   MyAPIGateway.Session.Factions.RemoveFaction(fac.Key);
                                                                           });
                             };
            timer.Start();
        }

        public static void RecreateFaction(FactionData data, long requester)
        {
            if (string.IsNullOrEmpty(data?.Tag))
                return;

            IMyFaction current = MyAPIGateway.Session.Factions.TryGetFactionByTag(data.Tag);
            if (current == null)
            {
                MyAPIGateway.Session.Factions.CreateFaction(requester, data.Tag, data.Name, data.Description, data.PrivateInfo);
                return;
            }

            if (current.IsMember(requester))
                return;

            MyAPIGateway.Session.Factions.SendJoinRequest(current.FactionId, requester);
            MyAPIGateway.Session.Factions.AcceptJoin(current.FactionId, requester);
        }

        public static IMyPlayer GetPlayerBySteamId(ulong steamId)
        {
            _playerCache.Clear();
            MyAPIGateway.Players.GetPlayers(_playerCache);
            return _playerCache.FirstOrDefault(p => p.SteamUserId == steamId);
        }

        public static IMyPlayer GetPlayerById(long identityId)
        {
            _playerCache.Clear();
            MyAPIGateway.Players.GetPlayers(_playerCache);
            return _playerCache.FirstOrDefault(p => p.IdentityId == identityId);
        }

        /// <summary>
        ///     Randomizes a vector by the given amount
        /// </summary>
        /// <param name="start"></param>
        /// <param name="distance"></param>
        /// <returns></returns>
        public static Vector3D RandomPositionFromPoint(Vector3D start, double distance)
        {
            double z = Random.NextDouble() * 2 - 1;
            double piVal = Random.NextDouble() * 2 * Math.PI;
            double zSqrt = Math.Sqrt(1 - z * z);
            var direction = new Vector3D(zSqrt * Math.Cos(piVal), zSqrt * Math.Sin(piVal), z);

            //var mat = MatrixD.CreateFromYawPitchRoll(RandomRadian, RandomRadian, RandomRadian);
            //Vector3D direction = Vector3D.Transform(start, mat);
            direction.Normalize();
            start += direction * -2;
            return start + direction * distance;
        }

        //Provided by Phoera
        public static void Explode(Vector3D position, float damage, double radius, IMyEntity owner, MyExplosionTypeEnum type, bool affectVoxels = true)
        {
            var exp = new MyExplosionInfo(damage, damage, new BoundingSphereD(position, radius), type, true)
                      {
                          Direction = Vector3D.Up,
                          ExplosionFlags = MyExplosionFlags.APPLY_FORCE_AND_DAMAGE | MyExplosionFlags.CREATE_PARTICLE_EFFECT | MyExplosionFlags.APPLY_DEFORMATION,
                          OwnerEntity = owner as MyEntity,
                          VoxelExplosionCenter = position
                      };
            if (affectVoxels)
                exp.AffectVoxels = true;
            MyExplosions.AddExplosion(ref exp);
        }
    }

    public static class Extensions
    {
        public static void AddOrUpdate<T>(this Dictionary<T, int> dic, T key, int value)
        {
            if (dic.ContainsKey(key))
                dic[key] += value;
            else
                dic[key] = value;
        }

        public static Vector3D GetPosition(this IMySlimBlock block)
        {
            return block.CubeGrid.GridIntegerToWorld(block.Position);
        }
    }
}

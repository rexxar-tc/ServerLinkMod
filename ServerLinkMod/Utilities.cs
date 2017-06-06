using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Timers;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.ComponentSystem;
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

        public enum GridLinkType
        {
            Single,
            Logical,
            Physical,
            Mechanical,
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

        public static HashSet<IMyCubeGrid> GetGridGroup(IMyCubeGrid gridInGroup, GridLinkType linkType)
        {
            var result = new HashSet<IMyCubeGrid>();

            if (linkType == GridLinkType.Single)
            {
                result.Add(gridInGroup);
                return result;
            }

            FindConnected(gridInGroup, linkType, result);

            return result;
        }

        /// <summary>
        /// Recursive function to find grids connected with the given constraint.
        /// Mechanical: connected by piston and rotor
        /// Logical: connected by ship connector or mechanical connection
        /// Physical: connected by landing gear or logical, or mechanical
        /// 
        /// Should be analagous to the data hidden in MyCubeGridGroups
        /// </summary>
        /// <param name="origin"></param>
        /// <param name="linkType"></param>
        /// <param name="result"></param>
        public static void FindConnected(IMyCubeGrid origin, GridLinkType linkType, HashSet<IMyCubeGrid> result)
        {
            if (origin == null)
                return;

            if (!result.Add(origin))
            {
                //we've already processed this grid;
                return;
            }

            var blocks = new List<IMySlimBlock>();
                origin.GetBlocks(blocks);
            if (linkType == GridLinkType.Mechanical || linkType == GridLinkType.Logical || linkType == GridLinkType.Physical)
            {
                foreach (var slim in blocks)
                {
                    var block = slim?.FatBlock;
                    if (block == null)
                        continue;

                    var mec = block as IMyMechanicalConnectionBlock;
                    if (mec != null)
                    {
                        FindConnected(mec.TopGrid, linkType, result);
                        continue;
                    }

                    var top = block as IMyAttachableTopBlock;
                    if (top != null)
                    {
                        FindConnected(top.Base?.CubeGrid, linkType, result);
                        continue;
                    }
                }
            }

            if (linkType == GridLinkType.Logical || linkType == GridLinkType.Physical)
            {
                foreach (var slim in blocks)
                {
                    var block = slim?.FatBlock;

                    var con = block as IMyShipConnector;
                    if (con != null)
                    {
                        FindConnected(con.OtherConnector?.CubeGrid, linkType, result);
                        continue;
                    }
                }
            }

            if (linkType == GridLinkType.Physical)
            {
                var box = origin.WorldAABB;

                foreach (var slim in blocks)
                {
                    var lg = slim?.FatBlock as IMyLandingGear;
                    var g = lg?.GetAttachedEntity() as IMyCubeGrid;
                    if (g == null)
                        continue;

                    FindConnected(g, linkType, result);
                }


                var ents = MyAPIGateway.Entities.GetTopMostEntitiesInBox(ref box);

                foreach (var ent in ents)
                {
                    var grid = ent as IMyCubeGrid;
                    if (grid == null)
                        return;

                    blocks.Clear();
                    grid.GetBlocks(blocks);

                    foreach (var slim in blocks)
                    {
                        var block = slim?.FatBlock;

                        var lg = block as IMyLandingGear;

                        var g = lg?.GetAttachedEntity() as IMyCubeGrid;
                        if (g == null)
                            continue;

                        if (!result.Contains(g))
                            continue;

                        FindConnected(grid, linkType, result);
                        break;
                    }
                }
            }
        }

        public static byte[] SerializeAndSign(IMyCubeGrid grid, GridLinkType linkType, IMyPlayer player, Vector3I block, string destIP)
        {
            IMyFaction fac = MyAPIGateway.Session.Factions.TryGetPlayerFaction(player.IdentityId);

            var group = GetGridGroup(grid, linkType);
            var obs = new MyObjectBuilder_CubeGrid[group.Count];
            int index = 0;
            var blocks = new List<IMySlimBlock>();
            foreach (var g in group)
            {
                UpdateGridData(g);
                blocks.Clear();
                g.GetBlocks(blocks);
                {
                    foreach (var slim in blocks)
                    {
                        var c = slim?.FatBlock as IMyCockpit;
                        c?.RemovePilot();
                    }
                }
                var ob = (MyObjectBuilder_CubeGrid)g.GetObjectBuilder();
                obs[index] = ob;
                index++;
            }

            var data = new ClientData(obs, fac, block, destIP);

            string totalStr = MyAPIGateway.Utilities.SerializeToXML(data);
            string evalStr = totalStr + Settings.Instance.Global.Password;

            var m = new MD5();
            m.Value = evalStr;
            totalStr += m.FingerPrint;

            return Encoding.UTF8.GetBytes(totalStr);
        }

        public static void UpdateGridData(IMyCubeGrid grid)
        {
            var data = new GridData(grid);
            var bytes = MyAPIGateway.Utilities.SerializeToBinary(data);
            var str = Convert.ToBase64String(bytes);
            if(grid.Storage==null)
                grid.Storage = new MyModStorageComponent();
            grid.Storage[Settings.STORAGE_GUID] = str;
        }

        public static GridData GetGridData(MyObjectBuilder_CubeGrid ob)
        {
            var comp = ob.ComponentContainer.Components.Find(c => c.Component is MyObjectBuilder_ModStorageComponent)?.Component as MyObjectBuilder_ModStorageComponent;
            if (comp == null)
                return null;

            var str = comp.Storage[Settings.STORAGE_GUID];
            var data = Convert.FromBase64String(str);

            var gridData = MyAPIGateway.Utilities.SerializeFromBinary<GridData>(data);
            return gridData;
        }

        public static VerifyResult DeserializeAndVerify(byte[] data, out ClientData clientData, bool ignoreTimestamp = false)
        {
            string input = Encoding.UTF8.GetString(data);
            try
            {
                string dataStr = input.Substring(0, input.Length - 32);
                string hash = input.Substring(input.Length - 32);

                var m = new MD5();
                m.Value = dataStr + Settings.Instance.Global.Password;
                string sign = m.FingerPrint;
                
                string obString = dataStr;

                clientData = MyAPIGateway.Utilities.SerializeFromXML<ClientData>(obString);

                var time = new DateTime(clientData.Timestamp);

                if(clientData.SourceIP != Settings.Instance.Global.CurrentIP && clientData.DestIP != Settings.Instance.Global.CurrentIP)
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
        public static void FindPositionAndSpawn(MyObjectBuilder_CubeGrid[] obs, long playerId, Vector3I controlledBlock, Action<IMyCubeGrid[]> callback = null )
        {
            MyAPIGateway.Entities.RemapObjectBuilderCollection(obs);
            for (int i = 0; i < obs.Length; i++)
            {
                var ob = obs[i];
                //MyAPIGateway.Entities.RemapObjectBuilder(ob);
                foreach (MyObjectBuilder_CubeBlock block in ob.CubeBlocks)
                {
                    block.Owner = playerId;
                    var c = block as MyObjectBuilder_Cockpit;
                    if (c == null)
                        continue;
                    c.Pilot = null;
                }
                ob.IsStatic = false;

                var counter = new SpawnCounter(obs, playerId, controlledBlock);
                //IMyEntity ent = MyAPIGateway.Entities.CreateFromObjectBuilder(ob);
                MyAPIGateway.Entities.CreateFromObjectBuilderParallel(ob, true, () => counter.Increment());
            }
        }

        private class SpawnCounter
        {
            private int _counter;
            private readonly int _maxCount;
            private readonly long _playerId;
            private readonly MyObjectBuilder_CubeGrid[] _grids;
            private readonly Vector3I _controlledBlock;
            private readonly Action<IMyCubeGrid[]> _callback;

            public SpawnCounter(MyObjectBuilder_CubeGrid[] grids, long playerId, Vector3I controlledBlock, Action<IMyCubeGrid[]> callback = null)
            {
                _grids = grids;
                _counter = 0;
                _maxCount = grids.Length;
                _controlledBlock = controlledBlock;
                _playerId = playerId;
                _callback = callback;
            }

            public void Increment()
            {
                _counter++;
                if (_counter < _maxCount)
                    return;

                var grids = SpawnCallback(_grids, _playerId, _controlledBlock);
                _callback?.Invoke(grids);
            }
        }

        private static IMyCubeGrid[] SpawnCallback(MyObjectBuilder_CubeGrid[] obs, long playerId, Vector3I controlledBlock)
        {
            IMyCubeGrid[] result = new IMyCubeGrid[obs.Length];
            BoundingSphereD sphere = new BoundingSphereD(Vector3D.Zero, 0);
            for (int i = 0; i < obs.Length; i++)
            {
                var ent = MyAPIGateway.Entities.GetEntityById(obs[i].EntityId) as IMyCubeGrid;
                if (ent == null)
                    throw new NullReferenceException();
                result[i] = ent;
                if (sphere.Radius == 0)
                    sphere = ent.WorldVolume;
                else
                    sphere.Include(ent.WorldVolume);
            }

            var target = Vector3D.Zero;

            IMyFaction fac = MyAPIGateway.Session.Factions.TryGetPlayerFaction(playerId);
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

                    target = RandomPositionFromPoint(g.GetPosition(), 100);

                    break;
                }
            }

            Vector3D pos = RandomPositionFromPoint(target, Random.NextDouble() * Settings.Instance.CurrentData.SpawnRadius);
            var offset = MyAPIGateway.Entities.FindFreePlace(pos, (float)sphere.Radius) ?? pos;

            MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                                                      {
                                                          foreach (var ent in result)
                                                          {
                                                              var m = ent.WorldMatrix;
                                                              m.Translation += offset;
                                                              ent.SetWorldMatrix(m);
                                                          }
                                                      });

            IMyPlayer player = GetPlayerById(playerId);

            IMySlimBlock slim = result[0]?.GetCubeBlock(controlledBlock);
            if (slim?.FatBlock is IMyCockpit && player?.Character != null)
            {
                var c = (IMyCockpit)slim.FatBlock;
                player.Character.SetPosition(c.GetPosition());
                c.AttachPilot(player.Character);
            }
            else
            {
                Vector3D cPos = RandomPositionFromPoint(sphere.Center, sphere.Radius + 10);
                player?.Character?.SetPosition(cPos);
            }

            return result;
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
                    byte[] payload = Utilities.SerializeAndSign(grid, GridLinkType.Logical, p, block?.Position ?? Vector3I.Zero, Settings.Instance.Global.HubIP);
                    Communication.SegmentAndSend(Communication.MessageType.ClientGridPart, payload, MyAPIGateway.Multiplayer.ServerId, p.SteamUserId);
                }
                Communication.RedirectClient(p.SteamUserId, Settings.Instance.Global.HubIP);
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

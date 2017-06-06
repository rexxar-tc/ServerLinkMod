using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using ProtoBuf;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Serialization;
using VRageMath;

namespace ServerLinkMod
{
    [Serializable, XmlInclude(typeof(FactionData))]
    public class ClientData
    {
        public long Timestamp;
        public SerializableVector3I ControlledBlock;
        public FactionData Faction;
        public MyObjectBuilder_CubeGrid[] Grids;
        public string SourceIP;
        public string DestIP;
        public ulong[] OnlinePlayers;

        public ClientData()
        {}

        public ClientData(MyObjectBuilder_CubeGrid[] grids, IMyFaction faction, Vector3I controlledBlock, string destIP )
        {
            Grids = grids;
            ControlledBlock = controlledBlock;
            if (faction != null)
                Faction = new FactionData(faction);
            else
                Faction = new FactionData();
            SourceIP = Settings.Instance.Global.CurrentIP;
            DestIP = destIP;
            List<IMyPlayer> players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);
            OnlinePlayers = players.Select(p => p.SteamUserId).ToArray();
            Timestamp = DateTime.UtcNow.Ticks;
        }
    }

    [Serializable]
    public class FactionData
    {
        public string Description;
        public string Name;
        public string PrivateInfo;
        public string Tag;

        public FactionData()
        {}

        public FactionData(IMyFaction faction)
        {
            Tag = faction.Tag;
            Name = faction.Name;
            PrivateInfo = faction.PrivateInfo;
            Description = faction.Description;
        }
    }

    [Serializable]
    [XmlInclude(typeof(OwnerData))]
    [ProtoContract]
    public class GridData
    {
        [ProtoMember]
        public SerializableDictionary<long, OwnerData> OwnerDic;

        [XmlIgnore]
        public Dictionary<long, OwnerData> OwnerLookup
        {
            get { return OwnerDic.Dictionary; }
            set { OwnerDic.Dictionary = value; }
        }

        public GridData()
        { }

        public GridData(IMyCubeGrid grid)
        {
            var dic = new Dictionary<long, OwnerData>();
            foreach (var owner in grid.SmallOwners)
            {
                if (!dic.ContainsKey(owner))
                    dic.Add(owner, new OwnerData(owner));
            }

            OwnerDic = new SerializableDictionary<long, OwnerData>(dic);
        }

        [Serializable]
        [ProtoContract]
        public class OwnerData
        {
            [ProtoMember]
            public ulong SteamId;
            [ProtoMember]
            public long Faction;

            public OwnerData()
            { }

            public OwnerData(long identityId)
            {
                SteamId = MyAPIGateway.Players.TryGetSteamId(identityId);
                Faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(identityId)?.FactionId ?? 0;
            }

            public OwnerData(ulong steamId, long factionId)
            {
                SteamId = steamId;
                Faction = factionId;
            }
        }
    }
}

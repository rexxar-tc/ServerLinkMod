using System;
using System.Xml.Serialization;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;

namespace ServerLinkMod
{
    [Serializable, XmlInclude(typeof(FactionData))]
    public class ClientData
    {
        public SerializableVector3I ControlledBlock;
        public FactionData Faction;
        public MyObjectBuilder_CubeGrid Grid;

        public ClientData()
        {}

        public ClientData(MyObjectBuilder_CubeGrid grid, IMyFaction faction, Vector3I controlledBlock)
        {
            Grid = grid;
            ControlledBlock = controlledBlock;
            if (faction != null)
                Faction = new FactionData(faction);
            else
                Faction = new FactionData();
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
}

using System;
using System.IO;
using System.Xml.Serialization;
using Sandbox.ModAPI;
using VRage.Serialization;

namespace ServerLinkMod
{
    [Serializable]
    [XmlInclude(typeof(GlobalSettings))]
    [XmlInclude(typeof(NodeData))]
    [XmlInclude(typeof(HubData))]
    public class Settings
    {
        public static readonly Guid STORAGE_GUID = new Guid("A71BEB1F-D34F-4BA4-857F-80A708ED300C");

        public enum NodeType
        {
            Invalid,
            Battle,
            Mining,
            NonPersistent,
            SemiPersistent,
        }

        public enum ExitBehavior
        {
            None,
            Punishment,
            ReturnToHub
        }

        public class GlobalSettings
        {
            public string HubIP;
            public string Password;
            public string CurrentIP;
        }

        public class NodeData
        {
            public string IP = string.Empty;
            public int BattleTime;
            public int JoinTime;
            public double SpawnRadius;
            public double WarnBoundary;
            public int MaxBlockCount;
            public int MaxPlayerCount;
            public bool WipeNodeWhenEmpty;
            public int NodeCleanupGridSize;
            public bool NodeCleanupSlimOnly;
            public bool NodeCleanupResetVoxels;
            public ExitBehavior NodeExitBehavior;
            public NodeType NodeType;
            public bool ReturnShip;
            public bool NodeEnforcement;
        }

        public class HubData : NodeData
        {
            public new string IP { get { return Instance.Global.HubIP; } }
            public new NodeType NodeType { get { return NodeType.Invalid; } }
            public bool HubEnforcemenEnabled;
            public bool HubEnforcementStaticGrids;
            public bool HubEnforcementWeaponsOff;
            public bool HubEnforcementDamageOff;
            public bool HubEnforcementNoMechanicalBllocks;
            public bool HubEnforcementOwnerOffline;
            public bool HubEnforcement;
        }

        public static Settings Instance;

        public SerializableDictionary<string, NodeData> Nodes;

        public GlobalSettings Global;

        private HubData _hub;

        public HubData Hub
        {
            get { return _hub ?? (_hub = (HubData)Nodes[Global.HubIP]); }
        }
        
        public bool IsHub
        {
            get { return Global.HubIP == Global.CurrentIP; }
        }
        
        private NodeData _current;
        public NodeData CurrentData
        {
            get { return _current ?? (_current = Nodes[Global.CurrentIP]); }
        }

        public static void LoadSettings()
        {
            try
            {
                TextReader reader = MyAPIGateway.Utilities.ReadFileInLocalStorage("ServerLink.xml", typeof(LinkModCore));
                string xml = reader.ReadToEnd();
                Logging.Instance.WriteDebug($"Loading settings: {xml}");
                Instance = MyAPIGateway.Utilities.SerializeFromXML<Settings>(xml);
                reader.Close();
            }
            catch (Exception ex)
            {
                Logging.Instance.WriteLine("Failed to deserialize settings.");
                Logging.Instance.WriteLine(ex.ToString());
            }
        }

        public static void SaveSettings()
        {
            try
            {
                TextWriter writer = MyAPIGateway.Utilities.WriteFileInLocalStorage("ServerLink.xml", typeof(LinkModCore));
                writer.WriteLine(MyAPIGateway.Utilities.SerializeToXML(Instance));
                writer.Flush();
                writer.Close();
            }
            catch (Exception ex)
            {
                Logging.Instance.WriteLine("Error saving settings.");
                Logging.Instance.WriteLine(ex.ToString());
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using Sandbox.ModAPI;
using VRage.Serialization;

namespace ServerLinkMod
{
    [Serializable]
    public class Settings
    {
        public enum NodeType
        {
            Battle,
            Mining,
            NonPersistent,
            SemiPersistent,
        }

        public enum ExitBehavior
        {
            None,
            Punishment,
            ReturnToHub,
        }

        public static Settings Instance;

        public SerializableDictionary<string, NodeType> Nodes;
        public string CurrentIP;
        public string HubIP;
        public string Password;

        public int BattleTime;
        public int JoinTime;
        public double SpawnRadius;

        public int MaxBlockCount;
        public int MaxPlayerCount;

        public bool HubEnforcemenEnabled;
        public bool HubEnforcementStaticGrids;
        public bool HubEnforcementWeaponsOff;
        public bool HubEnforcementDamageOff;
        public bool HubEnforcementNoMechanicalBllocks;
        public bool HubEnforcementOwnerOffline;

        public bool WipeNodeWhenEmpty;
        public int NodeCleanupGridSize;
        public bool NodeCleanupSlimOnly;
        public bool NodeCleanupResetVoxels;
        public ExitBehavior NodeExitBehavior;

        [XmlIgnore]
        public bool Hub
        {
            get { return HubIP == CurrentIP; }
        }

        public bool ReturnShip;
        public bool NodeEnforcement;
        public bool HubEnforcement;

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

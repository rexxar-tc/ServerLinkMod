using System;
using System.Collections.Generic;
using System.IO;
using Sandbox.ModAPI;

namespace ServerLinkMod
{
    [Serializable]
    public class Settings
    {
        public static Settings Instance;
        public List<string> BattleIPs;
        public int BattleTime;
        public bool Hub;
        public string HubIP;
        public int JoinTime;

        public int MaxBlockCount;
        public int MaxPlayerCount;
        public string Password;
        public double SpawnRadius;

        public static void LoadSettings()
        {
            try
            {
                TextReader reader = MyAPIGateway.Utilities.ReadFileInLocalStorage("ServerLink.xml", typeof(LinkModCore));
                string xml = reader.ReadToEnd();
                Logging.Instance.WriteLine($"Loading settings: {xml}");
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

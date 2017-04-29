using System;
using System.IO;
using System.Text;
using Sandbox.ModAPI;

namespace ServerLinkMod
{
    internal class Logging
    {
        private static Logging _instance;

        private static int lineCount;
        private static bool init;
        private static readonly string[] log = new string[10];
        private readonly StringBuilder _cache = new StringBuilder();
        private readonly TextWriter _writer;
        private int _indent;

        public Logging(string logFile)
        {
            try
            {
                _writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(logFile, typeof(Logging));
                _instance = this;
            }
            catch
            {}
        }

        public static Logging Instance
        {
            get
            {
                if (MyAPIGateway.Utilities == null)
                    return null;

                if (_instance == null)
                    _instance = new Logging("ServerLink.log");

                return _instance;
            }
        }

        public void IncreaseIndent()
        {
            _indent++;
        }

        public void DecreaseIndent()
        {
            if (_indent > 0)
                _indent--;
        }

        public void WriteLine(object o)
        {
            WriteLine(o.ToString());
        }

        public void WriteLine(string text)
        {
            try
            {
                if (_cache.Length > 0)
                    _writer.WriteLine(_cache);

                _cache.Clear();
                _cache.Append(DateTime.Now.ToString("[HH:mm:ss:ffff] "));
                for (var i = 0; i < _indent; i++)
                    _cache.Append("\t");

                _writer.WriteLine(_cache.Append(text));
                _writer.Flush();
                _cache.Clear();
            }
            catch
            {
                //logger failed, all hope is lost
            }
        }

        public void WriteDebug(string text)
        {
            if (LinkModCore.Debug)
                WriteLine(text);
        }

        public void Debug_obj(string text)
        {
            if (!LinkModCore.Debug)
                return;

            WriteLine("\tDEBUG_OBJ: " + text);

            //server can't show objectives. probably.
            if (MyAPIGateway.Session.Player == null)
                return;

            //I'm the only one that needs to see this
            if (MyAPIGateway.Session.Player.SteamUserId != 76561197996829390)
                return;

            text = $"{DateTime.Now.ToString("[HH:mm:ss:ffff]")}: {text}";

            if (!init)
            {
                init = true;
                MyAPIGateway.Utilities.GetObjectiveLine().Title = "Link debug";
                MyAPIGateway.Utilities.GetObjectiveLine().Objectives.Clear();
                MyAPIGateway.Utilities.GetObjectiveLine().Objectives.Add("Start");
                MyAPIGateway.Utilities.GetObjectiveLine().Show();
            }
            if (lineCount > 9)
                lineCount = 0;
            log[lineCount] = text;
            string[] oldLog = log;
            for (var i = 0; i < 9; i++)
                log[i] = oldLog[i + 1];
            log[9] = text;

            MyAPIGateway.Utilities.GetObjectiveLine().Objectives[0] = string.Join("\r\n", log);
            lineCount++;
        }

        public void Write(string text)
        {
            _cache.Append(text);
        }

        internal void Close()
        {
            if (_cache.Length > 0)
                _writer.WriteLine(_cache);

            _writer.Flush();
            _writer.Close();
        }
    }
}

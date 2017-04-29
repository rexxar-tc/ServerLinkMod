using System.Timers;
using VRage.Game;

namespace ServerLinkMod
{
    public class ServerItem
    {
        public readonly int Index;
        public readonly string IP;
        public Timer BattleTimer;
        public int CurrentUsers;
        public Timer JoinTimer;
        public bool MatchRunning;

        public ServerItem(int index, string ip)
        {
            Index = index;
            IP = ip;
            JoinTimer = new Timer(Settings.Instance.JoinTime * 60 * 1000);
            JoinTimer.Elapsed += JoinTimer_Elapsed;
            JoinTimer.AutoReset = false;

            BattleTimer = new Timer(Settings.Instance.BattleTime * 60 * 1000);
            BattleTimer.Elapsed += BattleTimer_Elapsed;
            BattleTimer.AutoReset = false;
        }

        public bool CanJoin
        {
            get { return !MatchRunning && CurrentUsers < Settings.Instance.MaxPlayerCount; }
        }

        public void Join(ulong steamId)
        {
            if (CanJoin)
                Communication.RedirectClient(steamId, IP);
            if (CurrentUsers == 0)
                Start();
            CurrentUsers++;
        }

        public void Reset()
        {
            JoinTimer.Stop();
            BattleTimer.Stop();
            MatchRunning = false;
            CurrentUsers = 0;
        }

        public void Start()
        {
            Communication.SendNotification(0, $"A match is being started on server {Index + 1}! You can join the match in the next {Settings.Instance.JoinTime} minutes!", MyFontEnum.Blue, 10000);
            JoinTimer.Start();
        }

        private void BattleTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            CurrentUsers = 0;
            MatchRunning = false;
            Communication.SendServerChat(0, $"The match on server {Index + 1} has ended, and it is now ready to start a new match!");
        }

        private void JoinTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            Communication.SendServerChat(0, $"Battle has begun on server {Index + 1} and is now closed to new players. Good luck!");
            MatchRunning = true;
            BattleTimer.Start();
        }
    }
}

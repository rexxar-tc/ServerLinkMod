using System;
using System.Collections.Generic;

/*
 * This was originally written by Jimmacle, then updated by me to include 
 * SteamID so we can receive concurrently from many players.
 */

namespace ServerLinkMod
{
    public class SegmentedReceiver
    {
        private const int PACKET_SIZE = 4090;
        private const int META_SIZE = sizeof(int) * 2 + sizeof(ulong);
        private const int DATA_LENGTH = PACKET_SIZE - META_SIZE;
        private readonly Dictionary<int, PartialMessage> messages = new Dictionary<int, PartialMessage>();

        public ulong SteamId;

        public SegmentedReceiver(ulong steamId)
        {
            SteamId = steamId;
        }

        /// <summary>
        ///     Segments a byte array.
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public List<byte[]> Segment(byte[] message)
        {
            byte[] steamId = BitConverter.GetBytes(SteamId);
            byte[] hash = BitConverter.GetBytes(message.GetHashCode());
            var packets = new List<byte[]>();
            var msgIndex = 0;

            int packetId = message.Length / DATA_LENGTH;

            while (packetId >= 0)
            {
                byte[] id = BitConverter.GetBytes(packetId);
                byte[] segment;

                if (message.Length - msgIndex > DATA_LENGTH)
                    segment = new byte[PACKET_SIZE];
                else
                    segment = new byte[META_SIZE + message.Length - msgIndex];

                //Copy packet "header" data.
                Array.Copy(steamId, segment, steamId.Length);
                Array.Copy(hash, 0, segment, steamId.Length, hash.Length);
                Array.Copy(id, 0, segment, hash.Length + steamId.Length, id.Length);

                //Copy segment of original message.
                Array.Copy(message, msgIndex, segment, META_SIZE, segment.Length - META_SIZE);

                packets.Add(segment);
                msgIndex += DATA_LENGTH;
                packetId--;
            }

            return packets;
        }

        /// <summary>
        ///     Reassembles a segmented byte array.
        /// </summary>
        /// <param name="packet">Array segment.</param>
        /// <param name="message">Full array, null if incomplete.</param>
        /// <returns>Message fully desegmented, "message" is assigned.</returns>
        public byte[] Desegment(byte[] packet)
        {
            ulong steamId = BitConverter.ToUInt64(packet, 0);

            if (steamId != SteamId)
                return null;

            int hash = BitConverter.ToInt32(packet, sizeof(ulong));
            int packetId = BitConverter.ToInt32(packet, sizeof(int) + sizeof(ulong));
            var dataBytes = new byte[packet.Length - META_SIZE];
            Array.Copy(packet, META_SIZE, dataBytes, 0, packet.Length - META_SIZE);

            if (!messages.ContainsKey(hash))
            {
                if (packetId == 0)
                    return dataBytes;
                messages.Add(hash, new PartialMessage(packetId));
            }

            PartialMessage message = messages[hash];
            message.WritePart(packetId, dataBytes);

            if (message.IsComplete)
            {
                messages.Remove(hash);
                return message.Data;
            }

            return null;
        }

        private class PartialMessage
        {
            private readonly int MaxId;
            private readonly HashSet<int> receivedPackets = new HashSet<int>();
            public byte[] Data;

            public PartialMessage(int startId)
            {
                MaxId = startId;
                Data = new byte[0];
            }

            public bool IsComplete
            {
                get { return receivedPackets.Count == MaxId + 1; }
            }

            public void WritePart(int id, byte[] data)
            {
                int index = MaxId - id;
                int requiredLength = index * DATA_LENGTH + data.Length;

                if (Data.Length < requiredLength)
                    Array.Resize(ref Data, requiredLength);

                Array.Copy(data, 0, Data, index * DATA_LENGTH, data.Length);
                receivedPackets.Add(id);
            }
        }
    }
}

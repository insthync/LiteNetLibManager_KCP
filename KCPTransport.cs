using System;
using System.Collections.Generic;
using System.Linq;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LiteNetLib;
using LiteNetLib.Utils;
using LiteNetLibManager;

namespace KCPTransportLayer
{
    public class KCPTransport : ITransport
    {
        public uint iconv;
        public KCPSetting clientSetting;
        public KCPSetting serverSetting;
        private KCPPeer clientPeer;
        private KCPPeer serverPeer;
        private readonly Dictionary<long, IPEndPoint> peerEndpointsById;
        private readonly Dictionary<IPEndPoint, long> peerIdsByEndpoint;
        private long connectionIdCounter = 0;

        public KCPTransport()
        {
            peerEndpointsById = new Dictionary<long, IPEndPoint>();
            peerIdsByEndpoint = new Dictionary<IPEndPoint, long>();
        }
        
        public bool ClientReceive(out TransportEventData eventData)
        {
            eventData = default(TransportEventData);
            if (clientPeer == null)
                return false;

            clientPeer.Update(DateTime.UtcNow);
            clientPeer.Recv();

            if (clientPeer.eventQueue.Count == 0)
                return false;

            eventData = clientPeer.eventQueue.Dequeue();
            switch (eventData.type)
            {
                case ENetworkEvent.DisconnectEvent:
                    // Disconnect from server
                    StopClient();
                    break;
            }

            return true;
        }

        public bool ClientSend(DeliveryMethod deliveryMethod, NetDataWriter writer)
        {
            if (IsClientStarted())
            {
                clientPeer.SendData(writer.Data);
                return true;
            }
            return false;
        }

        public void Destroy()
        {
            StopClient();
            StopServer();
        }

        public int GetServerPeersCount()
        {
            return peerIdsByEndpoint.Count;
        }

        public bool IsClientStarted()
        {
            return clientPeer != null;
        }

        public bool IsServerStarted()
        {
            return serverPeer != null;
        }

        public bool ServerDisconnect(long connectionId)
        {
            if (IsServerStarted() && peerEndpointsById.ContainsKey(connectionId))
            {
                IPEndPoint endPoint = peerEndpointsById[connectionId];
                // Send to target peer
                serverPeer.remoteEndPoint = endPoint;
                serverPeer.SendDisconnect();
                // Remove from dictionaries
                peerIdsByEndpoint.Remove(endPoint);
                peerEndpointsById.Remove(connectionId);
                return true;
            }
            return false;
        }

        public bool ServerReceive(out TransportEventData eventData)
        {
            eventData = default(TransportEventData);
            if (serverPeer == null)
                return false;

            serverPeer.Update(DateTime.UtcNow);
            serverPeer.Recv();
            
            if (serverPeer.eventQueue.Count == 0)
                return false;

            eventData = serverPeer.eventQueue.Dequeue();
            switch (eventData.type)
            {
                case ENetworkEvent.ConnectEvent:
                    if (!peerIdsByEndpoint.ContainsKey(eventData.endPoint))
                    {
                        long newConnectionId = ++connectionIdCounter;
                        peerIdsByEndpoint[eventData.endPoint] = newConnectionId;
                        peerEndpointsById[newConnectionId] = eventData.endPoint;
                        // Set event data
                        eventData.connectionId = newConnectionId;
                    }
                    break;
                case ENetworkEvent.DataEvent:
                    eventData.connectionId = peerIdsByEndpoint[eventData.endPoint];
                    break;
                case ENetworkEvent.DisconnectEvent:
                    if (peerIdsByEndpoint.ContainsKey(eventData.endPoint))
                    {
                        // Set event data
                        eventData.connectionId = peerIdsByEndpoint[eventData.endPoint];
                        // Remove from dictionaries
                        peerEndpointsById.Remove(eventData.connectionId);
                        peerIdsByEndpoint.Remove(eventData.endPoint);
                    }
                    break;
            }

            return true;
        }

        public bool ServerSend(long connectionId, DeliveryMethod deliveryMethod, NetDataWriter writer)
        {
            if (IsServerStarted() && peerEndpointsById.ContainsKey(connectionId))
            {
                serverPeer.remoteEndPoint = peerEndpointsById[connectionId];
                serverPeer.SendData(writer.Data);
                return true;
            }
            return false;
        }

        public bool StartClient(string connectKey, string address, int port)
        {
            clientPeer = new KCPPeer("CLIENT", iconv, clientSetting);
            clientPeer.Start();
            return clientPeer.Connect(address, port);
        }

        public bool StartServer(string connectKey, int port, int maxConnections)
        {
            connectionIdCounter = 0;
            peerEndpointsById.Clear();
            peerIdsByEndpoint.Clear();
            serverPeer = new KCPPeer("SERVER", iconv, serverSetting);
            serverPeer.Start(port);
            return true;
        }

        public void StopClient()
        {
            if (clientPeer != null)
            {
                clientPeer.SendDisconnect();
                clientPeer.Stop();
                clientPeer = null;
            }
        }

        public void StopServer()
        {
            if (serverPeer != null)
            {
                List<long> connectionIds = new List<long>(peerIdsByEndpoint.Values);
                foreach (long connectionId in connectionIds)
                {
                    ServerDisconnect(connectionId);
                }
                serverPeer.Stop();
                serverPeer = null;
            }
            peerEndpointsById.Clear();
            peerIdsByEndpoint.Clear();
        }
    }
}

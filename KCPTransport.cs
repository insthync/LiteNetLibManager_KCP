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
        private readonly Queue<TransportEventData> clientEventQueue;
        private readonly Queue<TransportEventData> serverEventQueue;
        private IPEndPoint tempCastedPeerEndPoint;
        private long connectionIdCounter = 0;

        public KCPTransport()
        {
            peerEndpointsById = new Dictionary<long, IPEndPoint>();
            peerIdsByEndpoint = new Dictionary<IPEndPoint, long>();
            clientEventQueue = new Queue<TransportEventData>();
            serverEventQueue = new Queue<TransportEventData>();
        }
        
        public bool ClientReceive(out TransportEventData eventData)
        {
            eventData = default(TransportEventData);
            if (clientPeer == null)
                return false;

            clientPeer.Update(DateTime.UtcNow);
            clientPeer.Recv();

            if (clientEventQueue.Count == 0)
                return false;

            eventData = clientEventQueue.Dequeue();

            return true;
        }

        public bool ClientSend(DeliveryMethod deliveryMethod, NetDataWriter writer)
        {
            if (IsClientStarted())
            {
                SendData(clientPeer, writer.Data);
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
                serverPeer.Send(new byte[] { (byte)ENetworkEvent.DisconnectEvent });
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
            
            if (serverEventQueue.Count == 0)
                return false;

            eventData = serverEventQueue.Dequeue();

            return true;
        }

        public bool ServerSend(long connectionId, DeliveryMethod deliveryMethod, NetDataWriter writer)
        {
            if (IsServerStarted() && peerEndpointsById.ContainsKey(connectionId))
            {
                serverPeer.remoteEndPoint = peerEndpointsById[connectionId];
                SendData(serverPeer, writer.Data);
                return true;
            }
            return false;
        }

        public bool StartClient(string connectKey, string address, int port)
        {
            clientPeer = new KCPPeer("CLIENT", iconv, clientSetting, OnClientReceive);
            clientPeer.Start();
            return clientPeer.Connect(address, port);
        }

        private void OnClientReceive(byte[] buffer, int length, EndPoint endPoint)
        {
            if (length <= 0)
                return;

            tempCastedPeerEndPoint = (IPEndPoint)endPoint;
            TransportEventData eventData = default(TransportEventData);
            eventData.type = (ENetworkEvent)buffer[0];
            switch (eventData.type)
            {
                case ENetworkEvent.DataEvent:
                    // Read data
                    byte[] data = new byte[length - 1];
                    Buffer.BlockCopy(buffer, 1, data, 0, length - 1);
                    eventData.reader = new NetDataReader(data);
                    clientEventQueue.Enqueue(eventData);
                    break;
                case ENetworkEvent.DisconnectEvent:
                    // Disconnect from server
                    StopClient();
                    clientEventQueue.Enqueue(eventData);
                    break;
            }
        }

        public bool StartServer(string connectKey, int port, int maxConnections)
        {
            connectionIdCounter = 0;
            serverPeer = new KCPPeer("SERVER", iconv, serverSetting, OnServerReceive);
            serverPeer.Start(port);
            return true;
        }

        private void OnServerReceive(byte[] buffer, int length, EndPoint endPoint)
        {
            if (length <= 0)
                return;

            tempCastedPeerEndPoint = (IPEndPoint)endPoint;
            TransportEventData eventData = default(TransportEventData);
            eventData.type = (ENetworkEvent)buffer[0];
            switch (eventData.type)
            {
                case ENetworkEvent.ConnectEvent:
                    if (!peerIdsByEndpoint.ContainsKey(tempCastedPeerEndPoint))
                    {
                        long newConnectionId = ++connectionIdCounter;
                        peerIdsByEndpoint[tempCastedPeerEndPoint] = newConnectionId;
                        peerEndpointsById[newConnectionId] = tempCastedPeerEndPoint;
                        // Set event data
                        eventData.connectionId = newConnectionId;
                        eventData.endPoint = tempCastedPeerEndPoint;
                        serverEventQueue.Enqueue(eventData);
                    }
                    break;
                case ENetworkEvent.DataEvent:
                    // Read data
                    byte[] data = new byte[length - 1];
                    Buffer.BlockCopy(buffer, 1, data, 0, length - 1);
                    eventData.reader = new NetDataReader(data);
                    eventData.connectionId = peerIdsByEndpoint[tempCastedPeerEndPoint];
                    eventData.endPoint = tempCastedPeerEndPoint;
                    serverEventQueue.Enqueue(eventData);
                    break;
                case ENetworkEvent.DisconnectEvent:
                    if (peerIdsByEndpoint.ContainsKey(tempCastedPeerEndPoint))
                    {
                        long connectionId = peerIdsByEndpoint[tempCastedPeerEndPoint];
                        // Set event data
                        eventData.connectionId = connectionId;
                        eventData.endPoint = tempCastedPeerEndPoint;
                        // Remove from dictionaries
                        peerEndpointsById.Remove(connectionId);
                        peerIdsByEndpoint.Remove(tempCastedPeerEndPoint);
                        serverEventQueue.Enqueue(eventData);
                    }
                    break;
            }
        }

        public void StopClient()
        {
            if (clientPeer != null)
            {
                clientPeer.Stop();
                clientPeer = null;
            }
        }

        public void StopServer()
        {
            if (serverPeer != null)
            {
                serverPeer.Stop();
                serverPeer = null;
            }
        }

        private void SendData(KCPPeer peer, byte[] sendingData)
        {
            byte[] sendBuffer = new byte[1 + sendingData.Length];
            sendBuffer[0] = (byte)ENetworkEvent.DataEvent;
            Buffer.BlockCopy(sendingData, 0, sendBuffer, 1, sendingData.Length);
            peer.Send(sendBuffer);
        }
    }
}

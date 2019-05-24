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
        private EndPoint tempPeerEndPoint = new IPEndPoint(IPAddress.Any, 0);
        private IPEndPoint tempCastedPeerEndPoint;
        private byte[] clientReceiveBuffer = new byte[1024 * 32];
        private byte[] serverReceiveBuffer = new byte[1024 * 32];
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

            int bufferLength = clientPeer.Recv(ref clientReceiveBuffer, ref tempPeerEndPoint);
            if (bufferLength == 0)
                return false;

            if (bufferLength > 0)
            {
                tempCastedPeerEndPoint = (IPEndPoint)tempPeerEndPoint;
                eventData.type = (ENetworkEvent)clientReceiveBuffer[0];
                switch (eventData.type)
                {
                    case ENetworkEvent.DataEvent:
                        // Read data
                        byte[] data = new byte[bufferLength - 1];
                        Buffer.BlockCopy(clientReceiveBuffer, 1, data, 0, bufferLength - 1);
                        eventData.reader = new NetDataReader(data);
                        return true;
                    case ENetworkEvent.DisconnectEvent:
                        // Disconnect from server
                        StopClient();
                        return true;
                }
            }

            return false;
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
            
            int bufferLength = serverPeer.Recv(ref serverReceiveBuffer, ref tempPeerEndPoint);
            if (bufferLength == 0)
                return false;

            if (bufferLength > 0)
            {
                tempCastedPeerEndPoint = (IPEndPoint)tempPeerEndPoint;
                KCPPeer tempPeer;
                eventData.type = (ENetworkEvent)serverReceiveBuffer[0];
                switch (eventData.type)
                {
                    case ENetworkEvent.ConnectEvent:
                        if (peerIdsByEndpoint.ContainsKey(tempCastedPeerEndPoint))
                        {
                            eventData.type = ENetworkEvent.DataEvent;
                            return false;
                        }
                        long newConnectionId = ++connectionIdCounter;
                        tempPeer = new KCPPeer("PEER_" + newConnectionId, iconv, serverSetting, newConnectionId);
                        tempPeer.Start();
                        tempPeer.Connect(tempCastedPeerEndPoint);
                        peerIdsByEndpoint[tempCastedPeerEndPoint] = newConnectionId;
                        peerEndpointsById[newConnectionId] = tempCastedPeerEndPoint;
                        // Set event data
                        eventData.connectionId = newConnectionId;
                        eventData.endPoint = tempCastedPeerEndPoint;
                        return true;
                    case ENetworkEvent.DataEvent:
                        if (!peerIdsByEndpoint.ContainsKey(tempCastedPeerEndPoint))
                        {
                            eventData.type = ENetworkEvent.DataEvent;
                            return false;
                        }
                        // Read data
                        byte[] data = new byte[bufferLength - 1];
                        Buffer.BlockCopy(serverReceiveBuffer, 1, data, 0, bufferLength - 1);
                        eventData.reader = new NetDataReader(data);
                        eventData.connectionId = peerIdsByEndpoint[tempCastedPeerEndPoint];
                        eventData.endPoint = tempCastedPeerEndPoint;
                        return true;
                    case ENetworkEvent.DisconnectEvent:
                        if (!peerIdsByEndpoint.ContainsKey(tempCastedPeerEndPoint))
                        {
                            eventData.type = ENetworkEvent.DataEvent;
                            return false;
                        }
                        long connectionId = peerIdsByEndpoint[tempCastedPeerEndPoint];
                        // Set event data
                        eventData.connectionId = connectionId;
                        eventData.endPoint = tempCastedPeerEndPoint;
                        // Remove from dictionaries
                        peerEndpointsById.Remove(connectionId);
                        peerIdsByEndpoint.Remove(tempCastedPeerEndPoint);
                        return true;
                }
            }

            return false;
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
            clientPeer = new KCPPeer("CLIENT", iconv, clientSetting, 0);
            clientPeer.Start();
            return clientPeer.Connect(address, port);
        }

        public bool StartServer(string connectKey, int port, int maxConnections)
        {
            connectionIdCounter = 0;
            serverPeer = new KCPPeer("SERVER", iconv, serverSetting, 0);
            serverPeer.Start(port);
            return true;
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

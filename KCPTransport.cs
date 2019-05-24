using System;
using System.Collections.Generic;
using System.Linq;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Net.Sockets.Kcp;
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
        private KCPClientHandle clientHandle;
        private KCPServerHandle serverHandle;
        private Kcp clientKcp;
        private Kcp serverKcp;
        private Socket clientSocket;
        private Socket serverSocket;
        private readonly Dictionary<long, IPEndPoint> peerEndpointsById;
        private readonly Dictionary<IPEndPoint, long> peerIdsByEndpoint;
        private EndPoint tempPeerEndPoint = new IPEndPoint(IPAddress.Any, 0);
        private IPEndPoint tempCastedPeerEndPoint;
        private readonly byte[] clientReceiveBuffer = new byte[1024 * 32];
        private readonly byte[] serverReceiveBuffer = new byte[1024 * 32];
        private long connectionIdCounter = 0;

        public KCPTransport()
        {
            peerEndpointsById = new Dictionary<long, IPEndPoint>();
            peerIdsByEndpoint = new Dictionary<IPEndPoint, long>();
        }

        private Kcp CreateKcp(KCPSetting setting, IKcpCallback callback)
        {
            Kcp kcp = new Kcp(iconv, callback);
            kcp.NoDelay(setting.GetNoDelay(), setting.interval, setting.GetFastResend(), setting.GetCongestionControl());
            kcp.WndSize(setting.sendWindowSize, setting.receiveWindowSize);
            kcp.SetMtu(setting.mtu);
            return kcp;
        }

        public bool ClientReceive(out TransportEventData eventData)
        {
            eventData = default(TransportEventData);
            if (clientSocket == null)
                return false;
            
            clientKcp.Update(DateTime.UtcNow);

            if (!clientSocket.Poll(0, SelectMode.SelectRead))
            {
                // No data
                return false;
            }

            int bufferLength;
            bufferLength = clientSocket.Receive(clientReceiveBuffer);

            clientKcp.Input(new Span<byte>(clientReceiveBuffer, 0, bufferLength));

            while ((bufferLength = clientKcp.PeekSize()) > 0)
            {
                byte[] buffer = new byte[bufferLength];
                if (clientKcp.Recv(buffer) >= 0)
                {
                    eventData.type = (ENetworkEvent)buffer[0];
                    switch (eventData.type)
                    {
                        case ENetworkEvent.DataEvent:
                            // Read data
                            byte[] data = new byte[bufferLength - 1];
                            Buffer.BlockCopy(buffer, 1, data, 0, bufferLength - 1);
                            eventData.reader = new NetDataReader(data);
                            return true;
                        case ENetworkEvent.DisconnectEvent:
                            // Disconnect from server
                            StopClient();
                            return true;
                    }
                }
            }

            return false;
        }

        public bool ClientSend(DeliveryMethod deliveryMethod, NetDataWriter writer)
        {
            if (IsClientStarted())
            {
                SendData(clientKcp, writer.Data);
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
            return clientSocket != null && clientSocket.Connected;
        }

        public bool IsServerStarted()
        {
            return serverSocket != null;
        }

        public bool ServerDisconnect(long connectionId)
        {
            if (IsServerStarted() && peerEndpointsById.ContainsKey(connectionId))
            {
                // Send to target peer
                serverHandle.sendingEndPoint = peerEndpointsById[connectionId];
                serverKcp.Send(new byte[] { (byte)ENetworkEvent.DisconnectEvent });
                // Remove from dictionaries
                peerIdsByEndpoint.Remove(peerEndpointsById[connectionId]);
                peerEndpointsById.Remove(connectionId);
                return true;
            }
            return false;
        }

        public bool ServerReceive(out TransportEventData eventData)
        {
            eventData = default(TransportEventData);
            if (serverSocket == null)
                return false;

            serverKcp.Update(DateTime.UtcNow);

            if (!serverSocket.Poll(0, SelectMode.SelectRead))
            {
                // No data
                return false;
            }
            
            int bufferLength;
            bufferLength = serverSocket.ReceiveFrom(serverReceiveBuffer, ref tempPeerEndPoint);
            tempCastedPeerEndPoint = (IPEndPoint)tempPeerEndPoint;

            serverKcp.Input(new Span<byte>(serverReceiveBuffer, 0, bufferLength));

            while ((bufferLength = serverKcp.PeekSize()) > 0)
            {
                byte[] buffer = new byte[bufferLength];
                if (serverKcp.Recv(buffer) >= 0)
                {
                    eventData.type = (ENetworkEvent)buffer[0];
                    switch (eventData.type)
                    {
                        case ENetworkEvent.ConnectEvent:
                            connectionIdCounter++;
                            eventData.connectionId = connectionIdCounter;
                            eventData.endPoint = tempCastedPeerEndPoint;
                            peerIdsByEndpoint[tempCastedPeerEndPoint] = connectionIdCounter;
                            peerEndpointsById[connectionIdCounter] = tempCastedPeerEndPoint;
                            return true;
                        case ENetworkEvent.DataEvent:
                            // Read data
                            byte[] data = new byte[bufferLength - 1];
                            Buffer.BlockCopy(buffer, 1, data, 0, bufferLength - 1);
                            eventData.reader = new NetDataReader(data);
                            eventData.connectionId = peerIdsByEndpoint[tempCastedPeerEndPoint];
                            eventData.endPoint = tempCastedPeerEndPoint;
                            return true;
                        case ENetworkEvent.DisconnectEvent:
                            eventData.connectionId = peerIdsByEndpoint[tempCastedPeerEndPoint];
                            eventData.endPoint = tempCastedPeerEndPoint;
                            peerEndpointsById.Remove(peerIdsByEndpoint[tempCastedPeerEndPoint]);
                            peerIdsByEndpoint.Remove(tempCastedPeerEndPoint);
                            return true;
                    }
                }
            }
            return false;
        }

        public bool ServerSend(long connectionId, DeliveryMethod deliveryMethod, NetDataWriter writer)
        {
            if (IsServerStarted() && peerEndpointsById.ContainsKey(connectionId))
            {
                serverHandle.sendingEndPoint = peerEndpointsById[connectionId];
                SendData(serverKcp, writer.Data);
                return true;
            }
            return false;
        }

        public bool StartClient(string connectKey, string address, int port)
        {
            IPAddress[] ipAddresses = Dns.GetHostAddresses(address);
            if (ipAddresses.Length == 0)
                return false;

            int indexOfAddress = -1;
            for (int i = 0; i < ipAddresses.Length; ++i)
            {
                IPAddress ipAddress = ipAddresses[i];
                if (ipAddresses[i].AddressFamily == AddressFamily.InterNetwork)
                {
                    indexOfAddress = i;
                    break;
                }
            }

            if (indexOfAddress < 0)
                return false;

            clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            clientSocket.Bind(new IPEndPoint(IPAddress.Any, 0));

            clientHandle = new KCPClientHandle(clientSocket, new IPEndPoint(ipAddresses[indexOfAddress], port));
            clientKcp = CreateKcp(clientSetting, clientHandle);
            clientKcp.Send(new byte[] { (byte)ENetworkEvent.ConnectEvent });

            return true;
        }

        public bool StartServer(string connectKey, int port, int maxConnections)
        {
            serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            serverSocket.Bind(new IPEndPoint(IPAddress.Any, port));

            serverHandle = new KCPServerHandle(serverSocket);
            serverKcp = CreateKcp(serverSetting, serverHandle);

            connectionIdCounter = 0;

            return true;
        }

        public void StopClient()
        {
            if (clientSocket != null)
            {
                clientSocket.Close();
                clientSocket.Dispose();
                clientSocket = null;
            }

            if (clientKcp != null)
            {
                clientKcp.Dispose();
                clientKcp = null;
            }
        }

        public void StopServer()
        {
            if (serverSocket != null)
            {
                serverSocket.Close();
                serverSocket.Dispose();
                serverSocket = null;
            }

            if (serverKcp != null)
            {
                serverKcp.Dispose();
                serverKcp = null;
            }
        }

        private void SendData(Kcp kcp, byte[] sendingData)
        {

            byte[] sendBuffer = new byte[1 + sendingData.Length];
            sendBuffer[0] = (byte)ENetworkEvent.DataEvent;
            Buffer.BlockCopy(sendingData, 0, sendBuffer, 1, sendingData.Length);
            kcp.Send(sendBuffer);
        }
    }
}

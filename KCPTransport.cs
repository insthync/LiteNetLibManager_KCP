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
        public KCPSetting clientSetting;
        public KCPSetting serverSetting;
        private KCPPeer clientPeer;
        private KCPPeer serverPeer;
        private long connectionIdCounter = 0;

        public KCPTransport()
        {

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
                case ENetworkEvent.ErrorEvent:
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
                clientPeer.SendData(writer.Data, writer.Length);
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
            if (serverPeer == null)
                return 0;
            return serverPeer.kcpHandles.Count;
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
            if (IsServerStarted() && serverPeer.kcpHandles.ContainsKey(connectionId))
            {
                serverPeer.Disconnect(connectionId);
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

            return true;
        }

        public bool ServerSend(long connectionId, DeliveryMethod deliveryMethod, NetDataWriter writer)
        {
            if (IsServerStarted())
            {
                serverPeer.SendData(connectionId, writer.Data, writer.Length);
                return true;
            }
            return false;
        }

        public bool StartClient(string connectKey, string address, int port)
        {
            clientPeer = new KCPPeer("CLIENT", clientSetting, serverSetting);
            clientPeer.Start();
            return clientPeer.Connect(address, port);
        }

        public bool StartServer(string connectKey, int port, int maxConnections)
        {
            connectionIdCounter = 0;
            serverPeer = new KCPPeer("SERVER", clientSetting, serverSetting);
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
    }
}

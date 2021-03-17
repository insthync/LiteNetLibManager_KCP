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
        public bool IsClientStarted
        {
            get { return clientPeer != null; }
        }
        public bool IsServerStarted
        {
            get { return serverPeer != null; }
        }
        public int ServerPeersCount
        {
            get
            {
                if (serverPeer == null)
                    return 0;
                return serverPeer.kcpHandles.Count;
            }
        }
        public int ServerMaxConnections { get; private set; }

        public bool ClientReceive(out TransportEventData eventData)
        {
            eventData = default(TransportEventData);
            if (clientPeer == null)
                return false;

            clientPeer.Recv();

            if (clientPeer.eventQueue.Count == 0)
                return false;

            if (!clientPeer.eventQueue.TryDequeue(out eventData))
                return false;

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

        public bool ClientSend(byte dataChannel, DeliveryMethod deliveryMethod, NetDataWriter writer)
        {
            if (IsClientStarted)
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

        public bool ServerDisconnect(long connectionId)
        {
            if (IsServerStarted && serverPeer.kcpHandles.ContainsKey(connectionId))
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

            serverPeer.Recv();
            
            if (serverPeer.eventQueue.Count == 0)
                return false;

            return serverPeer.eventQueue.TryDequeue(out eventData);
        }

        public bool ServerSend(long connectionId, byte dataChannel, DeliveryMethod deliveryMethod, NetDataWriter writer)
        {
            if (IsServerStarted)
            {
                serverPeer.SendData(connectionId, writer.Data, writer.Length);
                return true;
            }
            return false;
        }

        public bool StartClient(string address, int port)
        {
            clientPeer = new KCPPeer("CLIENT", clientSetting, serverSetting);
            clientPeer.Start();
            return clientPeer.Connect(address, port);
        }

        public bool StartServer(int port, int maxConnections)
        {
            ServerMaxConnections = maxConnections;
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

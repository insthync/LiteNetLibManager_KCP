using LiteNetLib;
using LiteNetLib.Utils;
using LiteNetLibManager;

namespace KCPTransportLayer
{
    public class KCPTransport : ITransport
    {
        public KCPSetting clientSetting;
        public KCPSetting serverSetting;
        private KCPPeer _clientPeer;
        private KCPPeer _serverPeer;
        public bool IsClientStarted
        {
            get { return _clientPeer != null; }
        }
        public bool IsServerStarted
        {
            get { return _serverPeer != null; }
        }
        public int ServerPeersCount
        {
            get
            {
                if (_serverPeer == null)
                    return 0;
                return _serverPeer.kcpHandles.Count;
            }
        }
        public int ServerMaxConnections { get; private set; }

        public bool HasImplementedPing
        {
            get { return false; }
        }

        public bool ClientReceive(out TransportEventData eventData)
        {
            eventData = default(TransportEventData);
            if (_clientPeer == null)
                return false;

            _clientPeer.Recv();

            if (_clientPeer.eventQueue.Count == 0)
                return false;

            if (!_clientPeer.eventQueue.TryDequeue(out eventData))
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
                _clientPeer.SendData(writer.Data, writer.Length);
                return true;
            }
            return false;
        }

        public void Destroy()
        {
            StopClient();
            StopServer();
        }

        public long GetClientRtt()
        {
            throw new System.NotImplementedException();
        }

        public long GetServerRtt(long connectionId)
        {
            throw new System.NotImplementedException();
        }

        public bool ServerDisconnect(long connectionId)
        {
            if (IsServerStarted && _serverPeer.kcpHandles.ContainsKey(connectionId))
            {
                _serverPeer.Disconnect(connectionId);
                return true;
            }
            return false;
        }

        public bool ServerReceive(out TransportEventData eventData)
        {
            eventData = default(TransportEventData);
            if (_serverPeer == null)
                return false;

            _serverPeer.Recv();
            
            if (_serverPeer.eventQueue.Count == 0)
                return false;

            return _serverPeer.eventQueue.TryDequeue(out eventData);
        }

        public bool ServerSend(long connectionId, byte dataChannel, DeliveryMethod deliveryMethod, NetDataWriter writer)
        {
            if (IsServerStarted)
            {
                _serverPeer.SendData(connectionId, writer.Data, writer.Length);
                return true;
            }
            return false;
        }

        public bool StartClient(string address, int port)
        {
            _clientPeer = new KCPPeer("CLIENT", clientSetting, serverSetting);
            _clientPeer.Start();
            return _clientPeer.Connect(address, port);
        }

        public bool StartServer(int port, int maxConnections)
        {
            ServerMaxConnections = maxConnections;
            _serverPeer = new KCPPeer("SERVER", clientSetting, serverSetting);
            _serverPeer.Start(port);
            return true;
        }

        public void StopClient()
        {
            if (_clientPeer != null)
            {
                _clientPeer.Stop();
                _clientPeer = null;
            }
        }

        public void StopServer()
        {
            if (_serverPeer != null)
            {
                _serverPeer.Stop();
                _serverPeer = null;
            }
        }
    }
}

using LiteNetLib.Utils;
using LiteNetLibManager;
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Net.Sockets.Kcp;
using System.Threading;
using UnityEngine;

namespace KCPTransportLayer
{
    public class KCPPeer
    {
        private static readonly NetDataWriter acceptWriter = new NetDataWriter();
        private static readonly NetDataWriter miscWriter = new NetDataWriter();
        private static readonly NetDataReader recvReader = new NetDataReader();
        private static uint ConnIdCounter = 1;
        private const int MaxPacketSize = 1024 * 1024;
        public string tag { get; private set; }
        private KCPSetting clientSetting;
        private KCPSetting serverSetting;
        private Socket connectSocket;
        private Socket dataSocket;
        private byte[] recvBuffer = new byte[MaxPacketSize];
        private uint clientConnId;
        private Thread updateThread;
        private Thread acceptThread;
        private bool updating;
        private bool accepting;
        public Queue<TransportEventData> eventQueue { get; private set; }
        public Dictionary<long, Socket> connections { get; private set; }
        public Dictionary<long, KCPHandle> kcpHandles { get; private set; }

        public KCPPeer(string tag, KCPSetting clientSetting, KCPSetting serverSetting)
        {
            this.tag = tag;
            this.clientSetting = clientSetting;
            this.serverSetting = serverSetting;
            eventQueue = new Queue<TransportEventData>();
            connections = new Dictionary<long, Socket>();
            kcpHandles = new Dictionary<long, KCPHandle>();
        }

        private KCPHandle CreateKcp(uint connectionId, KCPSetting setting)
        {
            KCPHandle handle = new KCPHandle(dataSocket, connectionId);
            handle.kcp.NoDelay(setting.noDelay, setting.interval, setting.resend, setting.noCongestion);
            handle.kcp.WndSize(setting.sendWindowSize, setting.receiveWindowSize);
            handle.kcp.SetMtu(setting.mtu);
            return handle;
        }

        public void Stop()
        {
            updating = false;
            accepting = false;

            if (dataSocket != null)
            {
                dataSocket.Close();
                dataSocket.Dispose();
                dataSocket = null;
            }

            if (connectSocket != null)
            {
                if (connectSocket.Connected)
                    connectSocket.Disconnect(false);
                connectSocket.Close();
                connectSocket.Dispose();
                connectSocket = null;
            }

            if (updateThread != null)
            {
                updateThread.Abort();
                updateThread.Join();
                updateThread = null;
            }

            if (acceptThread != null)
            {
                acceptThread.Abort();
                acceptThread.Join();
                acceptThread = null;
            }

            eventQueue.Clear();
            connections.Clear();
            kcpHandles.Clear();
        }

        public void Disconnect(long connectionId)
        {
            Socket disconnectSocket;
            if (connections.TryGetValue(connectionId, out disconnectSocket))
            {
                miscWriter.Reset();
                miscWriter.Put((byte)ENetworkEvent.DisconnectEvent);
                disconnectSocket.BeginSend(acceptWriter.Data, 0, acceptWriter.Length, SocketFlags.None, SendDisconnectedCallback, disconnectSocket);
            }
        }

        private void SendDisconnectedCallback(IAsyncResult result)
        {
            Socket disconnectSocket = (Socket)result.AsyncState;
            // TODO: Don't care about an error
            disconnectSocket.EndSend(result);
        }

        public void Start()
        {
            Start(0);
        }

        public void Start(int port)
        {
            SetupConnectSocket();

            // Listen for client if port more than 0
            if (port > 0)
            {
                connectSocket.Bind(new IPEndPoint(IPAddress.Any, port));
                connectSocket.Listen(100);

                accepting = true;
                acceptThread = new Thread(AcceptThreadFunction);
                acceptThread.IsBackground = true;
                acceptThread.Start();
            }

            // UDP socket always bind to any port
            SetupDataSocket();
            dataSocket.Bind(new IPEndPoint(IPAddress.Any, port));

            if (updateThread != null)
            {
                updateThread.Abort();
                updateThread = null;
            }

            updating = true;
            updateThread = new Thread(UpdateThreadFunction);
            updateThread.IsBackground = true;
            updateThread.Start();
        }

        private void AcceptThreadFunction()
        {
            try
            {
                Socket newClientSocket;
                uint newConnectionId;
                KCPHandle newKcpHandle;
                TransportEventData eventData;
                SocketError socketError;
                while (accepting)
                {
                    // Get the socket that handles the client request.
                    newClientSocket = connectSocket.Accept();

                    // Create new kcp for this client
                    newConnectionId = ConnIdCounter++;
                    newKcpHandle = CreateKcp(newConnectionId, serverSetting);

                    // Store kcp, socket to dictionaries
                    connections[newConnectionId] = newClientSocket;
                    kcpHandles[newConnectionId] = newKcpHandle;

                    // Store network event to queue
                    eventData = default(TransportEventData);
                    eventData.type = ENetworkEvent.ConnectEvent;
                    eventData.connectionId = newConnectionId;
                    eventData.endPoint = (IPEndPoint)newClientSocket.RemoteEndPoint;
                    eventQueue.Enqueue(eventData);

                    // Prepare connected message to send to client
                    acceptWriter.Reset();
                    acceptWriter.Put((byte)ENetworkEvent.ConnectEvent);
                    acceptWriter.Put(newConnectionId);
                    acceptWriter.Put(((IPEndPoint)dataSocket.LocalEndPoint).Port);

                    // Send connected message to client
                    if (newClientSocket.Send(acceptWriter.Data, 0, acceptWriter.Length, SocketFlags.None, out socketError) < 0)
                    {
                        // Error occurs
                        HandleError(newClientSocket.RemoteEndPoint, socketError);
                    }

                    Thread.Sleep(20);
                }
            }
            catch (ThreadAbortException)
            {
                // Happen when abort, do nothing
            }
            catch (Exception e)
            {
                // Another exception
                Debug.LogException(e);
            }
        }

        private void UpdateThreadFunction()
        {

            try
            {
                DateTime time;
                List<KCPHandle> updatingKcps = new List<KCPHandle>();
                List<long> connectionIds = new List<long>();
                TransportEventData eventData;
                while (updating)
                {
                    time = DateTime.UtcNow;
                    if (kcpHandles != null && kcpHandles.Count > 0)
                    {
                        updatingKcps.Clear();
                        updatingKcps.AddRange(kcpHandles.Values);
                        foreach (KCPHandle updatingKcp in updatingKcps)
                        {
                            updatingKcp.kcp.Update(time);
                        }
                    }
                    Thread.Sleep(20);
                    // Check disconnected connections
                    if (connections != null && connections.Count > 0)
                    {
                        connectionIds.Clear();
                        connectionIds.AddRange(connections.Keys);
                        foreach (long connectionId in connectionIds)
                        {
                            if (connections[connectionId].Connected &&
                                IsSocketConnected(connections[connectionId]))
                                continue;
                            connections[connectionId].Close();
                            connections[connectionId].Dispose();
                            kcpHandles[connectionId].Dispose();
                            connections.Remove(connectionId);
                            kcpHandles.Remove(connectionId);
                            // This event must enqueue at server only
                            eventData = default(TransportEventData);
                            eventData.type = ENetworkEvent.DisconnectEvent;
                            eventData.connectionId = connectionId;
                            eventQueue.Enqueue(eventData);
                        }
                    }
                    Thread.Sleep(20);
                }
            }
            catch (ThreadAbortException)
            {
                // Happen when abort, do nothing
            }
            catch (Exception e)
            {
                // Another exception
                Debug.LogException(e);
            }
        }

        private void SetupConnectSocket()
        {
            connectSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            connectSocket.NoDelay = true;
            connectSocket.Ttl = 255;
        }

        private void SetupDataSocket()
        {
            dataSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            dataSocket.ReceiveTimeout = 500;
            dataSocket.SendTimeout = 500;
            // Socket buffer size
            dataSocket.ReceiveBufferSize = MaxPacketSize;
            dataSocket.SendBufferSize = MaxPacketSize;

            try
            {
                dataSocket.ExclusiveAddressUse = false;
                dataSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            }
            catch
            {
                //Unity with IL2CPP throws an exception here, it doesn't matter in most cases so just ignore it
            }

            dataSocket.Ttl = 255;
            try { dataSocket.DontFragment = true; }
            catch (SocketException e)
            {
                Debug.LogException(e);
            }

            try { dataSocket.EnableBroadcast = true; }
            catch (SocketException e)
            {
                Debug.LogException(e);
            }
        }

        public bool Connect(string address, int port)
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

            return Connect(new IPEndPoint(ipAddresses[indexOfAddress], port));
        }

        public bool Connect(IPEndPoint remoteEndPoint)
        {
            // Cannot connect again if connected
            if (connectSocket.Connected)
                return false;
            
            connectSocket.Connect(remoteEndPoint);
            return connectSocket.Connected;
        }

        public int SendData(byte[] sendingData, int length)
        {
            if (connectSocket == null || !connectSocket.Connected)
                return -1;
            return SendData(clientConnId, sendingData, length);
        }

        public int SendData(long connectionId, byte[] sendingData, int length)
        {
            byte[] sendBuffer = new byte[1 + length];
            sendBuffer[0] = (byte)ENetworkEvent.DataEvent;
            Buffer.BlockCopy(sendingData, 0, sendBuffer, 1, length);
            return Send(connectionId, sendBuffer);
        }

        public int Send(long connectionId, byte[] data)
        {
            if (dataSocket == null || !kcpHandles.ContainsKey(connectionId))
                return -1;

            return kcpHandles[connectionId].kcp.Send(data);
        }

        public void Recv()
        {
            RecvConnection();
            RecvData();
        }

        private void RecvConnection()
        {
            if (connectSocket == null || 
                (!connectSocket.IsBound && !connectSocket.Connected))
                return;

            if (!connectSocket.Poll(0, SelectMode.SelectRead))
                return;

            EndPoint endPoint = new IPEndPoint(IPAddress.Any, 0);
            int recvLength;
            try
            {
                recvLength = connectSocket.Receive(recvBuffer, 0, recvBuffer.Length, SocketFlags.None);
                endPoint = connectSocket.RemoteEndPoint;
            }
            catch (SocketException ex)
            {
                HandleError(endPoint, ex.SocketErrorCode);
                return;
            }
            // Handle data directly for connect socket, not have to set kcp input (and read later)
            TransportEventData eventData = default(TransportEventData);
            eventData.endPoint = (IPEndPoint)endPoint;
            HandleRecvData(recvBuffer, recvLength, eventData);
        }

        private void RecvData()
        {
            if (dataSocket == null)
                return;

            if (!dataSocket.Poll(0, SelectMode.SelectRead))
                return;

            EndPoint endPoint = new IPEndPoint(IPAddress.Any, 0);
            int recvLength;
            try
            {
                recvLength = dataSocket.ReceiveFrom(recvBuffer, 0, recvBuffer.Length, SocketFlags.None, ref endPoint);
            }
            catch (SocketException ex)
            {
                HandleError(endPoint, ex.SocketErrorCode);
                return;
            }

            recvReader.Clear();
            recvReader.SetSource(recvBuffer, 0, recvLength);
            uint connectionId = recvReader.GetUInt();
            // Have to find which kcp send this data, then set its input
            KCPHandle kcpHandle = null;
            if (kcpHandles.TryGetValue(connectionId, out kcpHandle))
            {
                kcpHandle.remoteEndPoint = endPoint;
                kcpHandle.kcp.Input(new Span<byte>(recvBuffer, 0, recvLength));

                byte[] kcpData;
                while ((recvLength = kcpHandle.kcp.PeekSize()) > 0)
                {
                    kcpData = new byte[recvLength];
                    if (kcpHandle.kcp.Recv(kcpData) >= 0)
                    {
                        TransportEventData eventData = default(TransportEventData);
                        eventData.connectionId = connectionId;
                        eventData.endPoint = (IPEndPoint)endPoint;
                        HandleRecvData(kcpData, recvLength, eventData);
                    }
                }
            }
        }

        private void HandleRecvData(byte[] buffer, int length, TransportEventData eventData)
        {
            recvReader.Clear();
            recvReader.SetSource(buffer, 0, length);
            eventData.type = (ENetworkEvent)recvReader.GetByte();
            switch (eventData.type)
            {
                case ENetworkEvent.ConnectEvent:
                    // This must received at clients only, then create new kcp here
                    clientConnId = recvReader.GetUInt();
                    int remotePort = recvReader.GetInt();
                    eventData.connectionId = clientConnId;
                    eventQueue.Enqueue(eventData);
                    kcpHandles[clientConnId] = CreateKcp(clientConnId, clientSetting);
                    kcpHandles[clientConnId].remoteEndPoint = new IPEndPoint(eventData.endPoint.Address, remotePort);
                    break;
                case ENetworkEvent.DataEvent:
                    // Read remaining data
                    eventData.reader = new NetDataReader(recvReader.GetRemainingBytes());
                    eventQueue.Enqueue(eventData);
                    break;
                case ENetworkEvent.DisconnectEvent:
                    // This must received at clients only to force them to stop client
                    eventQueue.Enqueue(eventData);
                    break;
            }
        }

        private void HandleError(EndPoint endPoint, SocketError socketErrorCode)
        {
            switch (socketErrorCode)
            {
                case SocketError.Interrupted:
                case SocketError.NotSocket:
                case SocketError.ConnectionReset:
                case SocketError.MessageSize:
                case SocketError.TimedOut:
                    // Ignored error
                    break;
                default:
                    // Store error event to queue
                    TransportEventData eventData = default(TransportEventData);
                    eventData.type = ENetworkEvent.ErrorEvent;
                    eventData.endPoint = (IPEndPoint)endPoint;
                    eventData.socketError = socketErrorCode;
                    eventQueue.Enqueue(eventData);
                    break;
            }
        }

        public bool IsSocketConnected(Socket socket)
        {
            try
            {
                return !(socket.Poll(1, SelectMode.SelectRead) && socket.Available == 0);
            }
            catch (SocketException) { return false; }
        }
    }
}

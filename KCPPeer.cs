using LiteNetLibManager;
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Net.Sockets.Kcp;

namespace KCPTransportLayer
{
    public class KCPPeer : IKcpCallback
    {
        public string tag { get; private set; }
        public IPEndPoint remoteEndPoint { get; set; }
        private Socket socket;
        private Kcp kcp;
        private byte[] recvBuffer = new byte[1024 * 32];
        private Action<byte[], int, EndPoint> onReceive;

        public KCPPeer(string tag, uint iconv, KCPSetting setting, Action<byte[], int, EndPoint> onReceive)
        {
            this.tag = tag;
            this.onReceive = onReceive;
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            kcp = new Kcp(iconv, this);
            kcp.NoDelay(setting.noDelay, setting.interval, setting.resend, setting.nc);
            kcp.WndSize(setting.sendWindowSize, setting.receiveWindowSize);
            kcp.SetMtu(setting.mtu);
        }

        public void Stop()
        {
            if (socket != null)
            {
                socket.Close();
                socket.Dispose();
                socket = null;
            }

            if (kcp != null)
            {
                kcp.Dispose();
                kcp = null;
            }
        }

        public void Start()
        {
            Start(0);
        }

        public void Start(int port)
        {
            socket.Bind(new IPEndPoint(IPAddress.Any, port));
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
            this.remoteEndPoint = remoteEndPoint;
            return Send(new byte[] { (byte)ENetworkEvent.ConnectEvent }) >= 0;
        }

        public void Update(DateTime time)
        {
            if (kcp == null)
                return;

            kcp.Update(time);
        }

        public int Send(byte[] data)
        {
            if (socket == null)
                return -1;

            return kcp.Send(data);
        }

        public void Recv()
        {
            if (socket == null)
                return;

            if (!socket.Poll(0, SelectMode.SelectRead))
            {
                return;
            }

            EndPoint endPoint = new IPEndPoint(IPAddress.Any, 0);
            int recvLength;
            recvLength = socket.ReceiveFrom(recvBuffer, ref endPoint);

            kcp.Input(new Span<byte>(recvBuffer, 0, recvLength));

            byte[] data;
            while ((recvLength = kcp.PeekSize()) > 0)
            {
                data = new byte[recvLength];
                if (kcp.Recv(data) >= 0)
                {
                    onReceive(data, recvLength, endPoint);
                }
            }
        }

        public void Output(IMemoryOwner<byte> buffer, int avalidLength)
        {
            if (socket != null && remoteEndPoint != null)
            {
                socket.SendTo(buffer.Memory.ToArray(), avalidLength, SocketFlags.None, remoteEndPoint);
            }
        }

        public IMemoryOwner<byte> RentBuffer(int length)
        {
            return null;
        }
    }
}

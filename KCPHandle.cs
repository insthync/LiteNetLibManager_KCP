using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Net.Sockets.Kcp;
using LiteNetLib.Utils;

public class KCPHandle : IKcpCallback
{
    private static readonly NetDataWriter writer = new NetDataWriter();
    public Kcp kcp { get; private set; }
    public EndPoint remoteEndPoint;
    private Socket dataSocket;
    public KCPHandle(Socket dataSocket, uint iconv)
    {
        kcp = new Kcp(iconv, this);
        this.dataSocket = dataSocket;
    }

    public void Dispose()
    {
        kcp.Dispose();
    }

    public void Output(IMemoryOwner<byte> buffer, int avalidLength)
    {
        if (dataSocket != null && remoteEndPoint != null)
        {
            dataSocket.SendTo(buffer.Memory.ToArray(), avalidLength, SocketFlags.None, remoteEndPoint);
        }
    }

    public IMemoryOwner<byte> RentBuffer(int length)
    {
        return null;
    }
}

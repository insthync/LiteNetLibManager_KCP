using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Net.Sockets.Kcp;

public class KCPHandle : IKcpCallback
{
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

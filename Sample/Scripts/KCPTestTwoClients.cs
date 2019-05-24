using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LiteNetLib.Utils;
using LiteNetLibManager;
using KCPTransportLayer;

public class KCPTestTwoClients : MonoBehaviour
{
    // 1 server
    public KCPTransport server1;
    public KCPTransport server2;
    // 2 clients
    public KCPTransport client1;
    public KCPTransport client2;

    public uint iconv;
    public KCPSetting serverSetting;
    public KCPSetting clientSetting;

    private readonly NetDataWriter writer = new NetDataWriter();

    // Use this for initialization
    void Start()
    {
        server1 = new KCPTransport()
        {
            iconv = iconv,
            serverSetting = serverSetting,
            clientSetting = clientSetting,
        };
        server1.StartServer(string.Empty, 5555, 100);    // Max connections not affect anything for now

        server2 = new KCPTransport()
        {
            iconv = iconv,
            serverSetting = serverSetting,
            clientSetting = clientSetting,
        };
        server2.StartServer(string.Empty, 5556, 100);    // Max connections not affect anything for now

        client1 = new KCPTransport()
        {
            iconv = iconv,
            serverSetting = serverSetting,
            clientSetting = clientSetting,
        };
        client1.StartClient(string.Empty, "127.0.0.1", 5555);

        client2 = new KCPTransport()
        {
            iconv = iconv,
            serverSetting = serverSetting,
            clientSetting = clientSetting,
        };
        client2.StartClient(string.Empty, "127.0.0.1", 5556);
    }

    int clientSendCount1 = 0;
    int clientSendCount2 = 0;
    // Update is called once per frame
    void Update()
    {
        TransportEventData tempEventData;
        while (server1.ServerReceive(out tempEventData))
        {
            if (tempEventData.type != ENetworkEvent.DataEvent) continue;
            string data = tempEventData.reader.GetString();
            Debug.Log("Server1 receive " + data + " from " + tempEventData.endPoint);
            writer.Reset();
            writer.Put(data.ToLower());
            server1.ServerSend(tempEventData.connectionId, LiteNetLib.DeliveryMethod.ReliableOrdered, writer);
        }
        while (server2.ServerReceive(out tempEventData))
        {
            if (tempEventData.type != ENetworkEvent.DataEvent) continue;
            string data = tempEventData.reader.GetString();
            Debug.Log("Server2 receive " + data + " from " + tempEventData.endPoint);
            writer.Reset();
            writer.Put(data.ToLower());
            server2.ServerSend(tempEventData.connectionId, LiteNetLib.DeliveryMethod.ReliableOrdered, writer);
        }
        while (client1.ClientReceive(out tempEventData))
        {
            if (tempEventData.type != ENetworkEvent.DataEvent) continue;
            Debug.Log("Client 1 receive " + tempEventData.reader.GetString());
        }
        while (client2.ClientReceive(out tempEventData))
        {
            if (tempEventData.type != ENetworkEvent.DataEvent) continue;
            Debug.Log("Client 2 receive " + tempEventData.reader.GetString());
        }

        writer.Reset();
        writer.Put("`SEND FROM CLIENT1 = " + (++clientSendCount1) + "`");
        client1.ClientSend(LiteNetLib.DeliveryMethod.ReliableOrdered, writer);

        writer.Reset();
        writer.Put("`SEND FROM CLIENT2 = " + (++clientSendCount1) + "`");
        client2.ClientSend(LiteNetLib.DeliveryMethod.ReliableOrdered, writer);
    }
}

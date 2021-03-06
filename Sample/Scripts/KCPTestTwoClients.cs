﻿using System.Collections;
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
        StartCoroutine(StartRoutine());
    }

    IEnumerator StartRoutine()
    {
        server1 = new KCPTransport()
        {
            serverSetting = serverSetting,
            clientSetting = clientSetting,
        };
        server1.StartServer(5555, 100);    // Max connections not affect anything for now
        yield return 0;
        client1 = new KCPTransport()
        {
            serverSetting = serverSetting,
            clientSetting = clientSetting,
        };
        if (!client1.StartClient("127.0.0.1", 5555))
            Debug.LogError("`client1` Cannot connect to server");
        yield return 0;
        client2 = new KCPTransport()
        {
            serverSetting = serverSetting,
            clientSetting = clientSetting,
        };
        if (!client2.StartClient("127.0.0.1", 5555))
            Debug.LogError("`client2` Cannot connect to server");
    }

    int clientSendCount1 = 0;
    int clientSendCount2 = 0;
    // Update is called once per frame
    void Update()
    {
        if (server1 == null || client1 == null || client2 == null)
            return;

        TransportEventData tempEventData;
        while (server1.ServerReceive(out tempEventData))
        {
            if (tempEventData.type != ENetworkEvent.DataEvent) continue;
            string data = tempEventData.reader.GetString();
            Debug.Log("Server1 receive " + data + " from " + tempEventData.endPoint);
            writer.Reset();
            writer.Put(data.ToLower());
            server1.ServerSend(tempEventData.connectionId, 0, LiteNetLib.DeliveryMethod.ReliableOrdered, writer);
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
        client1.ClientSend(0, LiteNetLib.DeliveryMethod.ReliableOrdered, writer);

        writer.Reset();
        writer.Put("`SEND FROM CLIENT2 = " + (++clientSendCount2) + "`");
        client2.ClientSend(0, LiteNetLib.DeliveryMethod.ReliableOrdered, writer);
    }
}

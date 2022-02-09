using System;
using System.Collections.Generic;
using Google.Protobuf;
using Pb;
using UnityEngine;

namespace Net
{
    public enum ConnectionState : byte
    {
        NotConnected,
        Connecting,
        Connected,
    }

    public enum ConnectionEvent : byte
    {
        Connected,
        ConnectFailure, //Connecting期间失败
        ConnectedError, //Connected期间失败
        ManualDisconnect,
    }

    public class RequestData
    {
        public ResponseDelegate Callback;
        public float Timeout;
        public float Elapsed;

        public RequestData(ResponseDelegate callback, float timeout)
        {
            Callback = callback;
            Timeout = timeout;
            Elapsed = 0;
        }

        public bool Tick(float deltaTime)
        {
            Elapsed += deltaTime;
            return Elapsed >= Timeout;
        }
    }

    public delegate void OnConnectionStateChangeDelegate(ConnectionEvent connectionEvent, SocketErrorCode errorCode);

    public delegate void OnPushDelegate(Package package);

    public delegate void ResponseDelegate(bool success, Package package);

    public class TcpConnection
    {
        public ConnectionState State { private set; get; } = ConnectionState.NotConnected;

        private readonly ProtoPacker packer;
        private readonly Queue<Package> packageQueue = new Queue<Package>(8);
        private readonly Dictionary<uint, RequestData> requestDataDict = new Dictionary<uint, RequestData>(8);
        private readonly List<uint> toDeletePids = new List<uint>(8);
        private TcpClient client;
        private OnConnectionStateChangeDelegate onStateChangeHandler;
        private OnPushDelegate onPushHandler;
        private uint pid;


        public TcpConnection(ProtoPacker packer)
        {
            this.packer = packer;
        }

        public void SetOnStateChangeHandler(OnConnectionStateChangeDelegate onStateChangeHandler)
        {
            this.onStateChangeHandler = onStateChangeHandler;
        }
        
        public void SetOnPushHandler(OnPushDelegate onPushHandler)
        {
            this.onPushHandler = onPushHandler;
        }

        public void Connect(string host, int port)
        {
            if (State != ConnectionState.NotConnected) return;
            State = ConnectionState.Connecting;
            client = new TcpClient(packer);
            client.Connect(host, port);
        }

        public void Disconnect(bool broadcast = true)
        {
            if (State == ConnectionState.NotConnected) return;
            State = ConnectionState.NotConnected;
            client.Disconnect();
            Clear();
            if (broadcast)
                onStateChangeHandler(ConnectionEvent.ManualDisconnect, SocketErrorCode.None);
        }

        public void SendRequest(Package package, ResponseDelegate callback = null, float timeout = 5)
        {
            if (State != ConnectionState.Connected) return;
            SendRequest(package.Head, package.Body, callback, timeout);
        }

        public void SendRequest(uint type, IMessage request, ResponseDelegate callback = null, float timeout = 5)
        {
            if (State != ConnectionState.Connected) return;
            SendRequest(NewRequestHead(type), request, callback, timeout);
        }

        public void SendRequest(HeadPack head, IMessage request, ResponseDelegate callback = null, float timeout = 5)
        {
            if (State != ConnectionState.Connected) return;
            requestDataDict[head.Pid] = new RequestData(callback, timeout);
            client.Send(head, request);
        }

        //每帧调用
        public void Tick(float deltaTime)
        {
            switch (State)
            {
                case ConnectionState.Connecting:
                    if (client.ErrorCode != SocketErrorCode.None)
                        HandleConnectError(client.ErrorCode);
                    else if (client.Connected)
                        OnConnected();
                    break;
                case ConnectionState.Connected:
                    if (client.ErrorCode != SocketErrorCode.None)
                        HandleError(client.ErrorCode);
                    else
                    {
                        ReceivePackage();
                        TickRequestData(deltaTime);
                    }

                    break;
            }
        }

        public uint GetPid()
        {
            return ++pid;
        }

        public HeadPack NewRequestHead(uint opType)
        {
            return ProtoPacker.NewRequestHead(GetPid(), opType);
        }

        private void OnConnected()
        {
            State = ConnectionState.Connected;
            //心跳
            onStateChangeHandler?.Invoke(ConnectionEvent.Connected, SocketErrorCode.None);
        }

        private void HandleConnectError(SocketErrorCode errorCode)
        {
            State = ConnectionState.NotConnected;
            Clear();
            onStateChangeHandler?.Invoke(ConnectionEvent.ConnectFailure, errorCode);
        }

        private void HandleError(SocketErrorCode errorCode)
        {
            State = ConnectionState.NotConnected;
            Clear();
            onStateChangeHandler?.Invoke(ConnectionEvent.ConnectedError, errorCode);
        }

        private void ReceivePackage()
        {
            client.Receive(packageQueue);
            try
            {
                for (int i = 0, count = packageQueue.Count; i < count; i++)
                {
                    Package package = packageQueue.Dequeue();
                    switch (package.Head.ProtoType)
                    {
                        case ProtoType.Response:
                            OnResponseSuccess(package);
                            break;
                        case ProtoType.Push:
                            onPushHandler?.Invoke(package);
                            break;
                        default:
                            Debug.LogWarning($"Unknown proto type: {package.Head.ProtoType}");
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }

            packageQueue.Clear();
        }

        private void TickRequestData(float deltaTime)
        {
            foreach (var pair in requestDataDict)
            {
                //Timeout
                if (pair.Value.Tick(deltaTime))
                    toDeletePids.Add(pair.Key);
            }

            foreach (var pid in toDeletePids)
                OnResponseFailure(pid);
            toDeletePids.Clear();
        }

        private void OnResponseSuccess(Package package)
        {
            OnResponse(true, package.Head.Pid, package);
        }

        private void OnResponseFailure(uint pid)
        {
            OnResponse(false, pid, null);
        }

        private void OnResponse(bool success, uint pid, Package package)
        {
            if (!requestDataDict.TryGetValue(pid, out var data)) return;
            requestDataDict.Remove(pid);
            data?.Callback(success, package);
        }

        private void Clear()
        {
            client = null;
            packageQueue.Clear();
            foreach (var data in requestDataDict.Values)
                data?.Callback(false, null);
            requestDataDict.Clear();
            toDeletePids.Clear();
        }
    }
}
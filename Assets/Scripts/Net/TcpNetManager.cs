using Google.Protobuf;
using Pb;
using UnityEngine;

namespace Net
{
    public class TcpNetManager : MonoBehaviour
    {
        public const int HeartbeatInterval = 5; //s
        public const int HeartbeatMaxTimeoutCount = 3;

        private readonly TcpConnection connection = new TcpConnection(new Packer());

        private string host;
        private int port;
        private float hbTime;
        private int hbTimeoutCount;

        #region Instance

#if UNITY_EDITOR
        protected static bool onApplicationQuit;
#endif

        private static TcpNetManager instance;

        public static TcpNetManager Instance
        {
            get
            {
                //Avoid calling Instance in OnDestroy method to cause error when application quit
#if UNITY_EDITOR
                if (onApplicationQuit)
                {
                    // ReSharper disable once Unity.IncorrectMonoBehaviourInstantiation
                    return new TcpNetManager();
                }
#endif
                // if (instance == null)
                // {
                //     //Find
                //     instance = FindObjectOfType<NetManager>();
                //     //Create
                //     if (instance == null)
                //     {
                //         var go = new GameObject(typeof(T).Name);
                //         instance = go.AddComponent<T>();
                //         DontDestroyOnLoad(go);
                //     }
                // }

                //Create
                if (instance == null)
                {
                    var go = new GameObject(nameof(TcpNetManager));
                    instance = go.AddComponent<TcpNetManager>();
                    DontDestroyOnLoad(go);
                }

                return instance;
            }
        }

#if UNITY_EDITOR
        private void OnApplicationQuit()
        {
            onApplicationQuit = true;
        }
#endif

        #endregion

        private void Awake()
        {
            if (instance == null)
                instance = this;
            else if (instance != this)
            {
                Destroy(instance);
                return;
            }

            connection.SetOnStateChangeHandler(OnConnectionStateChange);
        }

        private void Update()
        {
            var deltaTime = Time.unscaledDeltaTime;
            connection.Tick(deltaTime);
            TickHeartbeat(deltaTime);
        }

        public void SetOnPushHandler(OnPushDelegate onPushHandler)
        {
            connection.SetOnPushHandler(onPushHandler);
        }

        public void Connect(string host, int port)
        {
            this.host = host;
            this.port = port;
            Reconnect();
        }

        public void Disconnect()
        {
            connection.Disconnect();
        }

        public void SendRequest(Package package, OnResponseDelegate callback = null, float timeout = 5)
        {
            connection.SendRequest(package, callback, timeout);
        }

        public void SendRequest(OpType opType, IMessage request, OnResponseDelegate callback = null, float timeout = 5)
        {
            connection.SendRequest((uint) opType, request, callback, timeout);
        }

        public void SendRequest(HeadPack head, IMessage request, OnResponseDelegate callback = null, float timeout = 5)
        {
            connection.SendRequest(head, request, callback, timeout);
        }

        private void Reconnect()
        {
            connection.Connect(host, port);
        }

        private void OnConnectionStateChange(ConnectionEvent connectionEvent, SocketErrorCode errorCode)
        {
            Debug.Log($"Tcp ConnectionStateChange. ConnectionEvent: {connectionEvent}, SocketErrorCode: {errorCode}.");
            switch (connectionEvent)
            {
                case ConnectionEvent.Connected:
                    OnConnected();
                    break;
                case ConnectionEvent.ConnectFailure:
                    switch (errorCode)
                    {
                        case SocketErrorCode.ConnectError:
                            //MARK 添加自定义操作
                            break;
                        case SocketErrorCode.ConnectTimeout:
                            Reconnect();
                            break;
                    }

                    break;
                case ConnectionEvent.ConnectedError:
                    Reconnect();
                    break;
                case ConnectionEvent.ManualDisconnect:
                    break;
            }
        }

        private void TickHeartbeat(float deltaTime)
        {
            if (connection.State != ConnectionState.Connected) return;
            hbTime += deltaTime;
            while (hbTime >= HeartbeatInterval)
            {
                hbTime -= HeartbeatInterval;
                Heartbeat();
            }
        }

        private void OnConnected()
        {
            hbTime = 0;
            hbTimeoutCount = 0;
        }

        private void Heartbeat()
        {
            if (connection.State != ConnectionState.Connected) return;
            //心跳超时
            if (++hbTimeoutCount > HeartbeatMaxTimeoutCount)
            {
                HeartbeatTimeout();
                return;
            }

            //时间到，发送心跳包
            SendRequest(OpType.Heartbeat, new HeartbeatReq(), OnHeartbeatResponse);
        }

        private void OnHeartbeatResponse(bool success, Package package)
        {
            if (success)
            {
                //收到心跳包，重置心跳包超时次数
                hbTimeoutCount = 0;
            }
        }

        private void HeartbeatTimeout()
        {
            Debug.LogError("Heartbeat timeout!");
            //超时断开连接
            connection.Disconnect(false);
            //重连
            Reconnect();
        }
    }
}
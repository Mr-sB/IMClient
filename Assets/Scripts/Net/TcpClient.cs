using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using Google.Protobuf;
using Pb;
using UnityEngine;

namespace Net
{
    public enum SocketErrorCode : byte
    {
        None,
        ConnectError,
        ConnectTimeout,
        ReadError,
        WriteError,
    }

    public class TcpClient
    {
        public const int DefaultConnectTimeout = 20; //s

        private readonly Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        private readonly ManualResetEvent connectWaiter = new ManualResetEvent(false);
        private readonly CancellationTokenSource closeTokenSource = new CancellationTokenSource(); //连接结束，用于停止所有线程
        private readonly SwitchQueue<byte[]> writeQueue = new SwitchQueue<byte[]>(8);
        private readonly AutoResetEvent writeWaiter = new AutoResetEvent(false);
        private readonly SwitchQueue<Package> readQueue = new SwitchQueue<Package>(8);
        private readonly ReaderWriterLockSlim closeRWLock = new ReaderWriterLockSlim();
        private readonly ProtoPacker packer;

        private bool isClose;

        public SocketErrorCode ErrorCode { private set; get; } = SocketErrorCode.None;
        public bool Connected => socket.Connected;

        public TcpClient(ProtoPacker packer)
        {
            this.packer = packer;
        }

        public void Connect(string host, int port)
        {
            new Thread(() =>
            {
                try
                {
                    socket.Connect(host, port);
                    connectWaiter.Set();
                    OnConnected();
                }
                catch (ThreadAbortException)
                {
                    //do nothing
                }
                catch (Exception e)
                {
                    //线程被取消
                    if (closeTokenSource.IsCancellationRequested) return;
                    Debug.LogError(e);
                    connectWaiter.Set();
                    SetError(SocketErrorCode.ConnectError);
                }
            }).Start();
            //超时检查
            new Thread(() =>
            {
                try
                {
                    //未超时
                    if (connectWaiter.WaitOne(DefaultConnectTimeout * 1000)) return;
                    //线程被取消
                    if (closeTokenSource.IsCancellationRequested) return;
                    SetError(SocketErrorCode.ConnectTimeout);
                }
                catch (Exception)
                {
                    SetError(SocketErrorCode.ConnectTimeout);
                }
            }).Start();
        }

        public void Disconnect()
        {
            closeRWLock.EnterWriteLock();
            if (isClose)
            {
                closeRWLock.ExitWriteLock();
                return;
            }

            isClose = true;
            closeRWLock.ExitWriteLock();
            Debug.Log($"Tcp disconnect! {socket.LocalEndPoint}");
            closeTokenSource.Cancel();
            closeTokenSource.Dispose();
            if (socket.Connected)
                socket.Shutdown(SocketShutdown.Both);
            socket.Close();
            writeQueue.Clear();
            readQueue.Clear();
        }

        public void Send(Package package)
        {
            Send(package.Head, package.Body);
        }

        public void Send(HeadPack head, IMessage body)
        {
            try
            {
                writeQueue.Enqueue(packer.Encode(head, body));
                writeWaiter.Set();
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                SetError(SocketErrorCode.WriteError);
            }
        }

        public void Receive(Queue<Package> packageQueue)
        {
            readQueue.Switch();
            while (!readQueue.Empty())
            {
                closeRWLock.EnterReadLock();
                if (isClose)
                {
                    closeRWLock.ExitReadLock();
                    return;
                }

                closeRWLock.ExitReadLock();
                packageQueue.Enqueue(readQueue.Dequeue());
            }
        }

        private void SetError(SocketErrorCode socketErrorCode)
        {
            ErrorCode = socketErrorCode;
            Disconnect();
        }

        private void OnConnected()
        {
            //读
            new Thread(Read).Start();
            //写
            new Thread(Write).Start();
        }

        private void Read()
        {
            while (!closeTokenSource.IsCancellationRequested)
            {
                try
                {
                    readQueue.Enqueue(packer.Decode(socket));
                }
                catch (ThreadAbortException)
                {
                    //do nothing
                }
                catch (ProtoPackException e)
                {
                    Debug.LogError(e);
                }
                catch (SocketException e)
                {
                    //不是Close引发的异常
                    if (!closeTokenSource.IsCancellationRequested)
                    {
                        Debug.LogError(e);
                        SetError(SocketErrorCode.ReadError);
                    }

                    return;
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                    SetError(SocketErrorCode.ReadError);
                    return;
                }
            }
        }

        private void Write()
        {
            while (true)
            {
                try
                {
                    //切换消费者
                    writeQueue.Switch();
                    while (!writeQueue.Empty())
                    {
                        if (closeTokenSource.IsCancellationRequested) return;
                        socket.Send(writeQueue.Dequeue());
                    }

                    //等待消息写入
                    writeWaiter.WaitOne();
                }
                catch (ThreadAbortException)
                {
                    //do nothing
                }
                catch (SocketException e)
                {
                    //不是Close引发的异常
                    if (!closeTokenSource.IsCancellationRequested)
                    {
                        Debug.LogError(e);
                        SetError(SocketErrorCode.WriteError);
                    }

                    return;
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                    SetError(SocketErrorCode.WriteError);
                    return;
                }
            }
        }
    }
}
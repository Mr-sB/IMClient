using System;
using System.Collections.Generic;
using System.Net.Sockets;
using Google.Protobuf;

namespace Pb
{
    public class Package
    {
        public HeadPack Head;
        public IMessage Body;

        public Package() : this(new HeadPack())
        {
        }

        public Package(HeadPack head, IMessage body = null)
        {
            Head = head;
            Body = body;
        }
    }

    public class ProtoPackException : Exception
    {
        public ProtoPackException()
        {
        }

        public ProtoPackException(string message) : base(message)
        {
        }

        public ProtoPackException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    public delegate IMessage ProtoBodyCreatorDelegate(HeadPack head);

    public class ProtoPacker
    {
        public const int HeadLen = 2; //不大于TotalLen
        public const int TotalLen = 4;

        private byte[] buffer = new byte[1024];
        private byte[] currentBytes = new byte[1024];
        private int currentBytesLength = 0;
        private readonly ProtoBodyCreatorDelegate bodyCreator;

        public ProtoPacker(ProtoBodyCreatorDelegate creator)
        {
            bodyCreator = creator;
        }

        public byte[] Encode(Package package)
        {
            return Encode(package.Head, package.Body);
        }

        public byte[] Encode(HeadPack head, IMessage body)
        {
            var headBytes = head.ToByteArray();
            var bodyBytes = body.ToByteArray();
            //头部长度
            var headLen = (ushort) headBytes.Length;
            //总长度
            var totalLen = (uint) (TotalLen + HeadLen + headLen + bodyBytes.Length);

            var message = new byte[totalLen];
            //写入总长度
            GetBytesLittleEndian(totalLen).CopyTo(message, 0);
            //写入头部长度
            GetBytesLittleEndian(headLen).CopyTo(message, TotalLen);
            //复制头部
            headBytes.CopyTo(message, TotalLen + HeadLen);
            //复制消息体
            bodyBytes.CopyTo(message, TotalLen + HeadLen + headLen);
            return message;
        }

        public Package Decode(Socket socket)
        {
            //读取消息长度
            ReadAtLeast(socket, 0, TotalLen);
            var totalLen = ToUInt32LittleEndian(currentBytes, 0);
            //读取消息头部长度
            ReadAtLeast(socket, TotalLen, HeadLen);
            var headLen = ToUInt16LittleEndian(currentBytes, TotalLen);

            Package package = new Package();
            //读取头部
            ReadAtLeast(socket, TotalLen + HeadLen, headLen);
            package.Head.MergeFrom(currentBytes, TotalLen + HeadLen, headLen);
            //读取消息体
            ReadAtLeast(socket, TotalLen + HeadLen + headLen, headLen);
            try
            {
                package.Body = bodyCreator?.Invoke(package.Head);
                package.Body?.MergeFrom(currentBytes, TotalLen + HeadLen + headLen, (int) (totalLen - TotalLen - HeadLen - headLen));
            }
            finally
            {
                //去除已读取的数据
                Array.Copy(currentBytes, totalLen, currentBytes, 0, currentBytesLength - totalLen);
                currentBytesLength -= (int) totalLen;
            }

            return package;
        }

        private void ReadAtLeast(Socket socket, int startIndex, int length)
        {
            Ensures(ref currentBytes, startIndex + length);
            int curLen = currentBytesLength - startIndex;
            while (curLen < length)
            {
                var len = socket.Receive(buffer);
                for (int i = 0; i < len; i++, currentBytesLength++)
                    currentBytes[currentBytesLength] = buffer[i];
                curLen += len;
            }
        }

        public static HeadPack NewRequestHead(uint pid, uint type)
        {
            return new HeadPack
            {
                ProtoType = ProtoType.Request,
                Pid = pid,
                Type = type,
            };
        }
        
        public static HeadPack NewResponseHead(uint pid, uint type, uint code)
        {
            return new HeadPack
            {
                ProtoType = ProtoType.Response,
                Pid = pid,
                Type = type,
                Code = code,
            };
        }

        public static HeadPack NewPushHead(uint type)
        {
            return new HeadPack
            {
                ProtoType = ProtoType.Push,
                Type = type,
            };
        }
        
        public static void Ensures<T>(ref T[] bytes, int length)
        {
            var curLen = bytes.Length;
            if (curLen >= length) return;
            var twoTimesLen = 2 * curLen;
            Array.Resize(ref bytes, length < twoTimesLen ? twoTimesLen : length);
        }

        public static byte[] GetBytesLittleEndian(short value)
        {
            var bytes = BitConverter.GetBytes(value);
            if (!BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return bytes;
        }

        public static byte[] GetBytesLittleEndian(ushort value)
        {
            return GetBytesLittleEndian((short) value);
        }

        public static byte[] GetBytesLittleEndian(int value)
        {
            var bytes = BitConverter.GetBytes(value);
            if (!BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return bytes;
        }

        public static byte[] GetBytesLittleEndian(uint value)
        {
            return GetBytesLittleEndian((int) value);
        }

        public static short ToInt16LittleEndian(byte[] value, int startIndex)
        {
            if (!BitConverter.IsLittleEndian)
                Array.Reverse(value, startIndex, 2);
            return BitConverter.ToInt16(value, startIndex);
        }

        public static ushort ToUInt16LittleEndian(byte[] value, int startIndex)
        {
            return (ushort) ToInt16LittleEndian(value, startIndex);
        }

        public static int ToInt32LittleEndian(byte[] value, int startIndex)
        {
            if (!BitConverter.IsLittleEndian)
                Array.Reverse(value, startIndex, 4);
            return BitConverter.ToInt32(value, startIndex);
        }

        public static uint ToUInt32LittleEndian(byte[] value, int startIndex)
        {
            return (uint) ToInt32LittleEndian(value, startIndex);
        }
    }
}
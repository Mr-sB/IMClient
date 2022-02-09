using System;
using System.Collections.Generic;
using Google.Protobuf;
using Net;

namespace Pb
{
    public class Packer : ProtoPacker
    {
        private static readonly Dictionary<ProtoType, Func<HeadPack, IMessage>> bodyCreatorRouter;
        private static readonly Dictionary<OpType, Func<IMessage>> reqMessageCreator;
        private static readonly Dictionary<OpType, Func<IMessage>> rspMessageCreator;
        private static readonly Dictionary<PushType, Func<IMessage>> pushMessageCreator;

        static Packer()
        {
            bodyCreatorRouter = new Dictionary<ProtoType, Func<HeadPack, IMessage>>
            {
                {ProtoType.Request, CreateRequest},
                {ProtoType.Response, CreateResponse},
                {ProtoType.Push, CreatePush},
            };
            reqMessageCreator = new Dictionary<OpType, Func<IMessage>>
            {
                {OpType.Heartbeat, () => new HeartbeatReq()},
                {OpType.Broadcast, () => new BroadcastReq()},
                {OpType.Query, () => new QueryReq()},
                {OpType.Rename, () => new RenameReq()},
                {OpType.PrivateChat, () => new PrivateChatReq()},
            };
            rspMessageCreator = new Dictionary<OpType, Func<IMessage>>
            {
                {OpType.Heartbeat, () => new HeartbeatRsp()},
                {OpType.Broadcast, () => new BroadcastRsp()},
                {OpType.Query, () => new QueryRsp()},
                {OpType.Rename, () => new RenameRsp()},
                {OpType.PrivateChat, () => new PrivateChatRsp()},
            };
            pushMessageCreator = new Dictionary<PushType, Func<IMessage>>
            {
                {PushType.Kick, () => new KickPush()},
                {PushType.Broadcast, () => new BroadcastPush()},
                {PushType.PrivateChat, () => new PrivateChatPush()},
            };
        }

        public Packer() : base(BodyCreator)
        {
        }

        public static HeadPack NewRequestHead(uint pid, OpType opType)
        {
            return ProtoPacker.NewRequestHead(pid, (uint) opType);
        }
        
        public static HeadPack NewResponseHead(uint pid, OpType opType, ResponseCode code)
        {
            return ProtoPacker.NewResponseHead(pid, (uint) opType, (uint)code);
        }

        public static HeadPack NewPushHead(PushType pushType)
        {
            return ProtoPacker.NewPushHead((uint) pushType);
        }

        private static IMessage BodyCreator(HeadPack head)
        {
            if (!bodyCreatorRouter.TryGetValue(head.ProtoType, out var router))
                throw new ProtoPackException($"Unknown proto type: {head.ProtoType}");
            return router(head);
        }

        private static IMessage CreateRequest(HeadPack head)
        {
            if (!reqMessageCreator.TryGetValue((OpType) head.Type, out var creator))
                throw new ProtoPackException($"Unknown op type: {head.Type}");
            return creator();
        }

        private static IMessage CreateResponse(HeadPack head)
        {
            if (!rspMessageCreator.TryGetValue((OpType) head.Type, out var creator))
                throw new ProtoPackException($"Unknown op type: {head.Type}");
            return creator();
        }

        private static IMessage CreatePush(HeadPack head)
        {
            if (!pushMessageCreator.TryGetValue((PushType) head.Type, out var creator))
                throw new ProtoPackException($"Unknown push type: {head.Type}");
            return creator();
        }
    }
}
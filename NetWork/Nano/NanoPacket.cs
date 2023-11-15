using GameFramework;
using GameFramework.Network;
using Pomelo.DotNetClient;
using SimpleJson;

namespace Nano
{
    public class NanoPacket : Packet
    {
        public override void Clear()
        {
            if (NanoPacketHeader != null)
            {
                ReferencePool.Release(NanoPacketHeader);
            }
            SendMessage = null;
            Message = null;
            Route = default;
            NanoPacketHeader = null;
            ReqId = 0;
        }

        public override int Id => 0;

        public string Route { get; set; }
        public uint ReqId { get; set; }
        public NanoPacketHeader NanoPacketHeader { get; private set; }
        public Message Message { get; set; }

        public JsonObject SendMessage;
        
        
        public static NanoPacket Create(string route,uint reqId,JsonObject msg)
        {
            NanoPacketHeader nanoPacketHeader = NanoPacketHeader.Create(PackageType.PKG_DATA);
            NanoPacket nanoPacket = Create(nanoPacketHeader,route,reqId,msg);

            nanoPacket.SendMessage = msg;
            nanoPacket.Route = route;
            nanoPacket.NanoPacketHeader = nanoPacketHeader;
            nanoPacket.ReqId = reqId;
            return nanoPacket;
        }
        
        public static NanoPacket Create(NanoPacketHeader nanoPacketHeader,string route = "",uint reqId = 0,JsonObject msg = null)
        {
            NanoPacket nanoPacket = ReferencePool.Acquire<NanoPacket>();
            nanoPacket.SendMessage = msg;
            nanoPacket.Route = route;
            nanoPacket.NanoPacketHeader = nanoPacketHeader;
            nanoPacket.ReqId = reqId;
            return nanoPacket;
        }
        
        public static NanoPacket HandShake(JsonObject user = null)
        {
            NanoPacketHeader nanoPacketHeader = NanoPacketHeader.Create(PackageType.PKG_HANDSHAKE);
            NanoPacket nanoPacket = Create(nanoPacketHeader);
            
            JsonObject msg = new JsonObject
            {
                ["sys"] = new JsonObject
                {
                    ["version"] = "0.0.1",
                    ["type"] = "cocos2dx-lua-client",
                    ["rsa"] = "{}",
                },
                ["user"] = user ?? new JsonObject()
            };

            nanoPacket.SendMessage = msg;
            return nanoPacket;
        }
        
        public static NanoPacket HandShakeAck()
        {
            NanoPacketHeader nanoPacketHeader = NanoPacketHeader.Create(PackageType.PKG_HANDSHAKE_ACK);
            NanoPacket nanoPacket = Create(nanoPacketHeader);
            
            return nanoPacket;
        }
        
        public static NanoPacket HeartBeat()
        {
            NanoPacketHeader nanoPacketHeader = NanoPacketHeader.Create(PackageType.PKG_HEARTBEAT);
            NanoPacket nanoPacket = Create(nanoPacketHeader);
            
            return nanoPacket;
        }

        
    }
}
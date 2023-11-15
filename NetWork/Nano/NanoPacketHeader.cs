using GameFramework;
using GameFramework.Network;
using Pomelo.DotNetClient;

namespace Nano
{
   
    
    public class NanoPacketHeader : IPacketHeader , IReference
    {
        public int PacketLength { get; private set; }
        public PackageType PacketHeaderType { get; private set; }
        
        public void Clear()
        {
            PacketLength = 0;
        }

        public static NanoPacketHeader Create(int pkgSize,PackageType headerType)
        {
            NanoPacketHeader header = ReferencePool.Acquire<NanoPacketHeader>();
            header.PacketLength = pkgSize;
            header.PacketHeaderType = headerType;
            
            return header;
        }
        
        public static NanoPacketHeader Create(PackageType headerType)
        {
            NanoPacketHeader header = ReferencePool.Acquire<NanoPacketHeader>();
            header.PacketLength = 0;
            header.PacketHeaderType = headerType;
            
            return header;
        }

    }
}
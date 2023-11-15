using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameFramework.Network;
using Moon;
using Pomelo.DotNetClient;
using SimpleJson;
using UnityGameFramework.Runtime;
using Xxtea;

namespace Nano
{
    public class NanoNetworkChannelHelper : INetworkChannelHelper
    {
        private const int HeadSize = 4;
        public int PacketHeaderLength => HeadSize;
        //
        private readonly byte[] _headBytes = new byte[HeadSize];
        //
        private uint _reqId = 100;
        private MessageProtocol _messageProtocol;
        //
        private INetworkChannel _networkChannel;
        public bool IsAuth { get; set; }

        private const bool IsCrypto = true;
        //
        private readonly Dictionary<string, Action<Message>> _actions;
        private readonly Dictionary<uint, AutoResetUniTaskCompletionSource<Message>> _tasks;

        
        
        public Action ActionConnectionComplete;
        
        public NanoNetworkChannelHelper()
        {
            _actions = new Dictionary<string, Action<Message>>();
            _tasks = new Dictionary<uint, AutoResetUniTaskCompletionSource<Message>>();
        }
        
        public void Initialize(INetworkChannel networkChannel)
        {
            _networkChannel = networkChannel;
            _networkChannel.SetDefaultHandler(Dispatch);
            
            Flower.GameEntry.Event.Subscribe(UnityGameFramework.Runtime.NetworkConnectedEventArgs.EventId, OnNetworkConnected);
            Flower.GameEntry.Event.Subscribe(UnityGameFramework.Runtime.NetworkClosedEventArgs.EventId, OnNetworkClosed);
            Flower.GameEntry.Event.Subscribe(UnityGameFramework.Runtime.NetworkErrorEventArgs.EventId, OnNetworkError);
            Flower.GameEntry.Event.Subscribe(UnityGameFramework.Runtime.NetworkCustomErrorEventArgs.EventId, OnNetworkCustomError);
            Flower.GameEntry.Event.Subscribe(UnityGameFramework.Runtime.NetworkMissHeartBeatEventArgs.EventId, OnNetworkMissHeartBeat);
            
            Log.Info("Initialize");
        }

        public void Shutdown()
        {
            _actions.Clear();
            _tasks.Clear();
            _networkChannel = null;
            IsAuth = false;
            
            Log.Info("Shutdown");
        }

        public void PrepareForConnecting()
        {
            IsAuth = false;

            Log.Info("PrepareForConnecting");
        }

        public bool SendHeartBeat()
        {
            if (IsAuth)
            {
                _networkChannel.Send(NanoPacket.HeartBeat());
            }
            return true;
        }

        public bool Serialize<T>(T packet, Stream destination) where T : Packet
        {
            NanoPacket nanoPacket = packet as NanoPacket;

            if (nanoPacket == null)
            {
                throw new NanoNetworkException("Serialize NanoPacket Err");
            }

            NanoPacketHeader nanoPacketHeader = nanoPacket.NanoPacketHeader;
            if (nanoPacketHeader == null)
            {
                throw new NanoNetworkException("Serialize NanoPacketHeader Err");
            }

            MemoryStream stream = (MemoryStream)destination;
            
            PackageType headerType = nanoPacketHeader.PacketHeaderType;
            _headBytes[0] = Convert.ToByte(headerType);

            if (headerType == PackageType.PKG_HANDSHAKE)
            {
                object message = nanoPacket.SendMessage;
            
                byte[] body = System.Text.Encoding.UTF8.GetBytes(message.ToString());
                int bodyLength = body.Length;
            
                //
                _headBytes.WriteLength(bodyLength);
                
                stream.Write(_headBytes, 0, HeadSize);
                stream.Write(body, 0, bodyLength);
            }
            else if (headerType == PackageType.PKG_HANDSHAKE_ACK || headerType == PackageType.PKG_HEARTBEAT)
            {
                _headBytes.WriteLength(0);
                stream.Write(_headBytes, 0, HeadSize);
            }
            else if (headerType == PackageType.PKG_DATA)
            {
                byte[] body = _messageProtocol.encode(nanoPacket.Route, nanoPacket.ReqId, nanoPacket.SendMessage);
                
                int bodyLength = body.Length;
                _headBytes.WriteLength(bodyLength);
                
                // Log.Info($"PKG_DATA Length={bodyLength}");
                stream.Write(_headBytes, 0, HeadSize);
                stream.Write(body, 0, bodyLength);
            }
            else
            {
                throw new NanoNetworkException($"PacketHeaderType Err : {headerType} !!");
            }
            
            // Log.Info($"Serialize headerType={headerType}");

            ReferencePool.Release(nanoPacket);
            
            return true;
        }

        public IPacketHeader DeserializePacketHeader(Stream source, out object customErrorData)
        {
            customErrorData = null;

            MemoryStream memoryStream = (MemoryStream)source;
            byte[] headBuffer = memoryStream.GetBuffer();

            // 1 byte type ,  3 bytes size
            // 类型
            PackageType headerType = (PackageType)headBuffer[0];
            
            // 大端
            int pkgLength = (headBuffer[1] << 16) + (headBuffer[2] << 8) + headBuffer[3];
            
            NanoPacketHeader header = NanoPacketHeader.Create(pkgLength,headerType);
            
            // Log.Info($"反序列化头 headerType={headerType} pkgLength={pkgLength}");
            
            return header;
        }

        public Packet DeserializePacket(IPacketHeader packetHeader, Stream source, out object customErrorData)
        {
            NanoPacketHeader nanoPacketHeader = packetHeader as NanoPacketHeader;

            if(nanoPacketHeader == null)
            {
                customErrorData = "Packet header is null.";
                return null;
            }

            customErrorData = null;
            
            NanoPacket nanoPacket = NanoPacket.Create(nanoPacketHeader);

            PackageType packetHeaderType = nanoPacketHeader.PacketHeaderType;

            MemoryStream memoryStream = (MemoryStream)source;
            
            int messageSize =  (int)(memoryStream.Length - memoryStream.Position);
            
            byte[] body = new byte[messageSize];

            Array.Copy(memoryStream.GetBuffer(),body,messageSize);

            // Log.Info($"body.Length = {messageSize}");

            if (packetHeaderType == PackageType.PKG_HANDSHAKE)
            {
                JsonObject jsonMsg = (JsonObject)SimpleJson.SimpleJson.DeserializeObject(body.Utf8ToStr());
                nanoPacket.SendMessage = jsonMsg;
            }
            else if (packetHeaderType == PackageType.PKG_DATA)
            {
                Message msg = _messageProtocol.decode(body);
                nanoPacket.Message = msg;
            }
            else if (packetHeaderType == PackageType.PKG_HEARTBEAT || packetHeaderType == PackageType.PKG_KICK)
            {
                
            }
            else
            {
                customErrorData = $"UnKnow packetHeaderType = {packetHeaderType}";
            }


            // Log.Info($"DeserializePacket PacketHeaderType = {packetHeaderType} body= {nanoPacket.SendMessage}");
            
            return nanoPacket;
        }

        void Dispatch(object sender, Packet packet)
        {
            if (sender != _networkChannel)
            {
                return;
            }

            NanoPacket nanoPacket = (NanoPacket)packet;
            
            NanoPacketHeader packetHeader = nanoPacket.NanoPacketHeader;
            PackageType packetHeaderType = packetHeader.PacketHeaderType;
            
            if (packetHeaderType == PackageType.PKG_HANDSHAKE)
            {
                // {"code":200,"sys":{"heartbeat":30,"servertime":1699954182}}
                JsonObject msg = nanoPacket.SendMessage;
                //Handshake error
                if (!msg.ContainsKey("code") || !msg.ContainsKey("sys") || Convert.ToInt32(msg["code"]) != 200)
                {
                    throw new NanoNetworkException("Handshake error! Please check your handshake config.");
                }

                //Set compress data
                JsonObject sys = (JsonObject)msg["sys"];

                JsonObject dict = new JsonObject();
                if (sys.ContainsKey("dict")) dict = (JsonObject)sys["dict"];

                JsonObject protos = new JsonObject();
                JsonObject serverProtos = new JsonObject();
                JsonObject clientProtos = new JsonObject();

                if (sys.ContainsKey("protos"))
                {
                    protos = (JsonObject)sys["protos"];
                    serverProtos = (JsonObject)protos["server"];
                    clientProtos = (JsonObject)protos["client"];
                }

                _messageProtocol = new MessageProtocol(dict, serverProtos, clientProtos,IsCrypto);

                //Init heartbeat service
                int interval = 0;
                if (sys.ContainsKey("heartbeat")) interval = Convert.ToInt32(sys["heartbeat"]);
                if (interval > 0)
                {
                    _networkChannel.HeartBeatInterval = interval;
                }

                _networkChannel.Send(NanoPacket.HandShakeAck());
                
                ActionConnectionComplete?.Invoke();
            }
            else if (packetHeaderType == PackageType.PKG_DATA)
            {
                Message msg = nanoPacket.Message;
                
                MessageType messageType = msg.type;
                if (messageType == MessageType.MSG_RESPONSE)
                {
                    
                    if (_tasks.TryGetValue(msg.id,out var tcs))
                    {
                        tcs.TrySetResult(msg);
                        _tasks.Remove(msg.id);
                    }
                    else
                    {
                        throw new NanoNetworkException($"_tasks err msg.id = {msg.id} !!!");
                    }
                    
                }
                else if (messageType == MessageType.MSG_PUSH)
                {
                    nanoPacket.Route = msg.route;
                    nanoPacket.SendMessage = msg.jsonObj;
                    
                    if (_actions.TryGetValue(msg.route,out var action))
                    {
                        action(msg);
                    }
                    else
                    {
                        throw new NanoNetworkException($"_actions err msg.route = {msg.route} !!!");
                    }
                    
                }
                else if (messageType == MessageType.MSG_SYS)
                {
                    if (msg.id != 0)
                    {
                        nanoPacket.ReqId = msg.id;
                        nanoPacket.SendMessage = msg.jsonObj;
                    }
                    else
                    {
                        nanoPacket.Route = msg.route;
                        nanoPacket.SendMessage = msg.jsonObj;
                    }
                }
                else
                {
                    throw new NanoNetworkException($"messageType err {messageType} !!!");
                }
            }

            // Log.Info($"Packet Dispatch PacketHeaderType={packetHeaderType}");
        }

        #region SocketLife

        private void OnNetworkMissHeartBeat(object sender, GameFramework.Event.GameEventArgs e)
        {
            UnityGameFramework.Runtime.NetworkMissHeartBeatEventArgs ne = (UnityGameFramework.Runtime.NetworkMissHeartBeatEventArgs) e;
            if (ne.NetworkChannel != _networkChannel)
            {
                return;
            }

            if (ne.MissCount > 2)
            {
                ne.NetworkChannel.Close();
            }
        }

        private void OnNetworkCustomError(object sender, GameFramework.Event.GameEventArgs e)
        {
            UnityGameFramework.Runtime.NetworkCustomErrorEventArgs ne = (UnityGameFramework.Runtime.NetworkCustomErrorEventArgs) e;
            if (ne.NetworkChannel != _networkChannel)
            {
                return;
            }

            if (ne.CustomErrorData != null)
            {
                Log.Error("Network Packet {0} CustomError : {1}.", ne.Id, ne.CustomErrorData);
                
                ne.NetworkChannel.Close();
            }
        }

        private void OnNetworkError(object sender, GameFramework.Event.GameEventArgs e)
        {
            UnityGameFramework.Runtime.NetworkErrorEventArgs ne = (UnityGameFramework.Runtime.NetworkErrorEventArgs) e;
            if (ne.NetworkChannel != _networkChannel)
            {
                return;
            }

            Log.Error("Network Packet {0} Error : {1}.", ne.Id, ne.ErrorMessage);
            
            ne.NetworkChannel.Close();
        }

        private void OnNetworkClosed(object sender, GameFramework.Event.GameEventArgs e)
        {
            UnityGameFramework.Runtime.NetworkClosedEventArgs ne = (UnityGameFramework.Runtime.NetworkClosedEventArgs) e;
            if (ne.NetworkChannel != _networkChannel)
            {
                return;
            }
            
            Log.Error("NetworkChannel {0} is closed.", ne.NetworkChannel.Name);
        }

        private void OnNetworkConnected(object sender, GameFramework.Event.GameEventArgs e)
        {
            UnityGameFramework.Runtime.NetworkConnectedEventArgs ne = (UnityGameFramework.Runtime.NetworkConnectedEventArgs) e;
            if (ne.NetworkChannel != _networkChannel)
            {
                return;
            }

            _networkChannel.Send(NanoPacket.HandShake());
            
            Log.Info("NetworkChannel {0} is connected. IP: {1}", ne.NetworkChannel.Name, ne.NetworkChannel.Socket.RemoteEndPoint.ToString());
        }

        #endregion

        public void Register(string route,Action<Message> callback)
        {
            _actions[route] = callback;
        }
        
        public async UniTask<Message> Call(string route,JsonObject msg)
        {
            NanoPacket nanoPacket = NanoPacket.Create(route , _reqId , msg);
            _networkChannel.Send(nanoPacket);
            
            var task = AutoResetUniTaskCompletionSource<Message>.Create();
            _tasks[_reqId] = task;
            
            _reqId++;
            if(_reqId >= uint.MaxValue) _reqId = 100;
            
           
            Message res = await task.Task;
            
            return res;
        }
        
        public void Notify(string route,JsonObject msg)
        {
            NanoPacket nanoPacket = NanoPacket.Create(route , 0 , msg);
            _networkChannel.Send(nanoPacket);
        }
    }
}
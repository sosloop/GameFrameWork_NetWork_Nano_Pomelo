
using System;
using System.Net;
using Cysharp.Threading.Tasks;
using GameFramework.Fsm;
using GameFramework.Network;
using GameFramework.Procedure;
using Moon;
using Nano;
using Pomelo.DotNetClient;
using SimpleJson;
using UnityEngine;
using UnityEngine.Networking;
using UnityGameFramework.Runtime;

namespace Flower
{
    public class ProcedureTestNanoNetWork : ProcedureBase
    {
        private INetworkChannel _networkChannel;
        private NanoNetworkChannelHelper _moonNetworkChannelHelper;
        
        protected override void OnInit(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnInit(procedureOwner);
        }

        protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnEnter(procedureOwner);

            Web().Forget();
        }

        async UniTask Web()
        {
            JsonObject jsonObject = new JsonObject()
            {
                ["appId"]="test",
                ["channelId"]="test",
                ["imei"]="test1",
            };
            string jsonStr = jsonObject.ToString();
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonStr);

            Log.Info("jsonStr="+jsonStr);

            using UnityWebRequest request = UnityWebRequest.Post("http://127.0.0.1:12307/v1/user/login/guest","POST");
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.SetRequestHeader("Content-Type", "application/json;charset=utf-8");
            request.SetRequestHeader("accept", "application/json");

            await request.SendWebRequest();
            string downloadHandlerText = request.downloadHandler.text;

            Log.Info("result="+downloadHandlerText);
            JsonObject guestLoginJson = (JsonObject)SimpleJson.SimpleJson.DeserializeObject(downloadHandlerText);
            // Debug.Log(guestLoginJson["name"]);

            JsonObject msg = new JsonObject();
            msg["uid"] = guestLoginJson["uid"];
            msg["name"] = guestLoginJson["name"];
            msg["headUrl"] = guestLoginJson["headUrl"];
            msg["sex"] = guestLoginJson["sex"];
            msg["fangka"] = guestLoginJson["fangka"];
            msg["ip"] = guestLoginJson["playerIp"];
            Log.Info(msg);
            
            if (IPAddress.TryParse("127.0.0.1",out IPAddress ipAddress))
            {
                _moonNetworkChannelHelper = new NanoNetworkChannelHelper();
                _networkChannel = GameEntry.Network.CreateNetworkChannel("Test",ServiceType.Tcp,_moonNetworkChannelHelper);
                _networkChannel.Connect(ipAddress,33251);
                Log.Info("开始连接");

                _moonNetworkChannelHelper.Register("onCoinChange", (res) =>
                {
                    Log.Info("onCoinChange="+res);
                });
                _moonNetworkChannelHelper.Register("onBroadcast", (res) =>
                {
                    Log.Info("onBroadcast="+res);
                });
                
                _moonNetworkChannelHelper.ActionConnectionComplete = async () =>
                {
                    Log.Info("握手完成，准备登录");

                    Message call = await _moonNetworkChannelHelper.Call("Manager.Login",msg);

                    _moonNetworkChannelHelper.IsAuth = true;
                    
                    Log.Info("Manager.Login = "+call);
                };

            }
        }

        async UniTask Login()
        {
            
        }

        protected override async void OnUpdate(IFsm<IProcedureManager> procedureOwner, float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);

            if (Input.GetKeyUp(KeyCode.A))
            {
                Message call = await _moonNetworkChannelHelper.Call("DeskManager.Join",new JsonObject()
                {
                    ["version"]="1.9.3",
                    ["deskId"]="123456",
                });
                Log.Info("DeskManager.Join = "+call);
            }
            
            if (Input.GetKeyUp(KeyCode.B))
            {
                Message call = await _moonNetworkChannelHelper.Call("DeskManager.UnCompleteDesk",new JsonObject());
                Log.Info("DeskManager.UnCompleteDesk = "+call);
            }
        }

        protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
        {
            base.OnLeave(procedureOwner, isShutdown);
        }

        protected override void OnDestroy(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnDestroy(procedureOwner);
            
            _networkChannel?.Close();
            _moonNetworkChannelHelper = null;
        }
    }
}
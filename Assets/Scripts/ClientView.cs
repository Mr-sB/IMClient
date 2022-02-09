using Google.Protobuf;
using Net;
using Pb;
using UnityEngine;
using UnityEngine.UI;

public class ClientView : MonoBehaviour
{
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private Text resultTxt;
    [SerializeField] private InputField inputField;
    [SerializeField] private Button connectBtn;
    [SerializeField] private Button broadcastBtn;
    [SerializeField] private Button queryBtn;
    [SerializeField] private Button renameBtn;
    [SerializeField] private Button privateChatBtn;

    private string host;
    private int port;

    private void Awake()
    {
        TcpNetManager.Instance.SetOnPushHandler(OnPush);

        connectBtn.onClick.AddListener(OnConnectClick);
        broadcastBtn.onClick.AddListener(OnBroadcastClick);
        queryBtn.onClick.AddListener(OnQueryClick);
        renameBtn.onClick.AddListener(OnRenameClick);
        privateChatBtn.onClick.AddListener(OnPrivateChatClick);
    }

    private void SendRequest(OpType opType, IMessage request, ResponseDelegate callback = null, float timeout = 5)
    {
        TcpNetManager.Instance.SendRequest(opType, request, (success, package) => { OnResponse(success, package, callback); }, timeout);
    }

    private void OnResponse(bool success, Package package, ResponseDelegate callback)
    {
        if (!success)
            ShowText("请求失败");
        else if ((ResponseCode) package.Head.Code != ResponseCode.Success)
            ShowText("请求失败,ResponseCode: " + (ResponseCode) package.Head.Code);
        callback?.Invoke(success, package);
    }

    private void OnPush(Package package)
    {
        switch ((PushType) package.Head.Type)
        {
            case PushType.Kick:
                ShowText("被踢下线");
                break;
            case PushType.Broadcast:
            {
                var body = (BroadcastPush) package.Body;
                ShowText("收到广播" + body.User + ":" + body.Content);
                break;
            }
            case PushType.PrivateChat:
            {
                var body = (PrivateChatPush) package.Body;
                ShowText("收到私聊" + body.User + ":" + body.Content);
                break;
            }
            default:
                ShowText("未知PushType: " + (PushType) package.Head.Type);
                break;
        }
    }

    private void ShowText(string txt)
    {
        resultTxt.text += txt + "\n";
        LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.content);
        scrollRect.verticalNormalizedPosition = 0;
    }

    private void OnBroadcastClick()
    {
        SendRequest(OpType.Broadcast, new BroadcastReq
        {
            Content = inputField.text
        });
    }

    private void OnQueryClick()
    {
        SendRequest(OpType.Query, new QueryReq(), (success, package) =>
        {
            if (!success || (ResponseCode) package.Head.Code != ResponseCode.Success) return;
            var body = (QueryRsp) package.Body;
            ShowText("在线列表:\n" + string.Join("\n", body.Users));
        });
    }

    private void OnRenameClick()
    {
        SendRequest(OpType.Rename, new RenameReq
        {
            NewName = inputField.text
        });
    }

    private void OnPrivateChatClick()
    {
        string[] strs = inputField.text.Split(' ');
        if (strs.Length < 2) return;
        SendRequest(OpType.PrivateChat, new PrivateChatReq
        {
            User = strs[0],
            Content = strs[1],
        });
    }

    private void OnConnectClick()
    {
        if (string.IsNullOrEmpty(inputField.text))
        {
            if (string.IsNullOrEmpty(host))
                host = "127.0.0.1";
            if (port == 0)
                port = 8000;
        }
        else
        {
            string[] strs = inputField.text.Split(' ');
            if (strs.Length < 2) return;
            host = strs[0];
            port = int.TryParse(strs[1], out var result) ? result : 0;
        }

        TcpNetManager.Instance.Disconnect();
        TcpNetManager.Instance.Connect(host, port);
    }
}
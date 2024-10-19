
namespace Popcorn.Player.Core.Web;

public class ChromiumWebBridge : IWebBridge
{
    public Action<string>? PostMessage;
    public Action<byte[]>? PostMessageBytes;


    public void SendBytesMessage(byte[] bytes)
    {
        PostMessageBytes?.Invoke(bytes);
    }

    public void SendMessage(string message)
    {
        PostMessage?.Invoke(message);
    }
}


namespace Popcorn.Player.Core.Web;

public interface IWebBridge
{
    void SendMessage(string message);
    void SendBytesMessage(byte[] message);
}

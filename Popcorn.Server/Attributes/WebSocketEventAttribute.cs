namespace Popcorn.Server.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class WebSocketEventAttribute : Attribute
{
    public string EventType { get; }
    public WebSocketEventAttribute(string eventType)
    {
        EventType = eventType;
    }
}
namespace Popcorn.Server.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class WebSocketBinaryEventAttribute : Attribute
{
    public string EventType { get; }
    public WebSocketBinaryEventAttribute(string eventType)
    {
        EventType = eventType;
    }
}
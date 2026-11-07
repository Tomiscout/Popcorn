using System.Reflection;
using Popcorn.Server.Attributes;

namespace Popcorn.Server;

public static class WebSocketServices
{
    public static void RegisterWebServices(WebSocketBus bus, Assembly assembly)
    {
        var serviceTypes = assembly.GetTypes()
            .Where(t => t.IsClass 
                        && !t.IsAbstract 
                        && t.GetCustomAttributes(typeof(WebSocketServiceAttribute), inherit: false).Any());

        foreach (var serviceType in serviceTypes)
        {
            foreach (var method in serviceType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                var eventAttrs = method.GetCustomAttributes<WebSocketEventAttribute>();
                foreach (var attr in eventAttrs)
                {
                    RegisterTextHandler(bus, serviceType, method, attr.EventType);
                }

                var binaryAttrs = method.GetCustomAttributes<WebSocketBinaryEventAttribute>();
                foreach (var attr in binaryAttrs)
                {
                    RegisterBinaryHandler(bus, serviceType, method, attr.EventType);
                }
            }
        }
    }

    private static void RegisterTextHandler(WebSocketBus bus, Type serviceType, MethodInfo method, string eventType)
{
    var parameters = method.GetParameters();

    if (parameters.Length == 0)
    {
        // No-arg handler
        var registerMethod = typeof(WebSocketBus)
            .GetMethod(nameof(WebSocketBus.RegisterWebServiceHandler))!
            .MakeGenericMethod(serviceType, typeof(object));

        // Build delegate: (service, _) => method.Invoke(service, null)
        var del = (Func<object, object, Task>)((service, _) =>
            (Task)method.Invoke(service, null)!);

        registerMethod.Invoke(bus, new object[] { eventType, del });
    }
    else if (parameters.Length == 1)
    {
        var paramType = parameters[0].ParameterType;

        var registerMethod = typeof(WebSocketBus)
            .GetMethod(nameof(WebSocketBus.RegisterWebServiceHandler))!
            .MakeGenericMethod(serviceType, paramType);

        // Build delegate: (service, request) => method.Invoke(service, new[] { request })
        var delType = typeof(Func<,,>).MakeGenericType(serviceType, paramType, typeof(Task));

        var del = Delegate.CreateDelegate(
            delType,
            null,
            method
        );

        registerMethod.Invoke(bus, new object[] { eventType, del });
    }
    else
    {
        throw new InvalidOperationException(
            $"Method {method.Name} has too many parameters for WebSocketEvent.");
    }
}

private static void RegisterBinaryHandler(WebSocketBus bus, Type serviceType, MethodInfo method, string eventType)
{
    var parameters = method.GetParameters();

    if (parameters.Length == 2 &&
        parameters[0].ParameterType == typeof(string[]) &&
        parameters[1].ParameterType == typeof(ArraySegment<byte>))
    {
        var registerMethod = typeof(WebSocketBus)
            .GetMethod(nameof(WebSocketBus.RegisterWebServiceBinaryHandler))!
            .MakeGenericMethod(serviceType);

        // Build delegate: (service, metadata, payload) => method.Invoke(service, new object[] { metadata, payload })
        var delType = typeof(Func<,,,>).MakeGenericType(
            serviceType,
            typeof(string[]),
            typeof(ArraySegment<byte>),
            typeof(Task)
        );

        var del = Delegate.CreateDelegate(delType, null, method);

        registerMethod.Invoke(bus, new object[] { eventType, del });
    }
    else
    {
        throw new InvalidOperationException(
            $"Binary handler {method.Name} must have signature (string[], ArraySegment<byte>).");
    }
}
}
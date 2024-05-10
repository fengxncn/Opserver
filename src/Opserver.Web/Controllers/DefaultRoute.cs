using Microsoft.AspNetCore.Mvc;

namespace Opserver.Controllers;

[AttributeUsage(AttributeTargets.Method)]
public sealed class DefaultRoute(string template) : RouteAttribute(template)
{
    private static Dictionary<Type, DefaultRoute> AllRoutes => [];

    public static DefaultRoute GetFor(Type t) => AllRoutes.TryGetValue(t, out var route) ? route : null;
}

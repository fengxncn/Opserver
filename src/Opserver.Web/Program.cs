using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using Opserver.Data;
using Opserver.Helpers;
using Opserver.Security;
using StackExchange.Profiling;
using SecurityManager = Opserver.Security.SecurityManager;

namespace Opserver;

public static partial class Program
{
    public static readonly DateTime StartDate = DateTime.UtcNow;

    private static int Main()
    {
        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging")).AddConsole();
            ConfigureAppConfiguration(builder.Configuration);
            ConfigureServices(builder.Services, builder.Configuration);
            var app = builder.Build();
            ConfigWepApp(app);
            app.Run();

            return 0;
        }
        catch
        {
            return 1;
        }
        finally
        {
            if (Debugger.IsAttached)
                Console.ReadKey();
        }
    }

    private static void ConfigWepApp(WebApplication app)
    {
        var securityManager = app.Services.GetRequiredService<SecurityManager>();
        var modules = app.Services.GetRequiredService<IEnumerable<StatusModule>>();
        var settings = app.Services.GetRequiredService<IOptions<OpserverSettings>>();
        app.UseForwardedHeaders()
        .UseResponseCompression()
        .UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = ctx =>
            {
                if (ctx.Context.Request.Query.ContainsKey("v")) // If cache-breaker versioned, cache for a year
                {
                    ctx.Context.Response.Headers[HeaderNames.CacheControl] = StaticContentCacheControl;
                }
            }
        })
        .UseExceptional()
        .UseMiniProfiler()
        .Use((httpContext, next) =>
        {
            httpContext.Response.Headers[HeaderNames.CacheControl] = DefaultCacheControl;

            Current.SetContext(new Current.CurrentContext(securityManager.CurrentProvider, httpContext, modules));
            return next();
        })
        .UseResponseCaching();
        app.MapControllers();
        NavTab.ConfigureAll(modules); // TODO: UseNavTabs() or something
        Cache.Configure(settings);
    }

    private static readonly StringValues DefaultCacheControl = new CacheControlHeaderValue
    {
        Private = true
    }.ToString();

    private static readonly StringValues StaticContentCacheControl = new CacheControlHeaderValue
    {
        Public = true,
        MaxAge = TimeSpan.FromDays(365)
    }.ToString();

    private static void ConfigureServices(IServiceCollection services, Microsoft.Extensions.Configuration.ConfigurationManager configuration)
    {
        // Register Opserver.Core config and polling
        services.AddCoreOpserverServices(configuration);

        services.AddResponseCaching();
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.AccessDeniedPath = "/denied";
                    options.LoginPath = "/login";
                    options.LogoutPath = "/logout";
                });

        services.AddResponseCompression(
            options =>
            {
                options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["image/svg+xml"]);
                options.Providers.Add<GzipCompressionProvider>();
                options.EnableForHttps = true;
            }
        );

        services
            .AddHttpContextAccessor()
            .AddMemoryCache()
            .AddExceptional(
                configuration.GetSection("Exceptional"),
                settings =>
                {
                    settings.UseExceptionalPageOnThrow = true;
                    settings.DataIncludeRegex = GetDataIncludeRegex();
                    settings.GetCustomData = (ex, data) =>
                    {
                        // everything below needs a context
                        // Don't *init* a user here, since that'll stack overflow when it errors
                        var u = Current.Context?.UserIfExists;
                        if (u != null)
                        {
                            data.Add("User", u.AccountName);
                            data.Add("Roles", u.Roles.ToString());
                        }

                        while (ex != null)
                        {
                            foreach (DictionaryEntry de in ex.Data)
                            {
                                var key = de.Key as string;
                                if (key.HasValue() && key.StartsWith(ExtensionMethods.ExceptionLogPrefix))
                                {
                                    data.Add(key.Replace(ExtensionMethods.ExceptionLogPrefix, ""), de.Value?.ToString() ?? "");
                                }
                            }
                            ex = ex.InnerException;
                        }
                    };
                }
            );

        services.AddSingleton<IConfigureOptions<MiniProfilerOptions>, MiniProfilerCacheStorageDefaults>();
        services.AddMiniProfiler(options =>
        {
            //options.RouteBasePath = "/profiler/";
            options.PopupRenderPosition = RenderPosition.Left;
            options.PopupMaxTracesToShow = 5;
            options.ShouldProfile = _ =>
            {
                return true;
                //switch (SiteSettings.ProfilingMode)
                //{
                //    case ProfilingModes.Enabled:
                //        return true;
                //    case SiteSettings.ProfilingModes.LocalOnly:
                //        return Current.User?.Is(Models.Roles.LocalRequest) == true;
                //    case SiteSettings.ProfilingModes.AdminOnly:
                //        return Current.User?.IsGlobalAdmin == true;
                //    default:
                //        return false;
                //}
            };
            options.EnableServerTimingHeader = true;
            options.IgnorePath("/graph")
            .IgnorePath("/login")
            .IgnorePath("/spark")
                   .IgnorePath("/top-refresh");
        });
        services.Configure<SecuritySettings>(configuration.GetSection("Security"));
        services.Configure<ActiveDirectorySecuritySettings>(configuration.GetSection("Security"));
        services.Configure<OIDCSecuritySettings>(configuration.GetSection("Security"));
        services.Configure<ForwardedHeadersOptions>(configuration.GetSection("ForwardedHeaders"));
        services.PostConfigure<ForwardedHeadersOptions>(
        options =>
        {
            // what's all this mess I hear you cry? well ForwardedHeadersOptions
            // has a bunch of read-only list props that can't be bound from configuration
            // so we need to go populate them ourselves
            var forwardedHeaders = configuration.GetSection("ForwardedHeaders");
            var allowedHosts = forwardedHeaders.GetSection(nameof(ForwardedHeadersOptions.AllowedHosts)).Get<List<string>>();
            if (allowedHosts != null)
            {
                options.AllowedHosts.Clear();
                foreach (var allowedHost in allowedHosts)
                {
                    options.AllowedHosts.Add(allowedHost);
                }
            }

            var knownProxies = forwardedHeaders.GetSection(nameof(ForwardedHeadersOptions.KnownProxies)).Get<List<string>>();
            var knownNetworks = forwardedHeaders.GetSection(nameof(ForwardedHeadersOptions.KnownNetworks)).Get<List<string>>();
            if (knownNetworks != null || knownProxies != null)
            {
                options.KnownProxies.Clear();
                options.KnownNetworks.Clear();
            }

            if (knownProxies != null)
            {
                foreach (var knownProxy in knownProxies)
                {
                    options.KnownProxies.Add(IPAddress.Parse(knownProxy));
                }
            }

            if (knownNetworks != null)
            {
                foreach (var knownNetwork in knownNetworks)
                {
                    var ipNet = IPNet.Parse(knownNetwork);
                    options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(ipNet.IPAddress, ipNet.CIDR));
                }
            }
        }
        );
        services.AddSingleton<SecurityManager>();
        services.AddMvc();
    }

    private static void ConfigureAppConfiguration(Microsoft.Extensions.Configuration.ConfigurationManager config)
    {
        config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile("localSettings.json", optional: true, reloadOnChange: true);
    }

    [GeneratedRegex("^(Redis|Elastic|ErrorLog|Jil)", RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex GetDataIncludeRegex();
}

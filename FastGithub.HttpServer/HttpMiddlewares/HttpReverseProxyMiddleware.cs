using FastGithub.Configuration;
using FastGithub.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Yarp.ReverseProxy.Forwarder;

namespace FastGithub.HttpServer.HttpMiddlewares
{
    /// <summary>
    /// 反向代理中间件
    /// </summary>
    sealed class HttpReverseProxyMiddleware
    {
        private static readonly DomainConfig defaultDomainConfig = new() { TlsSni = true };

        private readonly IHttpForwarder httpForwarder;
        private readonly IHttpClientFactory httpClientFactory;
        private readonly FastGithubConfig fastGithubConfig;
        private readonly ILogger<HttpReverseProxyMiddleware> logger;

        public HttpReverseProxyMiddleware(
            IHttpForwarder httpForwarder,
            IHttpClientFactory httpClientFactory,
            FastGithubConfig fastGithubConfig,
            ILogger<HttpReverseProxyMiddleware> logger)
        {
            this.httpForwarder = httpForwarder;
            this.httpClientFactory = httpClientFactory;
            this.fastGithubConfig = fastGithubConfig;
            this.logger = logger;
        }

        /// <summary>
        /// 处理请求
        /// </summary>
        /// <param name="context"></param>
        /// <param name="next"?
        /// <returns></returns>
        public async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            var host = context.Request.Host;
            if (TryGetDomainConfig(host, out var domainConfig) == false)
            {
                await next(context);
            }
            else if (domainConfig.Response == null)
            {
                var scheme = context.Request.Scheme;
                var destinationPrefix = GetDestinationPrefix(scheme, host, domainConfig.Destination);
                var httpClient = httpClientFactory.CreateHttpClient(host.Host, domainConfig);
                var error = await httpForwarder.SendAsync(context, destinationPrefix, httpClient, ForwarderRequestConfig.Empty, HttpTransformer.Empty);
                await HandleErrorAsync(context, error);
            }
            else
            {
                context.Response.StatusCode = domainConfig.Response.StatusCode;
                context.Response.ContentType = domainConfig.Response.ContentType;
                if (domainConfig.Response.ContentValue != null)
                {
                    await context.Response.WriteAsync(domainConfig.Response.ContentValue);
                }
            }
        }

        /// <summary>
        /// 获取域名的DomainConfig
        /// </summary>
        /// <param name="host"></param>
        /// <param name="domainConfig"></param>
        /// <returns></returns>
        private bool TryGetDomainConfig(HostString host, [MaybeNullWhen(false)] out DomainConfig domainConfig)
        {
            if (fastGithubConfig.TryGetDomainConfig(host.Host, out domainConfig) == true)
            {
                return true;
            }

            // 未配置的域名，但仍然被解析到本机ip的域名
            if (OperatingSystem.IsWindows() && IsDomain(host.Host))
            {
                logger.LogWarning($"域名{host.Host}可能已经被DNS污染，如果域名为本机域名，请解析为非回环IP");
                domainConfig = defaultDomainConfig;
                return true;
            }

            return false;

            // 是否为域名
            static bool IsDomain(string host)
            {
                return IPAddress.TryParse(host, out _) == false && host.Contains('.');
            }
        }

        /// <summary>
        /// 获取目标前缀
        /// </summary>
        /// <param name="scheme"></param>
        /// <param name="host"></param>
        /// <param name="destination"></param>
        /// <returns></returns>
        private string GetDestinationPrefix(string scheme, HostString host, Uri? destination)
        {
            var defaultValue = $"{scheme}://{host}/";
            if (destination == null)
            {
                return defaultValue;
            }

            var baseUri = new Uri(defaultValue);
            var result = new Uri(baseUri, destination).ToString();
            logger.LogInformation($"{defaultValue} => {result}");
            return result;
        }

        /// <summary>
        /// 处理错误信息
        /// </summary>
        /// <param name="context"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        private static async Task HandleErrorAsync(HttpContext context, ForwarderError error)
        {
            if (error == ForwarderError.None || context.Response.HasStarted)
            {
                return;
            }

            // 修剪(Trim)后匿名类型缺少JsonTypeInfo元数据，WriteAsJsonAsync会抛异常，故改为手写json
            var message = context.GetForwarderErrorFeature()?.Exception?.Message;
            var json = $"{{\"error\":{ToJsonString(error.ToString())},\"message\":{ToJsonString(message)}}}";
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(json);
        }

        /// <summary>
        /// 转换为json字符串字面量
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        private static string ToJsonString(string? value)
        {
            if (value == null)
            {
                return "null";
            }

            var builder = new StringBuilder(value.Length + 2);
            builder.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (c < 0x20)
                        {
                            builder.Append("\\u").Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            builder.Append(c);
                        }
                        break;
                }
            }
            builder.Append('"');
            return builder.ToString();
        }
    }
}

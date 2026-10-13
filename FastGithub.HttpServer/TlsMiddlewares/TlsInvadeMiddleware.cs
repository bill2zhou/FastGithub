using FastGithub.DomainResolve;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace FastGithub.HttpServer.TlsMiddlewares
{
    /// <summary>
    /// https入侵中间件
    /// </summary>
    sealed class TlsInvadeMiddleware
    {
        /// <summary>
        /// github的ssh over 443服务节点
        /// </summary>
        private static readonly DnsEndPoint sshEndPoint = new("ssh.github.com", 443);
        private readonly IDomainResolver domainResolver;
        private readonly TimeSpan connectTimeout = TimeSpan.FromSeconds(10d);

        /// <summary>
        /// https入侵中间件
        /// </summary>
        /// <param name="domainResolver"></param>
        public TlsInvadeMiddleware(IDomainResolver domainResolver)
        {
            this.domainResolver = domainResolver;
        }

        /// <summary>
        /// 执行中间件
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task InvokeAsync(ConnectionDelegate next, ConnectionContext context)
        {
            // 连接不是tls
            if (await IsTlsConnectionAsync(context) == false)
            {
                // ssh over 443的连接被dns劫持到本地，代理到ssh.github.com:443
                if (await TryProxySshAsync(context) == true)
                {
                    return;
                }

                // 没有任何tls中间件执行过
                if (context.Features.Get<ITlsConnectionFeature>() == null)
                {
                    // 设置假的ITlsConnectionFeature，迫使https中间件跳过自身的工作
                    context.Features.Set<ITlsConnectionFeature>(FakeTlsConnectionFeature.Instance);
                }
            }
            await next(context);
        }


        /// <summary>
        /// 尝试将ssh over 443的连接代理到ssh.github.com:443
        /// </summary>
        /// <param name="context"></param>
        /// <returns>是否为ssh连接并已代理</returns>
        private async Task<bool> TryProxySshAsync(ConnectionContext context)
        {
            ReadResult result;
            try
            {
                result = await context.Transport.Input.ReadAtLeastAsync(4, context.ConnectionClosed);
            }
            catch
            {
                return false;
            }

            // 不是ssh协议，数据放回缓冲区，走原有流程
            if (IsSshProtocol(result.Buffer) == false)
            {
                context.Transport.Input.AdvanceTo(result.Buffer.Start);
                return false;
            }

            try
            {
                using var connection = await CreateConnectionAsync(context.ConnectionClosed);
                // 缓冲区内的ssh握手数据未消费，会随双向拷贝送达远端
                var task1 = connection.CopyToAsync(context.Transport.Output, context.ConnectionClosed);
                var task2 = context.Transport.Input.CopyToAsync(connection, context.ConnectionClosed);
                await Task.WhenAny(task1, task2);
            }
            catch
            {
            }
            return true;
        }


        /// <summary>
        /// 连接到ssh.github.com:443
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="AggregateException"></exception>
        private async Task<Stream> CreateConnectionAsync(CancellationToken cancellationToken)
        {
            var innerExceptions = new List<Exception>();
            await foreach (var address in this.domainResolver.ResolveAsync(sshEndPoint, cancellationToken))
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    using var timeoutTokenSource = new CancellationTokenSource(this.connectTimeout);
                    using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(timeoutTokenSource.Token, cancellationToken);
                    await socket.ConnectAsync(address, sshEndPoint.Port, linkedTokenSource.Token);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (Exception ex)
                {
                    socket.Dispose();
                    cancellationToken.ThrowIfCancellationRequested();
                    innerExceptions.Add(ex);
                }
            }
            throw new AggregateException($"无法连接到{sshEndPoint.Host}:{sshEndPoint.Port}", innerExceptions);
        }


        /// <summary>
        /// 是否为tls协议
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        private static async Task<bool> IsTlsConnectionAsync(ConnectionContext context)
        {
            try
            {
                var result = await context.Transport.Input.ReadAtLeastAsync(2, context.ConnectionClosed);
                var state = IsTlsProtocol(result);
                context.Transport.Input.AdvanceTo(result.Buffer.Start);
                return state;
            }
            catch
            {
                return false;
            }

            static bool IsTlsProtocol(ReadResult result)
            {
                var reader = new SequenceReader<byte>(result.Buffer);
                return reader.TryRead(out var firstByte) &&
                    reader.TryRead(out var nextByte) &&
                    firstByte == 0x16 &&
                    nextByte == 0x3;
            }
        }


        /// <summary>
        /// 是否为ssh协议（客户端先发送SSH-2.0-xxx\r\n标识）
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        private static bool IsSshProtocol(ReadOnlySequence<byte> buffer)
        {
            if (buffer.Length < 4)
            {
                return false;
            }

            var span = new ReadOnlySpan<byte>(new byte[] { (byte)'S', (byte)'S', (byte)'H', (byte)'-' });
            return buffer.Slice(0, 4).ToArray().AsSpan().SequenceEqual(span);
        }
    }
}

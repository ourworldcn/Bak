﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using OW;
using OW.DDD;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace System.Net.Sockets
{
    public class OwRdmServerOptions : IOptions<OwRdmServerOptions>
    {
        public OwRdmServerOptions Value => this;

        /// <summary>
        /// 侦听地址。
        /// </summary>
        /// <value>默认侦听虚四段表示法中的 0.0.0.0。</value>
        public string ListernAddress { get; set; } = "0.0.0.0";

        /// <summary>
        /// 侦听端口。
        /// </summary>
        /// <value>默认端口0，即自动选定。</value>
        public ushort ListernPort { get; set; }
    }

    /// <summary>
    /// 远程客户端信息类
    /// </summary>
    public class OwRdmRemoteEntry
    {
        /// <summary>
        /// 构造函数。
        /// </summary>
        public OwRdmRemoteEntry()
        {
        }

        /// <summary>
        /// 远端的唯一标识。客户端是可能因为路由不同而在服务器端看来端点地址不同的。目前该版本仅低24位有效。大约支持400万客户端总数，未来可能考虑回收使用。
        /// </summary>
        public int Id { get; set; }

        #region 发送相关

        /// <summary>
        /// 已发送的数据。按收到的包号升序排序。
        /// 暂存这里等待确认到达后删除。
        /// </summary>
        public OrderedQueue<OwRdmDgram> SendedData { get; set; } = new OrderedQueue<OwRdmDgram>();

        /// <summary>
        /// 远程终结点。
        /// </summary>
        public volatile EndPoint RemoteEndPoint;

        /// <summary>
        /// 最后一次接到客户端发来数据的时间。
        /// </summary>
        public DateTime LastReceivedUtc { get; set; }

        /// <summary>
        /// 包序号，记录了已用的最大序号，可能需要回绕。
        /// </summary>
        public uint MaxSeq;

        /// <summary>
        /// 客户端确认收到的最大连续包的序号。
        /// </summary>
        public uint RemoteMaxReceivedSeq;
        #endregion 发送相关
    }

    /// <summary>
    /// 支持无连接、面向消息、以可靠方式发送的消息，并保留数据中的消息边界,底层使用Udp来实现。
    /// RDM（以可靠方式发送的消息）消息会依次到达，不会重复。 此外，如果消息丢失，将会通知发送方。 
    /// 如果使用 Rdm 初始化 Socket，则在发送和接收数据之前无需建立远程主机连接。 利用 Rdm，您可以与多个对方主机进行通信。
    /// </summary>
    public class OwRdmServer : SocketAsyncWrapper, IDisposable
    {

        public OwRdmServer(IOptions<OwRdmServerOptions> options, ILogger<OwRdmServer> logger, IHostApplicationLifetime hostApplicationLifetime)
            : base(new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
        {
            _Options = options;
            _Logger = logger;
            _HostApplicationLifetime = hostApplicationLifetime;
            //初始化
            Initialize();
        }

        void Initialize()
        {
            Socket.Bind(new IPEndPoint(IPAddress.Parse(_Options.Value.ListernAddress), _Options.Value.ListernPort));

            for (int i = 0; i < 2; i++) //暂定使用两个侦听，避免漏接
            {
                var dgram = OwRdmDgram.Rent();
                dgram.Count = dgram.Buffer.Length;
                ReceiveFromAsync(new ArraySegment<byte>(dgram.Buffer, dgram.Offset, dgram.Count), Socket.LocalEndPoint, dgram);
            }
            _Timer = new Timer(c =>
            {
                var now = DateTime.UtcNow;
                var timeout = TimeSpan.FromSeconds(2);
                foreach (var key in _Id2ClientEntry.Keys)
                {
                    using var dw = GetOrAddEntry(key, out var entry, TimeSpan.Zero);
                    if (dw.IsEmpty) continue;
                    for (var node = entry.SendedData.First; node is not null; node = node.Next)
                    {
                        var dgram = node.Value.Item1;
                        if (DateTime.UtcNow - dgram.LastSendDateTime > timeout)  //若超时未得到回应
                        {
                            dgram.LastSendDateTime = DateTime.UtcNow;
                            SendToAsync(new ArraySegment<byte>(dgram.Buffer, dgram.Offset, dgram.Count), entry.RemoteEndPoint, null);
                        }
                        else //若找到一个尚未超时的包
                            break;
                    }
                }
            }, null, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1));
        }

        #region 属性及相关

        Timer _Timer;

        /// <summary>
        /// 存储配置信息的字段。
        /// </summary>
        IOptions<OwRdmServerOptions> _Options;

        /// <summary>
        /// 存储日志接口字段。
        /// </summary>
        ILogger<OwRdmServer> _Logger;

        /// <summary>
        /// 允许通知使用者应用程序生存期事件。
        /// </summary>
        IHostApplicationLifetime _HostApplicationLifetime;

        /// <summary>
        /// 每个客户端的信息。
        /// </summary>
        ConcurrentDictionary<int, OwRdmRemoteEntry> _Id2ClientEntry = new ConcurrentDictionary<int, OwRdmRemoteEntry>();

        /// <summary>
        /// 已经使用的最大Id值。
        /// </summary>
        int _MaxId;

        /// <summary>
        /// 侦听的端点。
        /// </summary>
        public EndPoint ListernEndPoing { get => Socket.LocalEndPoint; }
        #endregion 属性及相关

        #region 方法

        #region 发送及相关

        public void SendToAsync(byte[] buffer, int startIndex, int count, int id)
        {
            if (!_Id2ClientEntry.ContainsKey(id))    //若尚未建立连接
            {
                SendAsync(buffer, startIndex, count, id);
                return;
            }
            using var dw = GetOrAddEntry(id, out var entry, TimeSpan.Zero);
            if (dw.IsEmpty) //若未能成功锁定
            {
                SendAsync(buffer, startIndex, count, id);
                return;
            }
            var list = OwRdmDgram.Split(buffer, startIndex, count);
            if (list.Count > 0)
            {
                list[0].Kind |= OwRdmDgramKind.StartDgram;
                list[^1].Kind |= OwRdmDgramKind.EndDgram;
                foreach (var dgram in list)
                {
                    dgram.Id = id;
                    dgram.Seq = (int)Interlocked.Increment(ref entry.MaxSeq);
                    SendToAsync(new ArraySegment<byte>(dgram.Buffer, dgram.Offset, dgram.Count), entry.RemoteEndPoint, null);
                    dgram.LastSendDateTime = DateTime.UtcNow;
                    entry.SendedData.Insert((uint)dgram.Seq, dgram);
                }
            }
        }

        public async void SendAsync(byte[] buffer, int startIndex, int count, int id)
        {
            var list = OwRdmDgram.Split(buffer, startIndex, count);
            await Task.Run(() =>
            {
                var now = DateTime.UtcNow;
                while (!_Id2ClientEntry.ContainsKey(id))    //若尚未建立连接
                {
                    if (DateTime.UtcNow - now > TimeSpan.FromMinutes(1))
                    {
                        _Logger.LogWarning("等待Id={Id}的客户端连接超时", id);
                        return;
                    }
                    Thread.Sleep(10);
                }
                using var dw = GetOrAddEntry(id, out var entry, Timeout.InfiniteTimeSpan);
                if (list.Count > 0)
                {
                    list[0].Kind |= OwRdmDgramKind.StartDgram;
                    list[^1].Kind |= OwRdmDgramKind.EndDgram;
                    foreach (var dgram in list)
                    {
                        dgram.Id = id;
                        dgram.Seq = (int)Interlocked.Increment(ref entry.MaxSeq);
                        SendToAsync(new ArraySegment<byte>(dgram.Buffer, dgram.Offset, dgram.Count), entry.RemoteEndPoint, null);
                        dgram.LastSendDateTime = DateTime.UtcNow;
                        entry.SendedData.Insert((uint)dgram.Seq, dgram);
                    }
                }
            });
        }

        #endregion 发送及相关

        /// <summary>
        /// 获取指定Id的远程端点信息，锁定并返回。
        /// </summary>
        /// <param name="id"></param>
        /// <param name="entry"></param>
        /// <param name="timeSpan"></param>
        /// <returns></returns>
        public DisposeHelper<OwRdmRemoteEntry> GetOrAddEntry(int id, out OwRdmRemoteEntry entry, TimeSpan timeSpan)
        {
            DisposeHelper<OwRdmRemoteEntry> result = DisposeHelper.Empty<OwRdmRemoteEntry>();
            entry = _Id2ClientEntry.GetOrAdd(id, c => new OwRdmRemoteEntry { Id = id });
            if (Monitor.TryEnter(entry, timeSpan))
            {
                if (_Id2ClientEntry.TryGetValue(id, out var entry2) && ReferenceEquals(entry2, entry))  //若成功锁定
                    result = DisposeHelper.Create(c => Monitor.Exit(c), entry);
                else
                {
                    Monitor.Exit(entry);
                    result = DisposeHelper.Empty<OwRdmRemoteEntry>();
                }
            }
            return result;
        }

        protected override void ProcessReceiveFrom(SocketAsyncEventArgs e)
        {
            if (e.BytesTransferred <= 0) goto goon;
            if (e.UserToken is not OwRdmDgram dgram || !dgram.Kind.HasFlag(OwRdmDgramKind.CommandDgram) ||
                !dgram.Kind.HasFlag(OwRdmDgramKind.StartDgram) || !dgram.Kind.HasFlag(OwRdmDgramKind.EndDgram)) goto goon;   //若非命令帧
            if (dgram.Id == 0)   //若是连接包
            {
                var id = Interlocked.Increment(ref _MaxId);
                using var dw = GetOrAddEntry(id, out var entry, TimeSpan.FromMilliseconds(1));
                if (dw.IsEmpty) goto goon;   //若无法锁定则忽略此连接包
                if (entry.RemoteEndPoint != null) goto goon; //若已被并发初始化或是重复连接包
                entry.RemoteEndPoint = e.RemoteEndPoint;
                var sendDgram = OwRdmDgram.Rent();
                sendDgram.Kind = OwRdmDgramKind.CommandDgram | OwRdmDgramKind.StartDgram | OwRdmDgramKind.EndDgram;
                sendDgram.Id = id;
                sendDgram.Seq = 0;
                sendDgram.Count = 8;
                SendToAsync(sendDgram.Buffer, entry.RemoteEndPoint, null);
            }
            else if (_Id2ClientEntry.ContainsKey(dgram.Id)) //若是心跳包
            {
                using var dw = GetOrAddEntry(dgram.Id, out var entry, TimeSpan.FromMilliseconds(1));
                if (dw.IsEmpty) goto goon;   //若无法锁定则忽略此连接包
                if (entry.MaxSeq >= (uint)dgram.Seq)    //若客户端确认的包号合法
                {
                    while (entry.SendedData.First?.Value.Item1.Seq <= (uint)dgram.Seq)   //若客户端确认新的包已经到达
                    {
                        var tmp = entry.SendedData.First.Value.Item1;
                        entry.SendedData.RemoveFirst();
                        OwRdmDgram.Return(tmp);
                    }
                }
            }
        goon:
            base.ProcessReceiveFrom(e);
        }

        #endregion 方法

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
            }
            //将大型字段设置为null以便于释放空间
            base.Dispose(disposing);
        }
    }

    public class TestRdm
    {
        public TestRdm(IServiceProvider serviceProvider)
        {
            _ServiceProvider = serviceProvider;
            Initialize();
        }

        IServiceProvider _ServiceProvider;

        OwRdmClient _Client;

        OwRdmServer _Server;

        public void Initialize()
        {
            _Server = _ServiceProvider.GetService<OwRdmServer>();
            _Client = new OwRdmClient(new IPEndPoint(IPAddress.Parse("192.168.0.104"), 50000));
        }

        public void Test()
        {
            var buffer = new byte[2048];
            for (int i = 0; i < buffer.Length; i++) buffer[i] = (byte)(i % byte.MaxValue + 1);

            _Server.SendToAsync(buffer, 0, buffer.Length, 1);
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using HexCastingTerraria.Addons.HexDebug.Core;
using Terraria;
using Terraria.ModLoader.Config;

namespace HexCastingTerraria.Addons.HexDebug.Game;

/// <summary>
/// HexDebug 的客户端配置项（上游 HexDebugClientConfig 里外部调试器的两项），挂在客户端「附属兼容」页。
/// </summary>
public sealed class HexDebugClientOptions
{
    /// <summary>
    /// 上游 openDebugPort（上游默认开）。移植版默认关：多数人只用游戏内的调试面板，用不到编辑器；要连 VSCode 再打开。
    /// </summary>
    [DefaultValue(false)]
    public bool OpenDebugPort { get; set; }

    /// <summary>上游 debugPort（1024..65535，默认 4444）。</summary>
    [DefaultValue(4444)]
    [Range(1024, 65535)]
    public int DebugPort { get; set; } = 4444;

    public override bool Equals(object? obj) => obj is HexDebugClientOptions o && o.OpenDebugPort == OpenDebugPort && o.DebugPort == DebugPort;

    public override int GetHashCode() => HashCode.Combine(OpenDebugPort, DebugPort);
}

/// <summary>
/// 外部调试器的本机端口（上游 adapter/proxy/DebugProxyClient.kt）：编辑器（VSCode 等）连到玩家自己电脑上的这个端口，
/// 收到的 DAP 消息不在本地解析，原样转给服务端（单机就是本地）的调试适配器；服务端回来的消息原样写回编辑器。
/// 同时只接一个编辑器；进世界时打开、退出世界时关掉。
///
/// 和上游不同：只监听本机（127.0.0.1）。上游监听所有网卡，同一局域网的人也能连进来操纵你的调试器。
/// </summary>
internal static class HexDebugProxy
{
    private static readonly object Lock = new();
    private static readonly ConcurrentQueue<string> Incoming = new();

    private static TcpListener? _listener;
    private static Thread? _thread;
    private static NetworkStream? _stream;
    private static int _port;

    /// <summary>
    /// 进世界、改了配置时调用：按配置开或关端口（端口号变了就重开）。
    /// 配置改动会在模组加载、配置还没全部加载时就调进来 —— 不在世界里（gameMenu）就只关端口，什么配置都不读。
    /// </summary>
    public static void Reconfigure(HexDebugClientOptions? options = null)
    {
        if (Main.dedServ) return;
        if (Main.gameMenu)
        {
            Stop();
            return;
        }
        var o = options ?? global::HexCastingTerraria.Config.HexAddonsClientConfig.Instance?.HexDebugOptions;
        if (o is not { OpenDebugPort: true } || !AddonRegistry.IsEnabled("hexdebug"))
        {
            Stop();
            return;
        }
        if (_listener is not null && _port == o.DebugPort) return;
        Stop();
        Start(o.DebugPort);
    }

    private static void Start(int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        try
        {
            listener.Start();
        }
        catch (SocketException e)
        {
            // 上游：Failed to open local proxy server!（只记日志）
            HexCastingTerraria.Instance?.Logger.Error($"[HexDebug] 打不开调试端口 {port}：{e.Message}");
            return;
        }
        _listener = listener;
        _port = port;
        _thread = new Thread(() => AcceptLoop(listener)) { IsBackground = true, Name = "HexDebugProxy" };
        _thread.Start();
        HexCastingTerraria.Instance?.Logger.Info($"[HexDebug] 等待调试客户端连接：127.0.0.1:{port}");
    }

    public static void Stop()
    {
        var listener = _listener;
        _listener = null;
        try { listener?.Stop(); }
        catch (SocketException) { }
        CloseClient();
        _thread = null;
        while (Incoming.TryDequeue(out _)) { }
    }

    private static void AcceptLoop(TcpListener listener)
    {
        while (ReferenceEquals(_listener, listener))
        {
            TcpClient client;
            try
            {
                client = listener.AcceptTcpClient();
            }
            catch (Exception)
            {
                return;   // 监听停了
            }
            HexCastingTerraria.Instance?.Logger.Debug("[HexDebug] 调试客户端已连接");
            using (client)
            {
                var stream = client.GetStream();
                lock (Lock) _stream = stream;
                var framing = new DapFraming();
                var buffer = new byte[8192];
                try
                {
                    int n;
                    while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        foreach (var msg in framing.Feed(buffer.AsSpan(0, n))) Incoming.Enqueue(msg);
                    }
                }
                catch (Exception)
                {
                    // 编辑器断开了
                }
                finally
                {
                    lock (Lock)
                    {
                        if (ReferenceEquals(_stream, stream)) _stream = null;
                    }
                }
            }
        }
    }

    private static void CloseClient()
    {
        lock (Lock)
        {
            try { _stream?.Close(); }
            catch (Exception) { }
            _stream = null;
        }
    }

    /// <summary>每帧（主线程）：把编辑器发来的消息转给服务端。</summary>
    public static void Pump()
    {
        if (Main.gameMenu || Main.LocalPlayer is not { active: true }) return;
        while (Incoming.TryDequeue(out var json)) HexDebugNet.SendDap(json);
    }

    /// <summary>服务端回来的一条消息：写回编辑器（没连着就丢掉，和上游一样）。</summary>
    public static void Deliver(string json)
    {
        lock (Lock)
        {
            if (_stream is null) return;
            try
            {
                var bytes = DapFraming.Frame(json);
                _stream.Write(bytes, 0, bytes.Length);
                _stream.Flush();
            }
            catch (Exception)
            {
                _stream = null;
            }
        }
    }
}

/// <summary>每帧转发编辑器的消息；退出世界、卸载时关端口。</summary>
public sealed class HexDebugProxySystem : AddonSystem
{
    public override string AddonId => "hexdebug";

    public override void PostUpdateEverything()
    {
        if (!Main.dedServ) HexDebugProxy.Pump();
    }

    public override void OnWorldUnload() => HexDebugProxy.Stop();

    public override void Unload() => HexDebugProxy.Stop();
}

using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using Serilog;

namespace Foldspace.App.Platform;

/// <summary>
/// 單一執行個體：具名 Mutex 判斷是否已在執行；已在執行時把命令列參數經 Named Pipe 交給它。
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly AppPaths _paths;
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cts = new();

    private SingleInstance(AppPaths paths, Mutex mutex)
    {
        _paths = paths;
        _mutex = mutex;
    }

    /// <summary>成為常駐執行個體；已有其他執行個體時回傳 null。</summary>
    public static SingleInstance? TryAcquire(AppPaths paths)
    {
        var mutex = new Mutex(initiallyOwned: true, paths.MutexName, out var createdNew);
        if (createdNew)
            return new SingleInstance(paths, mutex);
        mutex.Dispose();
        return null;
    }

    /// <summary>把參數交給既有執行個體。沒有參數表示「顯示設定視窗」。</summary>
    public static bool SendToRunningInstance(AppPaths paths, string[] args)
    {
        // 使用者剛啟動的是這個程序，只有它有權把視窗帶到前景；先把這個權限交給既有執行個體。
        // 否則 Windows 11 會讓設定視窗開在其他視窗後面。
        AllowSetForegroundWindow(AsfwAny);
        try
        {
            using var pipe = new NamedPipeClientStream(".", paths.PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(3000);
            JsonSerializer.Serialize(pipe, args);
            pipe.Flush();
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>接收其他啟動嘗試送來的參數（在背景執行緒呼叫 <paramref name="onArgs"/>）。</summary>
    public void Listen(Action<string[]> onArgs)
    {
        _ = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await using var pipe = new NamedPipeServerStream(_paths.PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(_cts.Token);
                    var args = await JsonSerializer.DeserializeAsync<string[]>(pipe, cancellationToken: _cts.Token) ?? [];
                    onArgs(args);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Single-instance pipe error");
                    await Task.Delay(500);
                }
            }
        });
    }

    private const int AsfwAny = -1;

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    public void Dispose()
    {
        _cts.Cancel();
        try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex.Dispose();
    }
}

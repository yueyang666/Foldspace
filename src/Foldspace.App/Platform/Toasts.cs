using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Toolkit.Uwp.Notifications;
using Serilog;
using Foldspace.Localization;

namespace Foldspace.App.Platform;

/// <summary>Windows Toast 通知。點擊通知或按鈕的事件在背景執行緒觸發。</summary>
public sealed class Toasts
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _questions = new();

    public Toasts()
    {
        ToastNotificationManagerCompat.OnActivated += OnActivated;
    }

    /// <summary>程式是否因為使用者點了舊的通知而被啟動（此時命令列參數不是檔案路徑）。</summary>
    public static bool WasProcessToastActivated()
    {
        try { return ToastNotificationManagerCompat.WasCurrentProcessToastActivated(); }
        catch { return false; }
    }

    public void Show(string title, string body, string? selectPath = null)
    {
        try
        {
            var builder = new ToastContentBuilder().AddText(title).AddText(body);
            if (selectPath is not null)
                builder.AddArgument("action", "reveal").AddArgument("path", selectPath);
            builder.Show();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not show notification: {Title}", title);
        }
    }

    /// <summary>「接收時詢問」：通知上有接收 / 拒絕按鈕。取消（60 秒逾時）視為拒絕。</summary>
    public async Task<bool> AskAsync(string title, string body, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _questions[id] = tcs;
        try
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(body)
                .AddArgument("question", id)
                .AddButton(new ToastButton().SetContent(Strings.Current.Accept).AddArgument("action", "accept").AddArgument("question", id))
                .AddButton(new ToastButton().SetContent(Strings.Current.Reject).AddArgument("action", "reject").AddArgument("question", id))
                .SetToastScenario(ToastScenario.Reminder) // 不會自動消失，直到使用者回應
                .Show(toast =>
                {
                    toast.Tag = id;
                    toast.ExpirationTime = DateTimeOffset.Now.AddSeconds(65);
                });

            await using var _ = ct.Register(() => tcs.TrySetResult(false));
            return await tcs.Task.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warning(ex, "Could not show the receive confirmation; treating it as declined");
            return false;
        }
        finally
        {
            _questions.TryRemove(id, out _);
            try { ToastNotificationManagerCompat.History.Remove(id); } catch { /* 通知可能已被移除 */ }
        }
    }

    private void OnActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        try
        {
            var args = ToastArguments.Parse(e.Argument);
            if (args.TryGetValue("question", out var id) && _questions.TryGetValue(id, out var question))
            {
                args.TryGetValue("action", out var action);
                // 點通知本體不算回答；只有按鈕算。
                if (action is "accept" or "reject")
                    question.TrySetResult(action == "accept");
                return;
            }

            if (args.TryGetValue("action", out var a) && a == "reveal" && args.TryGetValue("path", out var path))
                RevealInExplorer(path);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error while handling a notification click");
        }
    }

    /// <summary>開啟所在資料夾並選取該項目。</summary>
    public static void RevealInExplorer(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        else if (Path.GetDirectoryName(path) is { } dir && Directory.Exists(dir))
            OpenFolder(dir);
    }

    public static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }
}

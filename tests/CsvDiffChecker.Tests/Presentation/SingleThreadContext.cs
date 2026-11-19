using System.Collections.Concurrent;

namespace CsvDiffChecker.Tests.Presentation;

/// <summary>
/// 1つのスレッドで後続の処理を順に実行する同期コンテキスト。
/// WinForms の UI スレッドと同じく、await の後の処理が呼び出し元のスレッドに戻ることを再現する。
/// </summary>
internal sealed class SingleThreadContext : SynchronizationContext
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();

    public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

    public override void Send(SendOrPostCallback d, object? state) =>
        throw new NotSupportedException("このテスト用の同期コンテキストは Send に対応しない。");

    /// <summary>この同期コンテキストの上で非同期の処理を実行し、終わるまで待つ。</summary>
    public static void Run(Func<Task> work)
    {
        var previous = Current;
        var context = new SingleThreadContext();
        SetSynchronizationContext(context);
        try
        {
            var task = work();
            task.ContinueWith(_ => context._queue.CompleteAdding(), TaskScheduler.Default);
            foreach (var (callback, state) in context._queue.GetConsumingEnumerable())
                callback(state);
            task.GetAwaiter().GetResult();
        }
        finally
        {
            SetSynchronizationContext(previous);
        }
    }
}

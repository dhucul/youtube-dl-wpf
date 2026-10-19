namespace YoutubeDl.Engine.Http;

/// <summary>Cancels owned work and retires its gate only after active operations have drained.</summary>
internal sealed class SessionLifetime : IDisposable
{
    private readonly object _sync=new();
    private readonly CancellationTokenSource _shutdown=new();
    private bool _disposed,_canceling;private int _active;
    internal SemaphoreSlim Gate {get;}=new(1,1);
    internal Operation Begin(CancellationToken ct)
    {
        lock(_sync){ObjectDisposedException.ThrowIf(_disposed,this);ct.ThrowIfCancellationRequested();var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,_shutdown.Token);_active++;return new(this,linked);}
    }
    internal T Read<T>(Func<T> action){lock(_sync){ObjectDisposedException.ThrowIf(_disposed,this);return action();}}
    internal void Commit(CancellationToken ct,Action action){lock(_sync){ct.ThrowIfCancellationRequested();ObjectDisposedException.ThrowIf(_disposed,this);action();}}
    private void End(){lock(_sync){_active--;Retire();}}
    private void Retire(){if(_disposed&&!_canceling&&_active==0){Gate.Dispose();_shutdown.Dispose();}}
    internal sealed class Operation(SessionLifetime owner,CancellationTokenSource linked):IDisposable
    {internal CancellationToken Token=>linked.Token;public void Dispose(){linked.Dispose();owner.End();}}
    internal void Dispose(Action clear)
    {
        lock(_sync){if(_disposed)return;_disposed=true;_canceling=true;clear();}
        try{_shutdown.Cancel();}
        finally{lock(_sync){_canceling=false;Retire();}}
    }
    public void Dispose()=>Dispose(()=>{});
}

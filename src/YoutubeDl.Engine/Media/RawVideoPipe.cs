using System.Diagnostics;
using System.Text;

namespace YoutubeDl.Engine.Media;

internal sealed class RawVideoPipe:IAsyncDisposable
{
    private readonly Process _process;private readonly ProcessLifetime _lifetime;private readonly CancellationTokenRegistration _cancel;private readonly Task<string> _error;private readonly Task<string>? _stdout;private readonly bool _writer;private bool _finished;
    internal Stream Stream=>_writer?_process.StandardInput.BaseStream:_process.StandardOutput.BaseStream;
    internal RawVideoPipe(string tool,IEnumerable<string> arguments,bool writer,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();_writer=writer;var start=new ProcessStartInfo(tool){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=writer,RedirectStandardOutput=true,RedirectStandardError=true};if(!Path.GetFileNameWithoutExtension(tool).Equals("ffprobe",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add("-nostdin");foreach(var a in arguments)start.ArgumentList.Add(a);_process=new(){StartInfo=start};if(!_process.Start())throw new IOException("Could not start owned raw-video processor.");_lifetime=ProcessLifetime.Attach(_process);_cancel=ct.Register(Kill);_error=Tail(_process.StandardError);if(writer)_stdout=Tail(_process.StandardOutput);
    }
    internal async Task<bool> ReadAsync(Memory<byte> frame,CancellationToken ct)
    {
        var at=0;while(at<frame.Length){var count=await Stream.ReadAsync(frame[at..],ct).ConfigureAwait(false);if(count==0){if(at!=0)throw new InvalidDataException("Truncated decoded raw-video picture.");return false;}at+=count;}return true;
    }
    internal async Task FinishAsync(CancellationToken ct)
    {
        if(_writer)_process.StandardInput.Close();await _process.WaitForExitAsync(ct).ConfigureAwait(false);var error=await _error.ConfigureAwait(false);if(_stdout is not null)await _stdout.ConfigureAwait(false);if(_process.ExitCode!=0||!string.IsNullOrWhiteSpace(error))throw new InvalidDataException("Raw-video processor failed: "+error);_finished=true;
    }
    private void Kill(){try{if(!_process.HasExited)_process.Kill(true);}catch(InvalidOperationException){}}
    public async ValueTask DisposeAsync(){_cancel.Dispose();if(!_finished)Kill();try{await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);await _error.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);if(_stdout is not null)await _stdout.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);}finally{_lifetime.Dispose();_process.Dispose();}}
    private static async Task<string> Tail(StreamReader input){var text=new StringBuilder();var buffer=new char[2048];while(true){var count=await input.ReadAsync(buffer).ConfigureAwait(false);if(count==0)return text.ToString();text.Append(buffer,0,count);if(text.Length>8192)text.Remove(0,text.Length-8192);}}
}

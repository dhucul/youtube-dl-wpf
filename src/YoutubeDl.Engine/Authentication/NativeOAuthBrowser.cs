using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine.Authentication;

public static class NativeOAuthBrowser
{
    public static async Task<NativeOAuthCredentials> SignInAsync(NativeOAuthCredentials registration,MediaHttp http,CancellationToken ct,Func<Uri,CancellationToken,Task>? openBrowser=null)
    {
        NativeOAuthSession.ValidateRegistration(registration);
        var redirect=registration.RedirectUri;
        if(registration.Provider==NativeOAuthProvider.Vimeo&&redirect is null)throw new ArgumentException("Vimeo browser sign-in requires its registered IP-loopback callback.");
        if(redirect is not null&&(redirect.Scheme!="http"||redirect.Host is not ("127.0.0.1" or "[::1]")||redirect.IsDefaultPort||redirect.UserInfo.Length>0||redirect.Query.Length>0||redirect.Fragment.Length>0))throw new ArgumentException("Browser sign-in requires a registered IP-loopback callback with an explicit port.");
        var listener=new TcpListener(redirect?.Host=="[::1]"?IPAddress.IPv6Loopback:IPAddress.Loopback,redirect?.Port??0);listener.Start(4);
        using var stop=CancellationTokenSource.CreateLinkedTokenSource(ct);stop.CancelAfter(TimeSpan.FromMinutes(10));
        redirect??=new("http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port+"/oauth/"+Guid.NewGuid().ToString("N"));
        using var flow=new NativeOAuthLogin(registration.Provider,registration.ClientId,redirect,registration.ClientSecret);
        var callback=ReceiveAsync();
        try{await (openBrowser??OpenSystemBrowser)(flow.AuthorizationUrl,stop.Token).ConfigureAwait(false);return await callback.ConfigureAwait(false);}
        finally{stop.Cancel();listener.Stop();try{await callback.ConfigureAwait(false);}catch(Exception e)when(e is OperationCanceledException or SocketException or IOException or InvalidDataException or NotSupportedException or HttpRequestException or InvalidOperationException){} }
        async Task<NativeOAuthCredentials> ReceiveAsync()
        {
            for(var attempt=0;attempt<4;attempt++)
            {
                using var client=await listener.AcceptTcpClientAsync(stop.Token).ConfigureAwait(false);await using var stream=client.GetStream();
                var header=new List<byte>();var one=new byte[1];while(header.Count<8192){if(await stream.ReadAsync(one,stop.Token).ConfigureAwait(false)!=1)break;header.Add(one[0]);if(header.Count>=4&&header.TakeLast(4).SequenceEqual(new byte[]{13,10,13,10}))break;}
                Uri? returned=null;
                try
                {
                    if(header.Count>=8192||!header.TakeLast(4).SequenceEqual(new byte[]{13,10,13,10}))throw new InvalidDataException();
                    var lines=Encoding.ASCII.GetString(header.ToArray()).Split("\r\n");var first=lines[0].Split(' ');if(first.Length!=3||first[0]!="GET"||first[2]!="HTTP/1.1"||!first[1].StartsWith('/')||first[1].StartsWith("//"))throw new InvalidDataException();
                    var hosts=lines.Skip(1).Where(l=>l.StartsWith("Host:",StringComparison.OrdinalIgnoreCase)).Select(l=>l[5..].Trim()).ToArray();if(hosts.Length!=1||hosts[0]!=redirect.Authority)throw new InvalidDataException();
                    returned=new Uri(redirect.GetLeftPart(UriPartial.Authority)+first[1]);var values=NativeOAuthLogin.Parse(returned.Query);var expected=NativeOAuthLogin.Parse(flow.AuthorizationUrl.Query)["state"];
                    if(returned.GetLeftPart(UriPartial.Path)!=redirect.AbsoluteUri||values.GetValueOrDefault("state")!=expected)throw new InvalidDataException();
                }
                catch(Exception e)when(e is InvalidDataException or UriFormatException or ArgumentException){await Reply(stream,false,stop.Token).ConfigureAwait(false);continue;}
                try{var result=await flow.CompleteAsync(returned!,http,stop.Token).ConfigureAwait(false);await Reply(stream,true,stop.Token).ConfigureAwait(false);return result;}
                catch{await Reply(stream,false,stop.Token).ConfigureAwait(false);throw;}
            }
            throw new InvalidDataException("Browser sign-in received too many unrelated callbacks.");
        }
    }
    private static Task OpenSystemBrowser(Uri url,CancellationToken ct){ct.ThrowIfCancellationRequested();using var process=Process.Start(new ProcessStartInfo(url.AbsoluteUri){UseShellExecute=true});return Task.CompletedTask;}
    private static async Task Reply(Stream stream,bool success,CancellationToken ct)
    {
        var body=success?"Sign-in complete. Return to the app.":"Sign-in could not be verified. Return to the app.";var bytes=Encoding.ASCII.GetBytes("HTTP/1.1 "+(success?"200 OK":"400 Bad Request")+"\r\nContent-Type: text/plain; charset=utf-8\r\nCache-Control: no-store\r\nContent-Security-Policy: default-src 'none'\r\nConnection: close\r\nContent-Length: "+Encoding.UTF8.GetByteCount(body)+"\r\n\r\n"+body);await stream.WriteAsync(bytes,ct).ConfigureAwait(false);
    }
}

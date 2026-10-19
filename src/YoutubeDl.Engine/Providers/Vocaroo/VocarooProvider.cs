using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using YoutubeDl.Engine.Discovery;
using YoutubeDl.Engine.Download;
using YoutubeDl.Engine.Http;

namespace YoutubeDl.Engine.Providers.Vocaroo;

public sealed class VocarooProvider : IMediaProvider
{
    public MediaProviderInfo Info { get; } = new("vocaroo","Vocaroo public recordings and selected players",200);
    public bool CanHandle(Uri url) => VocarooAddress.Matches(url);
    public DownloadArchiveIdentity? GetArchiveIdentity(NativeDownloadRequest request) => new("vocaroo",VocarooAddress.Parse(request.Url).Id);
    public async Task<PageMetadata> ExtractAsync(NativeDownloadRequest request, MediaHttp http, CancellationToken ct)
    {
        var address = VocarooAddress.Parse(request.Url); ct.ThrowIfCancellationRequested(); using var anonymous = http.CreateAnonymousSession();
        var page = await anonymous.GetDocumentAsync(address.Page,512*1024,ct).ConfigureAwait(false);
        var final = VocarooAddress.Parse(page.Url);
        if (final.Id != address.Id || final.Direct || page.ContentType is not ("text/html" or "application/xhtml+xml")) throw new InvalidDataException("Vocaroo returned another recording page or unsupported document.");
        ValidateBootstrap(page.Text,ct);
        using var message = new HttpRequestMessage(HttpMethod.Head,address.Media); message.Headers.Referrer = new("https://vocaroo.com/");
        using var response = await anonymous.SendAsync(message,ct).ConfigureAwait(false); response.EnsureSuccessStatusCode(); ct.ThrowIfCancellationRequested();
        if (response.StatusCode != HttpStatusCode.OK || (response.RequestMessage?.RequestUri ?? address.Media) != address.Media)
            throw new InvalidDataException("Vocaroo redirected or returned a partial recording descriptor.");
        if (response.Content.Headers.ContentType?.MediaType is not ("audio/mpeg" or "audio/mp3") || response.Content.Headers.ContentLength is not > 0)
            throw new InvalidDataException("Vocaroo omitted a complete MP3 byte count or media type.");
        var bytes = response.Content.Headers.ContentLength!.Value;
        var declared = Header(response,"Content-Disposition"); string? filename = null;
        if (declared is not null)
        {
            if (!ContentDispositionHeaderValue.TryParse(declared,out var disposition)) throw new InvalidDataException("Vocaroo returned an invalid recording filename.");
            filename = disposition.FileNameStar ?? disposition.FileName?.Trim('"');
            if (filename != "Vocaroo "+address.Id+".mp3") throw new InvalidDataException("Vocaroo's MP3 filename declares another recording.");
        }
        var blob = Header(response,"x-bz-file-name");
        if (blob is not null && blob != "media/mp3/"+address.Id) throw new InvalidDataException("Vocaroo's storage descriptor declares another recording.");
        if (filename is null && blob is null) throw new InvalidDataException("Vocaroo omitted a recording-bound resource descriptor.");
        var sha1 = Header(response,"x-bz-content-sha1");
        if (sha1 is not null && !Regex.IsMatch(sha1,@"\A[0-9a-fA-F]{40}\z",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)))
            throw new NotSupportedException("This Vocaroo storage digest needs additional native support.");
        return new()
        {
            Id=address.Id,Extractor="vocaroo",ArchiveIdentity=new("vocaroo",address.Id),Title="Vocaroo "+address.Id,SourcePageUrl=address.Canonical,AnonymousPlayback=true,
            PlaybackHeaders=new Dictionary<string,string>{["Referer"]="https://vocaroo.com/"},
            MetadataFields=new Dictionary<string,object?>{["source_file_size"]=bytes,["source_sha1"]=sha1},
            Media=[new(){Id="mp3",Url=address.Media,Kind=MediaKind.Progressive,Extension="mp3",MimeType="audio/mpeg",Source="vocaroo",HasAudio=true,HasVideo=false,IsCompleteMedia=true,
                FileSize=bytes,VerificationFileSize=bytes,VerificationSha1=sha1,VerificationDemuxer="mp3"}],
            ReportedFormats=[new(){Id="mp3",Url=address.Media,Extension="mp3",MimeType="audio/mpeg",Protocol="https",AudioOnly=true,HasAudio=true,HasVideo=false,FileSize=bytes}]
        };
    }
    private static void ValidateBootstrap(string html,CancellationToken ct)
    {
        var clean = Regex.Replace(html,@"<!--.*?-->","",RegexOptions.Singleline|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
        var scripts = Regex.Matches(clean,@"<script\b[^>]*>(?<body>.*?)</script\s*>",RegexOptions.IgnoreCase|RegexOptions.Singleline|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100))
            .Select(match=>match.Groups["body"].Value).Where(body=>Regex.IsMatch(body,@"\A\s*var\s+process\s*=",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100))).ToArray();
        if (scripts.Length != 1) throw new InvalidDataException("Vocaroo did not expose one bounded public configuration.");
        var script = scripts[0]; var prefix = Regex.Match(script,@"\A\s*var\s+process\s*=\s*\{\s*env\s*:\s*",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
        if (!prefix.Success) throw new InvalidDataException("Vocaroo's public configuration is not a supported literal.");
        var data = JavaScriptDataLiteral.ObjectArgument(script,prefix.Length,out var end,ct);
        if (script[end..].Trim() is not ("}" or "};")) throw new InvalidDataException("Vocaroo appended an executable or ambiguous configuration.");
        if (data["MEDIA_MP3_FILE_URL"] is not System.Text.Json.Nodes.JsonValue first || !first.TryGetValue<string>(out var oldGateway) || oldGateway != "https://media.vocaroo.com/mp3/" ||
            data["MEDIA_MP3_FILE_URL1"] is not System.Text.Json.Nodes.JsonValue second || !second.TryGetValue<string>(out var currentGateway) || currentGateway != "https://media1.vocaroo.com/mp3/")
            throw new InvalidDataException("Vocaroo's published recording gateways changed or are incomplete.");
    }
    private static string? Header(HttpResponseMessage response,string name)
    {
        if (!response.Headers.TryGetValues(name,out var values) && !response.Content.Headers.TryGetValues(name,out values)) return null;
        var all=values.Take(2).ToArray();
        if (all.Length!=1 || all[0].Length>256 || all[0].Any(char.IsControl)) throw new InvalidDataException("Vocaroo returned an ambiguous resource descriptor.");
        return all[0].Trim();
    }
}

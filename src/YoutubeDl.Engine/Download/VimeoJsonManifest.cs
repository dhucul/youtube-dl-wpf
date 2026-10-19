using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static YoutubeDl.Engine.Providers.ProviderJson;
namespace YoutubeDl.Engine.Download;

// This is the issuer's bounded static MP4 inventory, not arbitrary JSON DASH.
internal static class VimeoJsonManifest
{
    internal static DashManifest Parse(string json,Uri url,bool live)
    {
        if(live||url.Scheme!="https"||!url.IsDefaultPort||!url.Host.EndsWith(".vimeocdn.com",StringComparison.OrdinalIgnoreCase)||url.UserInfo.Length>0||url.Fragment.Length>0||!url.AbsolutePath.EndsWith("/playlist.json",StringComparison.Ordinal))
            throw new NotSupportedException("Only the declared static Vimeo JSON segment inventory is supported.");
        using var doc=JsonDocument.Parse(json,new(){MaxDepth=64});var fields=0;Validate(doc.RootElement,ref fields);var root=doc.RootElement;
        var clip=Text(root,"clip_id");if(clip is null||!Guid.TryParseExact(clip,"D",out _)||!url.AbsolutePath.Contains("/"+clip+"/",StringComparison.Ordinal))throw new InvalidDataException("The Vimeo inventory changed its clip identity.");
        var prefix=url.AbsolutePath[..(url.AbsolutePath.IndexOf("/"+clip+"/",StringComparison.Ordinal)+clip.Length+2)];
        var baseUrl=Resolve(url,Text(root,"base_url")??throw new InvalidDataException("Vimeo omitted its media base."),prefix);
        var tracks=new List<DashRepresentation>();var ids=new HashSet<string>();var resources=0;var initTotal=0;
        foreach(var role in new[]{"video","audio"})
        {
            if(!root.TryGetProperty(role,out var inventory)||inventory.ValueKind!=JsonValueKind.Array||inventory.GetArrayLength()>32)throw new InvalidDataException("Vimeo omitted a bounded explicit track inventory.");
            foreach(var item in inventory.EnumerateArray())
            {
                var id=Text(item,"id");var mime=Text(item,"mime_type");var codec=Text(item,"codecs");var duration=Number(item,"duration");var bitrate=Integer(item,"bitrate");
                if(id is null||!Regex.IsMatch(id,@"\A[A-Za-z0-9_-]{1,100}\z")||!ids.Add(role+":"+id)||mime!=role+"/mp4"||codec is null||!Regex.IsMatch(codec,role=="video"?@"\A(?:avc1|hvc1|hev1|av01|vp09)\.[A-Za-z0-9.]{1,100}\z":@"\A(?:mp4a\.[A-Za-z0-9.]{1,100}|opus|fLaC)\z")||duration is not >0||!double.IsFinite(duration.Value)||duration>604800||bitrate is not >0)
                    throw new InvalidDataException("Vimeo supplied incomplete or unsupported track facts.");
                var trackBase=Resolve(baseUrl,Text(item,"base_url")??throw new InvalidDataException("Vimeo omitted its track base."),prefix);
                var inline=Text(item,"init_segment");var initUrl=Text(item,"init_segment_url");byte[]? initialization=null;Uri? remote=null;
                if(!string.IsNullOrEmpty(inline))
                {
                    if(!string.IsNullOrEmpty(initUrl)||inline.Length>350000)throw new InvalidDataException("Vimeo supplied ambiguous or excessive initialization.");
                    try{initialization=Convert.FromBase64String(inline);}catch(FormatException e){throw new InvalidDataException("Vimeo supplied invalid initialization encoding.",e);}
                    if(initialization.Length is <16 or >262144||(initTotal+=initialization.Length)>4*1024*1024)throw new InvalidDataException("Vimeo initialization exceeds its limit.");
                    Init(initialization);
                }
                else if(!string.IsNullOrEmpty(initUrl))remote=Resolve(trackBase,initUrl,trackBase.AbsolutePath);
                else throw new InvalidDataException("Vimeo omitted MP4 initialization.");
                if(!item.TryGetProperty("segments",out var list)||list.ValueKind!=JsonValueKind.Array||list.GetArrayLength()==0)throw new InvalidDataException("Vimeo omitted its complete segment list.");
                var segments=new List<DashSegment>();var end=0d;
                foreach(var segment in list.EnumerateArray())
                {
                    var start=Number(segment,"start");var next=Number(segment,"end");var size=Long(segment,"size");
                    if(++resources>100000||start is null||next is null||!double.IsFinite(start.Value)||!double.IsFinite(next.Value)||Math.Abs(start.Value-end)>0.00001||next<=start||next>duration+0.00001||size is not >0||size>512L*1024*1024)
                        throw new InvalidDataException("Vimeo segment timing, size or complete inventory is invalid.");
                    var resource=Resolve(trackBase,Text(segment,"url")??throw new InvalidDataException("Vimeo omitted a segment URL."),trackBase.AbsolutePath);
                    segments.Add(new(resource,null,(decimal)start.Value,next.Value-start.Value,segments.Count+1));end=next.Value;
                }
                if(Math.Abs(end-duration.Value)>0.00001)throw new InvalidDataException("Vimeo truncated its declared track duration.");
                tracks.Add(new(){Id=role+"-"+id,Url=trackBase,MimeType=mime,Codecs=codec,Height=role=="video"?Integer(item,"height"):null,Bandwidth=bitrate,FramesPerSecond=role=="video"?Number(item,"framerate"):null,Initialization=remote,InlineInitialization=initialization,Segments=segments});
            }
        }
        if(tracks.Count==0)throw new InvalidDataException("Vimeo supplied no complete clear MP4 tracks.");
        return new(){SourceUrl=url,Representations=tracks};
    }
    private static Uri Resolve(Uri parent,string relative,string prefix)
    {
        if(!Uri.TryCreate(parent,relative,out var uri)||uri.Scheme!="https"||uri.Host!=parent.Host||!uri.IsDefaultPort||uri.UserInfo.Length>0||uri.Fragment.Length>0||!uri.AbsolutePath.StartsWith(prefix,StringComparison.Ordinal))throw new InvalidDataException("Vimeo segments changed their declared clip/track scope.");
        return uri;
    }
    private static void Init(byte[] data)
    {
        var offset=0;var ftyp=false;var moov=false;
        while(offset<data.Length)
        {
            if(data.Length-offset<8)throw new InvalidDataException("Vimeo supplied incomplete MP4 initialization.");
            var size=BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset,4));if(size<8||size>data.Length-offset)throw new InvalidDataException("Vimeo supplied unsupported MP4 box addressing.");
            var type=Encoding.ASCII.GetString(data,offset+4,4);ftyp|=type=="ftyp";moov|=type=="moov";if(type=="mdat")throw new InvalidDataException("Vimeo initialization contains undeclared media.");offset+=(int)size;
        }
        if(!ftyp||!moov)throw new InvalidDataException("Vimeo initialization omitted its MP4 format or movie.");
        foreach(var marker in new[]{"pssh","sinf","tenc","encv","enca"})if(data.AsSpan().IndexOf(Encoding.ASCII.GetBytes(marker))>=0)throw new NotSupportedException("Protected Vimeo MP4 initialization cannot be transferred.");
    }
    private static void Validate(JsonElement node,ref int fields)
    {
        if(node.ValueKind==JsonValueKind.Object)
        {
            var names=new HashSet<string>();foreach(var p in node.EnumerateObject()){if(++fields>500000||!names.Add(p.Name))throw new InvalidDataException("Vimeo JSON fields are repeated or excessive.");if(p.Name is "drm" or "encryption" or "content_protection"&&p.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.False))throw new NotSupportedException("Protected Vimeo inventories cannot be transferred.");Validate(p.Value,ref fields);}
        }
        else if(node.ValueKind==JsonValueKind.Array)foreach(var item in node.EnumerateArray())Validate(item,ref fields);
    }
}

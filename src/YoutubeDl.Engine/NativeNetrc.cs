using System.Text;

namespace YoutubeDl.Engine;

/// <summary>Read-only, bounded netrc lookup for the explicitly selected server; default entries are not account scopes.</summary>
internal static class NativeNetrc
{
    internal static async Task<NativeAuthenticationOptions> ReadAsync(string location,string host,string requestedUser,CancellationToken ct)
    {
        string path;
        if(location.Length>0){path=Path.GetFullPath(location);if(Directory.Exists(path))path=Choose(path);}
        else path=Choose(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        try
        {
            await using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,8192,FileOptions.Asynchronous|FileOptions.SequentialScan);
            if(input.Length>1024*1024)throw new InvalidDataException("The netrc file exceeds its limit.");
            using var memory=new MemoryStream();var buffer=new byte[8192];int count;
            while((count=await input.ReadAsync(buffer,ct).ConfigureAwait(false))>0){if(memory.Length+count>1024*1024)throw new InvalidDataException("The netrc file exceeds its limit.");memory.Write(buffer,0,count);}
            return Parse(new UTF8Encoding(false,true).GetString(memory.ToArray()),host,requestedUser,ct);
        }
        catch(OperationCanceledException){throw;}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or DecoderFallbackException){throw new InvalidDataException("The selected netrc file could not be read.");}
    }
    private static string Choose(string directory)
    {
        var dot=Path.Combine(directory,".netrc");var windows=Path.Combine(directory,"_netrc");
        return File.Exists(dot)?dot:windows;
    }
    internal static NativeAuthenticationOptions Parse(string text,string host,string requestedUser,CancellationToken ct)
    {
        if(text.Length>1024*1024||text.Contains('\0'))throw new InvalidDataException("The netrc data is invalid or excessive.");
        text=text.TrimStart('\uFEFF');
        var tokens=Tokens(text,ct);var entries=new List<(string Machine,string User,string Password)>();string machine="",user="",password="";var hasUser=false;var hasPassword=false;
        void Store(){if(machine.Length>0)entries.Add((machine,user,password));}
        for(var index=0;index<tokens.Count;index++)
        {
            ct.ThrowIfCancellationRequested();var token=tokens[index];
            if(token is "machine" or "default"){Store();machine=token=="machine"?Value():"default";user=password="";hasUser=hasPassword=false;}
            else if(token is "login" or "user"){if(hasUser)throw new InvalidDataException("The netrc repeats an account field.");user=Value();hasUser=true;}
            else if(token=="password"){if(hasPassword)throw new InvalidDataException("The netrc repeats an account field.");password=Value();hasPassword=true;}
            else if(token=="account")_=Value();
            else throw new InvalidDataException("The netrc contains an unsupported or misplaced field.");
            string Value(){if(++index>=tokens.Count)throw new InvalidDataException("The netrc ends before a field value.");return tokens[index];}
        }
        Store();var matches=entries.Where(entry=>entry.Machine.Equals(host,StringComparison.OrdinalIgnoreCase)&&(requestedUser.Length==0||entry.User==requestedUser)).ToArray();
        if(matches.Length!=1||matches[0].User.Length==0||matches[0].Password.Length==0)throw new InvalidDataException("The netrc needs one complete, unambiguous account entry for the selected server.");
        return new(){Username=matches[0].User,Password=matches[0].Password};
    }
    private static List<string> Tokens(string text,CancellationToken ct)
    {
        var tokens=new List<string>();var index=0;var expectingValue=false;
        while(index<text.Length)
        {
            ct.ThrowIfCancellationRequested();if(char.IsWhiteSpace(text[index])){index++;continue;}
            if(text[index]=='#'){while(index<text.Length&&text[index]!='\n')index++;continue;}
            var value=new StringBuilder();var quote=text[index]=='"';if(quote)index++;var closed=!quote;
            while(index<text.Length)
            {
                var ch=text[index++];if(quote&&ch=='"'){closed=true;break;}if(!quote&&char.IsWhiteSpace(ch))break;
                if(ch=='\\'){if(index>=text.Length)throw new InvalidDataException("The netrc ends in an escape.");ch=text[index++];}
                if(char.IsControl(ch))throw new InvalidDataException("A netrc value contains a control character.");value.Append(ch);if(value.Length>16384)throw new InvalidDataException("A netrc value exceeds its limit.");
            }
            if(!closed)throw new InvalidDataException("The netrc has an unterminated quoted value.");
            var token=value.ToString();
            if(expectingValue){tokens.Add(token);expectingValue=false;continue;}
            if(token=="macdef")
            {
                // Macros are never executed. Their bodies run until a blank line.
                while(index<text.Length&&text[index]!='\n')index++;if(index<text.Length)index++;
                while(index<text.Length){var end=text.IndexOf('\n',index);if(end<0){index=text.Length;break;}var blank=string.IsNullOrWhiteSpace(text[index..end]);index=end+1;if(blank)break;}
                continue;
            }
            tokens.Add(token);if(tokens.Count>100000)throw new InvalidDataException("The netrc has too many fields.");
            expectingValue=token is "machine" or "login" or "user" or "password" or "account";
        }
        return tokens;
    }
}

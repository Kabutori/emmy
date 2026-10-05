using System.Security.Cryptography;
using System.Text;

namespace Emmy.Host;

public sealed class Secrets(string directory)
{
    private string PathName => Path.Combine(directory,"deepseek.secret");
    public string Read()
    {
        var environment=Environment.GetEnvironmentVariable("EMMY_DEEPSEEK_KEY");
        if(!string.IsNullOrEmpty(environment))return environment;
        if(!OperatingSystem.IsWindows() || !File.Exists(PathName))return "";
        try{return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(PathName),null,DataProtectionScope.CurrentUser));}
        catch(CryptographicException){return "";}
    }
    public void Write(string key)
    {
        if(string.IsNullOrWhiteSpace(key)) { if(File.Exists(PathName))File.Delete(PathName); return; }
        if(!OperatingSystem.IsWindows())throw new InvalidOperationException("Auf diesem System EMMY_DEEPSEEK_KEY verwenden");
        File.WriteAllBytes(PathName,ProtectedData.Protect(Encoding.UTF8.GetBytes(key),null,DataProtectionScope.CurrentUser));
    }
}

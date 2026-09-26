using System.Diagnostics;

namespace ArticleReplication;

public sealed record ExecutedCommand(string Executable,string[] Arguments,string WorkingDirectory,DateTime StartedUtc,DateTime FinishedUtc,int ExitCode,Dictionary<string,string> Environment);

public static class Commands
{
    public static readonly Dictionary<string,string> SingleThread = new(){["DOTNET_PROCESSOR_COUNT"]="1",["OMP_NUM_THREADS"]="1",["MKL_NUM_THREADS"]="1",["OPENBLAS_NUM_THREADS"]="1"};
    public static async Task Run(string logs,string name,string executable,IEnumerable<string> arguments,string cwd)
    {
        Directory.CreateDirectory(logs);string record=Path.Combine(logs,name+".json");
        if(File.Exists(record)||File.Exists(Path.Combine(logs,name+".started.json")))throw new IOException("Command already attempted: "+name);
        var args=arguments.ToArray();var started=DateTime.UtcNow;
        var info=new ProcessStartInfo(executable){WorkingDirectory=cwd,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(string a in args)info.ArgumentList.Add(a);
        foreach(var pair in SingleThread)info.Environment[pair.Key]=pair.Value;
        Files.Save(Path.Combine(logs,name+".started.json"),new{Executable=executable,Arguments=args,WorkingDirectory=cwd,StartedUtc=started,Environment=SingleThread});
        using var stdout=new FileStream(Path.Combine(logs,name+".stdout.log"),FileMode.CreateNew);
        using var stderr=new FileStream(Path.Combine(logs,name+".stderr.log"),FileMode.CreateNew);
        using var p=Process.Start(info)??throw new IOException("Could not start "+executable);
        await Task.WhenAll(p.StandardOutput.BaseStream.CopyToAsync(stdout),p.StandardError.BaseStream.CopyToAsync(stderr),p.WaitForExitAsync());
        Files.Save(record,new ExecutedCommand(executable,args,cwd,started,DateTime.UtcNow,p.ExitCode,SingleThread));
        if(p.ExitCode!=0)throw new IOException($"{name} exited {p.ExitCode}; inspect {logs}.");
    }
    public static Task Worker(string logs,string name,string cwd,params string[] args)=>Run(logs,name,"dotnet",new[]{typeof(Program).Assembly.Location}.Concat(args),cwd);
}

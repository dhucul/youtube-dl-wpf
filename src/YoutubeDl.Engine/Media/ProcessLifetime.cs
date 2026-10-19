using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace YoutubeDl.Engine.Media;

// The Windows app's owned media helpers must not keep writing after their parent dies.
internal sealed class ProcessLifetime:IDisposable
{
    private readonly SafeFileHandle? _job;
    private ProcessLifetime(SafeFileHandle? job)=>_job=job;
    internal static ProcessLifetime Attach(Process process)
    {
        if(!OperatingSystem.IsWindows())return new(null);
        var job=CreateJobObject(IntPtr.Zero,null);
        if(job.IsInvalid)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var limits=new ExtendedLimits{Basic=new(){Flags=0x2000}};
            if(!SetInformationJobObject(job,9,ref limits,(uint)Marshal.SizeOf<ExtendedLimits>())||!AssignProcessToJobObject(job,process.Handle))
            {
                var error=Marshal.GetLastWin32Error();try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}
                throw new System.ComponentModel.Win32Exception(error);
            }
            return new(job);
        }
        catch{job.Dispose();throw;}
    }
    public void Dispose()=>_job?.Dispose();
    [StructLayout(LayoutKind.Sequential)]private struct BasicLimits{public long ProcessTime,JobTime;public uint Flags;public UIntPtr MinimumWorkingSet,MaximumWorkingSet;public uint ActiveProcesses;public UIntPtr Affinity;public uint Priority,Scheduling;}
    [StructLayout(LayoutKind.Sequential)]private struct Counters{public ulong ReadOperations,WriteOperations,OtherOperations,ReadBytes,WriteBytes,OtherBytes;}
    [StructLayout(LayoutKind.Sequential)]private struct ExtendedLimits{public BasicLimits Basic;public Counters Io;public UIntPtr ProcessMemory,JobMemory,PeakProcessMemory,PeakJobMemory;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern SafeFileHandle CreateJobObject(IntPtr attributes,string? name);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool SetInformationJobObject(SafeFileHandle job,int informationClass,ref ExtendedLimits information,uint length);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool AssignProcessToJobObject(SafeFileHandle job,IntPtr process);
}

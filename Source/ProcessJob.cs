using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
public sealed class ProcessJob : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] struct Basic { public long PerProcessTime,PerJobTime; public uint Flags; public UIntPtr Minimum,Maximum; public uint Active; public UIntPtr Affinity; public uint Priority,Scheduling; }
    [StructLayout(LayoutKind.Sequential)] struct IO { public ulong ReadOps,WriteOps,OtherOps,ReadBytes,WriteBytes,OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] struct Extended { public Basic Basic; public IO IO; public UIntPtr ProcessMemory,JobMemory,PeakProcessMemory,PeakJobMemory; }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateJobObject(IntPtr attributes,string? name);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetInformationJobObject(IntPtr job,int info,ref Extended value,int length);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool TerminateJobObject(IntPtr job,uint exitCode);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    IntPtr handle;
    public ProcessJob(){handle=CreateJobObject(IntPtr.Zero,null);if(handle==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();var value=new Extended();value.Basic.Flags=0x2000;if(!SetInformationJobObject(handle,9,ref value,Marshal.SizeOf(value))){int error=Marshal.GetLastWin32Error();Dispose();throw new System.ComponentModel.Win32Exception(error);}}
    public void Attach(Process child){if(!AssignProcessToJobObject(handle,child.Handle)&&!child.HasExited)throw new System.ComponentModel.Win32Exception();}
    public void Stop(){if(handle!=IntPtr.Zero&&!TerminateJobObject(handle,1))throw new System.ComponentModel.Win32Exception();}
    public void Dispose(){if(handle!=IntPtr.Zero){CloseHandle(handle);handle=IntPtr.Zero;}}
}



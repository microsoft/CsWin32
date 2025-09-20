using System;

namespace CsWin32.BuildTask.Demo
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("CsWin32 BuildTask Demo");
            Console.WriteLine("This project demonstrates using the CsWin32 MSBuild task to generate P/Invoke methods at build time.");
            
            // When the MSBuild task is working, we would be able to use generated methods like:
            // var processId = NativeMethods.GetProcessId(NativeMethods.GetCurrentProcess());
            // Console.WriteLine($"Current process ID: {processId}");
        }
    }
}

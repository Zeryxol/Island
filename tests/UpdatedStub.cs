using System;
using System.IO;
using System.Reflection;
[assembly: AssemblyVersion("1.3.0.0")]
class UpdatedStub
{
    static void Main(string[] args) { File.WriteAllText(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "updated.txt"), string.Join(" ", args)); }
}

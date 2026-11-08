using System;
using System.Reflection;
using System.Windows;
class UpdaterFixture
{
    [STAThread] static void Main(string[] args)
    {
        var app = Assembly.LoadFrom(args[0]); var type = app.GetType("Island.Updates");
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        type.GetField("staged", flags).SetValue(null, args[1]);
        type.GetField("digest", flags).SetValue(null, type.GetMethod("Hash", flags).Invoke(null, new object[] { args[1] }));
        new Application(); type.GetMethod("Install").Invoke(null, null);
    }
}

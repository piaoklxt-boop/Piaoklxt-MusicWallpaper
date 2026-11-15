using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;

namespace QQMusicWallpaper;

internal enum DirectActionResult { Unsupported, Delivered, Rejected, Uncertain }
internal enum MediaSessionCommand { Toggle, Previous, Next, Seek, Play, Pause }

// Reflect the optional Windows 10 WinRT projection so the controller retains
// its existing framework target. No shell process, global media key or mouse.
internal static class MediaSessionCommands
{
    private const string Prefix = "Windows.Media.Control.GlobalSystemMediaTransportControlsSession";
    private static Type WinType(string name)
    { return Type.GetType(name + ", Windows.Media.Control, ContentType=WindowsRuntime", false); }
    private static object Call(object instance, Type type, string method, params object[] args)
    { return type.GetMethod(method).Invoke(instance, args); }
    private static object Property(object instance, Type type, string name)
    { return type.GetProperty(name).GetValue(instance, null); }
    private static object Await(object operation, Type result)
    {
        var assembly = Assembly.Load("System.Runtime.WindowsRuntime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089");
        foreach (var method in assembly.GetType("System.WindowsRuntimeSystemExtensions").GetMethods())
        {
            if (method.Name != "AsTask" || !method.IsGenericMethodDefinition || method.GetParameters().Length != 1 ||
                method.GetParameters()[0].ParameterType.Name != "IAsyncOperation`1") continue;
            var task = (Task)method.MakeGenericMethod(result).Invoke(null, new[] { operation });
            if (!task.Wait(1500)) throw new TimeoutException();
            return task.GetType().GetProperty("Result").GetValue(task, null);
        }
        throw new NotSupportedException();
    }
    private static object Session()
    {
        Type managerType = WinType(Prefix + "Manager");
        Type sessionType = WinType(Prefix);
        if (managerType == null || sessionType == null) return null;
        object manager = Await(Call(null, managerType, "RequestAsync"), managerType);
        object selected = null;
        foreach (object session in (IEnumerable)Call(manager, managerType, "GetSessions"))
        {
            if (!string.Equals((string)Property(session, sessionType, "SourceAppUserModelId"), "Tencent.QQMusic", StringComparison.Ordinal)) continue;
            if (selected != null) return null; // Never select a different/current player or guess among duplicates.
            selected = session;
        }
        return selected;
    }
    internal static string ReadPlaybackState()
    {
        object session = Session(); if (session == null) return "Unavailable";
        object info = Call(session, WinType(Prefix), "GetPlaybackInfo");
        return Property(info, WinType(Prefix + "PlaybackInfo"), "PlaybackStatus").ToString();
    }
    internal static DirectActionResult Execute(uint processId, MediaSessionCommand command, long positionTicks)
    {
        bool attempted = false;
        try
        {
            if (!UniquePlayer(processId)) return DirectActionResult.Rejected;
            object session = Session(); if (session == null) return DirectActionResult.Unsupported;
            Type sessionType = WinType(Prefix);
            object info = Call(session, sessionType, "GetPlaybackInfo");
            object controls = Property(info, WinType(Prefix + "PlaybackInfo"), "Controls");
            string capability, method;
            switch (command)
            {
                case MediaSessionCommand.Toggle: capability = "IsPlayPauseToggleEnabled"; method = "TryTogglePlayPauseAsync"; break;
                case MediaSessionCommand.Next: capability = "IsNextEnabled"; method = "TrySkipNextAsync"; break;
                case MediaSessionCommand.Previous: capability = "IsPreviousEnabled"; method = "TrySkipPreviousAsync"; break;
                case MediaSessionCommand.Seek: capability = "IsPlaybackPositionEnabled"; method = "TryChangePlaybackPositionAsync"; break;
                case MediaSessionCommand.Play: capability = "IsPlayEnabled"; method = "TryPlayAsync"; break;
                case MediaSessionCommand.Pause: capability = "IsPauseEnabled"; method = "TryPauseAsync"; break;
                default: return DirectActionResult.Unsupported;
            }
            if (!(bool)Property(controls, WinType(Prefix + "PlaybackControls"), capability)) return DirectActionResult.Unsupported;
            if (!UniquePlayer(processId)) return DirectActionResult.Rejected;
            if (command == MediaSessionCommand.Seek && positionTicks < 0) return DirectActionResult.Rejected;
            attempted = true;
            object operation = Call(session, sessionType, method, command == MediaSessionCommand.Seek ? new object[] { positionTicks } : new object[0]);
            // False/timeout after issuing a command is not permission to click again.
            return (bool)Await(operation, typeof(bool)) ? DirectActionResult.Delivered : DirectActionResult.Uncertain;
        }
        catch (Exception error)
        {
            RuntimeLog.Write("Native media command: " + command + "; attempted=" + attempted + "; " + error.GetType().Name);
            return attempted ? DirectActionResult.Uncertain : DirectActionResult.Unsupported;
        }
    }
    private static bool UniquePlayer(uint processId)
    {
        var players = Process.GetProcessesByName("QQMusic");
        try { return players.Length == 1 && players[0].Id == processId && !players[0].HasExited; }
        finally { foreach (var player in players) player.Dispose(); }
    }
}

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Nikkiward.Features.GamepadControl;

internal sealed record GameBarRegistryValue([property: JsonRequired] uint Type, [property: JsonRequired] byte[] Data)
{
    public static GameBarRegistryValue Disabled { get; } = new((uint)RegistryValueKind.DWord, [0, 0, 0, 0]);

    public bool IsDisabled => Type == Disabled.Type && Data.AsSpan().SequenceEqual(Disabled.Data);
}

internal interface IGameBarGuideRegistry
{
    GameBarRegistryValue? Read();
    void Write(GameBarRegistryValue? value);
}

internal sealed class GameBarGuideRegistry : IGameBarGuideRegistry
{
    private const string KeyPath = @"Software\Microsoft\GameBar";
    private const string ValueName = "UseNexusForGameBarEnabled";
    private const int FileNotFound = 2;
    private const int MoreData = 234;

    public GameBarRegistryValue? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        if (key is null)
        {
            return null;
        }

        // Read the native bytes so even unexpanded strings and uncommon registry types round trip.
        byte[] data = new byte[sizeof(int)];
        while (true)
        {
            var length = (uint)data.Length;
            var status = RegQueryValueEx(key.Handle, ValueName, IntPtr.Zero, out var type, data, ref length);
            switch (status)
            {
                case 0:
                    return new GameBarRegistryValue(type, data.AsSpan(0, checked((int)length)).ToArray());
                case FileNotFound:
                    return null;
                case MoreData:
                    data = new byte[length];
                    break;
                default:
                    throw new Win32Exception(status);
            }
        }
    }

    public void Write(GameBarRegistryValue? value)
    {
        if (value is null)
        {
            using var existing = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
            if (existing is not null)
            {
                existing.DeleteValue(ValueName, throwOnMissingValue: false);
                CheckStatus(RegFlushKey(existing.Handle));
            }
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        CheckStatus(RegSetValueEx(key.Handle, ValueName, 0, value.Type, value.Data, (uint)value.Data.Length));
        CheckStatus(RegFlushKey(key.Handle));
    }

    private static void CheckStatus(int status)
    {
        if (status != 0)
        {
            throw new Win32Exception(status);
        }
    }

    [DllImport("advapi32.dll", EntryPoint = "RegQueryValueExW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int RegQueryValueEx(SafeRegistryHandle key, string name, IntPtr reserved,
        out uint type, [Out] byte[] data, ref uint length);

    [DllImport("advapi32.dll", EntryPoint = "RegSetValueExW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int RegSetValueEx(SafeRegistryHandle key, string name, uint reserved,
        uint type, byte[] data, uint length);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern int RegFlushKey(SafeRegistryHandle key);
}

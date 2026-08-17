using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Windows.System;
using WindowsInput;

namespace Nikkiward.Features.GamepadControl;

/// <summary>
/// Translates between the space-separated key text a user types into the button
/// mapping box ("Ctrl Alt F1") and the virtual key codes SendInput needs.
/// Ported from Starward 0.18.1 (MIT, Copyright (c) 2023 Scighost).
/// </summary>
internal static class GamepadKeyNames
{
    /// <summary>
    /// Splits and validates mapping text. On success <paramref name="normalizedTextOrBadKey"/>
    /// is the canonical spelling to persist and echo back; on failure it is the
    /// first token that could not be recognised.
    /// </summary>
    public static bool TryParse(
        string? value,
        [NotNullWhen(true)] out string? normalizedTextOrBadKey,
        out VirtualKeyCode[] modifiers,
        out VirtualKeyCode[] keys)
    {
        var modifierList = new List<VirtualKeyCode>();
        var keyList = new List<VirtualKeyCode>();
        modifiers = [];
        keys = [];

        var tokens = value?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
        foreach (var token in tokens)
        {
            if (!KeyNameToVirtualKey.TryGetValue(token.ToUpperInvariant(), out var virtualKey))
            {
                normalizedTextOrBadKey = token;
                return false;
            }

            if (virtualKey is VirtualKey.Shift or VirtualKey.Control or VirtualKey.Menu or VirtualKey.LeftWindows)
            {
                modifierList.Add((VirtualKeyCode)virtualKey);
            }
            else
            {
                keyList.Add((VirtualKeyCode)virtualKey);
            }
        }

        modifiers = [.. modifierList];
        keys = [.. keyList];
        normalizedTextOrBadKey = Format(modifiers, keys);
        return true;
    }

    /// <summary>
    /// Renders a mapping back to display text, modifiers first.
    /// </summary>
    public static string Format(VirtualKeyCode[] modifiers, VirtualKeyCode[] keys) =>
        string.Join(
            " ",
            modifiers.Concat(keys).Select(x => VirtualKeyToKeyName.GetValueOrDefault((VirtualKey)x)));

    public static readonly Dictionary<string, VirtualKey> KeyNameToVirtualKey = new()
    {
        ["SHIFT"] = VirtualKey.Shift,
        ["CTRL"] = VirtualKey.Control,
        ["CONTROL"] = VirtualKey.Control,
        ["ALT"] = VirtualKey.Menu,
        ["MENU"] = VirtualKey.Menu,
        ["WIN"] = VirtualKey.LeftWindows,
        ["WINDOW"] = VirtualKey.LeftWindows,
        ["WINDOWS"] = VirtualKey.LeftWindows,

        ["INS"] = VirtualKey.Insert,
        ["INSERT"] = VirtualKey.Insert,
        ["DEL"] = VirtualKey.Delete,
        ["DELETE"] = VirtualKey.Delete,
        ["HOME"] = VirtualKey.Home,
        ["END"] = VirtualKey.End,
        ["PAGEUP"] = VirtualKey.PageUp,
        ["PGUP"] = VirtualKey.PageUp,
        ["PAGEDOWN"] = VirtualKey.PageDown,
        ["PGDN"] = VirtualKey.PageDown,
        ["←"] = VirtualKey.Left,
        ["↑"] = VirtualKey.Up,
        ["→"] = VirtualKey.Right,
        ["↓"] = VirtualKey.Down,
        ["LEFT"] = VirtualKey.Left,
        ["UP"] = VirtualKey.Up,
        ["RIGHT"] = VirtualKey.Right,
        ["DOWN"] = VirtualKey.Down,

        ["0"] = VirtualKey.Number0,
        ["1"] = VirtualKey.Number1,
        ["2"] = VirtualKey.Number2,
        ["3"] = VirtualKey.Number3,
        ["4"] = VirtualKey.Number4,
        ["5"] = VirtualKey.Number5,
        ["6"] = VirtualKey.Number6,
        ["7"] = VirtualKey.Number7,
        ["8"] = VirtualKey.Number8,
        ["9"] = VirtualKey.Number9,

        ["A"] = VirtualKey.A,
        ["B"] = VirtualKey.B,
        ["C"] = VirtualKey.C,
        ["D"] = VirtualKey.D,
        ["E"] = VirtualKey.E,
        ["F"] = VirtualKey.F,
        ["G"] = VirtualKey.G,
        ["H"] = VirtualKey.H,
        ["I"] = VirtualKey.I,
        ["J"] = VirtualKey.J,
        ["K"] = VirtualKey.K,
        ["L"] = VirtualKey.L,
        ["M"] = VirtualKey.M,
        ["N"] = VirtualKey.N,
        ["O"] = VirtualKey.O,
        ["P"] = VirtualKey.P,
        ["Q"] = VirtualKey.Q,
        ["R"] = VirtualKey.R,
        ["S"] = VirtualKey.S,
        ["T"] = VirtualKey.T,
        ["U"] = VirtualKey.U,
        ["V"] = VirtualKey.V,
        ["W"] = VirtualKey.W,
        ["X"] = VirtualKey.X,
        ["Y"] = VirtualKey.Y,
        ["Z"] = VirtualKey.Z,
        ["PAD0"] = VirtualKey.NumberPad0,
        ["PAD1"] = VirtualKey.NumberPad1,
        ["PAD2"] = VirtualKey.NumberPad2,
        ["PAD3"] = VirtualKey.NumberPad3,
        ["PAD4"] = VirtualKey.NumberPad4,
        ["PAD5"] = VirtualKey.NumberPad5,
        ["PAD6"] = VirtualKey.NumberPad6,
        ["PAD7"] = VirtualKey.NumberPad7,
        ["PAD8"] = VirtualKey.NumberPad8,
        ["PAD9"] = VirtualKey.NumberPad9,

        ["PAD+"] = VirtualKey.Add,
        ["PAD-"] = VirtualKey.Subtract,
        ["PAD*"] = VirtualKey.Multiply,
        ["PAD/"] = VirtualKey.Divide,
        ["PAD."] = VirtualKey.Decimal,

        ["F1"] = VirtualKey.F1,
        ["F2"] = VirtualKey.F2,
        ["F3"] = VirtualKey.F3,
        ["F4"] = VirtualKey.F4,
        ["F5"] = VirtualKey.F5,
        ["F6"] = VirtualKey.F6,
        ["F7"] = VirtualKey.F7,
        ["F8"] = VirtualKey.F8,
        ["F9"] = VirtualKey.F9,
        ["F10"] = VirtualKey.F10,
        ["F11"] = VirtualKey.F11,
        ["F12"] = VirtualKey.F12,

        ["`"] = (VirtualKey)192,
        ["-"] = (VirtualKey)189,
        ["="] = (VirtualKey)187,
        ["["] = (VirtualKey)219,
        ["]"] = (VirtualKey)221,
        ["\\"] = (VirtualKey)220,
        [";"] = (VirtualKey)186,
        ["'"] = (VirtualKey)222,
        [","] = (VirtualKey)188,
        ["."] = (VirtualKey)190,
        ["/"] = (VirtualKey)191,

        ["ESC"] = VirtualKey.Escape,
        ["ESCAPE"] = VirtualKey.Escape,
        ["SPACE"] = VirtualKey.Space,
        ["TAB"] = VirtualKey.Tab,
        ["ENTER"] = VirtualKey.Enter,
        ["BACKSPACE"] = VirtualKey.Back,
        ["BACK"] = VirtualKey.Back,
        ["CAPSLOCK"] = VirtualKey.CapitalLock,
        ["CAPITALLOCK"] = VirtualKey.CapitalLock,
        ["NUMLOCK"] = VirtualKey.NumberKeyLock,
        ["NUMLK"] = VirtualKey.NumberKeyLock,
        ["NUMBERKEYLOCK"] = VirtualKey.NumberKeyLock,
        ["SCROLL"] = VirtualKey.Scroll,
        ["SCROLLLOCK"] = VirtualKey.Scroll,
        ["PAUSE"] = VirtualKey.Pause,
        ["PRINT"] = VirtualKey.Print,
        ["PRTSC"] = VirtualKey.Print,
        ["PRINTSCREEN"] = VirtualKey.Print,
    };

    public static readonly Dictionary<VirtualKey, string> VirtualKeyToKeyName = new()
    {
        [VirtualKey.Shift] = "Shift",
        [VirtualKey.Control] = "Ctrl",
        [VirtualKey.Menu] = "Alt",
        [VirtualKey.LeftWindows] = "Win",
        [VirtualKey.RightWindows] = "Win",

        [VirtualKey.Insert] = "Insert",
        [VirtualKey.Delete] = "Delete",
        [VirtualKey.Home] = "Home",
        [VirtualKey.End] = "End",
        [VirtualKey.PageUp] = "PageUp",
        [VirtualKey.PageDown] = "PageDown",
        [VirtualKey.Left] = "←",
        [VirtualKey.Up] = "↑",
        [VirtualKey.Right] = "→",
        [VirtualKey.Down] = "↓",

        [VirtualKey.Number0] = "0",
        [VirtualKey.Number1] = "1",
        [VirtualKey.Number2] = "2",
        [VirtualKey.Number3] = "3",
        [VirtualKey.Number4] = "4",
        [VirtualKey.Number5] = "5",
        [VirtualKey.Number6] = "6",
        [VirtualKey.Number7] = "7",
        [VirtualKey.Number8] = "8",
        [VirtualKey.Number9] = "9",
        [VirtualKey.A] = "A",
        [VirtualKey.B] = "B",
        [VirtualKey.C] = "C",
        [VirtualKey.D] = "D",
        [VirtualKey.E] = "E",
        [VirtualKey.F] = "F",
        [VirtualKey.G] = "G",
        [VirtualKey.H] = "H",
        [VirtualKey.I] = "I",
        [VirtualKey.J] = "J",
        [VirtualKey.K] = "K",
        [VirtualKey.L] = "L",
        [VirtualKey.M] = "M",
        [VirtualKey.N] = "N",
        [VirtualKey.O] = "O",
        [VirtualKey.P] = "P",
        [VirtualKey.Q] = "Q",
        [VirtualKey.R] = "R",
        [VirtualKey.S] = "S",
        [VirtualKey.T] = "T",
        [VirtualKey.U] = "U",
        [VirtualKey.V] = "V",
        [VirtualKey.W] = "W",
        [VirtualKey.X] = "X",
        [VirtualKey.Y] = "Y",
        [VirtualKey.Z] = "Z",

        [VirtualKey.NumberPad0] = "Pad0",
        [VirtualKey.NumberPad1] = "Pad1",
        [VirtualKey.NumberPad2] = "Pad2",
        [VirtualKey.NumberPad3] = "Pad3",
        [VirtualKey.NumberPad4] = "Pad4",
        [VirtualKey.NumberPad5] = "Pad5",
        [VirtualKey.NumberPad6] = "Pad6",
        [VirtualKey.NumberPad7] = "Pad7",
        [VirtualKey.NumberPad8] = "Pad8",
        [VirtualKey.NumberPad9] = "Pad9",

        [VirtualKey.Add] = "Pad+",
        [VirtualKey.Subtract] = "Pad-",
        [VirtualKey.Multiply] = "Pad*",
        [VirtualKey.Divide] = "Pad/",
        [VirtualKey.Decimal] = "Pad.",

        [VirtualKey.F1] = "F1",
        [VirtualKey.F2] = "F2",
        [VirtualKey.F3] = "F3",
        [VirtualKey.F4] = "F4",
        [VirtualKey.F5] = "F5",
        [VirtualKey.F6] = "F6",
        [VirtualKey.F7] = "F7",
        [VirtualKey.F8] = "F8",
        [VirtualKey.F9] = "F9",
        [VirtualKey.F10] = "F10",
        [VirtualKey.F11] = "F11",
        [VirtualKey.F12] = "F12",

        [(VirtualKey)192] = "`",
        [(VirtualKey)189] = "-",
        [(VirtualKey)187] = "=",
        [(VirtualKey)219] = "[",
        [(VirtualKey)221] = "]",
        [(VirtualKey)220] = "\\",
        [(VirtualKey)186] = ";",
        [(VirtualKey)222] = "'",
        [(VirtualKey)188] = ",",
        [(VirtualKey)190] = ".",
        [(VirtualKey)191] = "/",

        [VirtualKey.Escape] = "Esc",
        [VirtualKey.Space] = "Space",
        [VirtualKey.Tab] = "Tab",
        [VirtualKey.Enter] = "Enter",
        [VirtualKey.Back] = "Backspace",
        [VirtualKey.CapitalLock] = "CapsLock",
        [VirtualKey.NumberKeyLock] = "NumberLock",
        [VirtualKey.Scroll] = "ScrollLock",
        [VirtualKey.Pause] = "Pause",
        [VirtualKey.Print] = "PrintScreen",
    };
}

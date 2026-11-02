using System.Runtime.InteropServices;

namespace Kd100Win.Input;

/// <summary>Thin wrapper over SendInput for keys, text, mouse buttons and the wheel.</summary>
internal static class InputSender
{
    const uint INPUT_MOUSE = 0;
    const uint INPUT_KEYBOARD = 1;
    const uint KEYEVENTF_EXTENDEDKEY = 0x1;
    const uint KEYEVENTF_KEYUP = 0x2;
    const uint KEYEVENTF_UNICODE = 0x4;
    const uint MOUSEEVENTF_WHEEL = 0x0800;
    const uint MOUSEEVENTF_HWHEEL = 0x1000;
    const uint MAPVK_VK_TO_VSC = 0;

    /// <summary>Marks our own injected input in dwExtraInfo.</summary>
    static readonly nint Signature = 0x4B443130; // "KD10"

    public static void Keys(IReadOnlyList<ushort> vks, bool down)
    {
        var inputs = new INPUT[vks.Count];
        for (int i = 0; i < vks.Count; i++)
        {
            // Press in order, release in reverse so modifiers wrap the key.
            ushort vk = down ? vks[i] : vks[vks.Count - 1 - i];
            inputs[i] = KeyInput(vk, down);
        }
        Send(inputs);
    }

    public static void Text(string text)
    {
        var inputs = new List<INPUT>(text.Length * 2);
        foreach (char ch in text)
        {
            inputs.Add(UnicodeInput(ch, KEYEVENTF_UNICODE));
            inputs.Add(UnicodeInput(ch, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
        }
        Send(inputs.ToArray());
    }

    public static void Wheel(int delta, bool horizontal) =>
        Send([MouseInput(horizontal ? MOUSEEVENTF_HWHEEL : MOUSEEVENTF_WHEEL, delta)]);

    public static void MouseButton(MouseButtons button, bool down)
    {
        (uint flags, int data) = button switch
        {
            MouseButtons.Left => (down ? 0x0002u : 0x0004u, 0),
            MouseButtons.Right => (down ? 0x0008u : 0x0010u, 0),
            MouseButtons.Middle => (down ? 0x0020u : 0x0040u, 0),
            MouseButtons.XButton1 => (down ? 0x0080u : 0x0100u, 1),
            MouseButtons.XButton2 => (down ? 0x0080u : 0x0100u, 2),
            _ => (0u, 0),
        };
        if (flags != 0)
            Send([MouseInput(flags, data)]);
    }

    static INPUT KeyInput(ushort vk, bool down)
    {
        uint flags = down ? 0 : KEYEVENTF_KEYUP;
        if (KeyNames.IsExtended(vk))
            flags |= KEYEVENTF_EXTENDEDKEY;
        return new INPUT
        {
            Type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                Ki = new KEYBDINPUT
                {
                    Vk = vk,
                    Scan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC),
                    Flags = flags,
                    ExtraInfo = Signature,
                },
            },
        };
    }

    static INPUT UnicodeInput(char ch, uint flags) => new()
    {
        Type = INPUT_KEYBOARD,
        U = new InputUnion { Ki = new KEYBDINPUT { Scan = ch, Flags = flags, ExtraInfo = Signature } },
    };

    static INPUT MouseInput(uint flags, int data) => new()
    {
        Type = INPUT_MOUSE,
        U = new InputUnion { Mi = new MOUSEINPUT { Flags = flags, MouseData = data, ExtraInfo = Signature } },
    };

    static void Send(INPUT[] inputs)
    {
        if (inputs.Length == 0)
            return;
        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
            Log.Info($"SendInput sent {sent}/{inputs.Length} (blocked by UIPI? the target app may run as administrator)");
    }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT
    {
        public uint Type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT Mi;
        [FieldOffset(0)] public KEYBDINPUT Ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT
    {
        public int Dx;
        public int Dy;
        public int MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    static extern uint MapVirtualKey(uint code, uint mapType);
}

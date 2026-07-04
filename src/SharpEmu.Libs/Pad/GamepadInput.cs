// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using Silk.NET.Input;
using Silk.NET.Windowing;

namespace SharpEmu.Libs.Pad;

internal readonly record struct GamepadSnapshot(
    uint Buttons,
    byte LeftX,
    byte LeftY,
    byte RightX,
    byte RightY,
    byte LeftTrigger,
    byte RightTrigger)
{
    public static GamepadSnapshot Neutral { get; } = new(0, 128, 128, 128, 128, 0, 0);
}

internal static class GamepadInput
{
    private const float StickDeadzone = 0.15f;
    private const float TriggerButtonThreshold = 0.5f;
    private static readonly object _gate = new();
    private static IInputContext? _inputContext;
    private static IGamepad? _gamepad;

    public static void Initialize(IWindow window)
    {
        lock (_gate)
        {
            if (_inputContext is not null)
            {
                return;
            }

            try
            {
                _inputContext = window.CreateInput();
                _inputContext.ConnectionChanged += OnConnectionChanged;
                SelectFirstGamepad();
            }
            catch (Exception exception)
            {
                _inputContext?.Dispose();
                _inputContext = null;
                Console.Error.WriteLine(
                    $"[PAD][WARN] Gamepad input initialization failed: {exception.Message}");
            }
        }
    }

    public static GamepadSnapshot Capture()
    {
        lock (_gate)
        {
            if (_gamepad is null || !_gamepad.IsConnected)
            {
                SelectFirstGamepad();
            }

            var gamepad = _gamepad;
            if (gamepad is null)
            {
                return GamepadSnapshot.Neutral;
            }

            uint buttons = 0;
            foreach (var button in gamepad.Buttons)
            {
                if (!button.Pressed)
                {
                    continue;
                }

                buttons |= button.Name switch
                {
                    ButtonName.A => 0x4000u, // Cross
                    ButtonName.B => 0x2000u, // Circle
                    ButtonName.X => 0x8000u, // Square
                    ButtonName.Y => 0x1000u, // Triangle
                    ButtonName.LeftBumper => 0x0400u,
                    ButtonName.RightBumper => 0x0800u,
                    ButtonName.LeftStick => 0x0002u,
                    ButtonName.RightStick => 0x0004u,
                    ButtonName.Start => 0x0008u,
                    ButtonName.Home => 0x00010000u, // PS button
                    ButtonName.Back => 0x00100000u, // Touch pad button
                    ButtonName.DPadUp => 0x0010u,
                    ButtonName.DPadRight => 0x0020u,
                    ButtonName.DPadDown => 0x0040u,
                    ButtonName.DPadLeft => 0x0080u,
                    _ => 0u,
                };
            }

            var left = gamepad.Thumbsticks.Count > 0
                ? gamepad.Thumbsticks[0]
                : default;
            var right = gamepad.Thumbsticks.Count > 1
                ? gamepad.Thumbsticks[1]
                : default;
            var leftTrigger = gamepad.Triggers.Count > 0
                ? ToTriggerByte(gamepad.Triggers[0].Position)
                : (byte)0;
            var rightTrigger = gamepad.Triggers.Count > 1
                ? ToTriggerByte(gamepad.Triggers[1].Position)
                : (byte)0;

            if (leftTrigger >= TriggerButtonThreshold * byte.MaxValue)
            {
                buttons |= 0x0100;
            }

            if (rightTrigger >= TriggerButtonThreshold * byte.MaxValue)
            {
                buttons |= 0x0200;
            }

            return new GamepadSnapshot(
                buttons,
                ToStickByte(left.X),
                ToStickByte(left.Y),
                ToStickByte(right.X),
                ToStickByte(right.Y),
                leftTrigger,
                rightTrigger);
        }
    }

    public static void Shutdown()
    {
        lock (_gate)
        {
            if (_inputContext is not null)
            {
                _inputContext.ConnectionChanged -= OnConnectionChanged;
                _inputContext.Dispose();
            }

            _gamepad = null;
            _inputContext = null;
        }
    }

    private static void OnConnectionChanged(IInputDevice device, bool connected)
    {
        lock (_gate)
        {
            if (!connected && ReferenceEquals(device, _gamepad))
            {
                _gamepad = null;
            }

            if (_gamepad is null)
            {
                SelectFirstGamepad();
            }
        }
    }

    private static void SelectFirstGamepad()
    {
        _gamepad = _inputContext?.Gamepads.FirstOrDefault(gamepad => gamepad.IsConnected);
        if (_gamepad is not null)
        {
            Console.Error.WriteLine($"[PAD][INFO] Using gamepad: {_gamepad.Name}");
        }
    }

    private static byte ToStickByte(float value)
    {
        value = Math.Clamp(value, -1.0f, 1.0f);
        if (Math.Abs(value) < StickDeadzone)
        {
            value = 0;
        }

        return (byte)Math.Clamp(
            (int)MathF.Round((value + 1.0f) * 127.5f),
            byte.MinValue,
            byte.MaxValue);
    }

    private static byte ToTriggerByte(float value) =>
        (byte)Math.Clamp(
            (int)MathF.Round(Math.Clamp(value, 0.0f, 1.0f) * byte.MaxValue),
            byte.MinValue,
            byte.MaxValue);
}

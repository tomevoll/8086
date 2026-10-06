using System;
using Silk.NET.SDL;

namespace Emulator80386.App.GFX
{
    public unsafe class GlDisplayWindow : IDisposable
    {
        private Sdl? _sdl;
        private Window* _window = null;
        private void* _glContext = null;
        private bool _initialized = false;

        public bool IsInitialized => _initialized;

        public GlDisplayWindow(string title, int width, int height)
        {
            try
            {
                _sdl = Sdl.GetApi();
                if (_sdl.Init(Sdl.InitVideo) < 0)
                {
                    Console.WriteLine("SDL Init Video failed (headless mode).");
                    return;
                }

                _window = _sdl.CreateWindow(
                    title,
                    Sdl.WindowposCentered,
                    Sdl.WindowposCentered,
                    width,
                    height,
                    (uint)(WindowFlags.Opengl | WindowFlags.Shown | WindowFlags.Resizable)
                );

                if (_window == null)
                {
                    Console.WriteLine("SDL Window creation failed (headless mode).");
                    return;
                }

                _glContext = _sdl.GLCreateContext(_window);
                if (_glContext == null)
                {
                    Console.WriteLine("OpenGL Context creation failed.");
                    return;
                }

                _initialized = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GlDisplayWindow initialization error (headless fallback enabled): {ex.Message}");
                _initialized = false;
            }
        }

        public bool PollEvents(Action<byte>? onScancode)
        {
            if (!_initialized || _sdl == null) return true;

            Event ev;
            while (_sdl.PollEvent(&ev) != 0)
            {
                if (ev.Type == (uint)EventType.Quit)
                {
                    return false;
                }
                else if (ev.Type == (uint)EventType.Keydown)
                {
                    byte scancode = (byte)((int)ev.Key.Keysym.Scancode & 0xFF);
                    onScancode?.Invoke(scancode);
                }
            }
            return true;
        }

        public void SwapBuffers()
        {
            if (_initialized && _sdl != null && _window != null)
            {
                _sdl.GLSwapWindow(_window);
            }
        }

        public void Dispose()
        {
            if (_initialized && _sdl != null)
            {
                if (_glContext != null) _sdl.GLDeleteContext(_glContext);
                if (_window != null) _sdl.DestroyWindow(_window);
                _sdl.Quit();
                _initialized = false;
            }
        }
    }
}

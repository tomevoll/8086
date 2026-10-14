using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Amiga500.Config;
using Amiga500.Disk;
using Amiga500.UI;
using Silk.NET.SDL;

namespace Amiga500;

public static unsafe class Program
{
    private static Sdl? _sdl;
    private static Window* _window;
    private static Renderer* _renderer;
    private static Texture* _texture;

    public static void Main(string[] args)
    {
        Console.WriteLine("==================================================");
        Console.WriteLine("   Commodore Amiga 500 Emulator (C# .NET 10)      ");
        Console.WriteLine("==================================================");

        var config = EmulatorConfig.Load();

        // Ensure Kickstart ROM exists
        if (!File.Exists(config.KickstartRomPath))
        {
            Console.WriteLine($"[Config] Kickstart ROM not found at '{config.KickstartRomPath}'.");
            Console.WriteLine("[Config] Opening Native File Dialog to select Kickstart ROM...");

            string? selectedRom = NativeFileDialog.SelectFile("Select Kickstart ROM (1.3/2.0)", "Kickstart ROM (*.rom;*.bin)|*.rom;*.bin|All files (*.*)|*.*");
            if (!string.IsNullOrEmpty(selectedRom) && File.Exists(selectedRom))
            {
                config.KickstartRomPath = selectedRom;
                config.Save();
                Console.WriteLine($"[Config] Kickstart ROM set to: {config.KickstartRomPath}");
            }
            else
            {
                Console.WriteLine("[Error] No Kickstart ROM supplied. Create/copy Kickstart ROM to 'roms/kick13.rom' or launch with settings.");
            }
        }

        byte[] romData = new byte[256 * 1024];
        if (File.Exists(config.KickstartRomPath))
        {
            romData = File.ReadAllBytes(config.KickstartRomPath);
            Console.WriteLine($"[System] Loaded Kickstart ROM ({romData.Length} bytes).");
        }
        else
        {
            Console.WriteLine("[System] Starting with placeholder ROM memory.");
        }

        var system = new AmigaSystem(config, romData);
        Console.WriteLine($"[System] Machine initialized: {config.ChipRamKB}KB Chip RAM, {config.VideoStandard} timing.");
        Console.WriteLine("[Controls] Press F12 to open Disk/ROM Picker dialog.");

        // Initialize SDL2
        _sdl = Sdl.GetApi();
        if (_sdl.Init(Sdl.InitVideo | Sdl.InitAudio | Sdl.InitEvents) < 0)
        {
            Console.WriteLine($"[SDL2] Init Error: {Marshal.PtrToStringAnsi((IntPtr)_sdl.GetError())}");
            return;
        }

        int winWidth = 640 * config.DisplayScale;
        int winHeight = 312 * config.DisplayScale;

        _window = _sdl.CreateWindow("Commodore Amiga 500 Emulator",
            Sdl.WindowposCentered, Sdl.WindowposCentered,
            winWidth, winHeight,
            (uint)WindowFlags.Resizable);

        _renderer = _sdl.CreateRenderer(_window, -1, (uint)RendererFlags.Accelerated | (uint)RendererFlags.Presentvsync);
        _texture = _sdl.CreateTexture(_renderer, Sdl.PixelformatArgb8888, (int)TextureAccess.Streaming, 640, 312);

        bool running = true;
        Event evt = default;

        while (running)
        {
            while (_sdl.PollEvent(ref evt) != 0)
            {
                if (evt.Type == (uint)EventType.Quit)
                {
                    running = false;
                }
                else if (evt.Type == (uint)EventType.Keydown)
                {
                    if (evt.Key.Keysym.Sym == (int)KeyCode.KF12)
                    {
                        // Open File Picker dialog for ADF disk selection / mounting
                        Console.WriteLine("[UI] F12 pressed - Opening Disk Mounting Dialog...");
                        string? adfPath = NativeFileDialog.SelectFile("Select Amiga Disk Image (DF0:)", "ADF Disk Images (*.adf)|*.adf|All files (*.*)|*.*");
                        if (!string.IsNullOrEmpty(adfPath) && File.Exists(adfPath))
                        {
                            var disk = AdfDiskImage.LoadFromFile(adfPath);
                            if (disk != null)
                            {
                                system.Floppy.Drives[0].InsertDisk(disk);
                                config.FloppyPaths[0] = adfPath;
                                config.Save();
                                Console.WriteLine($"[Floppy] Mounted '{adfPath}' into DF0:");
                            }
                        }
                    }
                }
            }

            // Step 1 full video frame
            system.StepFrame();

            // Update SDL texture with frame buffer
            fixed (uint* pBuffer = system.Denise.FrameBuffer)
            {
                _sdl.UpdateTexture(_texture, null, pBuffer, 640 * sizeof(uint));
            }

            _sdl.RenderClear(_renderer);
            _sdl.RenderCopy(_renderer, _texture, null, null);
            _sdl.RenderPresent(_renderer);

            // Frame pacing (~50 FPS)
            System.Threading.Thread.Sleep(15);
        }

        _sdl.DestroyTexture(_texture);
        _sdl.DestroyRenderer(_renderer);
        _sdl.DestroyWindow(_window);
        _sdl.Quit();
        Console.WriteLine("[System] Emulator stopped cleanly.");
    }
}

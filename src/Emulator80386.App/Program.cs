using System;
using Emulator80386.App.Config;
using Emulator80386.App.GFX;

namespace Emulator80386.App
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            Console.WriteLine("=================================================");
            Console.WriteLine("  80386 PC Emulator with SDL2 & OpenGL Rendering ");
            Console.WriteLine("=================================================");

            string configPath = args.Length > 0 ? args[0] : "config.ini";
            var config = EmulatorConfig.LoadFromFile(configPath);

            Console.WriteLine($"[Config] RAM Size: {config.RamSizeMB} MB");
            Console.WriteLine($"[Config] BIOS ROM Path: {config.RomPath}");
            Console.WriteLine($"[Config] BIOS ROM Low Path: {config.RomLowPath}");
            Console.WriteLine($"[Config] BIOS ROM High Path: {config.RomHighPath}");
            Console.WriteLine($"[Config] Drive C Folder: {config.DriveCFolder}");
            Console.WriteLine($"[Config] Display Resolution: {config.DisplayWidth}x{config.DisplayHeight}");

            var mb = new Motherboard(config);
            mb.Boot();

            using var display = new GlDisplayWindow("80386 PC Emulator", config.DisplayWidth, config.DisplayHeight);

            bool running = true;
            while (running)
            {
                running = display.PollEvents(scancode =>
                {
                    mb.Keyboard.EnqueueScancode(scancode);
                });

                mb.Step(instructionsCount: 1000);

                display.RenderFrameBuffer(mb.Vga.PixelBuffer);
                display.SwapBuffers();
            }

            Console.WriteLine("Emulator shutdown gracefully.");
        }
    }
}

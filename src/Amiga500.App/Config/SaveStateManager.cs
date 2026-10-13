using System;
using System.IO;
using System.Text.Json;

namespace Amiga500.Config;

public class CpuSaveState
{
    public uint[] D { get; set; } = new uint[8];
    public uint[] A { get; set; } = new uint[8];
    public uint PC { get; set; }
    public ushort SR { get; set; }
    public uint USP { get; set; }
    public uint SSP { get; set; }
}

public class AmigaSaveState
{
    public CpuSaveState Cpu { get; set; } = new CpuSaveState();
    public byte[] ChipRam { get; set; } = Array.Empty<byte>();
    public ushort[] CustomRegisters { get; set; } = new ushort[256];
    public bool OverlayEnabled { get; set; }
}

public static class SaveStateManager
{
    public static void SaveState(AmigaSystem system, string filePath)
    {
        var state = new AmigaSaveState
        {
            Cpu = new CpuSaveState
            {
                D = (uint[])system.Cpu.D.Clone(),
                A = (uint[])system.Cpu.A.Clone(),
                PC = system.Cpu.PC,
                SR = system.Cpu.SR,
                USP = system.Cpu.USP,
                SSP = system.Cpu.SSP
            },
            ChipRam = (byte[])system.Memory.ChipRam.Clone(),
            OverlayEnabled = system.Memory.OverlayEnabled
        };

        string json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(filePath, json);
    }

    public static bool LoadState(AmigaSystem system, string filePath)
    {
        if (!File.Exists(filePath)) return false;

        try
        {
            string json = File.ReadAllText(filePath);
            var state = JsonSerializer.Deserialize<AmigaSaveState>(json);
            if (state == null) return false;

            Array.Copy(state.Cpu.D, system.Cpu.D, 8);
            Array.Copy(state.Cpu.A, system.Cpu.A, 8);
            system.Cpu.PC = state.Cpu.PC;
            system.Cpu.SR = state.Cpu.SR;
            system.Cpu.USP = state.Cpu.USP;
            system.Cpu.SSP = state.Cpu.SSP;

            if (state.ChipRam.Length <= system.Memory.ChipRam.Length)
            {
                Array.Copy(state.ChipRam, system.Memory.ChipRam, state.ChipRam.Length);
            }

            system.Memory.OverlayEnabled = state.OverlayEnabled;
            return true;
        }
        catch
        {
            return false;
        }
    }
}

namespace Amiga500.Memory;

public interface IMemoryBus
{
    byte ReadByte(uint address);
    ushort ReadWord(uint address);
    uint ReadLong(uint address);

    void WriteByte(uint address, byte value);
    void WriteWord(uint address, ushort value);
    void WriteLong(uint address, uint value);
}

namespace Shared
{
    public static class Endian
    {
        public static void ConvertToBigEndian(int val, byte[] bytes, int offset = 0)
        {
            bytes[offset + 0] = (byte)((val >> 24) & 0xff);
            bytes[offset + 1] = (byte)((val >> 16) & 0xff);
            bytes[offset + 2] = (byte)((val >> 8) & 0xff);
            bytes[offset + 3] = (byte)((val) & 0xff);
        }

        public static int ConvertFromBigEndian(byte[] bytes, int offset = 0)
        {
            return bytes[offset + 0] << 24 |
                   bytes[offset + 1] << 16 |
                   bytes[offset + 2] << 8 |
                   bytes[offset + 3];
        }
    }
}

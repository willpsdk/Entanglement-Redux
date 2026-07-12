using System.Runtime.InteropServices;

namespace Entanglement.Extensions
{
    public static class ArrayExtensions {
        // Shortcut to append a byte array to another
        public static byte[] AddBytes(this byte[] self, byte[] array, ref int index) {
            for (int i = 0; i < array.Length; i++)
                self[index++] = array[i];
            return self;
        }

        public static byte[] AddBytes(this byte[] self, byte[] array, int index) {
            for (int i = 0; i < array.Length; i++)
                self[index++] = array[i];
            return self;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct FloatIntUnion {
            [FieldOffset(0)] public float f;
            [FieldOffset(0)] public int i;
        }

        // Allocation-free writers for the hot serializers, byte layout matches BitConverter
        public static void WriteFloat(this byte[] self, ref int index, float value) {
            FloatIntUnion u = default; u.f = value;
            self[index++] = (byte)u.i;
            self[index++] = (byte)(u.i >> 8);
            self[index++] = (byte)(u.i >> 16);
            self[index++] = (byte)(u.i >> 24);
        }

        public static void WriteShort(this byte[] self, ref int index, short value) {
            self[index++] = (byte)value;
            self[index++] = (byte)(value >> 8);
        }

        public static void WriteUShort(this byte[] self, ref int index, ushort value) {
            self[index++] = (byte)value;
            self[index++] = (byte)(value >> 8);
        }
    }
}

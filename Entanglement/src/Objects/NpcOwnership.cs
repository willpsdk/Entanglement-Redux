using System.Collections.Generic;

namespace Entanglement.Objects
{
    public static class NpcOwnership
    {
        static readonly Dictionary<ushort, ushort> seqs = new Dictionary<ushort, ushort>();

        public static void Clear() => seqs.Clear();

        /// <summary>The number of the last grant for this id, 0 for none.</summary>
        public static ushort Current(ushort id) {
            ushort seq;
            return seqs.TryGetValue(id, out seq) ? seq : (ushort)0;
        }

        /// <summary>Host: the number for a new grant.</summary>
        public static ushort Next(ushort id) {
            ushort seq = (ushort)(Current(id) + 1);
            if (seq == 0)
                seq = 1;
            seqs[id] = seq;
            return seq;
        }

        public static bool Accept(ushort id, ushort seq) {
            ushort have;
            if (!seqs.TryGetValue(id, out have) || have == 0) {
                seqs[id] = seq;
                return true;
            }
            if (seq == have)
                return true;
            if ((short)(seq - have) > 0) {
                seqs[id] = seq;
                return true;
            }
            return false;
        }
    }
}

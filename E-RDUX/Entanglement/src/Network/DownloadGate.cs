using UnityEngine;

using Entanglement.Sync;

namespace Entanglement.Network
{
    // Holds a just-joined client back until its playermodel/item downloads land, so you don't show
    // up to others half-loaded. Client-side only, no wire format change, so it can't break joins.
    public static class DownloadGate
    {
        static bool joining;
        static float joinTime;

        // Downloads are demand-driven and don't start the instant you join
        const float minSettleSeconds = 3f;

        // Don't hold forever on a stuck transfer
        const float maxHoldSeconds = 90f;

        public static void OnJoinedServer()
        {
            if (Node.isServer)
                return;

            joining = true;
            joinTime = Time.time;
        }

        public static void Reset()
        {
            joining = false;
        }

        public static bool IsGated
        {
            get
            {
                if (!joining)
                    return false;

                float held = Time.time - joinTime;

                if (held > maxHoldSeconds) {
                    joining = false;
                    return false;
                }

                if (FileTransferManager.HasActiveDownloads || held < minSettleSeconds)
                    return true;

                joining = false;
                return false;
            }
        }
    }
}

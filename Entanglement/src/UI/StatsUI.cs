using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Entanglement.Network;
using Entanglement.Representation;
using Entanglement.Objects;

using ModThatIsNotMod.BoneMenu;

using UnityEngine;

namespace Entanglement.UI
{
    public static class StatsUI
    {
        public static IntElement downElem, upElem, playerCountElem, objectCountElem;

        private static float bandwidthCheckTimer = 0f;
        private const float BANDWIDTH_CHECK_INTERVAL = 1f;
        private static float lastDownloadBps = 0f;
        private static float lastUploadBps = 0f;

        public static void CreateUI(MenuCategory category)
        {
            MenuCategory statsCategory = category.CreateSubCategory("Net Stats", Color.white);

            downElem = statsCategory.elements[statsCategory.elements.Count > 0 ? statsCategory.elements.Count - 1 : 0] as IntElement;
            upElem = statsCategory.elements[statsCategory.elements.Count > 0 ? statsCategory.elements.Count - 1 : 0] as IntElement;

            statsCategory.CreateIntElement("Bytes Down", Color.white, 0, null);
            downElem = statsCategory.elements[statsCategory.elements.Count - 1] as IntElement;

            statsCategory.CreateIntElement("Bytes Up", Color.white, 0, null);
            upElem = statsCategory.elements[statsCategory.elements.Count - 1] as IntElement;

            statsCategory.CreateIntElement("Players", Color.white, 0, null);
            playerCountElem = statsCategory.elements[statsCategory.elements.Count - 1] as IntElement;

            statsCategory.CreateIntElement("Objects Synced", Color.white, 0, null);
            objectCountElem = statsCategory.elements[statsCategory.elements.Count - 1] as IntElement;
        }

        public static void UpdateUI()
        {
            if (Node.activeNode == null)
                return;

            // Update byte counts
            downElem.SetValue((int)Node.activeNode.recievedByteCount);
            upElem.SetValue((int)Node.activeNode.sentByteCount);

            // Calculate bandwidth
            bandwidthCheckTimer += Time.deltaTime;
            if (bandwidthCheckTimer >= BANDWIDTH_CHECK_INTERVAL)
            {
                bandwidthCheckTimer = 0f;
                lastDownloadBps = Node.activeNode.recievedByteCount / BANDWIDTH_CHECK_INTERVAL;
                lastUploadBps = Node.activeNode.sentByteCount / BANDWIDTH_CHECK_INTERVAL;
            }

            // Update counts
            int playerCount = PlayerRepresentation.representations.Count;
            int objectCount = ObjectSync.syncedObjects.Count;
            playerCountElem.SetValue(playerCount);
            objectCountElem.SetValue(objectCount);

            // Reset counters
            Node.activeNode.recievedByteCount = 0;
            Node.activeNode.sentByteCount = 0;
        }
    }
}

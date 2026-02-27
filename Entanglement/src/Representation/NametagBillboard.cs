using UnityEngine;
using MelonLoader;

namespace Entanglement.Representation
{
    /// <summary>
    /// Makes the nametag billboard always face towards the local player camera
    /// </summary>
    [RegisterTypeInIl2Cpp]
    public class NametagBillboard : MonoBehaviour
    {
        public NametagBillboard(System.IntPtr intPtr) : base(intPtr) { }

        private Transform cameraTransform;

        private void Start()
        {
            // Get the camera transform (player's head/camera)
            var cam = Camera.main;
            if (cam != null)
                cameraTransform = cam.transform;
        }

        private void LateUpdate()
        {
            // Always face towards the camera
            if (cameraTransform != null)
            {
                transform.LookAt(cameraTransform);
                // Flip to face camera properly (inverse rotation)
                transform.Rotate(0, 180, 0);
            }
        }
    }
}

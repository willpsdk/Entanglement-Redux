using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ModThatIsNotMod.BoneMenu;
using Entanglement.Representation;
using Entanglement.Objects;
using Entanglement.Network;
using Entanglement.Data;
using StressLevelZero.VRMK;

namespace Entanglement.UI
{
    public static class DebugUI
    {
        private static readonly List<GameObject> debugColliderMeshes = new List<GameObject>();
        private static readonly List<GameObject> debugPhysicsRigMeshes = new List<GameObject>();

        public static bool showColliders = false;
        public static bool showPhysicsRig = false;
        public static bool logSyncEvents = false;
        public static bool logIKUpdates = false;

        private static IntElement connectedUsersElem;
        private static IntElement nodeTypeElem;

        public static void CreateUI(MenuCategory parentCategory)
        {
            // --- Visualization ---
            parentCategory.CreateBoolElement("Show Colliders", Color.cyan, showColliders, (val) =>
            {
                showColliders = val;
                PlayerRepresentation.debugShowPhysics = val;
                if (val) CreateColliderMeshes();
                else DestroyColliderMeshes();
            });

            parentCategory.CreateBoolElement("Show Physics Rig", Color.magenta, showPhysicsRig, (val) =>
            {
                showPhysicsRig = val;
                if (val) CreatePhysicsRigMeshes();
                else DestroyPhysicsRigMeshes();
            });

            parentCategory.CreateFunctionElement("Refresh Debug Meshes", Color.white, () =>
            {
                if (showColliders) CreateColliderMeshes();
                if (showPhysicsRig) CreatePhysicsRigMeshes();
                EntangleLogger.Log("Debug meshes refreshed.");
            });

            // --- Net Stats (live on menu) ---
            parentCategory.CreateIntElement("Connected Users", Color.green, 0, null);
            connectedUsersElem = parentCategory.elements.Last() as IntElement;

            parentCategory.CreateIntElement("Node Type (0=None 1=Srv 2=Cli)", Color.green, 0, null);
            nodeTypeElem = parentCategory.elements.Last() as IntElement;

            // --- Debug Toggles ---
            parentCategory.CreateBoolElement("Log Sync Events", Color.yellow, logSyncEvents, (val) =>
            {
                logSyncEvents = val;
                EntangleLogger.Log("Sync event logging " + (val ? "ENABLED" : "DISABLED"));
            });

            parentCategory.CreateBoolElement("Log IK Updates", Color.yellow, logIKUpdates, (val) =>
            {
                logIKUpdates = val;
                EntangleLogger.Log("IK update logging " + (val ? "ENABLED" : "DISABLED"));
            });
        }

        public static void UpdateUI()
        {
            try
            {
                if (Node.activeNode != null)
                {
                    if (connectedUsersElem != null)
                        connectedUsersElem.SetValue(Node.activeNode.connectedUsers.Count);
                    if (nodeTypeElem != null)
                        nodeTypeElem.SetValue(Node.isServer ? 1 : 2);
                }
                else
                {
                    if (connectedUsersElem != null)
                        connectedUsersElem.SetValue(0);
                    if (nodeTypeElem != null)
                        nodeTypeElem.SetValue(0);
                }
            }
            catch (Exception ex)
            {
                EntangleLogger.Error($"Error updating DebugUI: {ex.Message}");
            }
        }

        private static Material CreateDebugMaterial(Color color)
        {
            Material mat = new Material(Shader.Find("Standard"));
            mat.color = color;
            mat.SetFloat("_Mode", 3f);
            mat.SetInt("_SrcBlend", 5);
            mat.SetInt("_DstBlend", 10);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
            return mat;
        }

        // --- Collider Visualization ---
        public static void CreateColliderMeshes()
        {
            DestroyColliderMeshes();
            Material mat = CreateDebugMaterial(new Color(0f, 1f, 1f, 0.35f));

            foreach (var kvp in PlayerRepresentation.representations)
            {
                var rep = kvp.Value;
                if (rep == null || rep.colliders == null) continue;
                foreach (Collider col in rep.colliders)
                {
                    if (col == null) continue;
                    GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.name = "DebugCol_" + rep.playerName + "_" + col.name;
                    var cubeCol = cube.GetComponent<Collider>();
                    if (cubeCol != null) UnityEngine.Object.Destroy(cubeCol);
                    cube.GetComponent<Renderer>().material = mat;
                    cube.transform.SetParent(col.transform, false);
                    if (col is BoxCollider box)
                    {
                        cube.transform.localPosition = box.center;
                        cube.transform.localScale = box.size;
                    }
                    else if (col is SphereCollider sphere)
                    {
                        cube.transform.localPosition = sphere.center;
                        float d = sphere.radius * 2f;
                        cube.transform.localScale = new Vector3(d, d, d);
                    }
                    else if (col is CapsuleCollider capsule)
                    {
                        cube.transform.localPosition = capsule.center;
                        float r2 = capsule.radius * 2f;
                        float h = capsule.height;
                        switch (capsule.direction)
                        {
                            case 0: cube.transform.localScale = new Vector3(h, r2, r2); break;
                            case 2: cube.transform.localScale = new Vector3(r2, r2, h); break;
                            default: cube.transform.localScale = new Vector3(r2, h, r2); break;
                        }
                    }
                    else
                    {
                        Bounds b = col.bounds;
                        cube.transform.position = b.center;
                        cube.transform.localScale = b.size;
                    }
                    debugColliderMeshes.Add(cube);
                }
            }
            EntangleLogger.Log("Created " + debugColliderMeshes.Count + " collider debug meshes.");
        }

        public static void DestroyColliderMeshes()
        {
            foreach (var go in debugColliderMeshes)
            {
                if (go != null) UnityEngine.Object.Destroy(go);
            }
            debugColliderMeshes.Clear();
        }

        // --- Physics Rig Visualization ---
        public static void CreatePhysicsRigMeshes()
        {
            DestroyPhysicsRigMeshes();
            Material mat = CreateDebugMaterial(new Color(1f, 0f, 1f, 0.35f));
            float boneSize = 0.05f;

            foreach (var kvp in PlayerRepresentation.representations)
            {
                var rep = kvp.Value;
                if (rep == null || rep.repBody == null || rep.repBody.references == null) continue;

                SLZ_Body.References refs = rep.repBody.references;
                Transform[] bones = new Transform[]
                {
                    refs.skull, refs.c4Vertebra, refs.t1Offset, refs.t7Vertebra,
                    refs.l1Vertebra, refs.l3Vertebra, refs.sacrum,
                    refs.leftHip, refs.leftKnee, refs.leftAnkle,
                    refs.rightHip, refs.rightKnee, refs.rightAnkle,
                    refs.leftShoulder, refs.leftElbow, refs.leftWrist,
                    refs.rightShoulder, refs.rightElbow, refs.rightWrist
                };

                foreach (Transform bone in bones)
                {
                    if (bone == null) continue;
                    GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    sphere.name = "DebugRig_" + rep.playerName + "_" + bone.name;
                    var sphereCol = sphere.GetComponent<Collider>();
                    if (sphereCol != null) UnityEngine.Object.Destroy(sphereCol);
                    sphere.GetComponent<Renderer>().material = mat;
                    sphere.transform.SetParent(bone, false);
                    sphere.transform.localPosition = Vector3.zero;
                    sphere.transform.localScale = new Vector3(boneSize, boneSize, boneSize);
                    debugPhysicsRigMeshes.Add(sphere);
                }
            }
            EntangleLogger.Log("Created " + debugPhysicsRigMeshes.Count + " physics rig debug meshes.");
        }

        public static void DestroyPhysicsRigMeshes()
        {
            foreach (var go in debugPhysicsRigMeshes)
            {
                if (go != null) UnityEngine.Object.Destroy(go);
            }
            debugPhysicsRigMeshes.Clear();
        }
    }
}

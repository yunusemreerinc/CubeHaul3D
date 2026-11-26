using System.Collections.Generic;
using UnityEngine;

namespace CubeHaul.Presentation.Trucks
{
    /// <summary>
    /// The placeholder low-poly truck (Task 14): body (chassis), cabin with a hood and windshield, four wheels and an
    /// open cargo bed, from Unity's built-in cube and cylinder meshes, no colliders, no external assets. One model unit is
    /// one design unit: 21.4 long and 11.4 wide like cube-hunt's truck sprite, facing +Z, origin on the ground. The
    /// Editor saves it as Art/Prefabs/Truck.prefab; at runtime it is built when no prefab is given.
    /// </summary>
    public static class TruckModelBuilder
    {
        public const float WheelRadius = 2.2f;

        /// <summary>The model's materials. Null ones fall back to a URP Lit material of a fixed color.</summary>
        public sealed class Materials
        {
            public Material Paint, Trim, Tire, Glass, Light;
        }

        public static TruckVisual Build(Materials m = null)
        {
            m = m ?? new Materials();
            Material paint = m.Paint ?? Plain("Truck Paint", new Color(0.92f, 0.92f, 0.92f));
            Material trim = m.Trim ?? Plain("Truck Trim", new Color(0.35f, 0.35f, 0.38f));
            Material tire = m.Tire ?? Plain("Truck Tire", new Color(0.12f, 0.1f, 0.09f));
            Material glass = m.Glass ?? Plain("Truck Glass", new Color(0.16f, 0.24f, 0.32f));
            Material light = m.Light ?? Plain("Truck Light", new Color(1f, 0.97f, 0.78f));

            var root = new GameObject("Truck");
            var body = Group("Body", root.transform);
            var paints = new List<Renderer>();
            var trims = new List<Renderer>();

            // Body: the chassis everything sits on, its underside clear of the ground.
            trims.Add(Box("Chassis", body, new Vector3(0, 2.7f, 0), new Vector3(10.2f, 1.6f, 20.6f), trim));

            // Cabin: the cab with a windshield, and the hood in front of it with headlights.
            Transform cabin = Group("Cabin", root.transform);
            paints.Add(Box("Cab", cabin, new Vector3(0, 6.1f, 3.4f), new Vector3(10f, 5.2f, 5.2f), paint));
            paints.Add(Box("Hood", cabin, new Vector3(0, 4.9f, 8.3f), new Vector3(10f, 2.8f, 4.6f), paint));
            Box("Windshield", cabin, new Vector3(0, 7.2f, 6.05f), new Vector3(8.6f, 2.6f, 0.25f), glass);
            Box("Headlight L", cabin, new Vector3(-3.4f, 5.1f, 10.62f), new Vector3(1.6f, 1.1f, 0.2f), light);
            Box("Headlight R", cabin, new Vector3(3.4f, 5.1f, 10.62f), new Vector3(1.6f, 1.1f, 0.2f), light);

            // Cargo bed: an open box behind the cab.
            Transform bed = Group("Bed", root.transform);
            trims.Add(Box("Floor", bed, new Vector3(0, 3.9f, -4.9f), new Vector3(10.2f, 0.7f, 11.4f), trim));
            paints.Add(Box("Side L", bed, new Vector3(-4.75f, 5.6f, -4.9f), new Vector3(0.7f, 2.8f, 11.4f), paint));
            paints.Add(Box("Side R", bed, new Vector3(4.75f, 5.6f, -4.9f), new Vector3(0.7f, 2.8f, 11.4f), paint));
            paints.Add(Box("Tailgate", bed, new Vector3(0, 5.6f, -10.25f), new Vector3(10.2f, 2.8f, 0.7f), paint));
            Renderer cargo = Box("Cargo", bed, new Vector3(0, 7.75f, -4.9f), new Vector3(7f, 7f, 7f), paint);
            cargo.gameObject.SetActive(false);

            // Four wheels, axles along X so they roll about it.
            Transform wheelRoot = Group("Wheels", root.transform);
            var wheels = new List<Transform>();
            foreach (float z in new[] { -6.6f, 5.6f })
                foreach (float x in new[] { -5.3f, 5.3f })
                    wheels.Add(Wheel(x < 0 ? (z < 0 ? "Wheel RL" : "Wheel FL") : (z < 0 ? "Wheel RR" : "Wheel FR"), wheelRoot, new Vector3(x, WheelRadius, z), tire));

            var visual = root.AddComponent<TruckVisual>();
            visual.Configure(paints.ToArray(), trims.ToArray(), cargo, wheels.ToArray(), WheelRadius);
            return visual;
        }

        static Transform Group(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static Renderer Box(string name, Transform parent, Vector3 center, Vector3 size, Material material)
        {
            GameObject go = Primitive(PrimitiveType.Cube, name, parent);
            go.transform.localPosition = center;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            return r;
        }

        static Transform Wheel(string name, Transform parent, Vector3 center, Material material)
        {
            // The hub turns; the cylinder inside it lies on its side (Unity's cylinder stands along Y, 2 tall, radius 0.5).
            var hub = new GameObject(name).transform;
            hub.SetParent(parent, false);
            hub.localPosition = center;
            GameObject tire = Primitive(PrimitiveType.Cylinder, "Tire", hub);
            tire.transform.localRotation = Quaternion.Euler(0, 0, 90);
            tire.transform.localScale = new Vector3(WheelRadius * 2, 1.2f, WheelRadius * 2);
            tire.GetComponent<MeshRenderer>().sharedMaterial = material;
            return hub;
        }

        static GameObject Primitive(PrimitiveType type, string name, Transform parent)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            Collider c = go.GetComponent<Collider>();
            if (Application.isPlaying) Object.Destroy(c);
            else Object.DestroyImmediate(c);
            go.name = name;
            go.transform.SetParent(parent, false);
            return go;
        }

        static Material Plain(string name, Color color)
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(lit) { name = name, enableInstancing = true };
            m.SetColor("_BaseColor", color);
            m.color = color;
            return m;
        }
    }
}

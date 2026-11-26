using UnityEngine;

namespace CubeHaul.Presentation.Trucks
{
    /// <summary>
    /// The look of a truck (Task 14, docs/TRUCKS.md), and the only thing the game needs from a truck model: which
    /// renderers take the crate's color, which take a darker shade of it, the cargo cube, and the wheels to spin.
    /// A new model (an artist's FBX, say) replaces the placeholder by putting this on its root and filling these in:
    /// the model faces +Z, its origin on the ground between the wheels, one unit = one design unit (21.4 long).
    /// </summary>
    public sealed class TruckVisual : MonoBehaviour
    {
        [SerializeField, Tooltip("Painted in the crate's color (cabin, hood, bed sides).")]
        Renderer[] paint = new Renderer[0];
        [SerializeField, Tooltip("Painted in a darker shade of it (chassis, bed floor).")]
        Renderer[] trim = new Renderer[0];
        [SerializeField, Tooltip("The cube it carries home; shown only with cargo, in the crate's color.")]
        Renderer cargo;
        [SerializeField] Transform[] wheels = new Transform[0];
        [SerializeField, Min(0.01f), Tooltip("Wheel radius, model units, for spinning them as the truck drives.")]
        float wheelRadius = 2.2f;

        double lastOdometer;

        public Renderer[] Paint => paint;
        public Renderer[] Trim => trim;
        public Renderer Cargo => cargo;
        public Transform[] Wheels => wheels;

        /// <summary>Sets every part (used by the model builder).</summary>
        public void Configure(Renderer[] paintParts, Renderer[] trimParts, Renderer cargoCube, Transform[] wheelParts, float radius)
        {
            paint = paintParts;
            trim = trimParts;
            cargo = cargoCube;
            wheels = wheelParts;
            wheelRadius = radius;
        }

        /// <summary>Colors the truck: shared materials per color, so trucks of one color batch together.</summary>
        public void SetPaint(Material paintMaterial, Material trimMaterial)
        {
            foreach (Renderer r in paint) if (r != null) r.sharedMaterial = paintMaterial;
            foreach (Renderer r in trim) if (r != null) r.sharedMaterial = trimMaterial;
            if (cargo != null) cargo.sharedMaterial = paintMaterial;
        }

        public void SetCargo(bool loaded)
        {
            if (cargo != null && cargo.gameObject.activeSelf != loaded) cargo.gameObject.SetActive(loaded);
        }

        /// <summary>Turns the wheels by the distance driven since the last call, in model units.</summary>
        public void Roll(double odometer, float modelUnitsPerDesignUnit = 1f)
        {
            double d = (odometer - lastOdometer) * modelUnitsPerDesignUnit;
            lastOdometer = odometer;
            if (d <= 0) return;
            float degrees = (float)(d / wheelRadius * Mathf.Rad2Deg);
            foreach (Transform w in wheels)
                if (w != null) w.Rotate(Vector3.right, degrees, Space.Self);
        }

        public void ResetWheels() => lastOdometer = 0;
    }
}

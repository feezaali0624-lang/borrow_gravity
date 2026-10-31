#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace BorrowGravity
{
    /// <summary>
    /// Builds the Level 6 (swings) greybox from plain cubes: start platform, swings of different speeds,
    /// rest platforms, a gravity-zone gap with an upside-down walkway, and the finish.
    /// Menu: Borrowed Gravity > Level 6 > Build swing greybox.
    /// Everything it makes is a normal GameObject, so afterwards tune it in the Inspector (or edit the table below
    /// and build again). The level runs along +X; swings swing along X.
    /// Put this file in Assets/BorrowedGravity/Scripts/Editor.
    /// </summary>
    public static class Level6Builder
    {
        struct SwingDef
        {
            public float length, angle, period, phase;
            public SwingDef(float length, float angle, float period, float phase)
            { this.length = length; this.angle = angle; this.period = period; this.phase = phase; }
        }

        // ---- the difficulty table: edit these numbers (period = seconds per swing, smaller = faster) ----
        static readonly SwingDef[] SectionA = { // learn: long, slow, wide
            new SwingDef(8f, 28f, 5.0f, 0.0f), new SwingDef(8f, 30f, 4.6f, 0.5f), new SwingDef(8f, 30f, 4.4f, 0.0f) };
        static readonly SwingDef[] SectionB = { // rhythm: a bit faster, out of step
            new SwingDef(7f, 34f, 3.8f, 0.0f), new SwingDef(7f, 34f, 3.5f, 0.5f), new SwingDef(7f, 36f, 3.2f, 0.25f) };
        static readonly SwingDef[] SectionD = { // fast: short and quick
            new SwingDef(6.5f, 38f, 2.6f, 0.0f), new SwingDef(6.5f, 38f, 2.4f, 0.5f),
            new SwingDef(6.5f, 40f, 2.2f, 0.0f), new SwingDef(6.5f, 40f, 2.0f, 0.5f) };
        static readonly SwingDef[] SectionE = { // finale: one big slow, one very fast
            new SwingDef(9f, 40f, 3.2f, 0.0f), new SwingDef(5.5f, 42f, 1.9f, 0.3f) };

        const float JumpGap = 2.5f;        // empty space between a platform edge and a swing's nearest extreme
        const float SwingSize = 3.2f;      // swing platform width / depth
        const float GravityGap = 16f;      // length of the gravity-zone gap
        const float PlatformDepth = 6f;    // z size of the fixed platforms

        [MenuItem("Borrowed Gravity/Level 6/Build swing greybox")]
        static void Build()
        {
            var old = GameObject.Find("Level6_Greybox");
            if (old != null)
            {
                if (!EditorUtility.DisplayDialog("Level 6", "Level6_Greybox already exists. Delete it and build again?", "Rebuild", "Cancel")) return;
                Undo.DestroyObjectImmediate(old);
            }

            var root = new GameObject("Level6_Greybox");
            Undo.RegisterCreatedObjectUndo(root, "Build Level 6 greybox");
            var fixedRoot = Group(root, "FixedPlatforms");
            var swingRoot = Group(root, "Swings");
            var markers = Group(root, "Markers");

            float x = -4f;
            int checkpoint = 0;

            // start
            Platform(fixedRoot, "Start", ref x, 8f);
            Marker(markers, "SpawnA", new Vector3(-1f, 1f, -1.2f));
            Marker(markers, "SpawnB", new Vector3(-1f, 1f, 1.2f));
            CheckpointSlot(markers, ++checkpoint, new Vector3(x - 4f, 1.5f, 0f));

            Swings(swingRoot, "A", SectionA, ref x);
            Swings(swingRoot, "B", SectionB, ref x);

            // rest 1
            x += JumpGap;
            Platform(fixedRoot, "Rest1", ref x, 6f);
            CheckpointSlot(markers, ++checkpoint, new Vector3(x - 3f, 1.5f, 0f));

            // gravity gap: no floor; a ceiling walkway to reach by switching gravity upside down
            float gapStart = x;
            var ceil = Cube(fixedRoot, "GravityCeilingWalkway", new Vector3(gapStart + GravityGap * 0.5f, 8.5f, 0f), new Vector3(GravityGap + 8f, 1f, PlatformDepth));
            var zone = new GameObject("GravityZone_Slot");
            zone.transform.SetParent(markers, false);
            zone.transform.position = new Vector3(gapStart + GravityGap * 0.5f, 4f, 0f);
            var zoneBox = zone.AddComponent<BoxCollider>();
            zoneBox.isTrigger = true;
            zoneBox.size = new Vector3(GravityGap + 8f, 12f, 8f);
            x += GravityGap;

            // rest 2
            Platform(fixedRoot, "Rest2", ref x, 6f);
            CheckpointSlot(markers, ++checkpoint, new Vector3(x - 3f, 1.5f, 0f));

            Swings(swingRoot, "D", SectionD, ref x);

            // rest 3
            x += JumpGap;
            Platform(fixedRoot, "Rest3", ref x, 4f);
            CheckpointSlot(markers, ++checkpoint, new Vector3(x - 2f, 1.5f, 0f));

            Swings(swingRoot, "E", SectionE, ref x);

            // end
            x += JumpGap;
            float endStart = x;
            Platform(fixedRoot, "End", ref x, 8f);
            var finish = new GameObject("Finish");
            finish.transform.SetParent(markers, false);
            finish.transform.position = new Vector3(endStart + 4f, 1.5f, 0f);
            var box = finish.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(5f, 3f, 5f);
            finish.AddComponent<SwingFinishZone>();

            Selection.activeGameObject = root;
            Debug.Log("[Level6] greybox built, " + (x + 4f).ToString("0") + " units long. Next: move the players to SpawnA / SpawnB, add the Checkpoint component to the Checkpoint_* markers and the GravityZone to GravityZone_Slot.");
        }

        // ---- builders ----

        static void Swings(Transform parent, string section, SwingDef[] defs, ref float x)
        {
            for (int i = 0; i < defs.Length; i++)
            {
                var d = defs[i];
                float reach = d.length * Mathf.Sin(d.angle * Mathf.Deg2Rad);
                float pivotX = x + JumpGap + reach + SwingSize * 0.5f;
                float pivotY = d.length - 0.2f; // lowest point: platform top at y = 0

                var pivot = new GameObject("Swing_" + section + (i + 1));
                pivot.transform.SetParent(parent, false);
                pivot.transform.position = new Vector3(pivotX, pivotY, 0f);

                var seat = Cube(pivot.transform, "Seat", new Vector3(pivotX, -0.2f, 0f), new Vector3(SwingSize, 0.4f, SwingSize));

                var rope = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rope.name = "Rope";
                Object.DestroyImmediate(rope.GetComponent<Collider>()); // looks only, never blocks
                rope.transform.SetParent(pivot.transform, false);
                rope.transform.localScale = new Vector3(0.08f, 0.08f, d.length);
                Undo.RegisterCreatedObjectUndo(rope, "rope");

                var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere); // shows where it hangs from
                marker.name = "PivotMarker";
                Object.DestroyImmediate(marker.GetComponent<Collider>());
                marker.transform.SetParent(pivot.transform, false);
                marker.transform.localScale = Vector3.one * 0.35f;

                var sw = pivot.AddComponent<SwingPlatform>();
                sw.platform = seat.transform;
                sw.ropeCube = rope.transform;
                sw.maxAngle = d.angle;
                sw.period = d.period;
                sw.phase = d.phase;
                sw.swingAxis = new Vector3(0f, 0f, 1f); // swings along X, the way the level runs
                sw.keepLevel = true;

                x = pivotX + reach + SwingSize * 0.5f;
            }
        }

        static void Platform(Transform parent, string name, ref float x, float width)
        {
            Cube(parent, name, new Vector3(x + width * 0.5f, -0.5f, 0f), new Vector3(width, 1f, PlatformDepth));
            x += width;
        }

        static GameObject Cube(Transform parent, string name, Vector3 worldPos, Vector3 scale)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
            c.name = name;
            c.transform.SetParent(parent, false);
            c.transform.position = worldPos;
            c.transform.localScale = scale;
            Undo.RegisterCreatedObjectUndo(c, "cube");
            return c;
        }

        static Transform Group(GameObject root, string name)
        {
            var g = new GameObject(name);
            g.transform.SetParent(root.transform, false);
            return g.transform;
        }

        static void Marker(Transform parent, string name, Vector3 pos)
        {
            var m = new GameObject(name);
            m.transform.SetParent(parent, false);
            m.transform.position = pos;
        }

        static void CheckpointSlot(Transform parent, int n, Vector3 pos)
        {
            var m = new GameObject("Checkpoint_" + n + "_slot");
            m.transform.SetParent(parent, false);
            m.transform.position = pos;
            var box = m.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(4f, 3f, PlatformDepth - 1f);
        }
    }
}
#endif

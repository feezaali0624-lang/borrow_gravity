using UnityEngine;

namespace BorrowGravity
{
    /// <summary>
    /// Plate par player ho to darwaza khulta hai, aur hatne ke baad bhi kuch second khula rehta hai.
    /// Co-op: chhota time. Solo: lamba time. Har frame plate ke upar check hota hai (ginti nahi).
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class PressureDoor : MonoBehaviour
    {
        public Transform door;
        public Vector3 openOffset = new Vector3(0f, 6f, 0f);
        public float speed = 4f;
        public float coopHoldSeconds = 1.5f;
        public float soloHoldSeconds = 5f;

        Vector3 closedPos;
        Collider sensor;
        float closeAt;
        readonly Collider[] hits = new Collider[16];

        void Awake()
        {
            foreach (var c in GetComponents<Collider>())
                if (c.isTrigger) { sensor = c; break; }
            if (sensor == null) { sensor = GetComponent<Collider>(); sensor.isTrigger = true; }
            if (door != null) closedPos = door.position;
        }

        bool PlayerOnPlate()
        {
            Bounds b = sensor.bounds;
            int n = Physics.OverlapBoxNonAlloc(b.center, b.extents, hits, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (Players.From(hits[i]) != null) return true;
            return false;
        }

        void Update()
        {
            if (door == null) return;
            if (PlayerOnPlate()) closeAt = Time.time + (GameFlow.Solo ? soloHoldSeconds : coopHoldSeconds);
            bool open = Time.time < closeAt;
            Vector3 target = open ? closedPos + openOffset : closedPos;
            door.position = Vector3.MoveTowards(door.position, target, speed * Time.deltaTime);
        }
    }
}
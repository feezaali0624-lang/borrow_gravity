using System.Collections.Generic;
using UnityEngine;

namespace BorrowGravity
{
    /// <summary>
    /// The end of the swing level: when every player is inside this trigger box, the stage is complete.
    /// Needs a Collider with "Is Trigger" on the same object.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class SwingFinishZone : MonoBehaviour
    {
        readonly HashSet<PlayerController> inside = new HashSet<PlayerController>();

        void Reset() { GetComponent<Collider>().isTrigger = true; }

        void OnTriggerEnter(Collider other)
        {
            var p = other.GetComponentInParent<PlayerController>();
            if (p == null) return;
            inside.Add(p);
            Check();
        }

        void OnTriggerExit(Collider other)
        {
            var p = other.GetComponentInParent<PlayerController>();
            if (p != null) inside.Remove(p);
        }

        void Check()
        {
            if (NetSession.IsClient) return; // the host runs the stage
            var sm = StageManager.Instance;
            if (sm == null || sm.State != StageState.Playing) return;
            int total = 0;
            foreach (var p in Players.All)
            {
                if (p == null) continue;
                total++;
                if (!inside.Contains(p)) return; // someone is still on the way
            }
            if (total > 0) sm.CompleteStage();
        }
    }
}

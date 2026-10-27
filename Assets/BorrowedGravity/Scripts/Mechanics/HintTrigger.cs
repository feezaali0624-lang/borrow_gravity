using UnityEngine;

namespace BorrowGravity
{
    /// <summary>
    /// Jab player section mein aaye: section ka naam HUD mein, aur instruction ek chhoti notification
    /// ki tarah sirf 2-3 second ke liye (raaste mein board nahi).
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class HintTrigger : MonoBehaviour
    {
        [TextArea] public string message;
        public float duration = 3f;
        public string sectionTitle;
        public bool once = true;

        bool fired;

        void Awake() => GetComponent<Collider>().isTrigger = true;

        void OnTriggerEnter(Collider other)
        {
            if ((once && fired) || Players.From(other) == null) return;
            fired = true;
            if (!string.IsNullOrEmpty(sectionTitle)) GameUI.SetSection(sectionTitle);
            if (!string.IsNullOrEmpty(message)) GameUI.ShowHint(message, duration);
        }
    }
}
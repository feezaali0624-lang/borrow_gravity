using UnityEngine;

namespace BorrowGravity
{
    /// <summary>
    /// Switches levels inside the single Game scene: the current level's root is on, all others are off, so moving
    /// on is instant (no scene load). Index 0 = opening cutscene, 1-3 = stages, 4 = ending cutscene.
    /// </summary>
    public class GameStages : MonoBehaviour
    {
        public const int Intro = 0, Outro = 6;

        public static GameStages Instance { get; private set; }

        public StageRoot[] roots;

        public int Current { get; private set; } = -1;

        void Awake()
        {
            Instance = this;
            foreach (var r in roots) if (r != null) r.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // Start (not Awake) so the scene has finished loading when the first level comes on
        void Start()
        {
            if (Current < 0) Show(GameFlow.PendingStage);
        }

        public bool Has(int index) => index >= 0 && index < roots.Length && roots[index] != null;

        // levels already played in this scene keep their state (finished puzzles, a "complete" stage); showing
        // one again needs a fresh scene
        readonly System.Collections.Generic.HashSet<int> shown = new System.Collections.Generic.HashSet<int>();
        public bool WasShown(int index) => shown.Contains(index);

        /// <summary>
        /// Before a level comes on: keep only as many astronauts as there are players, switch on their powers
        /// (A/C gravity, B/D nullify, solo both — nullify on the second button) and, without a room, pick the camera
        /// layout (solo full screen). Online, NetWorld gives each device its own player's camera.
        /// </summary>
        public static void ApplyPlayers(StageRoot root)
        {
            int count = Mathf.Clamp(GameFlow.PlayerCount, 1, GameFlow.MaxPlayers);
            foreach (var pc in root.GetComponentsInChildren<PlayerController>(true))
            {
                int i = pc.playerIndex;
                bool on = i < count;
                pc.gameObject.SetActive(on);
                var gp = pc.GetComponent<GravityPower>();
                var np = pc.GetComponent<NullifyPower>();
                bool sim = !NetSession.IsClient; // a joining phone only mirrors the host's powers
                if (gp != null) gp.enabled = sim && on && GameFlow.HasGravity(i);
                if (np != null) { np.enabled = sim && on && GameFlow.HasNullify(i); np.useSecondary = GameFlow.Solo; }
                var link = pc.GetComponent<PlayerCameraLink>();
                var cam = link != null ? link.view : null;
                if (cam == null) continue;
                if (NetSession.Online) { cam.gameObject.SetActive(on); continue; } // NetWorld picks this device's one
                bool show = on && (count == 1 ? i == 0 : i <= 1);
                cam.gameObject.SetActive(show);
                if (show) cam.rect = count == 1 ? new Rect(0f, 0f, 1f, 1f) : new Rect(i == 0 ? 0f : 0.5f, 0f, 0.5f, 1f);
            }
        }

        public void Show(int index)
        {
            if (!Has(index)) index = 1;
            Time.timeScale = 1f;
            Sfx.StopAllLoops();
            Checkpoint.ClearActive();
            for (int i = 0; i < roots.Length; i++)
                if (roots[i] != null && i != index && roots[i].gameObject.activeSelf) roots[i].gameObject.SetActive(false);
            var root = roots[index];
            root.ApplyLighting();
            ApplyPlayers(root);
            root.gameObject.SetActive(true);
            Current = index;
            shown.Add(index);
            GameFlow.PendingStage = index;

            if (NetSession.IsHost) NetSession.SendStage(index, GameFlow.ShowIntro);
            if (NetSession.Online)
            {
                if (NetWorld.Instance == null)
                {
                    if (root.GetComponentInChildren<StageManager>() != null) NetWorld.Setup(gameObject.scene);
                }
                else NetWorld.Instance.OnStageShown();
            }
        }
    }
}

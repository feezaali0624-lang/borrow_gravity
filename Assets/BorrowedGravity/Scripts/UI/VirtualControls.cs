using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BorrowGravity
{
    /// <summary>
    /// On-screen joystick + JUMP + POWER for each player on this device: its own player full screen online or solo
    /// (solo adds a second power button, NULLIFY), A and B side by side on a shared screen.
    /// Works with multi-touch and the mouse; feeds <see cref="VirtualInput"/>. J toggles it.
    /// </summary>
    public class VirtualControls : MonoBehaviour
    {
        /// <summary>On-screen controls: shown on phones and in the editor, hidden in PC builds (keyboard play); J toggles.</summary>
        public static bool Visible = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Visible = Application.isMobilePlatform || Application.isEditor;

        public HudIcons icons;
        public float stickRadius = 70f;
        public float buttonRadius = 46f;
        [Tooltip("Camera turn per reference-pixel of finger drag (degrees).")]
        public float dragLookSpeed = 0.28f;
        [Tooltip("Camera turn per screen pixel of mouse movement (degrees).")]
        public float mouseLookSpeed = 0.12f;
        /// <summary>Sab on-screen buttons aur joystick ko itna bada karo (1 = purani size).</summary>
        const float StickBoost = 1.3f;    // joystick aur arrows
        const float ButtonBoost = 1.35f;  // jump, gravity, nullify
        // home = where the joystick sits (it never moves); center = its centre (always home); zone = the left half of
        // this player's screen, kept for the stick (no camera turning there)
        /// <summary>A press this many stick radii from the joystick's centre still grabs it.</summary>
        const float GrabRadius = 1.6f;

        class Stick { public int pointer = int.MinValue; public Vector2 center, home, knob, input; public Rect zone; }
        class Button { public Vector2 center; public bool held; }

        const int Max = GameFlow.MaxPlayers;
        readonly Stick[] sticks = { new Stick(), new Stick(), new Stick(), new Stick() };
        readonly Button[] jumps = { new Button(), new Button(), new Button(), new Button() };
        readonly Button[] powers = { new Button(), new Button(), new Button(), new Button() };
        readonly Button[] powers2 = { new Button(), new Button(), new Button(), new Button() };
        readonly List<(int id, Vector2 pos, bool began)> pointers = new List<(int, Vector2, bool)>();
        bool mouseWasDown;
        readonly Dictionary<int, (Vector2 last, int player)> lookPointers = new Dictionary<int, (Vector2, int)>();
        readonly HashSet<int> alive = new HashSet<int>();
        readonly List<int> gone = new List<int>();

        void Awake() => useGUILayout = false; // no GUILayout here: skip IMGUI's extra layout pass every frame

        /// <summary>Online each phone shows only its own player's controls, across the whole screen; solo just A;
        /// otherwise A and B share the screen.</summary>
        static bool Shows(int p) => NetSession.Online ? p == NetSession.LocalPlayer : p < (GameFlow.Solo ? 1 : 2);
        static bool FullScreen => NetSession.Online || GameFlow.Solo;

        // arrows mode: up, down, left, right (GUI y points down)
        static readonly Vector2[] ArrowDirs = { new Vector2(0f, -1f), new Vector2(0f, 1f), new Vector2(-1f, 0f), new Vector2(1f, 0f) };
        readonly bool[,] arrowHeld = new bool[Max, 4];
        float ArrowRadius => SR * 0.42f;
        Vector2 ArrowCenter(Stick st, int i) => st.home + ArrowDirs[i] * SR * 0.8f;

        // sizes chosen in Settings (ControlSettings)
        float SR => stickRadius * ControlSettings.StickSize * StickBoost;
        float BR => buttonRadius * ControlSettings.ButtonSize * ButtonBoost;

        void Layout()
        {
            float w = UIKit.Width, h = UIKit.Height, half = FullScreen ? w : w * 0.5f;
            float sr = SR, br = BR;
            float y = h - 80f - sr;
            for (int p = 0; p < Max; p++)
            {
                float x0 = FullScreen ? 0f : Mathf.Min(p, 1) * half;
                sticks[p].home = new Vector2(x0 + 50f + sr, y);
                // PUBG style: the left half of this player's screen is the joystick, the right half turns the camera
                sticks[p].zone = new Rect(x0, 110f, half * 0.5f, h - 110f);
                if (sticks[p].pointer == int.MinValue) sticks[p].center = sticks[p].home;
                jumps[p].center = new Vector2(x0 + half - 44f - br, h - 112f - br * 0.4f);
                powers[p].center = new Vector2(jumps[p].center.x - br * 2f - 18f, jumps[p].center.y - 40f * ControlSettings.ButtonSize);
                powers2[p].center = new Vector2(jumps[p].center.x - br * 0.4f, jumps[p].center.y - br * 2f - 26f);
            }
        }

        void Update()
        {
            if (PlayerKeys.Pressed(Key.J)) Visible = !Visible;
            float s = Screen.height / UIKit.ReferenceHeight;
            if (s <= 0f) return;
            Layout();
            GatherPointers(s);
            Look(s);
            if (!Visible)
            {
                for (int p = 0; p < Max; p++) if (Shows(p)) { VirtualInput.Set(p, Vector2.zero, false, false); VirtualInput.SetPower2(p, false); }
                return;
            }

            for (int p = 0; p < Max; p++)
            {
                if (!Shows(p)) continue; // the other players' input arrives over the network
                powers2[p].held = GameFlow.Solo && Touching(powers2[p].center, int.MinValue);
                VirtualInput.SetPower2(p, powers2[p].held);
                var st = sticks[p];
                if (ControlSettings.Movement == MoveStyle.Arrows)
                {
                    // four arrow buttons around the joystick's resting spot; two at once = diagonal
                    Vector2 mv = Vector2.zero;
                    for (int i = 0; i < 4; i++)
                    {
                        arrowHeld[p, i] = false;
                        foreach (var ptr in pointers)
                            if (Vector2.Distance(ptr.pos, ArrowCenter(st, i)) < ArrowRadius * 1.15f) { arrowHeld[p, i] = true; mv += ArrowDirs[i]; break; }
                    }
                    st.pointer = int.MinValue;
                    st.center = st.home;
                    st.knob = Vector2.zero;
                    st.input = Vector2.ClampMagnitude(mv, 1f);
                    jumps[p].held = Touching(jumps[p].center, int.MinValue);
                    powers[p].held = Touching(powers[p].center, int.MinValue);
                    VirtualInput.Set(p, new Vector2(st.input.x, -st.input.y), jumps[p].held, powers[p].held);
                    continue;
                }
                // keep the captured pointer, or capture a new press inside the stick area
                Vector2? pos = null;
                foreach (var ptr in pointers)
                {
                    if (ptr.id == st.pointer) { pos = ptr.pos; break; }
                    if (st.pointer == int.MinValue && ptr.began && Vector2.Distance(ptr.pos, st.home) < SR * GrabRadius && !OnButton(ptr.pos))
                    {
                        // fixed joystick: grab it on (or just around) the stick; it stays where it is
                        st.pointer = ptr.id;
                        st.center = st.home;
                        pos = ptr.pos;
                        break;
                    }
                }
                if (pos == null) { st.pointer = int.MinValue; st.center = st.home; st.knob = Vector2.zero; st.input = Vector2.zero; }
                else
                {
                    Vector2 raw = (pos.Value - st.center) / SR;
                    st.knob = Vector2.ClampMagnitude(raw, 1f);           // where the knob is drawn
                    st.input = ControlSettings.ApplyStick(raw);          // dead zone + sensitivity
                }

                jumps[p].held = Touching(jumps[p].center, st.pointer);
                powers[p].held = Touching(powers[p].center, st.pointer);
                // GUI y points down, the game's "forward" is up on the stick
                VirtualInput.Set(p, new Vector2(st.input.x, -st.input.y), jumps[p].held, powers[p].held);
            }
        }


        // ---------- camera look: drag a finger (or the mouse) anywhere that isn't a control; or just move the mouse ----------

        int PlayerAt(float x) => NetSession.Online ? NetSession.LocalPlayer : GameFlow.Solo ? 0 : (x < UIKit.Width * 0.5f ? 0 : 1);

        /// <summary>Not for the camera: the HUD strip, a player's joystick half, or a button.</summary>
        bool OnControl(Vector2 pos)
        {
            if (pos.y < 110f) return true; // power panels, objective banner, pause button
                                           // pause + camera buttons (right edge, GameUI)
            if (pos.x > UIKit.Width - 18f - GameUI.BtnSize - 10f && pos.y > 120f && pos.y < 132f + GameUI.BtnSize * 2f + 40f) return true;
            if (!Visible) return false;
            for (int p = 0; p < Max; p++)
                if (Shows(p) && sticks[p].zone.Contains(pos)) return true;
            return OnButton(pos);
        }

        bool OnButton(Vector2 pos)
        {
            for (int p = 0; p < Max; p++)
            {
                if (!Shows(p)) continue;
                if (Vector2.Distance(pos, jumps[p].center) < BR * 1.2f) return true;
                if (Vector2.Distance(pos, powers[p].center) < BR * 1.2f) return true;
                if (GameFlow.Solo && Vector2.Distance(pos, powers2[p].center) < BR * 1.2f) return true;
            }
            return false;
        }

        void Look(float scale)
        {
            var sm = StageManager.Instance;
            bool playing = sm != null && sm.isActiveAndEnabled && sm.State == StageState.Playing;
            alive.Clear();
            foreach (var ptr in pointers)
            {
                alive.Add(ptr.id);
                if (lookPointers.TryGetValue(ptr.id, out var lp))
                {
                    Vector2 d = ptr.pos - lp.last;
                    if (playing) VirtualInput.AddLook(lp.player, new Vector2(d.x, d.y) * dragLookSpeed * ControlSettings.LookSensitivity);
                    lookPointers[ptr.id] = (ptr.pos, lp.player);
                }
                else if (ptr.began && !OnControl(ptr.pos)) lookPointers[ptr.id] = (ptr.pos, PlayerAt(ptr.pos.x));
            }
            if (lookPointers.Count > 0)
            {
                gone.Clear();
                foreach (var id in lookPointers.Keys) if (!alive.Contains(id)) gone.Add(id);
                foreach (var id in gone) lookPointers.Remove(id);
            }

            // laptop: moving the mouse turns the camera of the player whose half it is over (not while a button is held)
            var mouse = Mouse.current;
            if (!playing || Application.isMobilePlatform || mouse == null || pointers.Count > 0) return;
            if (mouse.leftButton.isPressed || mouse.rightButton.isPressed) return;
            Vector2 delta = mouse.delta.ReadValue();
            if (delta.sqrMagnitude < 0.01f) return;
            Vector2 sp = mouse.position.ReadValue();
            if (sp.x < 0f || sp.y < 0f || sp.x > Screen.width || sp.y > Screen.height) return;
            VirtualInput.AddLook(PlayerAt(sp.x / scale), new Vector2(delta.x, -delta.y) * mouseLookSpeed * ControlSettings.LookSensitivity);
        }

        bool Touching(Vector2 center, int ignorePointer)
        {
            foreach (var ptr in pointers)
                if (ptr.id != ignorePointer && Vector2.Distance(ptr.pos, center) < BR * 1.2f) return true;
            return false;
        }

        void GatherPointers(float s)
        {
            pointers.Clear();
            var ts = Touchscreen.current;
            if (ts != null)
                foreach (var t in ts.touches)
                {
                    if (!t.press.isPressed) continue;
                    var sp = t.position.ReadValue();
                    pointers.Add((t.touchId.ReadValue(), new Vector2(sp.x / s, (Screen.height - sp.y) / s), t.press.wasPressedThisFrame));
                }
            // some phones also report a finger as a mouse: only use the mouse when no finger is down
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed && pointers.Count == 0)
            {
                var sp = mouse.position.ReadValue();
                pointers.Add((-1, new Vector2(sp.x / s, (Screen.height - sp.y) / s), !mouseWasDown));
            }
            mouseWasDown = mouse != null && mouse.leftButton.isPressed;
        }

        void OnGUI()
        {
            if (!Visible) return;
            var sm = StageManager.Instance;
            if (sm != null && sm.State != StageState.Playing) return;
            UIKit.Begin();
            Layout();
            for (int p = 0; p < Max; p++)
            {
                if (!Shows(p)) continue;
                var st = sticks[p];
                if (ControlSettings.Movement == MoveStyle.Arrows)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        var c = ArrowCenter(st, i);
                        float r = ArrowRadius;
                        var rect = new Rect(c.x - r, c.y - r, r * 2f, r * 2f);
                        if (arrowHeld[p, i]) MenuStyle.Box(rect, MenuStyle.W25, 0f, r);
                        MenuStyle.Box(rect, arrowHeld[p, i] ? 1f : MenuStyle.W50, 1f, r);
                        Chevron(c, i, r * 0.55f, arrowHeld[p, i] ? 1f : MenuStyle.W80);
                    }
                }
                else
                {
                    // white outline joystick, always at its home spot
                    var ring = new Rect(st.center.x - SR, st.center.y - SR, SR * 2f, SR * 2f);
                    MenuStyle.Box(ring, st.pointer != int.MinValue ? MenuStyle.W80 : MenuStyle.W50, 1f, SR);
                    var knob = st.center + st.knob * SR;
                    float k = 26f * ControlSettings.StickSize;
                    MenuStyle.Box(new Rect(knob.x - k, knob.y - k, k * 2f, k * 2f), st.pointer != int.MinValue ? 0.85f : 0.45f, 0f, k);
                }
                MenuStyle.Text(new Rect(st.home.x - 70, st.home.y + SR + 6, 140, 14), "PLAYER " + GameFlow.Letter(p), UIKit.Face.Heavy, 9, MenuStyle.W50, 0.3f);
                DrawButton(jumps[p], "JUMP");
                DrawButton(powers[p], GameFlow.HasGravity(p) ? "GRAVITY" : "NULLIFY");
                if (GameFlow.Solo) DrawButton(powers2[p], "NULLIFY");
            }
        }

        /// <summary>An arrow glyph pointing up / down / left / right (rotated "‹").</summary>
        public static void Chevron(Vector2 c, int dir, float size, float alpha)
        {
            float angle = dir == 0 ? 90f : dir == 1 ? -90f : dir == 2 ? 0f : 180f;
            var keep = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, c * UIKit.Scale);
            MenuStyle.Text(new Rect(c.x - size, c.y - size, size * 2f, size * 2f), "‹", UIKit.Face.Heavy, Mathf.RoundToInt(size * 1.6f), alpha, 0f, TextAnchor.MiddleCenter, -2f);
            GUI.matrix = keep;
        }

        void DrawButton(Button b, string label)
        {
            var rect = new Rect(b.center.x - BR, b.center.y - BR, BR * 2f, BR * 2f);
            if (b.held) MenuStyle.Box(rect, MenuStyle.W25, 0f, BR);
            MenuStyle.Box(rect, b.held ? 1f : MenuStyle.W50, 1f, BR);
            MenuStyle.Text(rect, label, UIKit.Face.Heavy, Mathf.RoundToInt(Mathf.Clamp(13f * ControlSettings.ButtonSize, 11f, 18f)), 1f, 0.15f);
        }
    }
}
